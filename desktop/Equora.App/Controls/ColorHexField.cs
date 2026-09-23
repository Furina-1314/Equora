using Equora.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Equora.App.Controls;

/// <summary>
/// 色号输入行:文本框 + 36×36 预览方块。方块实时反映输入的色号,点击弹出选色表
/// (WinUI ColorPicker),在选色表中选色会回填文本框。样式与设置页主题色一致。
/// </summary>
internal static class ColorHexField
{
    public static (TextBox Box, FrameworkElement Row) Create(string header, string initial)
    {
        var box = new TextBox { Header = header, Text = initial, MaxLength = 7 };
        // 方角、无边框的正方形:边长始终等于文本框(仅输入框,不含标题)的实际高度。
        var preview = new Border
        {
            Width = 32, Height = 32, VerticalAlignment = VerticalAlignment.Bottom,
            Background = new SolidColorBrush(Color.FromArgb(255, 211, 211, 211))
        };
        box.SizeChanged += (_, _) =>
        {
            if (box.ActualHeight > 0) preview.Width = preview.Height = box.ActualHeight;
        };
        void Sync()
        {
            preview.Background = Appearance.TryColor(box.Text.Trim(), out var color)
                ? new SolidColorBrush(color)
                : new SolidColorBrush(Color.FromArgb(255, 211, 211, 211));
        }
        box.TextChanged += (_, _) => Sync();
        var picker = new ColorPicker { IsAlphaEnabled = false, IsColorSliderVisible = true, MinWidth = 260 };
        var flyout = new Flyout { Content = picker };
        preview.Tapped += (_, _) =>
        {
            if (Appearance.TryColor(box.Text.Trim(), out var color)) picker.Color = color;
            flyout.ShowAt(preview);
        };
        picker.ColorChanged += (_, e) =>
            box.Text = $"#{e.NewColor.R:X2}{e.NewColor.G:X2}{e.NewColor.B:X2}";
        Sync();
        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(box, 0);
        Grid.SetColumn(preview, 1);
        row.Children.Add(box);
        row.Children.Add(preview);
        return (box, row);
    }
}
