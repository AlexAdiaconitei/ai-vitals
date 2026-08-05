using System.Text.Json;
using AIVitals.Application;

namespace AIVitals.Infrastructure;

public sealed class JsonPreferencesStore : IAppPreferencesStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _path;
    private readonly string _backupPath;

    public JsonPreferencesStore(string path)
    {
        _path = path;
        _backupPath = path + ".bak";
    }

    public async Task<AppPreferences> LoadAsync(CancellationToken cancellationToken = default)
    {
        var primary = await TryReadAsync(_path, cancellationToken).ConfigureAwait(false);
        if (primary is not null) return primary;

        var backup = await TryReadAsync(_backupPath, cancellationToken).ConfigureAwait(false);
        return backup ?? new AppPreferences();
    }

    public async Task SaveAsync(AppPreferences preferences, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var temporaryPath = _path + ".tmp";
        await using (var stream = new FileStream(
                         temporaryPath,
                         FileMode.Create,
                         FileAccess.Write,
                         FileShare.None,
                         4096,
                         FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await JsonSerializer.SerializeAsync(stream, preferences, JsonOptions, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        if (File.Exists(_path)) File.Copy(_path, _backupPath, overwrite: true);
        File.Move(temporaryPath, _path, overwrite: true);
    }

    private static async Task<AppPreferences?> TryReadAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return null;

        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
            var preferences = await JsonSerializer.DeserializeAsync<AppPreferences>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);
            if (preferences is null) return null;
            // Older schemas upgrade in place: fields added later arrive with their record defaults.
            if (preferences.SchemaVersion is < 1 or > AppPreferences.CurrentSchemaVersion) return null;
            return preferences with { SchemaVersion = AppPreferences.CurrentSchemaVersion };
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }
}
