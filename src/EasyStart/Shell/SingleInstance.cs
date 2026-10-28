namespace EasyStart.Shell;

/// <summary>单例（Mutex + 命名管道，防多开）</summary>
public sealed class SingleInstance
{
    private System.Threading.Mutex? _mutex;

    /// <summary>返回 true=首次运行（继续），false=已有实例（应退出）</summary>
    public static bool TryAcquire()
    {
        bool created;
        var m = new System.Threading.Mutex(true, @"Local\EasyStart_SingleInstance", out created);
        if (!created) return false;
        // 持有引用防 GC
        GC.KeepAlive(m);
        return true;
    }
}
