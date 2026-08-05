using AIVitals.Application;

namespace AIVitals.App;

internal static class WidgetGeometry
{
    public static (double Width, double Height) Calculate(WidgetViewModel viewModel, WidgetPreferences preferences)
    {
        var count = Math.Clamp(viewModel.Connections.Count, 1, 4);
        return preferences.Mode switch
        {
            WidgetVisualMode.Rings when count <= 1 => (146, 178),
            WidgetVisualMode.Rings when count <= 2 => (274, 178),
            WidgetVisualMode.Rings => (274, 294),
            WidgetVisualMode.HorizontalBars => (420, 58 + viewModel.TotalBandCount * 17 + count * 4),
            WidgetVisualMode.VerticalBars => (Math.Max(104, 34 + viewModel.VerticalContentWidth), 420),
            _ => (274, 178)
        };
    }
}
