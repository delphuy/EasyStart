using Microsoft.Win32;

namespace EasyStart.Shell;

/// <summary>开机自启（HKCU Run 键，可关）</summary>
public static class AutoStartService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "EasyStart";

    public static void Apply(bool enabled)
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return;
        using var reg = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", writable: true);
        if (reg == null) return;
        if (enabled)
            reg.SetValue(ValueName, $"\"{exe}\" --minimized");
        else
            reg.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
