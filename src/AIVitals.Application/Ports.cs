using AIVitals.Domain;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace AIVitals.Application;

public sealed record AppPreferences(
    int SchemaVersion = AppPreferences.CurrentSchemaVersion,
    bool StartMinimized = true,
    string Theme = "System",
    string Language = "en",
    bool FakeAdapterEnabled = true,
    WidgetPreferences? Widget = null,
    bool OnboardingCompleted = false,
    bool AutomaticUpdateCheckEnabled = true,
    bool StartWithWindows = false)
{
    /// <summary>Version 2 added the update and startup preferences; version 1 files upgrade with their defaults.</summary>
    public const int CurrentSchemaVersion = 2;

    public WidgetPreferences EffectiveWidget => WidgetPreferenceRules.Normalize(Widget ?? new WidgetPreferences());
}

public enum WidgetVisualMode
{
    Rings,
    HorizontalBars,
    VerticalBars
}

public sealed record WidgetPreferences(
    bool IsVisible = true,
    WidgetVisualMode Mode = WidgetVisualMode.Rings,
    bool IsLocked = false,
    bool IsClickThrough = false,
    double? Left = null,
    double? Top = null,
    string[]? PinnedProviderIds = null);

public static class WidgetPreferenceRules
{
    private static readonly string[] DefaultProviders = ["codex", "claude-code"];

    public static WidgetPreferences Normalize(WidgetPreferences preferences)
    {
        var providers = (preferences.PinnedProviderIds ?? DefaultProviders)
            .Where(provider => !string.IsNullOrWhiteSpace(provider))
            .Select(provider => provider.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .ToArray();
        if (providers.Length == 0) providers = [DefaultProviders[0]];

        return preferences with
        {
            IsLocked = preferences.IsLocked || preferences.IsClickThrough,
            PinnedProviderIds = providers
        };
    }
}

public readonly record struct WidgetBounds(double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;
    public double Bottom => Top + Height;
}

public static class WidgetPlacement
{
    public static WidgetBounds MoveToMonitor(
        double pointerX,
        double pointerY,
        double width,
        double height,
        IReadOnlyList<WidgetBounds> workAreas)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (workAreas.Count == 0) return new WidgetBounds(0, 0, width, height);

        var target = workAreas.FirstOrDefault(area =>
            pointerX >= area.Left && pointerX < area.Right &&
            pointerY >= area.Top && pointerY < area.Bottom);
        if (target.Width <= 0 || target.Height <= 0) target = workAreas[0];

        return Normalize(
            target.Right - width - 24,
            target.Top + 24,
            width,
            height,
            [target]);
    }

    public static WidgetBounds Normalize(
        double? requestedLeft,
        double? requestedTop,
        double width,
        double height,
        IReadOnlyList<WidgetBounds> workAreas,
        double snapDistance = 12)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (workAreas.Count == 0) return new WidgetBounds(0, 0, width, height);

        var left = requestedLeft ?? workAreas[0].Right - width - 24;
        var top = requestedTop ?? workAreas[0].Top + 24;
        var desired = new WidgetBounds(left, top, width, height);
        var target = workAreas
            .OrderByDescending(area => IntersectionArea(desired, area))
            .First();
        if (IntersectionArea(desired, target) <= 0) target = workAreas[0];

        left = Math.Clamp(left, target.Left, Math.Max(target.Left, target.Right - width));
        top = Math.Clamp(top, target.Top, Math.Max(target.Top, target.Bottom - height));

        if (Math.Abs(left - target.Left) <= snapDistance) left = target.Left;
        if (Math.Abs(left + width - target.Right) <= snapDistance) left = target.Right - width;
        if (Math.Abs(top - target.Top) <= snapDistance) top = target.Top;
        if (Math.Abs(top + height - target.Bottom) <= snapDistance) top = target.Bottom - height;

        return new WidgetBounds(left, top, width, height);
    }

    private static double IntersectionArea(WidgetBounds first, WidgetBounds second)
    {
        var width = Math.Max(0, Math.Min(first.Right, second.Right) - Math.Max(first.Left, second.Left));
        var height = Math.Max(0, Math.Min(first.Bottom, second.Bottom) - Math.Max(first.Top, second.Top));
        return width * height;
    }
}

public interface IObservationRepository
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task AppendAsync(UsageObservation observation, CancellationToken cancellationToken = default);
    Task<UsageObservation?> GetLatestAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<string, UsageObservation>> GetLatestByProviderAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UsageObservation>> QueryAsync(ObservationQuery query, CancellationToken cancellationToken = default);
    Task<int> DeleteAsync(ObservationQuery query, CancellationToken cancellationToken = default);
}

public sealed record ObservationQuery(
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? ToUtc = null,
    string? ProviderId = null,
    UsageCapability? Capability = null,
    int Limit = 1000)
{
    public ObservationQuery Normalize() => this with
    {
        FromUtc = FromUtc?.ToUniversalTime(),
        ToUtc = ToUtc?.ToUniversalTime(),
        ProviderId = string.IsNullOrWhiteSpace(ProviderId) ? null : ProviderId.Trim(),
        Limit = Math.Clamp(Limit, 1, 10_000)
    };
}

public static class ObservationExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string ToCsv(IEnumerable<UsageObservation> observations)
    {
        var builder = new StringBuilder();
        builder.AppendLine("observedAtUtc,providerId,connectionId,capability,value,unit,quality,windowStartsAtUtc,windowResetsAtUtc,model,anonymousSessionId,source");
        foreach (var item in observations)
        {
            string?[] fields =
            [
                item.ObservedAtUtc.ToString("O"), item.ProviderId, item.ConnectionId, item.Capability.ToString(),
                item.Value?.ToString(CultureInfo.InvariantCulture), item.Unit, item.Quality.ToString(),
                item.Window?.StartsAtUtc.ToString("O"), item.Window?.ResetsAtUtc?.ToString("O"),
                item.Model, item.AnonymousSessionId, item.Source
            ];
            builder.AppendLine(string.Join(',', fields.Select(EscapeCsv)));
        }
        return builder.ToString();
    }

    public static string ToJson(IEnumerable<UsageObservation> observations) =>
        JsonSerializer.Serialize(observations.Select(item => new
        {
            item.ObservedAtUtc,
            item.ProviderId,
            item.ConnectionId,
            Capability = item.Capability.ToString(),
            item.Value,
            item.Unit,
            Quality = item.Quality.ToString(),
            WindowStartsAtUtc = item.Window?.StartsAtUtc,
            WindowResetsAtUtc = item.Window?.ResetsAtUtc,
            item.Model,
            item.AnonymousSessionId,
            item.Source
        }), JsonOptions);

    private static string EscapeCsv(string? value)
    {
        if (value is null) return string.Empty;
        return value.IndexOfAny([',', '"', '\r', '\n']) < 0
            ? value
            : $"\"{value.Replace("\"", "\"\"")}\"";
    }
}

public interface IAppPreferencesStore
{
    Task<AppPreferences> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(AppPreferences preferences, CancellationToken cancellationToken = default);
}
