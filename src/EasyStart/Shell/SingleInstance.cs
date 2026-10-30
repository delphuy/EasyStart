namespace EasyStart.Shell;

/// <summary>单例（Mutex，防多开）</summary>
public sealed class SingleInstance
{
    // 2026-10-29 修：必须 static 长持。之前 Mutex 是 TryAcquire 的局部变量，
    // 方法返回后引用丢失，GC 一回收锁就被系统释放 → 后启动的实例照样跑起来。
    // 实锤（10-29 08:48 日志）：开机曾跑出第二个实例，互抢 Alt+Space 全局热键，
    // 两个实例一个抢到热键、另一个静默失败 → 表现为「开机后按热键没反应」。
    private static System.Threading.Mutex? _mutex;

    /// <summary>返回 true=首次运行（继续），false=已有实例（应退出）</summary>
    public static bool TryAcquire()
    {
        _mutex = new System.Threading.Mutex(true, @"Local\EasyStart_SingleInstance", out bool created);
        if (!created)
        {
            _mutex.Dispose();
            _mutex = null;
            return false;
        }
        // static 字段持有引用直到进程退出，锁不会被 GC 悄悄放掉
        return true;
    }
}
