using System.Diagnostics;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AIVitals.Adapters.Codex;

namespace AIVitals.AdapterContractTests;

public sealed class CodexSocketTransportTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "AV.Ws", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Control_socket_negotiates_websocket_handles_fragmented_json_and_keeps_the_server_alive()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "control.sock");
        using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(path));
        listener.Listen(2);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var server = ServeAsync(listener, timeout.Token);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            await using var client = CodexAppServerClient.ForSocket(path);
            await client.StartAsync(timeout.Token);
            var response = await client.RequestAsync("test/read", null, timeout.Token);
            Assert.Equal(attempt, response.GetProperty("connection").GetInt32());
        }
        await server;
    }

    [Fact]
    public async Task A_daemon_without_a_handshake_leaves_time_for_the_cli_fallback()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "control.sock");
        using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(path));
        listener.Listen(1);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var watch = Stopwatch.StartNew();
        var continuation = CodexDaemonContinuation.TryContinueAsync("thread", "turn", "Continue", timeout.Token, path);
        using var socket = await listener.AcceptAsync(timeout.Token);
        Assert.Equal(CodexDaemonContinueResult.DaemonUnavailable, await continuation);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(12));
        Assert.False(timeout.IsCancellationRequested);
    }

    private static async Task ServeAsync(Socket listener, CancellationToken token)
    {
        for (var connection = 0; connection < 2; connection++)
        {
            using var socket = await listener.AcceptAsync(token);
            await using var stream = new NetworkStream(socket, ownsSocket: false);
            var headers = new StringBuilder();
            var single = new byte[1];
            while (!headers.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
            {
                Assert.Equal(1, await stream.ReadAsync(single, token));
                headers.Append((char)single[0]);
                Assert.True(headers.Length < 16384);
            }
            Assert.StartsWith("GET / HTTP/1.1", headers.ToString());
            var key = headers.ToString().Split("\r\n").Single(line => line.StartsWith("Sec-WebSocket-Key:", StringComparison.OrdinalIgnoreCase))
                .Split(':', 2)[1].Trim();
            var accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
            await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: {accept}\r\n\r\n"), token);
            using var webSocket = WebSocket.CreateFromStream(stream, isServer: true, subProtocol: null, keepAliveInterval: Timeout.InfiniteTimeSpan);
            var buffer = new byte[4096];
            while (true)
            {
                WebSocketReceiveResult frame;
                try { frame = await webSocket.ReceiveAsync(buffer, token); }
                catch (WebSocketException) { break; }
                if (frame.MessageType == WebSocketMessageType.Close) break;
                using var request = JsonDocument.Parse(buffer.AsMemory(0, frame.Count));
                if (!request.RootElement.TryGetProperty("id", out var id)) continue;
                var json = JsonSerializer.SerializeToUtf8Bytes(new { id = id.GetInt64(), result = new { connection } });
                await webSocket.SendAsync(json.AsMemory(0, 3), WebSocketMessageType.Text, endOfMessage: false, token);
                await webSocket.SendAsync(json.AsMemory(3), WebSocketMessageType.Text, endOfMessage: true, token);
            }
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
