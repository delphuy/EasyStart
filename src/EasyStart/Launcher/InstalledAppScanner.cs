using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using EasyStart.Models;
using Microsoft.Win32;

namespace EasyStart.Launcher;

/// <summary>
/// 全盘程序扫描（P2 添加窗口·已安装的应用程序页签）：
/// 注册表卸载列表 + 开始菜单快捷方式，合并去重（辉哥哥 2026-10-28 定的口径）。
/// 只收有可启动目标的条目；更新补丁/驱动运行库等不可启动项自动排除；商店 UWP 本轮不收。
/// </summary>
public static class InstalledAppScanner
{
    public static List<LaunchItem> Scan()
    {
        var dict = new Dictionary<string, LaunchItem>(StringComparer.OrdinalIgnoreCase);

        ScanRegistry(dict);   // 先注册表（显示名更正式）
        ScanStartMenu(dict);  // 后开始菜单（补绿色软件），同目标自动去重

        var list = dict.Values.ToList();
        list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        return list;
    }

    // ===== 1. 注册表卸载列表（HKLM 64/32 视图 + HKCU）=====
    private static void ScanRegistry(Dictionary<string, LaunchItem> dict)
    {
        (RegistryKey Root, string Path)[] roots =
        [
            (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
            (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
            (Registry.CurrentUser,  @"Software\Microsoft\Windows\CurrentVersion\Uninstall"),
        ];
        foreach (var (root, path) in roots)
        {
            try
            {
                using var key = root.OpenSubKey(path);
                if (key == null) continue;
                foreach (var sub in key.GetSubKeyNames())
                {
                    try
                    {
                        using var sk = key.OpenSubKey(sub);
                        if (sk == null) continue;

                        var name = sk.GetValue("DisplayName") as string;
                        if (string.IsNullOrWhiteSpace(name)) continue;
                        if (IsJunkEntry(name, sub)) continue;

                        var displayIcon = sk.GetValue("DisplayIcon") as string;
                        var installLoc = sk.GetValue("InstallLocation") as string;

                        var target = ResolveExecutable(displayIcon) ?? FindFirstExe(installLoc);
                        if (target == null) continue; // 无可启动目标（补丁/运行库）→ 不收

                        Add(dict, new LaunchItem
                        {
                            Type = LaunchItemType.Shortcut,
                            Name = name,
                            Target = target,
                        });
                    }
                    catch { }
                }
            }
            catch { }
        }
    }

    // ===== 2. 开始菜单快捷方式（公共 + 当前用户，含子目录）=====
    private static void ScanStartMenu(Dictionary<string, LaunchItem> dict)
    {
        var dirs = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs"),
        };
        foreach (var dir in dirs.Where(Directory.Exists))
        {
            foreach (var lnk in SafeEnumFiles(dir, "*.lnk"))
            {
                try
                {
                    // 解析 .lnk 真实目标；COM 解析失败退化用 .lnk 本身（shell 启动 .lnk 一样能跑）
                    var target = ResolveShortcutTarget(lnk) ?? lnk;
                    if (!File.Exists(target) && !Directory.Exists(target)) continue;

                    var item = ItemFactory.FromDrop(target, null);
                    if (item == null) continue;
                    item.Name = Path.GetFileNameWithoutExtension(lnk); // 显示名跟开始菜单一致
                    Add(dict, item);
                }
                catch { }
            }
        }
    }

    /// <summary>DisplayIcon → 可执行目标：去 ",0" 索引、去引号、展开环境变量，只认可启动扩展名</summary>
    private static string? ResolveExecutable(string? displayIcon)
    {
        if (string.IsNullOrWhiteSpace(displayIcon)) return null;
        var s = displayIcon.Trim().Trim('"');
        var comma = s.IndexOf(',');
        if (comma >= 0) s = s[..comma];        // "C:\x\app.exe,0" → "C:\x\app.exe"
        if (s.Length == 0 || s.StartsWith('@')) return null; // "@dll,-123" 资源串不可执行
        try { s = Environment.ExpandEnvironmentVariables(s); } catch { }
        if (!File.Exists(s)) return null;
        return Path.GetExtension(s).ToLowerInvariant() is ".exe" or ".lnk" or ".bat" or ".cmd"
            ? s
            : null; // .ico/.dll 只是图标，不是启动目标
    }

    /// <summary>无 DisplayIcon 时：InstallLocation 目录里找第一个 exe（绿色软件常见形态）</summary>
    private static string? FindFirstExe(string? installLoc)
    {
        if (string.IsNullOrWhiteSpace(installLoc)) return null;
        try
        {
            var dir = Environment.ExpandEnvironmentVariables(installLoc.Trim().Trim('"'));
            if (!Directory.Exists(dir)) return null;
            return Directory.EnumerateFiles(dir, "*.exe").FirstOrDefault();
        }
        catch { return null; }
    }

    /// <summary>Windows 更新补丁/热修条目不进程序列表</summary>
    private static bool IsJunkEntry(string name, string subKeyName)
    {
        if (subKeyName.StartsWith("KB", StringComparison.OrdinalIgnoreCase)) return true;
        var n = name.TrimStart();
        return n.StartsWith("Update for", StringComparison.OrdinalIgnoreCase)
            || n.StartsWith("Security Update", StringComparison.OrdinalIgnoreCase)
            || n.StartsWith("Hotfix", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>WScript.Shell COM 后期绑定解析 .lnk 目标（不依赖动态绑定）</summary>
    private static string? ResolveShortcutTarget(string lnkPath)
    {
        try
        {
            var wshType = Type.GetTypeFromProgID("WScript.Shell");
            if (wshType == null) return null;
            var wsh = Activator.CreateInstance(wshType);
            if (wsh == null) return null;
            var sc = wshType.InvokeMember("CreateShortcut",
                BindingFlags.InvokeMethod, null, wsh, new object[] { lnkPath });
            if (sc == null) return null;
            var target = sc.GetType().InvokeMember("TargetPath",
                BindingFlags.GetProperty, null, sc, null) as string;
            return string.IsNullOrWhiteSpace(target) ? null : target;
        }
        catch { return null; }
    }

    /// <summary>合并去重：先到先得（注册表条目优先），Key = 归一化目标路径</summary>
    private static void Add(Dictionary<string, LaunchItem> dict, LaunchItem item)
    {
        var key = item.Type switch
        {
            LaunchItemType.Uri => item.Uri,
            _ => item.Target,
        };
        if (string.IsNullOrWhiteSpace(key)) return;
        key = key.Trim().Trim('"');
        if (!dict.ContainsKey(key)) dict[key] = item;
    }

    /// <summary>耐错枚举（无权限子目录跳过）</summary>
    private static IEnumerable<string> SafeEnumFiles(string dir, string pattern)
    {
        var queue = new Queue<string>();
        queue.Enqueue(dir);
        while (queue.Count > 0)
        {
            var d = queue.Dequeue();
            string[] subDirs = [], files = [];
            try { subDirs = Directory.GetDirectories(d); } catch { }
            try { files = Directory.GetFiles(d, pattern); } catch { }
            foreach (var f in files) yield return f;
            foreach (var s in subDirs) queue.Enqueue(s);
        }
    }
}
