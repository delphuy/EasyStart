using System.Diagnostics;
using EasyStart.Models;

namespace EasyStart.SystemOps;

/// <summary>12 个系统动作实现（P0-F14），全部走系统自带入口，不装额外组件</summary>
public static class SystemActions
{
    /// <summary>执行一个系统动作；返回是否成功发起</summary>
    public static bool Run(string key)
    {
        try
        {
            switch (key)
            {
                case ActionKey.Shutdown:
                    Process.Start("shutdown", "/s /t 5 /c \"EasyStart 关机\"");
                    return true;
                case ActionKey.Reboot:
                    Process.Start("shutdown", "/r /t 5 /c \"EasyStart 重启\"");
                    return true;
                case ActionKey.Logoff:
                    Process.Start("shutdown", "/l /t 0");
                    return true;
                case ActionKey.Lock:
                    Process.Start("rundll32.exe", "user32.dll,LockWorkStation");
                    return true;
                case ActionKey.Sleep:
                    Process.Start(new ProcessStartInfo("rundll32.exe", "powrprof.dll,SetSuspendState 0,1,0")
                    { CreateNoWindow = true, UseShellExecute = false });
                    return true;
                case ActionKey.OpenTaskManager:
                    StartShell("taskmgr");
                    return true;
                case ActionKey.OpenControlPanel:
                    StartShell("control");
                    return true;
                case ActionKey.ScreenCapture:
                    // 实锤修复：.NET 8 默认 UseShellExecute=false，"ms-screenclip:" 协议串根本拉不起来
                    StartShell("ms-screenclip:");
                    return true;
                // ===== P2 系统功能页签扩充（辉哥哥 2026-10-28 定的 8 项）=====
                case ActionKey.Hibernate:
                    Process.Start(new ProcessStartInfo("rundll32.exe", "powrprof.dll,SetSuspendState 1,1,0")
                    { CreateNoWindow = true, UseShellExecute = false });
                    return true;
                case ActionKey.NetworkSettings:
                    StartShell("control", "ncpa.cpl");
                    return true;
                case ActionKey.DisplaySettings:
                    StartShell("ms-settings:display");
                    return true;
                case ActionKey.SoundSettings:
                    StartShell("ms-settings:sound");
                    return true;
                case ActionKey.WindowsSettings:
                    StartShell("ms-settings:");
                    return true;
                case ActionKey.ThisPC:
                    StartShell("explorer.exe", "shell:MyComputerFolder");
                    return true;
                case ActionKey.RecycleBin:
                    StartShell("explorer.exe", "shell:RecycleBinFolder");
                    return true;
                case ActionKey.RunDialog:
                    // Win+R 组合键（keybd_event 模拟，同音量键思路）
                    KeybdEvent(0x5B, 0, 0, 0);  // VK_LWIN ↓
                    KeybdEvent(0x52, 0, 0, 0);  // R ↓
                    KeybdEvent(0x52, 0, 2, 0);  // R ↑
                    KeybdEvent(0x5B, 0, 2, 0);  // VK_LWIN ↑
                    return true;
                case ActionKey.VolumeMute:
                case ActionKey.VolumeUp:
                case ActionKey.VolumeDown:
                    SetVolume(key);
                    return true;
                case ActionKey.ToggleTouchpad:
                    ToggleTouchPad();
                    return true;
                default:
                    return false;
            }
        }
        catch { return false; }
    }

    /// <summary>ShellExecute 启动（control / ms-settings: 协议串等必须走 shell）</summary>
    private static void StartShell(string file, string? args = null)
    {
        Process.Start(new ProcessStartInfo(file, args ?? "") { UseShellExecute = true });
    }

    /// <summary>调音量：Mute 切静音 / Up 加 / Down 减，用物理按键最稳</summary>
    private static void SetVolume(string key)
    {
        if (key == ActionKey.VolumeMute)
        {
            KeybdEvent(0x5D, 0, 0, 0);
            KeybdEvent(0x5D, 0, 2, 0);
        }
        else if (key == ActionKey.VolumeUp)
        {
            KeybdEvent(0x42, 0, 0, 0);
            KeybdEvent(0x42, 0, 2, 0);
        }
        else if (key == ActionKey.VolumeDown)
        {
            KeybdEvent(0x43, 0, 0, 0);
            KeybdEvent(0x43, 0, 2, 0);
        }
    }

    /// <summary>触摸板开关：禁用/启用 HID 触摸板设备</summary>
    private static void ToggleTouchPad()
    {
        Process.Start(new ProcessStartInfo("powershell",
            @"-NoProfile -Command """ +
            @"$d = Get-PnpDevice -Class Mouse -PresentOnly | Where-Object {$_.FriendlyName -match 'touchpad|TSSTC|Synaptics|ELAN|I2C HID|HID-compliant touch pad'} | Select-Object -First 1; " +
            @"if($d){ if($d.Status -eq 'OK'){Disable-PnpDevice -InstanceId $d.InstanceId -Confirm:$false} else {Enable-PnpDevice -InstanceId $d.InstanceId -Confirm:$false} }""" +
            "\"")
        { CreateNoWindow = true, UseShellExecute = false });
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern void KeybdEvent(byte bVk, byte bScan, uint dwFlags, IntPtr dwExtraInfo);
}
