namespace HttpMonitorApp.Models;

public sealed class ClientRequestResult
{
    public int StatusCode { get; init; }

    public string ResponseBody { get; init; } = string.Empty;

    public string ResponseHeaders { get; init; } = string.Empty;

    public bool IsError { get; init; }
}
