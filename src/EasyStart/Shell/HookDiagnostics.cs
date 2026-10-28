using System.Runtime.InteropServices;

namespace EasyStart.Shell;

/// <summary>
/// 钩子/系统键排障（辉哥哥：中键反复没效果、Ctrl 不隐藏没拦住——不猜，写实锤）。
/// 把"钩子装没装、收到过哪些鼠标消息、中键到没到、Ctrl 此刻物理按下没"落到
/// exe 同目录 diagnostics.log，出问题时打开文件一看便知。
/// </summary>
public static class HookDiagnostics
{
    private static readonly object _lock = new();
    private static readonly string _path =
        System.IO.Path.Combine(System.AppContext.BaseDirectory, "diagnostics.log");

    /// <summary>写一行日志（钩子线程可安全调用：只写文件不做重活）</summary>
    public static void Log(string msg)
    {
        try
        {
            lock (_lock)
            {
                System.IO.File.AppendAllText(_path,
                    $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n");
            }
        }
        catch { /* 排障日志自身失败必须静默 */ }
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vk);

    // Ctrl 键位：左 0xA0 / 右 0xA1 / 通用 0x11。
    // 辉哥哥实测：5 次失焦时他按着 Ctrl 但 L/R 都读 False →
    // 加通用 VK_CONTROL 兜底（注入/改键类工具常常只置通用键位）
    private const int VK_LCONTROL = 0xA0;
    private const int VK_RCONTROL = 0xA1;
    private const int VK_CONTROL = 0x11;

    /// <summary>此刻物理 Ctrl 是否按下（左/右/通用 三个键位任一）</summary>
    public static bool CtrlNow() => CtrlState().down;

    /// <summary>Ctrl 键状态 + 三个键位的原始值（落盘排障用）</summary>
    public static (bool down, string raw) CtrlState()
    {
        short l = GetAsyncKeyState(VK_LCONTROL);
        short r = GetAsyncKeyState(VK_RCONTROL);
        short g = GetAsyncKeyState(VK_CONTROL);
        bool down = (l & 0x8000) != 0 || (r & 0x8000) != 0 || (g & 0x8000) != 0;
        return (down, $"L=0x{l:X4} R=0x{r:X4} Ctrl=0x{g:X4}");
    }

    /// <summary>当前诊断日志文件路径（辉哥哥排障用）</summary>
    public static string LogPath => _path;
}
