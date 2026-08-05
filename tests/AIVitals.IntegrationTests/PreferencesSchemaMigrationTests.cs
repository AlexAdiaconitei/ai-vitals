using AIVitals.Application;
using AIVitals.Infrastructure;

namespace AIVitals.IntegrationTests;

public sealed class PreferencesSchemaMigrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ai-vitals-preferences-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task A_version_one_file_keeps_its_settings_and_gains_the_new_defaults()
    {
        var path = Path.Combine(_root, "preferences.json");
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(
            path,
            """
            {
              "schemaVersion": 1,
              "startMinimized": false,
              "theme": "Dark",
              "language": "es",
              "fakeAdapterEnabled": false,
              "onboardingCompleted": true
            }
            """);

        var preferences = await new JsonPreferencesStore(path).LoadAsync();

        Assert.Equal(AppPreferences.CurrentSchemaVersion, preferences.SchemaVersion);
        Assert.Equal("Dark", preferences.Theme);
        Assert.Equal("es", preferences.Language);
        Assert.True(preferences.OnboardingCompleted);
        Assert.True(preferences.AutomaticUpdateCheckEnabled);
        Assert.False(preferences.StartWithWindows);
    }

    [Fact]
    public async Task A_file_from_a_newer_schema_is_ignored_rather_than_misread()
    {
        var path = Path.Combine(_root, "preferences.json");
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(
            path,
            $$"""
            { "schemaVersion": {{AppPreferences.CurrentSchemaVersion + 1}}, "theme": "Light" }
            """);

        var preferences = await new JsonPreferencesStore(path).LoadAsync();

        Assert.Equal("System", preferences.Theme);
    }

    [Fact]
    public async Task Update_preferences_survive_a_save_and_load_round_trip()
    {
        var path = Path.Combine(_root, "preferences.json");
        var store = new JsonPreferencesStore(path);

        await store.SaveAsync(new AppPreferences(
            AutomaticUpdateCheckEnabled: false,
            StartWithWindows: true));
        var preferences = await store.LoadAsync();

        Assert.False(preferences.AutomaticUpdateCheckEnabled);
        Assert.True(preferences.StartWithWindows);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
