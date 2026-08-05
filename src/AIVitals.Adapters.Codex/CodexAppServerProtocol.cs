using System.Text.Json;

namespace AIVitals.Adapters.Codex;

internal sealed record CodexServerNotification(string Method, JsonElement Params);

internal interface ICodexAppServerClient : IAsyncDisposable
{
    IAsyncEnumerable<CodexServerNotification> ReadNotificationsAsync(CancellationToken cancellationToken);
    Task StartAsync(CancellationToken cancellationToken);
    Task<JsonElement> RequestAsync(string method, object? parameters, CancellationToken cancellationToken);
}

internal interface ICodexAppServerClientFactory
{
    ICodexAppServerClient Create();
}

internal sealed class CodexRpcException(int code, string message) : Exception(message)
{
    public int Code { get; } = code;
}
