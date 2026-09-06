using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Dsh.Converter;

/// <summary>bool 取反后转 Visibility（true → Collapsed）</summary>
public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Visibility.Visible;
}

/// <summary>字符串非空转 Visibility（空/null → Collapsed）。
/// 用于更新状态文本等"有内容才占位"的场景，避免检查结束后结果被 IsBusy 联动隐藏</summary>
public class StringNotEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
