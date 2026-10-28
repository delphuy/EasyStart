using System;
using System.Runtime.InteropServices;
using System.Threading;

// EasyStart 修复验证用输入驱动（独立 exe，注入才有效；pwsh 注入在这台机器会被吞，实测悬案）
// 用法:
//   inputtest show                → 注入 Alt+Space（呼出/隐藏面板热键）
//   inputtest hold x y ms         → 在 (x,y) 按住左键 ms 毫秒后松开（拖拽保护验证）
//   inputtest click x y           → 左键点一下
//   inputtest rightclick x y      → 右键点一下
//   inputtest drag x1 y1 x2 y2    → 从 (x1,y1) 左键拖拽到 (x2,y2) 松开（真 OLE 拖拽）
//   inputtest ctrll               → Ctrl+L（资源管理器地址栏）
//   inputtest type "文本"          → 逐字符注入（KEYEVENTF_UNICODE）
//   inputtest enter               → 回车
class P
{
    [StructLayout(LayoutKind.Sequential)]
    struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr extra; }

    [StructLayout(LayoutKind.Sequential)]
    struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr extra; }

    const uint KEYEVENTF_KEYUP = 0x0002, KEYEVENTF_UNICODE = 0x0004;
    const uint MOUSEEVENTF_LEFTDOWN = 0x0002, MOUSEEVENTF_LEFTUP = 0x0004;
    const uint MOUSEEVENTF_RIGHTDOWN = 0x0008, MOUSEEVENTF_RIGHTUP = 0x0010;

    [DllImport("user32.dll", SetLastError = true)]
    static extern uint SendInput(uint n, INPUT[] inputs, int size);
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);

    [StructLayout(LayoutKind.Sequential)]
    struct INPUT
    {
        public uint type;
        public KEYBDINPUT ki;
        public MOUSEINPUT mi;
        public static int Size => Marshal.SizeOf<INPUT>();
        public static INPUT Key(ushort vk, bool up) => new()
        { type = 1, ki = new KEYBDINPUT { wVk = vk, dwFlags = up ? KEYEVENTF_KEYUP : 0 } };
        public static INPUT KeyChar(char c, bool up) => new()
        { type = 1, ki = new KEYBDINPUT { wScan = c, dwFlags = (up ? KEYEVENTF_KEYUP : 0) | KEYEVENTF_UNICODE } };
        public static INPUT Mouse(uint flags) => new()
        { type = 0, mi = new MOUSEINPUT { dwFlags = flags } };
    }

    static void Send(params INPUT[] inputs) => SendInput((uint)inputs.Length, inputs, INPUT.Size);

    static void Tap(ushort vk) { Send(INPUT.Key(vk, false), INPUT.Key(vk, true)); Thread.Sleep(60); }

    static void Main(string[] a)
    {
        var mode = a[0];
        switch (mode)
        {
            case "show": // Alt+Space
                Send(INPUT.Key(0x12, false), INPUT.Key(0x20, false), INPUT.Key(0x20, true), INPUT.Key(0x12, true));
                Console.WriteLine("已注入 Alt+Space");
                break;
            case "hold":
            {
                int x = int.Parse(a[1]), y = int.Parse(a[2]), ms = int.Parse(a[3]);
                SetCursorPos(x, y); Thread.Sleep(120);
                Send(INPUT.Mouse(MOUSEEVENTF_LEFTDOWN));
                Console.WriteLine($"已按下 ({x},{y})，按住 {ms}ms");
                Thread.Sleep(ms);
                Send(INPUT.Mouse(MOUSEEVENTF_LEFTUP));
                Console.WriteLine("已松开");
                break;
            }
            case "click":
            {
                int x = int.Parse(a[1]), y = int.Parse(a[2]);
                SetCursorPos(x, y); Thread.Sleep(120);
                Send(INPUT.Mouse(MOUSEEVENTF_LEFTDOWN)); Thread.Sleep(40);
                Send(INPUT.Mouse(MOUSEEVENTF_LEFTUP));
                Console.WriteLine($"已左键 ({x},{y})");
                break;
            }
            case "rightclick":
            {
                int x = int.Parse(a[1]), y = int.Parse(a[2]);
                SetCursorPos(x, y); Thread.Sleep(120);
                Send(INPUT.Mouse(MOUSEEVENTF_RIGHTDOWN)); Thread.Sleep(40);
                Send(INPUT.Mouse(MOUSEEVENTF_RIGHTUP));
                Console.WriteLine($"已右键 ({x},{y})");
                break;
            }
            case "drag":
            {
                int x1 = int.Parse(a[1]), y1 = int.Parse(a[2]), x2 = int.Parse(a[3]), y2 = int.Parse(a[4]);
                int steps = a.Length > 5 ? int.Parse(a[5]) : 25;
                SetCursorPos(x1, y1); Thread.Sleep(200);
                Send(INPUT.Mouse(MOUSEEVENTF_LEFTDOWN));
                Thread.Sleep(150);
                for (int i = 1; i <= steps; i++)
                {
                    SetCursorPos(x1 + (x2 - x1) * i / steps, y1 + (y2 - y1) * i / steps);
                    Thread.Sleep(18);
                }
                Thread.Sleep(250);
                Send(INPUT.Mouse(MOUSEEVENTF_LEFTUP));
                Console.WriteLine($"已拖拽 ({x1},{y1}) → ({x2},{y2}) 并松开");
                break;
            }
            case "ctrll": // Ctrl+L
                Send(INPUT.Key(0x11, false), INPUT.Key(0x4C, false), INPUT.Key(0x4C, true), INPUT.Key(0x11, true));
                Console.WriteLine("已注入 Ctrl+L");
                break;
            case "type":
                foreach (var c in a[1])
                {
                    Send(INPUT.KeyChar(c, false), INPUT.KeyChar(c, true));
                    Thread.Sleep(25);
                }
                Console.WriteLine("已键入 " + a[1]);
                break;
            case "enter": Tap(0x0D); Console.WriteLine("已回车"); break;
            case "esc": Tap(0x1B); Console.WriteLine("已 Esc"); break;
        }
    }
}
