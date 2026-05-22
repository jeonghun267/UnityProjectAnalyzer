using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace UnityProjectAnalyzer.Converters;

/// <summary>
/// nav 배지 스타일 ("danger"/"warn"/"info") → 색상
/// </summary>
public class BadgeStyleToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value?.ToString() switch
        {
            "warn"   => new SolidColorBrush(Color.FromRgb(0xd2, 0x99, 0x22)), // #d29922
            "info"   => new SolidColorBrush(Color.FromRgb(0x58, 0xa6, 0xff)), // #58a6ff
            _        => new SolidColorBrush(Color.FromRgb(0xf8, 0x51, 0x49))  // danger #f85149
        };
    }
    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// "ERR" / "WARN" / "INFO" 문자열 → 색상
/// </summary>
public class SeverityToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value?.ToString() switch
        {
            "ERR"  => new SolidColorBrush(Color.FromRgb(0xf8, 0x51, 0x49)), // #f85149
            "WARN" => new SolidColorBrush(Color.FromRgb(0xd2, 0x99, 0x22)), // #d29922
            "INFO" => new SolidColorBrush(Color.FromRgb(0x58, 0xa6, 0xff)), // #58a6ff
            "OK"   => new SolidColorBrush(Color.FromRgb(0x3f, 0xb9, 0x50)), // #3fb950
            _      => new SolidColorBrush(Color.FromRgb(0x8b, 0x94, 0x9e))  // #8b949e
        };
    }
    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// 채팅 메시지 role → 정렬 방향
/// </summary>
public class RoleToAlignmentConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value?.ToString() == "user"
            ? System.Windows.HorizontalAlignment.Right
            : System.Windows.HorizontalAlignment.Left;
    }
    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// 채팅 메시지 role → 배경색
/// </summary>
public class RoleToBackgroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value?.ToString() == "user"
            ? new SolidColorBrush(Color.FromArgb(0x33, 0x58, 0xa6, 0xff))   // 채팅 user 말풍선 (GitHub blue 33%)
            : new SolidColorBrush(Color.FromRgb(0x21, 0x26, 0x2d));         // 채팅 ai 말풍선 (Surface2)
    }
    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// bool → Visibility
/// </summary>
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        var b = value is bool boolean && boolean;
        return b ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    }
    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// bool 반전
/// </summary>
public class InvertBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        return !(value is bool b && b);
    }
    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        return !(value is bool b && b);
    }
}

/// <summary>
/// bool 반전 → Visibility (false면 Visible, true면 Collapsed). 빈 상태 표시용
/// </summary>
public class InvertBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        var b = value is bool boolean && boolean;
        return b ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
    }
    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
