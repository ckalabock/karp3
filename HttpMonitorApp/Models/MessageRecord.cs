namespace HttpMonitorApp.Models;

public sealed class MessageRecord
{
    public Guid Id { get; init; }

    public string Message { get; init; } = string.Empty;

    public DateTime ReceivedAt { get; init; } = DateTime.Now;
}
