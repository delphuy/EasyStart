using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EasyStart.Models;
using EasyStart.Shell;

namespace EasyStart.Launcher;

/// <summary>
/// 启动分发：Shortcut / Uri / Folder / System + preferPrivileged + 成功/失败回调（P0-F16 反馈）。
/// </summary>
public static class LaunchRunner
{
    /// <summary>
    /// 启动一个快捷项。返回 true=成功发起，false=失败（已调用 fail 反馈）。
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
                    if (string.IsNullOrEmpty(item.Target) || !Directory.Exists(item.Target))
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
                    var psi = CreateShortcutStartInfo(item);
                    if (psi == null) return Fail("目标不存在或缺少目标");

                    if (item.PreferPrivileged)
                        psi.Verb = "runas";

                    var p = Process.Start(psi);
                    if (p == null) return Fail("无法启动");
                    break;
            }

            success?.Invoke($"{item.Name ?? item.Target} 已启动");
            return true;

            bool Fail(string msg)
            {
                fail?.Invoke(item.Name ?? item.Target ?? "启动项", msg);
                return false;
            }
        }
        catch (Exception ex)
        {
            fail?.Invoke(item.Name ?? item.Target ?? "启动项", ex.Message);
            return false;
        }
    }

    /// <summary>编辑/查看属性：定位文件。稳定 .lnk 存在时定位 .lnk，否则定位最终真实目标。</summary>
    public static void Reveal(LaunchItem item)
    {
        if (item.Type == LaunchItemType.Uri || item.Type == LaunchItemType.System) return;
        var path = GetExistingPath(item.Target) ?? GetExistingPath(item.ResolvedTarget);
        if (path == null) return;
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"")
        {
            UseShellExecute = true,
        });
    }

    // ===== 2026-11-11（辉哥哥：点图标大概率卡一下、偶尔无响应）：启动链的异步化 =====
    // 根因：Launch/Reveal 的整条链（File.Exists 目标检查 / WScript.Shell COM 解析 /
    // ShellExecuteEx 进程创建——后者要等杀软扫描、SmartScreen、UWP broker、UAC 应答）
    // 全部同步跑在 UI 线程上，消息泵被押住：短则顿挫，长则"(未响应)"，Hide 排在链尾
    // 只能干等着。以下入口把工作扔到专用 STA 后台线程，UI 线程立刻返回。

    /// <summary>启动的异步版：UI 线程 await 本方法即返回，真正启动在后台 STA 线程执行。
    /// 结果语义与同步 Launch 完全一致（true=成功发起；失败已走 fail 回调）。
    /// success/fail 回调在**后台线程**执行——ToastService 内部自带 Dispatcher.Invoke，天然跨线程安全。</summary>
    public static Task<bool> LaunchAsync(LaunchItem item, Action<string>? success, Action<string, string>? fail)
        => RunOnSta(() =>
        {
            var sw = Stopwatch.StartNew();
            var ok = Launch(item, success, fail);
            // 落盘耗时：事后打开 diagnostics.log 一眼验证"耗时还在、但已不卡 UI"
            HookDiagnostics.Log($"启动[{item.Name ?? item.Target ?? "?"}] 后台耗时 {sw.ElapsedMilliseconds}ms 结果={(ok ? "成功" : "失败")}");
            return ok;
        });

    /// <summary>右键「定位文件」的异步版（fire-and-forget）：explorer /select 同样会被
    /// 慢速/网络路径卡住，不再占 UI 线程。</summary>
    public static void RevealAsync(LaunchItem item)
        => RunOnSta(() => { Reveal(item); return true; });

    /// <summary>把工作丢到专用 STA 后台线程执行，TaskCompletionSource 回传结果。
    /// 为什么 STA 而不是 Task.Run：UseShellExecute=true 的 ShellExecuteEx 与
    /// WScript.Shell COM 都要求 STA；线程池线程是 MTA，不保险。
    /// 异常一律吞掉返回 false（Launch 内部已 catch，这里兜线程级异常），绝不逃回 async void。</summary>
    private static Task<bool> RunOnSta(Func<bool> work)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var t = new Thread(() =>
        {
            try { tcs.TrySetResult(work()); }
            catch (Exception ex)
            {
                HookDiagnostics.Log("启动线程异常: " + ex.Message);
                tcs.TrySetResult(false);
            }
        })
        { IsBackground = true, Name = "es-launch" };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        return tcs.Task;
    }

    /// <summary>
    /// 构造 Shortcut 启动信息：
    /// 1. Target 有效且是 .lnk：默认直接启动 .lnk，让系统使用它当前指向、内嵌参数和工作目录；
    /// 2. 用户显式配置 Args/WorkDir 时重新解析 .lnk，显式字段优先，未显式字段使用 .lnk 内嵌值；
    /// 3. Target 失效时使用 ResolvedTarget fallback，显式字段仍优先。
    /// </summary>
    private static ProcessStartInfo? CreateShortcutStartInfo(LaunchItem item)
    {
        var target = GetExistingPath(item.Target);

        if (target == null)
        {
            var fallback = GetExistingPath(item.ResolvedTarget);
            return fallback == null
                ? null
                : CreateStartInfo(
                    fallback,
                    item.Args ?? item.ResolvedArgs,
                    item.WorkDir ?? item.ResolvedWorkDir);
        }

        if (!ShortcutResolver.IsShortcut(target))
            return CreateStartInfo(target, item.Args, item.WorkDir);

        // 用户没有覆盖参数和工作目录：直接走稳定 .lnk，版本更新后始终拿到最新目标。
        if (item.Args == null && item.WorkDir == null)
            return CreateStartInfo(target, null, null);

        // 用户覆盖任一字段时不直接启动 .lnk，避免“用户参数 + .lnk 内嵌参数”被拼在一起。
        var resolved = ShortcutResolver.ResolveShortcut(target);
        var resolvedTarget = resolved == null ? null : GetExistingPath(resolved.TargetPath);
        if (resolvedTarget != null && resolved != null)
        {
            return CreateStartInfo(
                resolvedTarget,
                item.Args ?? resolved.Arguments,
                item.WorkDir ?? resolved.WorkingDirectory);
        }

        // 当前 .lnk 读取失败时优先使用上次记录的真实目标；再兜底回 .lnk 本身。
        var recordedFallback = GetExistingPath(item.ResolvedTarget);
        return CreateStartInfo(
            recordedFallback ?? target,
            item.Args ?? item.ResolvedArgs,
            item.WorkDir ?? item.ResolvedWorkDir);
    }

    private static ProcessStartInfo CreateStartInfo(string target, string? args, string? workDir)
        => new()
        {
            FileName = target,
            Arguments = args ?? string.Empty,
            WorkingDirectory = string.IsNullOrWhiteSpace(workDir) ? null : workDir,
            UseShellExecute = true,
        };

    private static string? GetExistingPath(string? path)
        => string.IsNullOrWhiteSpace(path)
           || (!File.Exists(path) && !Directory.Exists(path))
            ? null
            : path;
}