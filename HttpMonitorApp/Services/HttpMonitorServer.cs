using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using HttpMonitorApp.Models;

namespace HttpMonitorApp.Services;

public sealed class HttpMonitorServer
{
    private readonly ConcurrentDictionary<Guid, MessageRecord> _messages = new();
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
    private readonly RequestMonitorService _monitorService;
    private readonly Stopwatch _uptime = new();

    private HttpListener? _listener;
    private CancellationTokenSource? _cancellationTokenSource;

    public HttpMonitorServer(RequestMonitorService monitorService)
    {
        _monitorService = monitorService;
    }

    public bool IsRunning => _listener?.IsListening ?? false;

    public int CurrentPort { get; private set; }

    public TimeSpan Uptime => _uptime.IsRunning ? _uptime.Elapsed : TimeSpan.Zero;

    public int StoredMessagesCount => _messages.Count;

    public Task StartAsync(int port)
    {
        if (IsRunning)
        {
            return Task.CompletedTask;
        }

        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://localhost:{port}/");
        _listener.Start();

        CurrentPort = port;
        _uptime.Restart();
        _cancellationTokenSource = new CancellationTokenSource();
        _ = Task.Run(() => AcceptLoopAsync(_listener, _cancellationTokenSource.Token));

        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        _cancellationTokenSource?.Cancel();

        if (_listener is not null)
        {
            if (_listener.IsListening)
            {
                _listener.Stop();
            }

            _listener.Close();
        }

        _listener = null;
        _uptime.Stop();
        CurrentPort = 0;

        return Task.CompletedTask;
    }

    private async Task AcceptLoopAsync(HttpListener listener, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && listener.IsListening)
        {
            HttpListenerContext? context = null;

            try
            {
                context = await listener.GetContextAsync();
            }
            catch (HttpListenerException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }

            if (context is null)
            {
                continue;
            }

            _ = Task.Run(() => HandleRequestAsync(context), cancellationToken);
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;
        var stopwatch = Stopwatch.StartNew();
        var requestBody = await ReadBodyAsync(request);
        var responseBody = string.Empty;
        var responseHeaders = string.Empty;
        var statusCode = 200;

        try
        {
            switch (request.HttpMethod.ToUpperInvariant())
            {
                case "GET":
                    responseBody = BuildStatusResponse();
                    response.StatusCode = 200;
                    response.ContentType = "application/json; charset=utf-8";
                    break;

                case "POST":
                    var payload = JsonSerializer.Deserialize<IncomingMessagePayload>(requestBody, _jsonOptions);
                    if (string.IsNullOrWhiteSpace(payload?.Message))
                    {
                        statusCode = 400;
                        responseBody = JsonSerializer.Serialize(new { error = "JSON должен содержать поле message." }, _jsonOptions);
                    }
                    else
                    {
                        var messageRecord = new MessageRecord
                        {
                            Id = Guid.NewGuid(),
                            Message = payload.Message,
                            ReceivedAt = DateTime.Now
                        };

                        _messages[messageRecord.Id] = messageRecord;
                        statusCode = 201;
                        responseBody = JsonSerializer.Serialize(new
                        {
                            id = messageRecord.Id,
                            receivedAt = messageRecord.ReceivedAt,
                            message = messageRecord.Message
                        }, _jsonOptions);
                    }

                    response.StatusCode = statusCode;
                    response.ContentType = "application/json; charset=utf-8";
                    break;

                default:
                    statusCode = 405;
                    responseBody = JsonSerializer.Serialize(new { error = "Поддерживаются только GET и POST запросы." }, _jsonOptions);
                    response.StatusCode = statusCode;
                    response.ContentType = "application/json; charset=utf-8";
                    break;
            }
        }
        catch (JsonException exception)
        {
            statusCode = 400;
            responseBody = JsonSerializer.Serialize(new { error = $"Некорректный JSON: {exception.Message}" }, _jsonOptions);
            response.StatusCode = statusCode;
            response.ContentType = "application/json; charset=utf-8";
        }
        catch (Exception exception)
        {
            statusCode = 500;
            responseBody = JsonSerializer.Serialize(new { error = $"Внутренняя ошибка сервера: {exception.Message}" }, _jsonOptions);
            response.StatusCode = statusCode;
            response.ContentType = "application/json; charset=utf-8";
        }
        finally
        {
            stopwatch.Stop();
            response.ContentEncoding = Encoding.UTF8;

            try
            {
                var bytes = Encoding.UTF8.GetBytes(responseBody);
                response.ContentLength64 = bytes.LongLength;
                await response.OutputStream.WriteAsync(bytes);
                response.OutputStream.Close();
                responseHeaders = $"Content-Type: {response.ContentType}{Environment.NewLine}Content-Length: {response.ContentLength64}";
            }
            catch (Exception exception)
            {
                responseHeaders = "Ошибка записи ответа";
                responseBody = $"{responseBody}{Environment.NewLine}Ошибка отправки ответа: {exception.Message}".Trim();
            }
            finally
            {
                response.Close();
            }

            _monitorService.Record(new LogEntry
            {
                Timestamp = DateTime.Now,
                Direction = LogDirection.Incoming,
                Method = request.HttpMethod.ToUpperInvariant(),
                Url = request.Url?.ToString() ?? string.Empty,
                Headers = FormatHeaders(request.Headers),
                RequestBody = requestBody,
                StatusCode = statusCode,
                ResponseBody = responseBody,
                ResponseHeaders = responseHeaders,
                DurationMs = stopwatch.ElapsedMilliseconds == 0 ? 1 : stopwatch.ElapsedMilliseconds
            });
        }
    }

    private string BuildStatusResponse()
    {
        var snapshot = _monitorService.CreateSnapshot(Uptime, StoredMessagesCount);

        return JsonSerializer.Serialize(new
        {
            server = "HttpMonitorServer",
            port = CurrentPort,
            uptime = snapshot.Uptime.ToString(@"dd\.hh\:mm\:ss"),
            totalRequests = snapshot.TotalIncomingRequests,
            getRequests = snapshot.GetRequests,
            postRequests = snapshot.PostRequests,
            averageProcessingTimeMs = Math.Round(snapshot.AverageProcessingTimeMs, 2),
            storedMessages = snapshot.StoredMessages
        }, _jsonOptions);
    }

    private static async Task<string> ReadBodyAsync(HttpListenerRequest request)
    {
        if (!request.HasEntityBody)
        {
            return string.Empty;
        }

        using var reader = new StreamReader(
            request.InputStream,
            request.ContentEncoding == Encoding.Default ? Encoding.UTF8 : request.ContentEncoding);

        return await reader.ReadToEndAsync();
    }

    private static string FormatHeaders(System.Collections.Specialized.NameValueCollection headers)
    {
        var builder = new StringBuilder();

        foreach (var key in headers.AllKeys)
        {
            if (key is not null)
            {
                builder.AppendLine($"{key}: {headers[key]}");
            }
        }

        return builder.ToString().Trim();
    }
}
