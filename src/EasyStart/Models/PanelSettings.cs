namespace EasyStart.Models;

/// <summary>面板设置（对齐 Rolan 5 实测 settings 字段）</summary>
public class PanelSettings
{
    /// <summary>开机自启（Rolan autoStart 实测 true）</summary>
    public bool AutoStart { get; set; }

    /// <summary>窗口背景：Mica / Acrylic / Solid（Rolan windowBackdrop 实测）</summary>
    public string WindowBackdrop { get; set; } = "Mica";

    /// <summary>图标尺寸档位 0-3（Rolan itemIconSize 实测 1）</summary>
    public int ItemIconSize { get; set; } = 1;

    /// <summary>呼出热键（Rolan toggleHotkey 实测 "Alt + Space"）</summary>
    public string ToggleHotkey { get; set; } = "Alt + Space";

    /// <summary>鼠标中键呼出（Rolan showOnMousePressL 实测存在，默认关防误触）</summary>
    public bool ShowOnMiddleClick { get; set; }

    /// <summary>失焦自动隐藏（Rolan hideOnSmartDetect 实测）</summary>
    public bool HideOnSmartDetect { get; set; } = true;

    /// <summary>按住 Ctrl 时不隐藏（Rolan preventHideOnCtrlHold 实测）</summary>
    public bool PreventHideOnCtrlHold { get; set; } = true;

    /// <summary>悬停切换标签（Rolan switchTabsOnHover 实测）</summary>
    public bool SwitchTabsOnHover { get; set; }

    /// <summary>悬停切换标签的停留时间 ms</summary>
    public int HoverSwitchDelayMs { get; set; } = 300;

    /// <summary>滚轮切换标签（Rolan switchTabsOnHover + wheelScroll 实测）</summary>
    public bool SwitchTabsOnWheel { get; set; } = true;

    /// <summary>滚轮循环切换（Rolan circulate 实测）</summary>
    public bool WheelCirculate { get; set; } = true;

    /// <summary>数字键切换标签/启动项（Rolan switchNumberPress 实测 true）</summary>
    public bool NumberKeyLaunch { get; set; } = true;

    /// <summary>边缘热区唤醒（P2，默认关）</summary>
    public bool EdgeZoneWake { get; set; }

    /// <summary>主题名：纯色深色 / 纯色浅色（辉哥哥：只要这两套，默认浅色）</summary>
    public string Theme { get; set; } = "SolidLight";

    /// <summary>整体不透明度 0.30-1.00</summary>
    public double Opacity { get; set; } = 1.0;

    /// <summary>端口自包含模式（设置页开关）</summary>
    public bool Portable { get; set; }

    /// <summary>上次备份存放文件夹（备份/恢复选择器默认开在这里；辉哥哥 2026-10-28：下次记住）</summary>
    public string? LastBackupFolder { get; set; }
}
