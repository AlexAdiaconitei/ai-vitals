using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json.Nodes;
using AIVitals.Adapters.ClaudeCode;

// Claude Code always writes UTF-8, while Console.In would decode with the console code page
// and corrupt anything the previous status line receives back.
string payload;
await using (var standardInput = Console.OpenStandardInput())
using (var reader = new StreamReader(standardInput, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true))
{
    payload = await reader.ReadToEndAsync();
}

var payloadBytes = Encoding.UTF8.GetBytes(payload);
var configuration = await ClaudeCodeBridgeProtocol.ReadConfigurationAsync();

// An oversized payload only means AI Vitals skips this snapshot. The user's status line
// must still render, so it is never a reason to abort.
if (payloadBytes.Length <= ClaudeCodeBridgeProtocol.MaximumPayloadBytes)
    await TryForwardAsync(payloadBytes);

var previousCommand = GetPreviousCommand(configuration?.PreviousStatusLine);
if (!string.IsNullOrWhiteSpace(previousCommand) && previousCommand != configuration?.InstalledCommand)
{
    var previousOutput = await RunPreviousStatusLineAsync(previousCommand, payload);
    if (!string.IsNullOrEmpty(previousOutput)) Console.Write(previousOutput);
}
else
{
    Console.Write("AI Vitals");
}

return 0;

static async Task TryForwardAsync(byte[] payloadBytes)
{
    try
    {
        await using var pipe = new NamedPipeClientStream(
            ".",
            ClaudeCodeBridgeProtocol.PipeName,
            PipeDirection.Out,
            PipeOptions.Asynchronous);
        await pipe.ConnectAsync(100);

        var length = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(length, payloadBytes.Length);
        await pipe.WriteAsync(length);
        await pipe.WriteAsync(payloadBytes);
        await pipe.FlushAsync();
    }
    catch (Exception)
    {
        // Claude's existing status line must keep working when the tracker is closed,
        // busy, or unreachable for any reason. Delivery is strictly best-effort.
    }
}

static string? GetPreviousCommand(JsonNode? previousStatusLine)
{
    return previousStatusLine is JsonObject statusLine &&
           statusLine["type"]?.GetValue<string>() == "command" &&
           statusLine["command"] is JsonValue commandValue &&
           commandValue.TryGetValue<string>(out var command)
        ? command
        : null;
}

static async Task<string?> RunPreviousStatusLineAsync(string command, string payload)
{
    var startInfo = CreateShellCommand(command);
    startInfo.UseShellExecute = false;
    startInfo.RedirectStandardInput = true;
    startInfo.RedirectStandardOutput = true;
    startInfo.RedirectStandardError = true;
    startInfo.CreateNoWindow = true;
    startInfo.StandardInputEncoding = new UTF8Encoding(false);
    startInfo.StandardOutputEncoding = Encoding.UTF8;
    startInfo.StandardErrorEncoding = Encoding.UTF8;

    using var process = new Process { StartInfo = startInfo };
    try
    {
        if (!process.Start()) return null;
        await process.StandardInput.WriteAsync(payload);
        process.StandardInput.Close();

        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        await stderr;
        return await stdout;
    }
    catch (Exception exception) when (exception is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
    {
        return null;
    }
}

static ProcessStartInfo CreateShellCommand(string command)
{
    var gitBashCandidates = new[]
    {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "bin", "bash.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Git", "bin", "bash.exe")
    };
    var gitBash = gitBashCandidates.FirstOrDefault(File.Exists);
    if (gitBash is not null)
    {
        var bashInfo = new ProcessStartInfo { FileName = gitBash };
        bashInfo.ArgumentList.Add("-lc");
        bashInfo.ArgumentList.Add(command);
        return bashInfo;
    }

    var powershellInfo = new ProcessStartInfo { FileName = "powershell.exe" };
    powershellInfo.ArgumentList.Add("-NoProfile");
    powershellInfo.ArgumentList.Add("-Command");
    powershellInfo.ArgumentList.Add(command);
    return powershellInfo;
}
