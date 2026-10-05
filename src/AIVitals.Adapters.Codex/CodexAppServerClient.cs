using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace AIVitals.Adapters.Codex;

internal sealed class CodexAppServerClientFactory(string? executablePath = null, string? codexHome = null) : ICodexAppServerClientFactory
{
    public ICodexAppServerClient Create()
    {
        var command = CodexExecutableLocator.Resolve(executablePath);
        return new CodexAppServerClient(codexHome is null ? command : command with
        { EnvironmentOverrides = new Dictionary<string, string> { ["CODEX_HOME"] = codexHome } });
    }
}

internal sealed class CodexAppServerClient : ICodexAppServerClient
{
    private readonly CodexLaunchCommand? _command;
    private readonly string? _socketPath;
    private ClientWebSocket? _webSocket;
    private HttpMessageInvoker? _socketHttp;
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
    private Exception? _readerFailure;

    public CodexAppServerClient(CodexLaunchCommand command) => _command = command;
    private CodexAppServerClient(string socketPath) => _socketPath = socketPath;
    internal static CodexAppServerClient ForSocket(string socketPath) => new(socketPath);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_process is not null || _webSocket is not null) throw new InvalidOperationException("The app-server client is already started.");

        if (_socketPath is not null)
        {
            _webSocket = new ClientWebSocket();
            _socketHttp = new HttpMessageInvoker(new SocketsHttpHandler
            {
                UseProxy = false,
                ConnectCallback = async (_, token) =>
                {
                    var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                    try
                    {
                        await socket.ConnectAsync(new UnixDomainSocketEndPoint(_socketPath), token).ConfigureAwait(false);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch { socket.Dispose(); throw; }
                }
            });
            await _webSocket.ConnectAsync(new Uri("ws://localhost/"), _socketHttp, cancellationToken).ConfigureAwait(false);
            _readerTask = ReadLoopAsync(null, _lifetime.Token);
        }
        else
        {
            var command = _command ?? throw new InvalidOperationException("No Codex transport configured.");

            var startInfo = new ProcessStartInfo
            {
                FileName = command.FileName,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            foreach (var argument in command.Arguments) startInfo.ArgumentList.Add(argument);
            foreach (var pair in command.EnvironmentOverrides ?? new Dictionary<string, string>())
                startInfo.Environment[pair.Key] = pair.Value;

            _process = new Process { StartInfo = startInfo };
            if (!_process.Start()) throw new InvalidOperationException("Codex app-server could not be started.");
            _processStarted = true;

            _readerTask = ReadLoopAsync(_process.StandardOutput, _lifetime.Token);
            _stderrTask = DrainStandardErrorAsync(_process.StandardError, _lifetime.Token);
        }

        await RequestAsync(
            "initialize",
            new
            {
                clientInfo = new { name = "ai_vitals", title = "AI Vitals", version = "0.1.0" },
                capabilities = new { experimentalApi = true }
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
        if (_process is null && _webSocket is null) throw new InvalidOperationException("The app-server client has not been started.");

        var requestId = Interlocked.Increment(ref _nextRequestId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(requestId, completion)) throw new InvalidOperationException("Duplicate request id.");

        var message = new Dictionary<string, object?> { ["method"] = method, ["id"] = requestId };
        if (parameters is not null) message["params"] = parameters;

        try
        {
            if (Volatile.Read(ref _readerFailure) is { } readerFailure) throw new IOException("Codex transport has closed.", readerFailure);
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
        _webSocket?.Abort();
        if (_process is not null && _processStarted)
        {
            if (!_process.HasExited)
            {
                // Closing a Node shim's stdin first can let it exit before its native child is reaped.
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync().ConfigureAwait(false);
            }
            try { _process.StandardInput.Close(); } catch (InvalidOperationException) { }
        }

        await IgnoreCancellationAsync(_readerTask).ConfigureAwait(false);
        await IgnoreCancellationAsync(_stderrTask).ConfigureAwait(false);
        _process?.Dispose();
        _webSocket?.Dispose();
        _socketHttp?.Dispose();
        _writeLock.Dispose();
        _lifetime.Dispose();
    }

    private async Task SendAsync(object message, CancellationToken cancellationToken)
    {
        if (_webSocket is null && (_process is null || _process.HasExited)) throw new InvalidOperationException("Codex app-server is not running.");
        var json = JsonSerializer.Serialize(message);

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_webSocket is not null)
                await _webSocket.SendAsync(Encoding.UTF8.GetBytes(json).AsMemory(), WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
            else
            {
                await _process!.StandardInput.WriteLineAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
                await _process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task<string?> ReadMessageAsync(StreamReader? reader, CancellationToken token)
    {
        if (_webSocket is null) return await reader!.ReadLineAsync(token).ConfigureAwait(false);
        using var message = new MemoryStream();
        var buffer = new byte[8192];
        ValueWebSocketReceiveResult frame;
        do
        {
            frame = await _webSocket.ReceiveAsync(buffer.AsMemory(), token).ConfigureAwait(false);
            if (frame.MessageType == WebSocketMessageType.Close) return null;
            if (frame.MessageType != WebSocketMessageType.Text) throw new IOException("Unexpected binary Codex message.");
            if (message.Length + frame.Count > 16 * 1024 * 1024) throw new IOException("Codex message exceeds the transport limit.");
            message.Write(buffer, 0, frame.Count);
        } while (!frame.EndOfMessage);
        return Encoding.UTF8.GetString(message.GetBuffer(), 0, checked((int)message.Length));
    }

    private async Task ReadLoopAsync(StreamReader? reader, CancellationToken cancellationToken)
    {
        Exception? failure = null;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await ReadMessageAsync(reader, cancellationToken).ConfigureAwait(false);
                if (line is null) break;

                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (!root.TryGetProperty("method", out _) && root.TryGetProperty("id", out var idElement) && idElement.TryGetInt64(out var id))
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
            Volatile.Write(ref _readerFailure, failure ?? new OperationCanceledException());
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
