namespace HttpMonitorApp.Models;

public sealed class MonitoringSnapshot
{
    public int TotalIncomingRequests { get; init; }

    public int GetRequests { get; init; }

    public int PostRequests { get; init; }

    public double AverageProcessingTimeMs { get; init; }

    public int StoredMessages { get; init; }

    public TimeSpan Uptime { get; init; }
}
