using System.Windows;
using System.Windows.Media;
using System.Windows.Interop;
using System.Windows.Threading;
using EasyStart.HotKey;
using EasyStart.Panel;
using EasyStart.Shell;

namespace EasyStart;

public partial class App : Application
{
    private TrayIconService? _tray;
    private GlobalHotKeyManager? _hotkey;
    private PanelWindow? _panel;
    private Shell.MiddleClickWakeService? _middleWake;

    protected override void OnStartup(StartupEventArgs e)
    {
        // 全局异常兜底：崩前写 crash.log（exe 同目录），便于双击闪退排查
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            LogCrash("UnhandledException", args.ExceptionObject as Exception);
        DispatcherUnhandledException += (_, args) =>
        {
            LogCrash("DispatcherUnhandledException", args.Exception);
            args.Handled = true; // 不崩，留托盘
        };
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, args) =>
            LogCrash("TaskUnobserved", args.Exception);

        base.OnStartup(e);

        // 1. 单例
        if (!SingleInstance.TryAcquire())
        {
            MessageBox.Show("EasyStart 已在运行（看托盘图标）", "EasyStart");
            Shutdown();
            return;
        }

        // 2. 加载 profile
        var misc = Storage.ProfileStore.LoadMisc();
        string profileId;
        if (string.IsNullOrEmpty(misc.CurrentProfile)
            || !System.IO.Directory.Exists(Storage.ProfileStore.ProfileDir(misc.CurrentProfile)))
        {
            var def = Storage.ProfileStore.CreateDefault();
            profileId = def.Id;
        }
        else
        {
            profileId = misc.CurrentProfile;
        }
        misc.CurrentProfile = profileId;
        Storage.ProfileStore.SaveMisc(misc);

        // 3. 应用便携模式 + 初始化 AppContext（onChanged：设置变化时重注册全局热键 + 重应用外观/行为）
        // 用可空委托转发，避免 lambda 在 ctx 赋值完成前引用 ctx
        Action? _onSettingsChanged = null;
        var ctx = AppContext.Init(profileId, onChanged: () => _onSettingsChanged?.Invoke());
        _onSettingsChanged = () =>
        {
            if (_panel == null) return;
            _panel.Dispatcher.Invoke(() =>
            {
                // 逐项 try/catch：任何一步（含 toast）崩了不能炸断其余项。
                // 实锤：上一版 ApplyMiddleClickWake 里的 toast 崩溃把整条链炸断，
                // 后面的 ApplySettings() 根本没执行 → "保存了不生效"
                try { SetupHotKey(ctx); }                    // 全局热键（随 ToggleHotkey 重新注册）
                catch (Exception ex) { Shell.HookDiagnostics.Log("SetupHotKey异常: " + ex.Message); }
                try { _panel.ApplySettings(); }              // 外观 + 行为开关（先应用，别被后面的提示炸断）
                catch (Exception ex) { Shell.HookDiagnostics.Log("ApplySettings异常: " + ex.Message); }
                try { ApplyMiddleClickWake(ctx, verbose: true); } // 中键钩子（随 ShowOnMiddleClick 启停）
                catch (Exception ex) { Shell.HookDiagnostics.Log("ApplyMiddleClickWake异常: " + ex.Message); }
            });
        };
        Storage.ProfileStore.Portable = ctx.Profile.Settings.Portable;

        // 4. 主面板（启动时完整显示，失焦后才隐藏，P1-F3）
        _panel = new PanelWindow(ctx.SettingsService);
        _panel.Show();

        // 5. 托盘
        _tray = new TrayIconService();
        _tray.Create(
            onShowPanel: () => TogglePanel(),
            onSettings: () => Panel.SettingsWindow.Show(ctx.SettingsService),
            onBackup: () => Shell.BackupService.Export(ctx.SettingsService, _panel),
            onRestore: () =>
            {
                if (!Shell.BackupService.Restore(ctx.SettingsService, _panel) || _panel == null) return;
                // 恢复成功：数据重载 + 重渲染 + 热键/外观全链路重应用（任何一步失败不炸其余，实锤教训）
                try { _panel.RaiseRefresh(); }
                catch (Exception ex) { Shell.HookDiagnostics.Log("恢复后刷新面板异常: " + ex.Message); }
                try { SetupHotKey(ctx); }
                catch (Exception ex) { Shell.HookDiagnostics.Log("恢复后重注册热键异常: " + ex.Message); }
                try { _panel.ApplySettings(); }
                catch (Exception ex) { Shell.HookDiagnostics.Log("恢复后应用外观异常: " + ex.Message); }
            },
            onExit: () => ExitApp());
        _tray.SetIcon(LoadIcon());

        // 6. 全局热键（Alt+Space 默认）
        SetupHotKey(ctx);

        // 6.5 中键唤出钩子（ShowOnMiddleClick 开启时全局生效）；启动时静默（verbose:false，别弹 toast）
        ApplyMiddleClickWake(ctx, verbose: false);

        // 7. 应用开机自启
        Shell.AutoStartService.Apply(ctx.Profile.Settings.AutoStart);
    }

    /// <summary>按 ShowOnMiddleClick 开关启停全局中键钩子。
    /// verbose=true（设置页保存触发）才弹成功提示；启动时不弹（免噪声）。
    /// toast 全部 try/catch：提示崩了绝不能炸断调用链（上一版的实锤教训）。</summary>
    private void ApplyMiddleClickWake(AppContext ctx, bool verbose)
    {
        bool want = ctx.Profile.Settings.ShowOnMiddleClick;
        if (want && _middleWake == null)
        {
            _middleWake = new Shell.MiddleClickWakeService(TogglePanel);
            var (ok, reason) = _middleWake.Enable();
            if (!ok)
            {
                try { Panel.ToastService.Show($"中键呼出未生效：{reason}", true); }
                catch { }
            }
            else if (verbose)
            {
                try { Panel.ToastService.Show("中键呼出已启用：中键点击即可显示/隐藏面板。"); }
                catch { }
            }
        }
        else if (!want && _middleWake != null)
        {
            _middleWake.Dispose();
            _middleWake = null;
            if (verbose)
            {
                try { Panel.ToastService.Show("中键呼出已关闭。"); }
                catch { }
            }
        }
    }

    private System.Drawing.Icon? LoadIcon()
    {
        // 三级加载（2026-10-28 修：安装版托盘无图标）
        // ① 磁盘 es.ico（多尺寸原版，开发目录/手动放置时优先用）
        try
        {
            var p = System.IO.Path.Combine(System.AppContext.BaseDirectory, "es.ico");
            if (System.IO.File.Exists(p))
                return new System.Drawing.Icon(p);
        }
        catch { }
        // ② exe 内嵌图标（csproj ApplicationIcon 恒在：单文件/安装版/绿色版通吃，
        //    打包后磁盘上没有 es.ico，这里是托盘图标的主通道）
        try
        {
            var exePath = Environment.ProcessPath
                ?? System.Reflection.Assembly.GetExecutingAssembly().Location;
            if (!string.IsNullOrEmpty(exePath) && System.IO.File.Exists(exePath))
                return System.Drawing.Icon.ExtractAssociatedIcon(exePath);
        }
        catch { }
        // ③ 纯色占位（兜底；注意 new Icon(流) 只认 .ico 格式，PNG 必抛——改走 GetHicon）
        try
        {
            var bmp = new System.Drawing.Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using var g = System.Drawing.Graphics.FromImage(bmp);
            g.Clear(System.Drawing.Color.FromArgb(0x0A, 0x6C, 0xF1));
            g.FillEllipse(System.Drawing.Brushes.White, 8, 8, 16, 16);
            return System.Drawing.Icon.FromHandle(bmp.GetHicon());
        }
        catch { return null; }
    }

    private void TogglePanel()
    {
        if (_panel == null) return;
        if (_panel.IsVisible)
        {
            Shell.HookDiagnostics.Log("面板切换: 可见→隐藏");
            _panel.Hide();
        }
        else
        {
            Shell.HookDiagnostics.Log("面板切换: 隐藏→可见");
            _panel.Show();
            _panel.Activate();
        }
    }

    private void SetupHotKey(AppContext ctx)
    {
        _hotkey?.Dispose();
        _hotkey = new GlobalHotKeyManager(_panel);

        var combo = ctx.Profile.Settings.ToggleHotkey;
        if (string.IsNullOrWhiteSpace(combo))
        {
            // 清除热键：不注册，静默（辉哥哥可用设置页捕获框重新设）
        }
        else if (!_hotkey.Register(combo, TogglePanel))
        {
            // 注册失败（被占用）：提示但不停用其他热键
            Panel.ToastService.Show($"呼出热键 {combo} 被占用或未生效", true);
        }

        // 每个快捷项的全局热键（P1-F13）
        foreach (var t in ctx.Profile.Tabs)
        {
            foreach (var it in t.Items)
            {
                if (!string.IsNullOrEmpty(it.GlobalHotkey))
                {
                    var cap = it;
                    _hotkey.Register(cap.GlobalHotkey, () =>
                        Launcher.LaunchRunner.Launch(cap,
                            success: m => Panel.ToastService.Show(m),
                            fail: (n, err) => Panel.ToastService.Show($"{n}：{err}", true)));
                }
            }
        }
    }

    private void ExitApp()
    {
        _hotkey?.Dispose();
        _middleWake?.Dispose();
        _tray?.Dispose();
        if (_panel != null)
        {
            _panel.AllowClose = true;
            _panel.Close();
        }
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkey?.Dispose();
        _middleWake?.Dispose();
        _tray?.Dispose();
        base.OnExit(e);
    }

    private static void LogCrash(string kind, Exception? ex)
    {
        try
        {
            var p = System.IO.Path.Combine(System.AppContext.BaseDirectory, "crash.log");
            System.IO.File.AppendAllText(p,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {kind}\n{ex}\n\n");
        }
        catch { }
    }
}
