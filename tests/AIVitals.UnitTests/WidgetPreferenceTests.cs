using AIVitals.Application;

namespace AIVitals.UnitTests;

public sealed class WidgetPreferenceTests
{
    [Fact]
    public void Defaults_pin_both_current_providers()
    {
        var preferences = new AppPreferences().EffectiveWidget;

        Assert.Equal(["codex", "claude-code"], preferences.PinnedProviderIds!);
        Assert.True(preferences.IsVisible);
        Assert.Equal(WidgetVisualMode.Rings, preferences.Mode);
    }

    [Fact]
    public void Normalization_limits_connections_and_makes_click_through_recoverable()
    {
        var normalized = WidgetPreferenceRules.Normalize(new WidgetPreferences(
            IsLocked: false,
            IsClickThrough: true,
            PinnedProviderIds: ["codex", "CODEX", "claude-code", "third", "fourth", "fifth"]));

        Assert.True(normalized.IsLocked);
        Assert.Equal(["codex", "claude-code", "third", "fourth"], normalized.PinnedProviderIds!);
    }

    [Fact]
    public void Placement_snaps_to_edges_and_stays_inside_the_selected_monitor()
    {
        WidgetBounds[] monitors =
        [
            new(0, 0, 1920, 1040),
            new(1920, 0, 2560, 1400)
        ];

        var placement = WidgetPlacement.Normalize(4231, 1387, 240, 160, monitors);

        Assert.Equal(4240, placement.Left);
        Assert.Equal(1240, placement.Top);
    }

    [Fact]
    public void Placement_recovers_from_a_disconnected_monitor()
    {
        WidgetBounds[] remainingMonitor = [new(0, 0, 1920, 1040)];

        var placement = WidgetPlacement.Normalize(4000, 200, 240, 160, remainingMonitor);

        Assert.Equal(1680, placement.Left);
        Assert.Equal(200, placement.Top);
    }

    [Fact]
    public void Placement_can_move_to_the_monitor_containing_the_pointer()
    {
        WidgetBounds[] monitors =
        [
            new(-1920, 0, 1920, 1040),
            new(0, 0, 2560, 1400)
        ];

        var placement = WidgetPlacement.MoveToMonitor(
            pointerX: 800,
            pointerY: 500,
            width: 340,
            height: 230,
            monitors);

        Assert.Equal(2196, placement.Left);
        Assert.Equal(24, placement.Top);
    }
}
