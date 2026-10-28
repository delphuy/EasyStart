using System.Windows;
using System.Windows.Controls;

namespace EasyStart.Panel;

/// <summary>
/// 弹窗主题直注（辉哥哥 2026-10-28 反馈：弹窗背景像透明，面板内容透出来干扰弹窗）。
/// 根因：SystemControl* 键在 ModernWPF 字典里解析到的是近透明色，弹窗 Border
/// Background 等于没画。修法：面板同款 ThemePalette 实心色——①窗级资源注入
/// （DynamicResource 就近命中）；②关键底板 Border 直接属性赋值，双保险。
/// </summary>
internal static class DialogTheme
{
    public static void Apply(Window w, string theme)
    {
        var pal = ThemePalette.Get(theme);
        w.Resources["SystemControlBackgroundLightBrush"] = pal.Bg();
        w.Resources["SystemControlForegroundBaseHighBrush"] = pal.Text();
        w.Resources["SystemControlForegroundBaseMediumBrush"] = pal.TextSec();
        w.Resources["SystemControlForegroundBaseLowBrush"] = pal.CardBorderBrush();

        // 双保险：底板直接赋值（不依赖资源链，必定生效）
        if (w.FindName("DialogRoot") is Border root)
        {
            root.Background = pal.Bg();
            root.BorderBrush = pal.CardBorderBrush();
        }
    }
}
