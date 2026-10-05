using System.Text.Json;
using System.Text.Json.Serialization;
using AIVitals.Application;

namespace AIVitals.Infrastructure;

public sealed class CodexResumeDiagnosticLog(string path)
{
    private const long MaximumBytes = 512 * 1024;
    private readonly object _gate = new();
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public void Write(CodexResumeDiagnostic diagnostic)
    {
        try
        {
            lock (_gate)
            {
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                if (File.Exists(path) && new FileInfo(path).Length >= MaximumBytes)
                    File.Move(path, path + ".previous", overwrite: true);
                File.AppendAllText(path, JsonSerializer.Serialize(diagnostic, Options) + Environment.NewLine);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Diagnostics must not interrupt scanning or turn submission.
        }
    }
}
