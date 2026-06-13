using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace RfidScanner.Converters;

public class RssiToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not int rssi)
            return new SolidColorBrush(Color.FromRgb(160, 160, 160));

        // Stronger signal (less negative) = greener
        var brush = rssi switch
        {
            >= -40 => Color.FromRgb(76, 175, 80),
            >= -55 => Color.FromRgb(139, 195, 74),
            >= -70 => Color.FromRgb(255, 193, 7),
            >= -85 => Color.FromRgb(255, 152, 0),
            _ => Color.FromRgb(244, 67, 54)
        };

        return new SolidColorBrush(brush);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
