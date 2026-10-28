using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using EasyStart.Models;
using EasyStart.Panel;

namespace EasyStart;

/// <summary>
/// 主题应用（P1-F15）：把 ThemePalettes 调色板套到面板窗口 + 面板内控件。
/// Win11 22H2+ 走 DWM Mica/Acrylic 材质；老系统降级纯色。
/// 深色主题下文字/页签/hover/卡片边框全量适配（辉哥哥：深色模式图标没适配）。
/// </summary>
public static class ThemeService
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const int DWMSBT_DISABLE = 1;
    private const int DWMSBT_MAINWINDOW = 2;      // Mica
    private const int DWMSBT_TRANSIENTWINDOW = 3; // Acrylic
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;

    public static void Apply(PanelSettings s, PanelWindow panel)
    {
        var win = panel;
        var pal = ThemePalette.Get(s.Theme);
        var handle = new WindowInteropHelper(win).EnsureHandle();
        if (handle != IntPtr.Zero)
        {
            int corner = DWMWCP_ROUND;
            DwmSetWindowAttribute(handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));

            // 辉哥哥：主题只要 纯色浅色/纯色深色 两套 → 禁用 DWM 背景层，
            // 纯色底 + WPF Opacity，不透明度 0.30-1.0 全范围连续可见
            int backdrop = DWMSBT_DISABLE;
            DwmSetWindowAttribute(handle, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));
        }

        // 窗口背景 = 主题纯色底
        win.Background = pal.Bg();
        // 不透明度（0.30-1.0 全范围连续）
        win.Opacity = Math.Clamp(s.Opacity, 0.30, 1.0);

        // 面板内固定文字（标题区）
        if (panel.TitleLabel != null)
        {
            panel.TitleLabel.Foreground = pal.Text();
        }
        if (panel.TitleHint != null)
        {
            panel.TitleHint.Foreground = pal.TextSec();
        }
        // 重新渲染页签条 + 图标网格，让新调色板作用到所有控件
        panel.RenderThemeApplied();
    }
}
