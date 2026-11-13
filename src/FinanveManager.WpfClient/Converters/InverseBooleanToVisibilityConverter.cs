using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace FinanveManager.WpfClient.Converters;

/// <summary>true → Collapsed, false → Visible. Удобно для парных кнопок формы.</summary>
public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool flag && flag ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility visibility && visibility != Visibility.Visible;
}
