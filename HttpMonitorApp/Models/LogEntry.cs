using System.Globalization;

namespace HttpMonitorApp.Models;

public sealed class LogEntry
{
    public DateTime Timestamp { get; init; } = DateTime.Now;

    public LogDirection Direction { get; init; }

    public string Method { get; init; } = string.Empty;

    public string Url { get; init; } = string.Empty;

    public string Headers { get; init; } = string.Empty;

    public string RequestBody { get; init; } = string.Empty;

    public int StatusCode { get; init; }

    public string ResponseBody { get; init; } = string.Empty;

    public string ResponseHeaders { get; init; } = string.Empty;

    public long DurationMs { get; init; }

    public string StatusDisplay =>
        StatusCode == 0 ? "Ошибка" : StatusCode.ToString(CultureInfo.InvariantCulture);

    public string StatusCategory => StatusCode switch
    {
        >= 200 and < 300 => "2xx",
        >= 300 and < 400 => "3xx",
        >= 400 and < 500 => "4xx",
        >= 500 and < 600 => "5xx",
        _ => "Ошибка"
    };
}
