using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace EasyStart.Panel;

/// <summary>
/// 轻量主题化确认框（P2 批量删除前确认，辉哥哥 2026-10-28 定的口径）。
/// 跟应用同款圆角无边框 + ModernWPF 动态资源，替代系统 MessageBox。
/// </summary>
internal static class MiniDialogs
{
    /// <summary>确定返回 true；取消/关闭/按 Esc 返回 false。theme 来自设置，弹窗与面板同款实心底</summary>
    public static bool Confirm(Window owner, string title, string message, string okText = "确定", string theme = "")
    {
        var result = false;
        var pal = ThemePalette.Get(theme);

        var dlg = new Window
        {
            Title = title,
            Width = 400,
            Height = 200,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = owner,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
        };

        // 辉哥哥 2026-10-28：SystemControl* 键解析到透明色 → 面板内容透进弹窗。
        // 改为面板同款 ThemePalette 实心色 + 投影，与大弹窗一致。
        var root = new Border
        {
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            Background = pal.Bg(),
            BorderBrush = pal.CardBorderBrush(),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 18,
                ShadowDepth = 3,
                Opacity = 0.35,
            },
        };
        var fg = pal.Text();

        var grid = new Grid { Margin = new Thickness(22) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // fg 已由上方 pal.Text() 提供

        var titleText = new TextBlock
        {
            Text = title,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = fg,
        };
        Grid.SetRow(titleText, 0);

        var msg = new TextBlock
        {
            Text = message,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 12, 0, 12),
            Foreground = fg,
        };
        Grid.SetRow(msg, 1);

        var btns = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        Grid.SetRow(btns, 2);

        var cancel = new Button { Content = "取消", Width = 84, Height = 32, Margin = new Thickness(0, 0, 8, 0) };
        var ok = new Button
        {
            Content = okText,
            Width = 84,
            Height = 32,
            Background = Brushes.IndianRed,
            Foreground = Brushes.White,
        };
        cancel.Click += (_, _) => { result = false; dlg.Close(); };
        ok.Click += (_, _) => { result = true; dlg.Close(); };
        btns.Children.Add(cancel);
        btns.Children.Add(ok);

        grid.Children.Add(titleText);
        grid.Children.Add(msg);
        grid.Children.Add(btns);
        root.Child = grid;
        // 外层留 12px 给投影，和大弹窗同款层次
        dlg.Content = new Grid { Margin = new Thickness(12), Children = { root } };
        dlg.KeyDown += (_, e) => { if (e.Key == Key.Escape) { result = false; dlg.Close(); } };

        dlg.ShowDialog();
        return result;
    }
}
