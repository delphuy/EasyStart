using System.Text.Json;
using System.Text.Json.Serialization;

namespace EasyStart.Models;

/// <summary>全局缓存（对齐 Rolan cache/misc.ini：LastVersion/CurrentProfile/mp）</summary>
public class GlobalMisc
{
    public string LastVersion { get; set; } = "2.0.0";
    public string? CurrentProfile { get; set; }
}

/// <summary>系统动作 Key 常量（F14，供 UI/数据模型使用）</summary>
public static class ActionKey
{
    public const string Shutdown = "shutdown";
    public const string Reboot = "reboot";
    public const string Lock = "lock";
    public const string Sleep = "sleep";
    public const string Logoff = "logoff";
    public const string VolumeMute = "volume_mute";
    public const string VolumeUp = "volume_up";
    public const string VolumeDown = "volume_down";
    public const string ToggleTouchpad = "toggle_touchpad";
    public const string OpenTaskManager = "open_taskmanager";
    public const string OpenControlPanel = "open_control_panel";
    public const string ScreenCapture = "screen_capture";
    // P2 添加窗口·系统功能页签扩充（辉哥哥 2026-10-28 定的 8 项）
    public const string Hibernate = "hibernate";
    public const string NetworkSettings = "network_settings";
    public const string DisplaySettings = "display_settings";
    public const string SoundSettings = "sound_settings";
    public const string WindowsSettings = "windows_settings";
    public const string ThisPC = "this_pc";
    public const string RecycleBin = "recycle_bin";
    public const string RunDialog = "run_dialog";

    public static readonly (string Key, string Label, string Icon)[] All =
    [
        (Shutdown, "关机", "⏻"),
        (Reboot, "重启", "⟳"),
        (Lock, "锁定", "🔒"),
        (Sleep, "睡眠", "⏾"),
        (Hibernate, "休眠", "🌙"),
        (Logoff, "注销", "🚪"),
        (VolumeMute, "静音", "🔇"),
        (VolumeUp, "音量+", "🔊"),
        (VolumeDown, "音量-", "🔉"),
        (ToggleTouchpad, "触摸板开关", "🖱"),
        (OpenTaskManager, "任务管理器", "📊"),
        (OpenControlPanel, "控制面板", "⚙"),
        (ScreenCapture, "截屏", "📷"),
        (NetworkSettings, "网络设置", "🌐"),
        (DisplaySettings, "显示设置", "🖥"),
        (SoundSettings, "声音设置", "🎚"),
        (WindowsSettings, "Windows 设置", "🪟"),
        (ThisPC, "此电脑", "💻"),
        (RecycleBin, "回收站", "🗑"),
        (RunDialog, "运行", "▶"),
    ];
}
