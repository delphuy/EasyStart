using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace EasyStart.HotKey;

/// <summary>
/// 全局热键管理（RegisterHotKey + HwndSource.AddHook 接收 WM_HOTKEY）。
/// 2026-10-29 重构：不再挂在 PanelWindow 的 hwnd 上——面板窗口的显示/隐藏/关闭生命周期会牵连热键
/// （窗口真关闭后 EnsureHandle 抛「关闭窗口后…」、每次重注册都要先 Dispose 再新建，失败即全军覆没）。
/// 改为自建一个 0×0 隐藏 WS_POPUP 宿主窗口，进程存活期间常驻：RegisterHotKey 注册的是系统级热键，
/// WM_HOTKEY 直接投递到本宿主，与面板任何状态彻底解耦（实测：隐藏窗口收 WM_HOTKEY 毫无问题）。
/// 冲突时 Register 返回 false（写入 diagnostics.log 留证）。
/// </summary>
public sealed class GlobalHotKeyManager : IDisposable
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8;
    private const int WS_POPUP = unchecked((int)0x80000000);

    private readonly HwndSource _source;
    private readonly IntPtr _hwnd;
    private readonly HwndSourceHook _hook;
    private readonly Dictionary<int, Action> _handlers = new();
    private readonly Dictionary<string, int> _comboToId = new();
    private int _nextId = 1;

    public GlobalHotKeyManager()
    {
        // 专用隐藏宿主：WS_POPUP、0×0、不带 WS_VISIBLE → 永不显示、不进任务栏、不抢焦点
        var p = new HwndSourceParameters("EasyStart_HotkeyHost", 0, 0)
        {
            WindowStyle = WS_POPUP,
        };
        _source = new HwndSource(p);
        _hwnd = _source.Handle;
        _hook = OnHwndMessage;
        _source.AddHook(_hook);
        Shell.HookDiagnostics.Log($"热键宿主窗口已创建 hwnd=0x{_hwnd:X}");
    }

    private IntPtr OnHwndMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            int id = wParam.ToInt32();
            if (_handlers.TryGetValue(id, out var action))
            {
                action();
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    /// <summary>解析 "Alt + Space" / "Ctrl+Alt+D" 为 (modifiers, vk)</summary>
    public static (uint Modifiers, uint Vk)? Parse(string? combo)
    {
        if (string.IsNullOrWhiteSpace(combo)) return null;
        uint mod = 0;
        string keyPart = combo;
        foreach (var t in combo.Split('+'))
        {
            var tok = t.Trim();
            switch (tok.ToLowerInvariant())
            {
                case "alt": mod |= MOD_ALT; break;
                case "ctrl": case "control": mod |= MOD_CONTROL; break;
                case "shift": mod |= MOD_SHIFT; break;
                case "win": case "windows": mod |= MOD_WIN; break;
                default: keyPart = tok; break;
            }
        }
        if (keyPart.Length == 0) return null;
        uint vk = keyPart.Length == 1
            ? keyPart.ToUpperInvariant()[0]
            : VkFromName(keyPart);
        if (vk == 0) return null;
        return (mod, vk);
    }

    private static uint VkFromName(string name) => name.ToLowerInvariant() switch
    {
        "space" or "spacebar" => 0x20,
        "enter" or "return" => 0x0D,
        "tab" => 0x09,
        "esc" or "escape" => 0x1B,
        "backspace" => 0x08,
        "delete" or "del" => 0x2E,
        "home" => 0x24,
        "end" => 0x23,
        "insert" => 0x2D,
        "up" => 0x26, "down" => 0x28, "left" => 0x25, "right" => 0x27,
        "pageup" or "pgup" => 0x21, "pagedown" or "pgdn" => 0x22,
        "f1" => 1, "f2" => 2, "f3" => 3, "f4" => 4, "f5" => 5, "f6" => 6,
        "f7" => 7, "f8" => 8, "f9" => 9, "f10" => 10, "f11" => 11, "f12" => 12,
        // WPF Key.ToString() 形式（设置页按键捕获框生成）
        "d0" => (uint)'0', "d1" => (uint)'1', "d2" => (uint)'2', "d3" => (uint)'3', "d4" => (uint)'4',
        "d5" => (uint)'5', "d6" => (uint)'6', "d7" => (uint)'7', "d8" => (uint)'8', "d9" => (uint)'9',
        "a" => (uint)'A', "b" => (uint)'B', "c" => (uint)'C', "d" => (uint)'D', "e" => (uint)'E',
        "f" => (uint)'F', "g" => (uint)'G', "h" => (uint)'H', "i" => (uint)'I', "j" => (uint)'J',
        "k" => (uint)'K', "l" => (uint)'L', "m" => (uint)'M', "n" => (uint)'N', "o" => (uint)'O',
        "p" => (uint)'P', "q" => (uint)'Q', "r" => (uint)'R', "s" => (uint)'S', "t" => (uint)'T',
        "u" => (uint)'U', "v" => (uint)'V', "w" => (uint)'W', "x" => (uint)'X', "y" => (uint)'Y', "z" => (uint)'Z',
        _ => 0,
    };

    /// <summary>注册；成功返回 true，解析失败或热键被占用返回 false（失败写 diagnostics.log 留证）</summary>
    public bool Register(string combo, Action handler)
    {
        var parsed = Parse(combo);
        if (parsed is null)
        {
            Shell.HookDiagnostics.Log($"热键注册失败: {combo}（无法解析）");
            return false;
        }
        var (mod, vk) = parsed.Value;
        int id = _nextId++;
        if (!RegisterHotKey(_hwnd, id, mod, vk))
        {
            _nextId--;
            Shell.HookDiagnostics.Log($"热键注册失败: {combo}（被其他程序占用）");
            return false;
        }
        _handlers[id] = handler;
        _comboToId[combo] = id;
        return true;
    }

    public void Unregister(string combo)
    {
        if (_comboToId.TryGetValue(combo, out int id))
        {
            UnregisterHotKey(_hwnd, id);
            _handlers.Remove(id);
            _comboToId.Remove(combo);
        }
    }

    public void UnregisterAll()
    {
        foreach (var id in _handlers.Keys.ToList())
        {
            UnregisterHotKey(_hwnd, id);
            _handlers.Remove(id);
        }
        _comboToId.Clear();
    }

    public void Dispose()
    {
        UnregisterAll();
        _source.RemoveHook(_hook);
        _source.Dispose();
    }
}
