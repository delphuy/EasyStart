using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using EasyStart.Models;

namespace EasyStart.HotKey;

/// <summary>
/// 全局热键管理（RegisterHotKey 到某 WPF 窗口 + HwndSource.AddHook 接收 WM_HOTKEY）：
/// - 热键注册到指定的宿主窗口（PanelWindow 的 hwnd），即使面板隐藏，
///   RegisterHotKey 注册的是系统级热键（不依赖窗口前台），仍能触发。
/// - WM_HOTKEY 通过 HwndSource 钩子路由到对应 handler。
/// 冲突时 Register 返回 false。
/// </summary>
public sealed class GlobalHotKeyManager : IDisposable
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8;

    private readonly HwndSource _source;
    private readonly IntPtr _hwnd;
    private readonly HwndSourceHook _hook;
    private readonly Dictionary<int, Action> _handlers = new();
    private readonly Dictionary<string, int> _comboToId = new();
    private int _nextId = 1;

    public GlobalHotKeyManager(Window hostWindow)
    {
        var helper = new WindowInteropHelper(hostWindow);
        _hwnd = helper.EnsureHandle();
        _source = HwndSource.FromHwnd(_hwnd);
        if (_source == null)
            throw new InvalidOperationException("宿主窗口 HwndSource 为空，无法挂热键钩子");
        _hook = OnHwndMessage;
        _source.AddHook(_hook);
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

    /// <summary>注册；成功返回 true，解析失败或热键被占用返回 false</summary>
    public bool Register(string combo, Action handler)
    {
        var parsed = Parse(combo);
        if (parsed is null) return false;
        var (mod, vk) = parsed.Value;
        int id = _nextId++;
        if (!RegisterHotKey(_hwnd, id, mod, vk))
        {
            _nextId--;
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
    }
}
