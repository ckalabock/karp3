namespace HttpMonitorApp.Models;

public sealed class LoadChartBar
{
    public string Label { get; init; } = string.Empty;

    public int Count { get; init; }

    public double Height { get; init; }
}
