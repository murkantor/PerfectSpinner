using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace GoldenSpinner.Converters
{
    /// <summary>
    /// Converts a CSS hex color string (e.g. "#E74C3C") to an Avalonia <see cref="SolidColorBrush"/>.
    /// Returns a gray brush if the value is null, empty, or unparseable.
    /// </summary>
    public sealed class HexColorToBrushConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string hex && !string.IsNullOrWhiteSpace(hex))
            {
                try { return new SolidColorBrush(Color.Parse(hex)); }
                catch { /* fall through */ }
            }
            return new SolidColorBrush(Colors.Gray);
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
