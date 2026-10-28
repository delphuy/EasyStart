using System.Diagnostics;
using System.Windows;
using EasyStart.Models;

namespace EasyStart.Launcher;

/// <summary>
/// 启动分发：Shortcut / Uri / Folder / System + preferPrivileged + 成功/失败回调（P0-F16 反馈）。
/// </summary>
public static class LaunchRunner
{
    /// <summary>
    /// 启动一个快捷项。返回 true=成功发起，false=失败（已调用 fail 回调）。
    /// success/fail 回调在调用线程执行，用于 toast。
    /// </summary>
    public static bool Launch(LaunchItem item, Action<string>? success, Action<string, string>? fail)
    {
        try
        {
            switch (item.Type)
            {
                case LaunchItemType.Uri:
                    if (string.IsNullOrEmpty(item.Uri)) return Fail("缺少网址");
                    Process.Start(new ProcessStartInfo(item.Uri) { UseShellExecute = true });
                    break;

                case LaunchItemType.Folder:
                    if (string.IsNullOrEmpty(item.Target) || !System.IO.Directory.Exists(item.Target))
                        return Fail("文件夹不存在");
                    Process.Start(new ProcessStartInfo("explorer.exe", item.Target) { UseShellExecute = true });
                    break;

                case LaunchItemType.System:
                    if (SystemOps.SystemActions.Run(item.Action ?? ""))
                        success?.Invoke($"{item.Name ?? item.Action} 已执行");
                    else
                        return Fail($"系统动作 {item.Action} 执行失败");
                    return true;

                case LaunchItemType.Shortcut:
                default:
                    if (string.IsNullOrEmpty(item.Target)) return Fail("缺少目标");
                    var psi = new ProcessStartInfo(item.Target!, item.Args)
                    {
                        UseShellExecute = true,
                        WorkingDirectory = string.IsNullOrEmpty(item.WorkDir) ? null : item.WorkDir,
                    };
                    if (item.PreferPrivileged)
                        psi.Verb = "runas";
                    var p = Process.Start(psi);
                    if (p == null) return Fail("无法启动");
                    break;
            }
            success?.Invoke($"{item.Name ?? item.Target} 已启动");
            return true;

            bool Fail(string msg) { fail?.Invoke(item.Name ?? item.Target ?? "启动项", msg); return false; }
        }
        catch (Exception ex)
        {
            fail?.Invoke(item.Name ?? item.Target ?? "启动项", ex.Message);
            return false;
        }
    }

    /// <summary>编辑/查看属性：定位文件</summary>
    public static void Reveal(LaunchItem item)
    {
        if (item.Type == LaunchItemType.Uri || item.Type == LaunchItemType.System) return;
        if (string.IsNullOrEmpty(item.Target)) return;
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{item.Target}\"")
        {
            UseShellExecute = true,
        });
    }
}
