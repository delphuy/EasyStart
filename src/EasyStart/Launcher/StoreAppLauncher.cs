using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using EasyStart.Shell;

namespace EasyStart.Launcher;

/// <summary>
/// 商店应用启动（2026-11-23 辉哥哥：应用商店装的程序要能一键启动）。
/// 主通道 IApplicationActivationManager::ActivateApplication——开始菜单/磁贴同款
/// 激活 API，返回真实 HRESULT 与目标进程 PID（可诊断、可回报失败）；
/// 失败再兜底 explorer.exe shell:AppsFolder\{AUMID} 代启动。
/// 说明：开发机实测从受限令牌进程树四条激发路线全部静默失败（含 explorer 代启动、
/// Shell COM InvokeVerb、直接 ShellExecute 伪路径报拒绝访问），属于调用方令牌受限，
/// 正常用户令牌下主通道即系统标准路径。
/// </summary>
public static class StoreAppLauncher
{
    /// <summary>
    /// AAM 原始调用（public：验证宿主需要读取真实 HRESULT）。
    /// 任意 apartment 可调：Apartment 注册类由 COM 自动落在宿主 STA。
    /// </summary>
    public static bool TryActivateAam(string aumid, out int hr)
    {
        hr = -1;
        try
        {
            var mgr = (IApplicationActivationManager)new CoClassApplicationActivationManager();
            hr = mgr.ActivateApplication(aumid, string.Empty, AO_NONE, out _);
            return hr == 0;
        }
        catch (Exception ex)
        {
            hr = ex.HResult;
            return false;
        }
    }

    /// <summary>启动一个商店应用。true=至少一条通道成功发起；false=三条通道都失败（已记日志）。</summary>
    public static bool Launch(string aumid)
    {
        // 通道 1：AAM 按 Apartment 注册：当前线程不是 STA 就开短命 STA 线程执行
        //（与 LaunchRunner.RunOnSta 同一套路，保证任意调用方语义一致）。
        // 本开发机（很新的 Win11 构建）实测该 COM 类已不注册（REGDB_E_CLASSNOTREG），
        // 失败即降级，代价一次 CoCreate，可忽略。
        var hr = -1;
        bool ok;
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
        {
            ok = TryActivateAam(aumid, out hr);
        }
        else
        {
            ok = false;
            var t = new Thread(() => { ok = TryActivateAam(aumid, out hr); })
            {
                IsBackground = true,
                Name = "es-store-activate",
            };
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();
        }

        if (ok) return true;
        HookDiagnostics.Log($"商店激活 AAM hr=0x{hr:X8} aumid={aumid}，降级 ShellExecute");

        // 通道 2：直接 ShellExecute 伪路径（shell: 命名空间由 shell 解析为"打开"动词）
        try
        {
            Process.Start(new ProcessStartInfo("shell:AppsFolder\\" + aumid)
            {
                UseShellExecute = true,
            });
            return true;
        }
        catch (Exception ex)
        {
            HookDiagnostics.Log("商店启动 ShellExecute 伪路径失败：" + ex.Message);
        }

        // 通道 3：explorer.exe 带参代启动（fire-and-forget）
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", "shell:AppsFolder\\" + aumid)
            {
                UseShellExecute = true,
            });
            return true;
        }
        catch (Exception ex)
        {
            HookDiagnostics.Log("商店启动 explorer 兜底也失败：" + ex.Message);
            return false;
        }
    }

    private const uint AO_NONE = 0;

    [ComImport, Guid("2e941141-7f75-4761-93b6-11e561e35a55"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationActivationManager
    {
        // 槽位必须与 shobjidl_core.h 声明一致，顺序不能错：
        // ActivateApplication → ActivateForFile → ActivateForProtocol
        [PreserveSig]
        int ActivateApplication(
            [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
            [MarshalAs(UnmanagedType.LPWStr)] string arguments,
            uint options,
            out uint processId);
        [PreserveSig]
        int ActivateForFile(
            [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
            IntPtr itemArray,
            [MarshalAs(UnmanagedType.LPWStr)] string verb,
            out uint processId);
        [PreserveSig]
        int ActivateForProtocol(
            [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
            IntPtr itemArray,
            out uint processId);
    }

    [ComImport, Guid("315e7d40-5ca6-471c-91db-e779c6ee1e9b")]
    private class CoClassApplicationActivationManager
    {
    }
}
