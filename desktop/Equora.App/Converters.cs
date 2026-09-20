using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Equora.App.NativeInterop;
using TaskStatus = Equora.App.NativeInterop.TaskStatus;

namespace Equora.App;

/// <summary>状态枚举 → 勾选(完成/取消视为勾选)。</summary>
public sealed class DoneStatusConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is TaskStatus.Done;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>截止时间 → 短文本;空为空串。</summary>
public sealed class DueTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is DateTimeOffset due ? due.ToLocalTime().ToString("MM-dd HH:mm") : "";

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>优先级 → 左侧色条(非颜色冗余:列表同时显示文本)。</summary>
public sealed class PriorityBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush None = new(Microsoft.UI.Colors.Transparent);
    private static readonly SolidColorBrush Low = new(Microsoft.UI.Colors.Gray);
    private static readonly SolidColorBrush Normal = new(Microsoft.UI.Colors.DarkSlateGray);
    private static readonly SolidColorBrush High = new(Microsoft.UI.Colors.DarkOrange);
    private static readonly SolidColorBrush Urgent = new(Microsoft.UI.Colors.OrangeRed);

    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is Priority p
            ? p switch
            {
                Priority.Low => Low,
                Priority.Normal => Normal,
                Priority.High => High,
                Priority.Urgent => Urgent,
                _ => None,
            }
            : None;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

public sealed class TrueToVisibleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

public sealed class FalseToVisibleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is false ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>详情加载状态 → 面板可见性。</summary>
public sealed class BoolToVisibleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>未加载 → 可见(占位提示用)。</summary>
public sealed class BoolToInvisibleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>非空字符串 → 可见。</summary>
public sealed class NonEmptyToVisibleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is string s && s.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>bool 反转可见性(未开始配置区)。</summary>
public sealed class FalseToVisibleXConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is false ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>bool 可见性(运行控制区)。</summary>
public sealed class TrueToVisibleConverter2 : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
