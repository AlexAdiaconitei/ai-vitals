using System.Globalization;

namespace AIVitals.Application;

public static class ObservationValueFormatter
{
    public static string Format(decimal? value, string unit, string language)
    {
        if (value is null) return "—";
        var culture = CultureInfo.GetCultureInfo(
            language.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? "en-US" : "es-ES");
        return unit.Trim().ToLowerInvariant() switch
        {
            "milliseconds" => FormatDuration(value.Value, culture),
            "percent" => $"{value.Value.ToString("0.#", culture)}%",
            "tokens" => $"{value.Value.ToString("N0", culture)} tokens",
            "usd" => $"{value.Value.ToString("0.####", culture)} USD",
            _ => $"{value.Value.ToString("0.####", culture)} {unit}"
        };
    }

    private static string FormatDuration(decimal milliseconds, CultureInfo culture)
    {
        var duration = TimeSpan.FromMilliseconds((double)Math.Max(0, milliseconds));
        if (duration.TotalHours >= 1)
            return $"{(int)duration.TotalHours} h {duration.Minutes:00} min";
        if (duration.TotalMinutes >= 1)
            return $"{(int)duration.TotalMinutes} min {duration.Seconds:00} s";
        return $"{duration.TotalSeconds.ToString("0.#", culture)} s";
    }
}
