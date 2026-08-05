using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace AIVitals.Adapters.Codex;

internal sealed class CodexAppServerClientFactory(string? executablePath = null) : ICodexAppServerClientFactory
{
    public ICodexAppServerClient Create() => new CodexAppServerClient(CodexExecutableLocator.Resolve(executablePath));
}

internal sealed class CodexAppServerClient : ICodexAppServerClient
{
    private readonly CodexLaunchCommand _command;
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly Channel<CodexServerNotification> _notifications = Channel.CreateUnbounded<CodexServerNotification>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private Process? _process;
    private Task? _readerTask;
    private Task? _stderrTask;
    private long _nextRequestId;
    private bool _processStarted;

    public CodexAppServerClient(CodexLaunchCommand command) => _command = command;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_process is not null) throw new InvalidOperationException("The app-server client is already started.");

        var startInfo = new ProcessStartInfo
        {
            FileName = _command.FileName,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in _command.Arguments) startInfo.ArgumentList.Add(argument);

        _process = new Process { StartInfo = startInfo };
        if (!_process.Start()) throw new InvalidOperationException("Codex app-server could not be started.");
        _processStarted = true;

        _readerTask = ReadLoopAsync(_process.StandardOutput, _lifetime.Token);
        _stderrTask = DrainStandardErrorAsync(_process.StandardError, _lifetime.Token);

        await RequestAsync(
            "initialize",
            new
            {
                clientInfo = new { name = "ai_vitals", title = "AI Vitals", version = "0.1.0" }
            },
            cancellationToken).ConfigureAwait(false);
        await SendAsync(new Dictionary<string, object?>
        {
            ["method"] = "initialized",
            ["params"] = new { }
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<JsonElement> RequestAsync(string method, object? parameters, CancellationToken cancellationToken)
    {
        if (_process is null) throw new InvalidOperationException("The app-server client has not been started.");

        var requestId = Interlocked.Increment(ref _nextRequestId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(requestId, completion)) throw new InvalidOperationException("Duplicate request id.");

        var message = new Dictionary<string, object?> { ["method"] = method, ["id"] = requestId };
        if (parameters is not null) message["params"] = parameters;

        try
        {
            await SendAsync(message, cancellationToken).ConfigureAwait(false);
            return await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _pending.TryRemove(requestId, out _);
        }
    }

    public async IAsyncEnumerable<CodexServerNotification> ReadNotificationsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var notification in _notifications.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return notification;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _lifetime.CancelAsync().ConfigureAwait(false);
        if (_process is not null && _processStarted)
        {
            try { _process.StandardInput.Close(); } catch (InvalidOperationException) { }
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync().ConfigureAwait(false);
            }
        }

        await IgnoreCancellationAsync(_readerTask).ConfigureAwait(false);
        await IgnoreCancellationAsync(_stderrTask).ConfigureAwait(false);
        _process?.Dispose();
        _writeLock.Dispose();
        _lifetime.Dispose();
    }

    private async Task SendAsync(object message, CancellationToken cancellationToken)
    {
        if (_process is null || _process.HasExited) throw new InvalidOperationException("Codex app-server is not running.");
        var json = JsonSerializer.Serialize(message);

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _process.StandardInput.WriteLineAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
            await _process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task ReadLoopAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        Exception? failure = null;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null) break;

                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (root.TryGetProperty("id", out var idElement) && idElement.TryGetInt64(out var id))
                {
                    if (!_pending.TryGetValue(id, out var completion)) continue;
                    if (root.TryGetProperty("error", out var error))
                    {
                        var code = error.TryGetProperty("code", out var codeElement) && codeElement.TryGetInt32(out var parsedCode)
                            ? parsedCode
                            : -1;
                        var message = error.TryGetProperty("message", out var messageElement)
                            ? messageElement.GetString() ?? "Codex app-server request failed."
                            : "Codex app-server request failed.";
                        completion.TrySetException(new CodexRpcException(code, message));
                    }
                    else if (root.TryGetProperty("result", out var result))
                    {
                        completion.TrySetResult(result.Clone());
                    }
                    continue;
                }

                if (root.TryGetProperty("method", out var methodElement))
                {
                    var method = methodElement.GetString();
                    if (!string.IsNullOrEmpty(method))
                    {
                        var parameters = root.TryGetProperty("params", out var paramsElement)
                            ? paramsElement.Clone()
                            : JsonSerializer.SerializeToElement(new { });
                        await _notifications.Writer.WriteAsync(new CodexServerNotification(method, parameters), cancellationToken)
                            .ConfigureAwait(false);
                    }
                }
            }

            if (!cancellationToken.IsCancellationRequested)
                failure = new EndOfStreamException("Codex app-server ended its output stream.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        finally
        {
            foreach (var pending in _pending.Values)
                pending.TrySetException(failure ?? new OperationCanceledException());
            _notifications.Writer.TryComplete(failure);
        }
    }

    private static async Task DrainStandardErrorAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        try
        {
            while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is not null)
            {
                // Deliberately discard stderr: it can contain local paths or provider diagnostics.
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private static async Task IgnoreCancellationAsync(Task? task)
    {
        if (task is null) return;
        try { await task.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        catch (EndOfStreamException) { }
    }
}
