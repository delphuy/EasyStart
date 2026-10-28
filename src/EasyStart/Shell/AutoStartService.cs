using Microsoft.Win32;

namespace EasyStart.Shell;

/// <summary>开机自启（HKCU Run 键，可关）</summary>
public static class AutoStartService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ValueName = "EasyStart";

    public static void Apply(bool enabled)
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return;

        using (var reg = Registry.CurrentUser.OpenSubKey(RunKey, writable: true))
        {
            if (reg == null) return;
            if (enabled)
                reg.SetValue(ValueName, $"\"{exe}\" --minimized");
            else
                reg.DeleteValue(ValueName, throwOnMissingValue: false);
        }

        // 2026-10-28 修（辉哥哥实锤：设置里勾选了，启动应用列表里却没有）：
        // Win11「启动应用」的禁用标记（StartupApproved\Run，03 开头）会盖掉 Run 键——
        // Run 键写得再对，被标 03 就等于没加入。勾选时必须一并接管该标记，
        // 用户在 EasyStart 设置里的选择说了算；取消勾选则连标记一起清干净。
        using (var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey, writable: true))
        {
            if (approved == null) return;
            if (enabled)
            {
                // 02 00 00 00 = 启用（后 8 字节 FILETIME 置 0 同样有效，任务管理器同款格式）
                approved.SetValue(ValueName, new byte[] { 0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 },
                    RegistryValueKind.Binary);
            }
            else
            {
                approved.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
    }
}
