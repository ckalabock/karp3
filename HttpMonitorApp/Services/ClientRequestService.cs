using System.Diagnostics;
using System.Net.Http;
using System.Text;
using HttpMonitorApp.Models;

namespace HttpMonitorApp.Services;

public sealed class ClientRequestService
{
    private readonly HttpClient _httpClient = new();
    private readonly RequestMonitorService _monitorService;

    public ClientRequestService(RequestMonitorService monitorService)
    {
        _monitorService = monitorService;
    }

    public async Task<ClientRequestResult> SendAsync(string url, string method, string? requestBody)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), url);

        if (string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase))
        {
            request.Content = new StringContent(requestBody ?? string.Empty, Encoding.UTF8, "application/json");
        }

        var requestHeaders = BuildRequestHeaders(request);
        var stopwatch = Stopwatch.StartNew();
        var responseBody = string.Empty;
        var responseHeaders = string.Empty;
        var statusCode = 0;

        try
        {
            using var response = await _httpClient.SendAsync(request);
            statusCode = (int)response.StatusCode;
            responseBody = await response.Content.ReadAsStringAsync();
            responseHeaders = FormatHeaders(response.Headers) + FormatHeaders(response.Content.Headers);

            return new ClientRequestResult
            {
                StatusCode = statusCode,
                ResponseBody = responseBody,
                ResponseHeaders = responseHeaders,
                IsError = false
            };
        }
        catch (Exception exception)
        {
            responseBody = $"Ошибка выполнения запроса: {exception.Message}";

            return new ClientRequestResult
            {
                StatusCode = 0,
                ResponseBody = responseBody,
                ResponseHeaders = string.Empty,
                IsError = true
            };
        }
        finally
        {
            stopwatch.Stop();

            _monitorService.Record(new LogEntry
            {
                Timestamp = DateTime.Now,
                Direction = LogDirection.Outgoing,
                Method = method.ToUpperInvariant(),
                Url = url,
                Headers = requestHeaders,
                RequestBody = requestBody ?? string.Empty,
                StatusCode = statusCode,
                ResponseBody = responseBody,
                ResponseHeaders = responseHeaders,
                DurationMs = stopwatch.ElapsedMilliseconds == 0 ? 1 : stopwatch.ElapsedMilliseconds
            });
        }
    }

    private static string BuildRequestHeaders(HttpRequestMessage request)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Request-Uri: {request.RequestUri}");

        foreach (var header in request.Headers)
        {
            builder.AppendLine($"{header.Key}: {string.Join(", ", header.Value)}");
        }

        if (request.Content is not null)
        {
            foreach (var header in request.Content.Headers)
            {
                builder.AppendLine($"{header.Key}: {string.Join(", ", header.Value)}");
            }
        }

        return builder.ToString().Trim();
    }

    private static string FormatHeaders(IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers)
    {
        var builder = new StringBuilder();

        foreach (var header in headers)
        {
            builder.AppendLine($"{header.Key}: {string.Join(", ", header.Value)}");
        }

        return builder.Length == 0 ? string.Empty : builder.ToString().Trim() + Environment.NewLine;
    }
}
