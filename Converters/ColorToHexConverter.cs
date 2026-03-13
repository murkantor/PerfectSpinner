using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace GoldenSpinner.Converters
{
    /// <summary>
    /// Two-way converter between a CSS hex colour string (e.g. "#00B140") and
    /// an <see cref="Avalonia.Media.Color"/> struct.
    ///
    /// Convert  (string  → Color): parses the hex string; alpha is forced to 255
    ///   so the ColorView never shows a partial-transparency state.
    /// ConvertBack (Color → string): formats as #RRGGBB, discarding alpha so
    ///   the stored value always stays an opaque RGB hex.
    /// </summary>
    public class ColorToHexConverter : IValueConverter
    {
        public static readonly ColorToHexConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string hex)
            {
                try
                {
                    var c = Color.Parse(hex);
                    return new Color(255, c.R, c.G, c.B);   // force full opacity
                }
                catch { /* fall through to default */ }
            }

            return new Color(255, 0, 177, 64);   // broadcast-safe green fallback
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is Color color)
                return $"#{color.R:X2}{color.G:X2}{color.B:X2}";

            return "#00B140";
        }
    }
}
