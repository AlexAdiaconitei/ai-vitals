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
        Assert.False(preferences.EffectiveActivityWidget.IsVisible);
        Assert.False(preferences.EffectiveActivityIntegrations.ClaudeCodeEnabled);
        Assert.False(preferences.EffectiveActivityIntegrations.CodexEnabled);
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
    public async Task Legacy_global_resume_is_not_migrated_into_unbounded_authorization()
    {
        var path = Path.Combine(_root, "preferences.json");
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(path,
            """{"schemaVersion":4,"codexResume":{"autoResumeEnabled":true,"dismissedTurnIds":["dismissed"]}}""");
        var loaded = await new JsonPreferencesStore(path).LoadAsync();
        Assert.False(loaded.EffectiveCodexResume.AutoResumeEnabled);
        Assert.Contains("dismissed", loaded.EffectiveCodexResume.EffectiveDismissedTurnIds);
        Assert.Null(loaded.EffectiveCodexResume.State);
    }

    [Fact]
    public async Task Pending_reset_arming_and_launch_counts_survive_preferences_round_trip()
    {
        var path = Path.Combine(_root, "preferences.json");
        var reset = DateTimeOffset.UtcNow.AddHours(1);
        var state = new CodexResumeState(
            [new("thread", "turn", reset.AddMinutes(-30), [new("codex:primary", reset)], Armed: true)],
            [new(reset, 2)]);
        var store = new JsonPreferencesStore(path);
        await store.SaveAsync(new AppPreferences(CodexResume: new(State: state)));
        var loaded = await store.LoadAsync();
        Assert.True(Assert.Single(loaded.EffectiveCodexResume.State!.Entries).Armed);
        Assert.Equal(reset, Assert.Single(Assert.Single(loaded.EffectiveCodexResume.State.Entries).Windows).ResetsAtUtc);
        Assert.Equal(2, Assert.Single(loaded.EffectiveCodexResume.State.LaunchCounts).Count);
    }

    [Fact]
    public async Task Appearance_save_cannot_overwrite_an_in_flight_resume_reservation()
    {
        var store = new DelayedPreferencesStore();
        await using var monitor = new UsageMonitorService([], new SqliteObservationRepository(Path.Combine(_root, "unused.db")), store);
        var state = new CodexResumeState([new("thread", "turn", DateTimeOffset.UtcNow, [],
            Phase: PausedThreadPhase.Starting)], []);
        var reserve = monitor.UpdatePreferencesAsync(current => current with { CodexResume = new(State: state) });
        await store.FirstWriteStarted.Task;
        var appearance = monitor.UpdatePreferencesAsync(current => current with { Theme = "Dark" });
        store.ReleaseFirstWrite.SetResult();
        await Task.WhenAll(reserve, appearance);
        Assert.Equal("Dark", monitor.State.Preferences.Theme);
        Assert.Equal(PausedThreadPhase.Starting, Assert.Single(store.Saved!.EffectiveCodexResume.State!.Entries).Phase);
    }

    private sealed class DelayedPreferencesStore : IAppPreferencesStore
    {
        public TaskCompletionSource FirstWriteStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseFirstWrite { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public AppPreferences? Saved { get; private set; }
        public Task<AppPreferences> LoadAsync(CancellationToken token = default) => Task.FromResult(new AppPreferences());
        public async Task SaveAsync(AppPreferences preferences, CancellationToken token = default)
        {
            if (!FirstWriteStarted.Task.IsCompleted)
            {
                FirstWriteStarted.SetResult();
                await ReleaseFirstWrite.Task.WaitAsync(token);
            }
            Saved = preferences;
        }
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
