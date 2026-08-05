using System.Windows;
using System.Windows.Media;
using AIVitals.Application;
using Microsoft.Win32;
using MediaColor = System.Windows.Media.Color;
using MediaColorConverter = System.Windows.Media.ColorConverter;
using WpfSystemColors = System.Windows.SystemColors;

namespace AIVitals.App;

internal static class WindowsAppearance
{
    public static bool MotionEnabled { get; private set; }

    public static void Apply(AppPreferences preferences)
    {
        var resources = System.Windows.Application.Current.Resources;
        foreach (var pair in UiLanguageCatalog.For(preferences.Language))
            resources[pair.Key] = pair.Value;

        var highContrast = SystemParameters.HighContrast;
        var light = !highContrast && ResolveLightTheme(preferences.Theme);
        if (highContrast)
        {
            Set(resources, "CanvasBrush", WpfSystemColors.WindowColor);
            Set(resources, "SidebarBrush", WpfSystemColors.WindowColor);
            Set(resources, "TopBarBrush", WpfSystemColors.WindowColor);
            Set(resources, "PanelBrush", WpfSystemColors.ControlColor);
            Set(resources, "PanelAltBrush", WpfSystemColors.ControlColor);
            Set(resources, "SurfaceBrush", WpfSystemColors.ControlColor);
            Set(resources, "ElevatedBrush", WpfSystemColors.ControlColor);
            Set(resources, "SelectionBrush", WpfSystemColors.HighlightColor);
            Set(resources, "NavSelectedBrush", WpfSystemColors.ControlColor);
            Set(resources, "WarmSurfaceBrush", WpfSystemColors.ControlColor);
            Set(resources, "WidgetGlassBrush", WpfSystemColors.WindowColor);
            Set(resources, "WidgetSurfaceBrush", WpfSystemColors.ControlColor);
            Set(resources, "ScrollThumbBrush", WpfSystemColors.GrayTextColor);
            Set(resources, "LineBrush", WpfSystemColors.WindowTextColor);
            Set(resources, "TextBrush", WpfSystemColors.WindowTextColor);
            Set(resources, "AccentTextBrush", WpfSystemColors.HighlightTextColor);
            Set(resources, "MutedTextBrush", WpfSystemColors.GrayTextColor);
            Set(resources, "SignalBrush", WpfSystemColors.HighlightColor);
            Set(resources, "CodexBrush", WpfSystemColors.HighlightColor);
            Set(resources, "CodexSurfaceBrush", WpfSystemColors.ControlColor);
            Set(resources, "WarmBrush", WpfSystemColors.HotTrackColor);
            Set(resources, "ClaudeBrandBrush", WpfSystemColors.HotTrackColor);
            Set(resources, "ButtonBrush", WpfSystemColors.ControlColor);
            Set(resources, "DangerBrush", WpfSystemColors.HighlightColor);
        }
        else if (light)
        {
            Set(resources, "CanvasBrush", "#EEF3F8");
            Set(resources, "SidebarBrush", "#E6EDF5");
            Set(resources, "TopBarBrush", "#F8FAFD");
            Set(resources, "PanelBrush", "#FFFFFF");
            Set(resources, "PanelAltBrush", "#F7FAFD");
            Set(resources, "SurfaceBrush", "#E8EFF6");
            Set(resources, "ElevatedBrush", "#DCE7F1");
            Set(resources, "SelectionBrush", "#CCEAE7");
            Set(resources, "NavSelectedBrush", "#DCE6F0");
            Set(resources, "WarmSurfaceBrush", "#F8E8D2");
            Set(resources, "WidgetGlassBrush", "#D9F7FAFD");
            Set(resources, "WidgetSurfaceBrush", "#A6DCE7F1");
            Set(resources, "ScrollThumbBrush", "#718397");
            Set(resources, "LineBrush", "#B9C7D6");
            Set(resources, "TextBrush", "#132033");
            Set(resources, "MutedTextBrush", "#526277");
            Set(resources, "AccentTextBrush", "#06151B");
            Set(resources, "SignalBrush", "#087F78");
            Set(resources, "CodexBrush", "#1769D2");
            Set(resources, "CodexSurfaceBrush", "#DCEAFF");
            Set(resources, "WarmBrush", "#965300");
            Set(resources, "ClaudeBrandBrush", "#A94F36");
            Set(resources, "ButtonBrush", "#DDE7F1");
            Set(resources, "DangerBrush", "#A33141");
        }
        else
        {
            Set(resources, "CanvasBrush", "#07111F");
            Set(resources, "SidebarBrush", "#091524");
            Set(resources, "TopBarBrush", "#07101B");
            Set(resources, "PanelBrush", "#0D1B2B");
            Set(resources, "PanelAltBrush", "#0A1726");
            Set(resources, "SurfaceBrush", "#122238");
            Set(resources, "ElevatedBrush", "#172A42");
            Set(resources, "SelectionBrush", "#163B55");
            Set(resources, "NavSelectedBrush", "#122033");
            Set(resources, "WarmSurfaceBrush", "#352719");
            Set(resources, "WidgetGlassBrush", "#B80A1726");
            Set(resources, "WidgetSurfaceBrush", "#681A2B3D");
            Set(resources, "ScrollThumbBrush", "#53677C");
            Set(resources, "LineBrush", "#263B53");
            Set(resources, "TextBrush", "#F5F8FC");
            Set(resources, "MutedTextBrush", "#B7C4D3");
            Set(resources, "AccentTextBrush", "#06151B");
            Set(resources, "SignalBrush", "#31D6C6");
            Set(resources, "CodexBrush", "#3D8BFF");
            Set(resources, "CodexSurfaceBrush", "#102A4A");
            Set(resources, "WarmBrush", "#FF9B2F");
            Set(resources, "ClaudeBrandBrush", "#D97757");
            Set(resources, "ButtonBrush", "#14263C");
            Set(resources, "DangerBrush", "#FF9B9B");
        }

        MotionEnabled = !highContrast && SystemParameters.ClientAreaAnimation;
        resources["HighContrastStatus"] = highContrast
            ? UiLanguageCatalog.Get(preferences.Language, "HighContrastActive")
            : UiLanguageCatalog.Get(preferences.Language, light ? "LightTheme" : "DarkTheme");
    }

    private static bool ResolveLightTheme(string? theme)
    {
        if (theme?.Equals("Light", StringComparison.OrdinalIgnoreCase) == true) return true;
        if (theme?.Equals("Dark", StringComparison.OrdinalIgnoreCase) == true) return false;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value != 0;
        }
        catch
        {
            return false;
        }
    }

    private static void Set(ResourceDictionary resources, string key, string color) =>
        Set(resources, key, (MediaColor)MediaColorConverter.ConvertFromString(color));

    private static void Set(ResourceDictionary resources, string key, MediaColor color)
    {
        if (resources[key] is SolidColorBrush { IsFrozen: false } brush)
            brush.Color = color;
        else
            resources[key] = new SolidColorBrush(color);
    }
}
