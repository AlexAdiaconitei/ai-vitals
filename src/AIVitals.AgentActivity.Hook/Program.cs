using System.IO.Pipes;
using AIVitals.AgentActivity;

try
{
    var arguments = ParseArguments(args);
    if (arguments is null) return 0;

    var input = await ReadBoundedAsync(Console.OpenStandardInput(), AgentActivityBridgeProtocol.MaximumHookInputBytes);
    if (input is null) return 0;

    var key = AgentActivityHookPayload.LoadOrCreateKey();
    var message = AgentActivityHookPayload.Project(
        input,
        arguments.Value.Provider,
        arguments.Value.Event,
        key,
        DateTimeOffset.UtcNow,
        arguments.Value.IncludeWorkspaceLabel,
        Environment.CurrentDirectory);
    if (message is null) return 0;

    await using var pipe = new NamedPipeClientStream(
        ".",
        AgentActivityBridgeProtocol.PipeName,
        PipeDirection.Out,
        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
    using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
    await pipe.ConnectAsync(timeout.Token);
    await AgentActivityBridgeProtocol.WriteFrameAsync(pipe, message, timeout.Token);
}
catch (Exception exception) when (exception is IOException
                                  or UnauthorizedAccessException
                                  or OperationCanceledException
                                  or System.Security.Cryptography.CryptographicException)
{
    // Activity delivery is best-effort and must never interrupt the agent lifecycle.
}

return 0;

static (AgentActivityProvider Provider, AgentActivityEvent Event, bool IncludeWorkspaceLabel)? ParseArguments(
    string[] arguments)
{
    string? provider = null;
    string? activityEvent = null;
    var includeWorkspaceLabel = false;
    for (var index = 0; index < arguments.Length; index++)
    {
        switch (arguments[index])
        {
            case "--labels":
                includeWorkspaceLabel = true;
                break;
            case "--provider" when index + 1 < arguments.Length:
                provider = arguments[++index];
                break;
            case "--event" when index + 1 < arguments.Length:
                activityEvent = arguments[++index];
                break;
        }
    }

    if (!Enum.TryParse<AgentActivityProvider>(provider, ignoreCase: true, out var parsedProvider)
        || !Enum.TryParse<AgentActivityEvent>(activityEvent, ignoreCase: true, out var parsedEvent))
        return null;
    return (parsedProvider, parsedEvent, includeWorkspaceLabel);
}

static async Task<byte[]?> ReadBoundedAsync(Stream input, int maximumBytes)
{
    using var buffer = new MemoryStream(capacity: Math.Min(maximumBytes, 16 * 1024));
    var chunk = new byte[8192];
    while (true)
    {
        var read = await input.ReadAsync(chunk);
        if (read == 0) return buffer.ToArray();
        if (buffer.Length + read > maximumBytes) return null;
        await buffer.WriteAsync(chunk.AsMemory(0, read));
    }
}
