using AIVitals.Adapters.Abstractions;
using AIVitals.Adapters.Fake;
using AIVitals.Application;
using AIVitals.Infrastructure;
using AIVitals.Domain;

namespace AIVitals.IntegrationTests;

public sealed class PhaseZeroPersistenceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "AIVitals.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Fake_observation_is_persisted_and_available_after_restart()
    {
        Directory.CreateDirectory(_root);
        var paths = new AppDataPaths(_root);
        var firstObservation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var repository = new SqliteObservationRepository(paths.DatabasePath);

        await using (var monitor = new UsageMonitorService(
                         new IUsageAdapter[] { new FakeUsageAdapter(TimeSpan.FromHours(1)) },
                         repository,
                         new JsonPreferencesStore(paths.PreferencesPath)))
        {
            monitor.StateChanged += (_, state) =>
            {
                if (state.LatestObservation is not null) firstObservation.TrySetResult();
            };
            await monitor.StartAsync();
            await firstObservation.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }

        var reopenedRepository = new SqliteObservationRepository(paths.DatabasePath);
        await reopenedRepository.InitializeAsync();
        var restored = await reopenedRepository.GetLatestAsync();

        Assert.NotNull(restored);
        Assert.Equal("fake", restored.ProviderId);
        Assert.Equal(37m, restored.Value);
    }

    [Fact]
    public async Task Preferences_are_restored_and_corrupt_primary_falls_back_to_backup()
    {
        Directory.CreateDirectory(_root);
        var paths = new AppDataPaths(_root);
        var store = new JsonPreferencesStore(paths.PreferencesPath);
        var first = new AppPreferences(Language: "en", Theme: "Dark", StartMinimized: false);

        await store.SaveAsync(first);
        await store.SaveAsync(first with { Theme = "Light" });
        await File.WriteAllTextAsync(paths.PreferencesPath, "{not valid json");

        var restored = await new JsonPreferencesStore(paths.PreferencesPath).LoadAsync();

        Assert.Equal(first, restored);
    }

    [Fact]
    public async Task Widget_preferences_round_trip_without_breaking_older_preference_files()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "preferences.json");
        await File.WriteAllTextAsync(path, "{\"schemaVersion\":1,\"startMinimized\":true,\"theme\":\"System\",\"language\":\"es\"}");
        var store = new JsonPreferencesStore(path);

        var legacy = await store.LoadAsync();
        Assert.Equal(["codex", "claude-code"], legacy.EffectiveWidget.PinnedProviderIds!);

        var configured = legacy with
        {
            Widget = new WidgetPreferences(
                IsVisible: false,
                Mode: WidgetVisualMode.VerticalBars,
                IsLocked: true,
                Left: 120,
                Top: 80,
                PinnedProviderIds: ["claude-code"])
        };
        await store.SaveAsync(configured);

        var restored = await store.LoadAsync();
        Assert.False(restored.EffectiveWidget.IsVisible);
        Assert.Equal(WidgetVisualMode.VerticalBars, restored.EffectiveWidget.Mode);
        Assert.Equal(["claude-code"], restored.EffectiveWidget.PinnedProviderIds!);
        Assert.Equal(120, restored.EffectiveWidget.Left);
    }

    [Fact]
    public async Task Latest_observation_is_restored_independently_for_each_provider()
    {
        Directory.CreateDirectory(_root);
        var repository = new SqliteObservationRepository(Path.Combine(_root, "usage.db"));
        await repository.InitializeAsync();
        var observedAt = new DateTimeOffset(2026, 8, 5, 8, 0, 0, TimeSpan.Zero);
        await repository.AppendAsync(CreateObservation("codex", 12m, observedAt));
        await repository.AppendAsync(CreateObservation("claude-code", 44m, observedAt.AddMinutes(1)));
        await repository.AppendAsync(CreateObservation("codex", 31m, observedAt.AddMinutes(2)));

        var latest = await repository.GetLatestByProviderAsync();

        Assert.Equal(2, latest.Count);
        Assert.Equal(31m, latest["codex"].Value);
        Assert.Equal(44m, latest["claude-code"].Value);
    }

    [Fact]
    public async Task History_only_returns_providers_configured_for_the_running_application()
    {
        Directory.CreateDirectory(_root);
        var paths = new AppDataPaths(_root);
        var repository = new SqliteObservationRepository(paths.DatabasePath);
        await repository.InitializeAsync();
        var observedAt = new DateTimeOffset(2026, 8, 5, 8, 0, 0, TimeSpan.Zero);
        await repository.AppendAsync(CreateObservation("codex", 24m, observedAt));
        await repository.AppendAsync(CreateObservation("fake", 61m, observedAt.AddMinutes(1)));

        await using var monitor = new UsageMonitorService(
            [new SilentAdapter("codex")],
            repository,
            new JsonPreferencesStore(paths.PreferencesPath));
        await monitor.StartAsync();

        var history = await monitor.QueryObservationsAsync(new ObservationQuery(Limit: 100));

        var observation = Assert.Single(history);
        Assert.Equal("codex", observation.ProviderId);
        Assert.DoesNotContain("fake", monitor.State.LatestByProvider.Keys);
    }

    [Fact]
    public async Task Monitor_restores_each_quota_window_instead_of_overwriting_by_provider()
    {
        Directory.CreateDirectory(_root);
        var paths = new AppDataPaths(_root);
        var repository = new SqliteObservationRepository(paths.DatabasePath);
        await repository.InitializeAsync();
        var observedAt = new DateTimeOffset(2026, 8, 5, 8, 0, 0, TimeSpan.Zero);
        await repository.AppendAsync(CreateWindowObservation(
            "claude-code",
            36m,
            observedAt,
            "claude-code:statusline:rate-limit:five-hour",
            TimeSpan.FromHours(5)));
        await repository.AppendAsync(CreateWindowObservation(
            "claude-code",
            62m,
            observedAt,
            "claude-code:statusline:rate-limit:seven-day",
            TimeSpan.FromDays(7)));

        await using var monitor = new UsageMonitorService(
            Array.Empty<IUsageAdapter>(),
            repository,
            new JsonPreferencesStore(paths.PreferencesPath));
        await monitor.StartAsync();

        var restored = monitor.State.LatestQuotaByProvider["claude-code"];
        Assert.Equal(2, restored.Count);
        Assert.Contains(restored, item => item.Source.EndsWith("five-hour", StringComparison.Ordinal));
        Assert.Contains(restored, item => item.Source.EndsWith("seven-day", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Monitor_restores_statusline_quota_snapshots_per_session()
    {
        Directory.CreateDirectory(_root);
        var paths = new AppDataPaths(_root);
        var repository = new SqliteObservationRepository(paths.DatabasePath);
        await repository.InitializeAsync();
        var observedAt = new DateTimeOffset(2026, 8, 5, 8, 0, 0, TimeSpan.Zero);
        await repository.AppendAsync(CreateWindowObservation(
            "claude-code",
            56m,
            observedAt,
            "claude-code:statusline:rate-limit:seven-day",
            TimeSpan.FromDays(7),
            "session-one"));
        await repository.AppendAsync(CreateWindowObservation(
            "claude-code",
            67m,
            observedAt.AddSeconds(1),
            "claude-code:statusline:rate-limit:seven-day",
            TimeSpan.FromDays(7),
            "session-two"));

        await using var monitor = new UsageMonitorService(
            Array.Empty<IUsageAdapter>(),
            repository,
            new JsonPreferencesStore(paths.PreferencesPath));
        await monitor.StartAsync();

        var restored = monitor.State.LatestQuotaByProvider["claude-code"];
        Assert.Equal(2, restored.Count);
        Assert.Equal(
            ["session-one", "session-two"],
            restored.Select(item => item.AnonymousSessionId!).Order().ToArray());
    }

    [Fact]
    public async Task History_query_and_scoped_delete_share_the_same_filters()
    {
        Directory.CreateDirectory(_root);
        var repository = new SqliteObservationRepository(Path.Combine(_root, "history.db"));
        await repository.InitializeAsync();
        var day = new DateTimeOffset(2026, 8, 5, 0, 0, 0, TimeSpan.Zero);
        await repository.AppendAsync(CreateObservation("codex", 10m, day.AddHours(1)));
        await repository.AppendAsync(CreateObservation("claude-code", 20m, day.AddHours(2)));
        await repository.AppendAsync(CreateObservation("codex", 30m, day.AddDays(2)));
        var filter = new ObservationQuery(day, day.AddDays(1), "codex");

        var matching = await repository.QueryAsync(filter);
        var deleted = await repository.DeleteAsync(filter);
        var remaining = await repository.QueryAsync(new ObservationQuery());

        Assert.Single(matching);
        Assert.Equal(10m, matching[0].Value);
        Assert.Equal(1, deleted);
        Assert.Equal(2, remaining.Count);
        Assert.DoesNotContain(remaining, item => item.Value == 10m);
    }

    private static UsageObservation CreateObservation(string provider, decimal value, DateTimeOffset observedAt) =>
        new(
            Guid.NewGuid(),
            provider,
            provider + ":default",
            UsageCapability.QuotaWindow,
            value,
            "percent",
            observedAt,
            provider + ":test",
            DataQuality.Exact);

    private static UsageObservation CreateWindowObservation(
        string provider,
        decimal value,
        DateTimeOffset observedAt,
        string source,
        TimeSpan duration,
        string? anonymousSessionId = null) =>
        new(
            Guid.NewGuid(),
            provider,
            provider + ":default",
            UsageCapability.QuotaWindow,
            value,
            "percent",
            observedAt,
            source,
            DataQuality.Exact,
            new QuotaWindow(observedAt.Subtract(duration / 2), observedAt.Add(duration / 2)),
            anonymousSessionId: anonymousSessionId);

    private sealed class SilentAdapter(string providerId) : IUsageAdapter
    {
        public AdapterDescriptor Descriptor { get; } = new(
            providerId,
            providerId,
            new HashSet<UsageCapability> { UsageCapability.QuotaWindow },
            new FreshnessPolicy(TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5)));

        public async IAsyncEnumerable<AdapterEvent> WatchAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            yield break;
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
