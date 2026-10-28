using System.Windows.Media;

namespace EasyStart;

/// <summary>
/// 主题调色板（辉哥哥：深色模式要全量适配，不只背景）。
/// 每套主题给：窗口背景 / 主文字 / 次文字 / 强调色 / 悬停 / 卡片边框 / 深色标记。
/// </summary>
public sealed record ThemePalette(
    string Name,
    Color WindowBg,
    Color TextPrimary,
    Color TextSecondary,
    Color Accent,
    Color Hover,
    Color CardBorder,
    bool IsDark)
{
    public static ThemePalette Get(string theme) => theme switch
    {
        "MicaDark" or "SolidDark" => new ThemePalette(
            theme,
            Color.FromRgb(0x1B, 0x1B, 0x1F),   // 窗底
            Color.FromRgb(0xE8, 0xE8, 0xEC),   // 主文字
            Color.FromRgb(0x9E, 0x9E, 0xA8),   // 次文字
            Color.FromRgb(0x4D, 0xB8, 0xFF),   // 强调（浅蓝，深底可见）
            Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF), // 悬停（白 16%）
            Color.FromRgb(0x3A, 0x3A, 0x42),   // 卡片边框
            true),
        "Acrylic" => new ThemePalette(
            theme,
            Color.FromArgb(0xB4, 0xF3, 0xF3, 0xF3), // 窗底（半透明）
            Color.FromRgb(0x20, 0x20, 0x20),
            Color.FromRgb(0x5A, 0x5A, 0x5A),
            Color.FromRgb(0x1A, 0x6C, 0xD8),
            Color.FromArgb(0x28, 0x1A, 0x6C, 0xD8),
            Color.FromRgb(0xD0, 0xD0, 0xD0),
            false),
        _ => new ThemePalette( // MicaLight / SolidLight / 默认
            theme,
            Color.FromRgb(0xF3, 0xF3, 0xF3),
            Color.FromRgb(0x20, 0x20, 0x20),
            Color.FromRgb(0x5A, 0x5A, 0x5A),
            Color.FromRgb(0x1A, 0x6C, 0xD8),
            Color.FromArgb(0x28, 0x1A, 0x6C, 0xD8),
            Color.FromRgb(0xE0, 0xE0, 0xE0),
            false),
    };

    public SolidColorBrush Bg() => new(WindowBg);
    public SolidColorBrush Text() => new(TextPrimary);
    public SolidColorBrush TextSec() => new(TextSecondary);
    public SolidColorBrush AccentBrush() => new(Accent);
    public SolidColorBrush HoverBrush() => new(Hover);
    public SolidColorBrush CardBorderBrush() => new(CardBorder);
}
