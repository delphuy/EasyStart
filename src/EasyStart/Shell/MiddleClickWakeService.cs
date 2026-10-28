using System.Runtime.InteropServices;

namespace EasyStart.Shell;

/// <summary>
/// 全局中键唤出（WH_MOUSE_LL 低级鼠标钩子），是中键切换面板的唯一入口：
/// 面板失焦隐藏后收不到任何鼠标事件，必须靠系统级钩子在全局监听中键按下 →
/// 切回 UI 线程触发 TogglePanel（可见→隐藏 / 隐藏→可见）。
///
/// 实锤记录（辉哥哥反馈"中键无效果"的四代根因，全在卑职代码里）：
///   第一版：消息号写成 WM_LBUTTONDOWN(0x0201)——中键按下(wParam=0x0207)根本进不了分支；
///   第二版：消息号改对了，又加了一道 mouseData==0x0004 的门——MSLLHOOKSTRUCT.mouseData
///           只在滚轮/X键消息里携带数据，标准三键的按钮号在消息号里，门永远不匹配；
///   第三版：只认消息号 0x0207、回调轻量化——但钩子装在 UI 线程上，回调要排队进 UI
///           消息泵：UI 一忙（217 行列表重渲染等）回调超时，Windows 静默摘钩子
///           （2026-10-28 E2E 实锤：UI 线程活着、钩子没了，注入中键日志一片空白）；
///   本版（第四代）：钩子搬专用线程 + 自带 GetMessage 消息泵——该线程除了泵消息
///           什么都不干，回调永不排队堵塞，从机制上根除被摘钩子。
/// </summary>
public sealed class MiddleClickWakeService : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int x; public int y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint dwFlags;
        public int time;
        public int dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG msg, IntPtr hWnd, uint min, uint max);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessageW(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    private const int WH_MOUSE_LL = 14;
    // 低级鼠标钩子 wParam = 消息号；中键按下 = WM_MBUTTONDOWN(0x0207)。
    // （左=0x0201 / 右=0x0204 / 中=0x0207 / 滚轮=0x020A）
    private const int WM_MBUTTONDOWN = 0x0207;
    private const int WM_MBUTTONUP = 0x0208;
    private const uint WM_QUIT = 0x0012;

    private readonly Action _toggle;             // 长持引用，防 GC
    private readonly Action _toggleForDispatcher;
    private IntPtr _hook = IntPtr.Zero;           // 仅钩子线程读写（Enable 经 ready 事件同步后可读）
    private readonly HookProc _proc;             // 持有委托引用防止 GC 回收导致钩子崩溃
    private Thread? _thread;
    private uint _threadId;
    private volatile bool _enabled;
    private volatile int _triggerCount;           // 中键触发计数（自检/排障用）

    public MiddleClickWakeService(Action toggle)
    {
        _toggle = toggle;
        _toggleForDispatcher = toggle;
        _proc = HookCallback;
    }

    public bool IsEnabled => _enabled;

    /// <summary>中键触发的次数（排障/自检用）</summary>
    public int TriggerCount => _triggerCount;

    /// <summary>启用全局中键监听（专用线程 + 消息泵）。
    /// 返回 (装上了没, 失败原因)。
    /// 装不上（被系统策略拦截/权限不足）时给出原因，调用方要提示用户，不能静默。</summary>
    public (bool ok, string reason) Enable()
    {
        if (_thread is { IsAlive: true } && _enabled) return (true, "");

        string? fail = null;
        var ready = new System.Threading.ManualResetEventSlim(false);
        _thread = new Thread(() =>
        {
            _threadId = GetCurrentThreadId();
            _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(null), 0);
            if (_hook == IntPtr.Zero)
            {
                fail = $"SetWindowsHookEx 失败 (Win32 error={Marshal.GetLastWin32Error()})";
                ready.Set();
                return;
            }
            HookDiagnostics.Log($"中键钩子已安装 (hook=0x{_hook:X}, 专用线程 {_threadId})");
            _enabled = true;
            ready.Set();

            // 消息泵：LL 钩子回调经由安装线程的消息循环分发；PostThreadMessage(WM_QUIT) 退出
            while (GetMessage(out _, IntPtr.Zero, 0, 0) > 0) { }

            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
            _enabled = false;
            HookDiagnostics.Log("中键钩子线程退出，钩子已卸载");
        })
        { IsBackground = true, Name = "es-middle-hook" };
        _thread.Start();

        if (!ready.Wait(3000))
            return (false, "钩子线程 3 秒内没就绪");
        return fail != null ? (false, fail) : (_enabled ? (true, "") : (false, "钩子未启用"));
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        // 回调保持轻量：只做计数 + BeginInvoke，文件日志/切换都回 UI 线程做。
        // 落盘只记中键（0x0207/0x0208），辉哥哥下轮实测时 diagnostics.log 一眼定位：
        //   有"钩子收到 0x0207" → 钩子活着、中键到货（切换逻辑本轮 E2E 已验过）；
        //   一条都没有 → 中键被更上层（驱动/常驻工具）吃掉，锅不在本程序。
        if (nCode >= 0)
        {
            int msg = (int)wParam;
            if (msg == WM_MBUTTONDOWN || msg == WM_MBUTTONUP)
            {
                bool isDown = msg == WM_MBUTTONDOWN;
                if (isDown) _triggerCount++;
                var count = _triggerCount;
                var f = _toggleForDispatcher;
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                {
                    HookDiagnostics.Log($"钩子收到 0x{msg:X4}{(isDown ? "【中键按下】" : "")}");
                    if (isDown)
                    {
                        HookDiagnostics.Log($"中键命中 累计={count} → 切换面板");
                        f();
                    }
                }));
            }
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        _enabled = false;
        if (_threadId != 0) PostThreadMessageW(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _thread?.Join(1500);
        HookDiagnostics.Log("中键钩子已卸载");
    }
}
