using System.Globalization;
using System.IO;
using System.Text;
using HttpMonitorApp.Models;

namespace HttpMonitorApp.Services;

public sealed class RequestMonitorService
{
    private readonly object _syncRoot = new();
    private readonly string _logFilePath;
    private readonly List<LogEntry> _logs = [];

    public RequestMonitorService(string logFilePath)
    {
        _logFilePath = logFilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(logFilePath)!);
    }

    public event Action<LogEntry>? LogRecorded;

    public string LogFilePath => _logFilePath;

    public void Record(LogEntry entry)
    {
        lock (_syncRoot)
        {
            _logs.Add(entry);
        }

        _ = File.AppendAllTextAsync(_logFilePath, FormatLogEntry(entry) + Environment.NewLine + new string('-', 90) + Environment.NewLine);
        LogRecorded?.Invoke(entry);
    }

    public IReadOnlyList<LogEntry> GetLogs()
    {
        lock (_syncRoot)
        {
            return _logs.OrderByDescending(entry => entry.Timestamp).ToList();
        }
    }

    public MonitoringSnapshot CreateSnapshot(TimeSpan uptime, int storedMessages)
    {
        List<LogEntry> incomingLogs;

        lock (_syncRoot)
        {
            incomingLogs = _logs.Where(entry => entry.Direction == LogDirection.Incoming).ToList();
        }

        var getRequests = incomingLogs.Count(entry => entry.Method == "GET");
        var postRequests = incomingLogs.Count(entry => entry.Method == "POST");
        var averageTime = incomingLogs.Count == 0 ? 0 : incomingLogs.Average(entry => entry.DurationMs);

        return new MonitoringSnapshot
        {
            TotalIncomingRequests = incomingLogs.Count,
            GetRequests = getRequests,
            PostRequests = postRequests,
            AverageProcessingTimeMs = averageTime,
            StoredMessages = storedMessages,
            Uptime = uptime
        };
    }

    public IReadOnlyList<LoadPoint> BuildLoadPoints(LoadGranularity granularity)
    {
        var now = DateTime.Now;
        var intervalCount = granularity == LoadGranularity.Minute ? 10 : 12;
        var step = granularity == LoadGranularity.Minute ? TimeSpan.FromMinutes(1) : TimeSpan.FromHours(1);
        var start = granularity == LoadGranularity.Minute
            ? new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0).AddMinutes(-(intervalCount - 1))
            : new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0).AddHours(-(intervalCount - 1));

        List<LogEntry> incomingLogs;

        lock (_syncRoot)
        {
            incomingLogs = _logs.Where(entry => entry.Direction == LogDirection.Incoming).ToList();
        }

        var grouped = incomingLogs
            .GroupBy(entry => Truncate(entry.Timestamp, granularity))
            .ToDictionary(group => group.Key, group => group.Count());

        var points = new List<LoadPoint>(intervalCount);

        for (var index = 0; index < intervalCount; index++)
        {
            var pointTime = start.AddTicks(step.Ticks * index);
            grouped.TryGetValue(pointTime, out var count);

            points.Add(new LoadPoint
            {
                Label = granularity == LoadGranularity.Minute
                    ? pointTime.ToString("HH:mm", CultureInfo.InvariantCulture)
                    : pointTime.ToString("dd.MM HH:00", CultureInfo.InvariantCulture),
                Count = count
            });
        }

        return points;
    }

    private static DateTime Truncate(DateTime timestamp, LoadGranularity granularity) =>
        granularity == LoadGranularity.Minute
            ? new DateTime(timestamp.Year, timestamp.Month, timestamp.Day, timestamp.Hour, timestamp.Minute, 0)
            : new DateTime(timestamp.Year, timestamp.Month, timestamp.Day, timestamp.Hour, 0, 0);

    private static string FormatLogEntry(LogEntry entry)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss}] {(entry.Direction == LogDirection.Incoming ? "IN" : "OUT")} {entry.Method} {entry.Url}");
        builder.AppendLine($"Status: {entry.StatusDisplay} | Duration: {entry.DurationMs} ms");
        builder.AppendLine("Headers:");
        builder.AppendLine(string.IsNullOrWhiteSpace(entry.Headers) ? "<empty>" : entry.Headers);
        builder.AppendLine("Request body:");
        builder.AppendLine(string.IsNullOrWhiteSpace(entry.RequestBody) ? "<empty>" : entry.RequestBody);
        builder.AppendLine("Response headers:");
        builder.AppendLine(string.IsNullOrWhiteSpace(entry.ResponseHeaders) ? "<empty>" : entry.ResponseHeaders);
        builder.AppendLine("Response body:");
        builder.AppendLine(string.IsNullOrWhiteSpace(entry.ResponseBody) ? "<empty>" : entry.ResponseBody);
        return builder.ToString().TrimEnd();
    }
}
