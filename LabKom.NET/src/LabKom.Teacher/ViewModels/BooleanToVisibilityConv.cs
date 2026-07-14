using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace LabKom.Teacher.ViewModels;

/// <summary>
/// Converter sederhana: bool true → Visible, false → Collapsed.
/// Diakses lewat x:Static binding di XAML.
/// </summary>
public class BooleanToVisibilityConv : IValueConverter
{
    public static readonly BooleanToVisibilityConv Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Visibility.Visible;
}
