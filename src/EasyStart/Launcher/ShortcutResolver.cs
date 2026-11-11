using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using EasyStart.Models;

namespace EasyStart.Launcher;

/// <summary>
/// 统一规范化 .lnk：Target 保存稳定入口，ResolvedTarget 保存最终真实目标。
/// 桌面 .lnk 不作为长期入口：先逐层追踪 .lnk 链路，优先保存开始菜单稳定 .lnk；
/// 找不到稳定 .lnk 时才保存最终真实 exe/文件夹。
/// </summary>
public static class ShortcutResolver
{
    private const int MaxShortcutChainDepth = 10;

    private static readonly object StartMenuCacheLock = new();
    private static List<StartMenuShortcut>? _startMenuCache;
    private static DateTime _startMenuCacheTime = DateTime.MinValue;
    private static readonly TimeSpan StartMenuCacheDuration = TimeSpan.FromSeconds(5);

    public static void Normalize(LaunchItem item)
    {
        if (item.Type != LaunchItemType.Shortcut)
        {
            ClearResolved(item);
            return;
        }

        var target = TrimPathText(item.Target);
        item.Target = target;
        if (string.IsNullOrWhiteSpace(target) || !IsShortcut(target))
        {
            ClearResolved(item);
            return;
        }

        var sourceName = NormalizeShortcutName(Path.GetFileNameWithoutExtension(target));

        // 已删除的 .lnk：尝试按名字找回开始菜单里的稳定入口；找不到则保留旧 ResolvedTarget 作 fallback。
        if (!File.Exists(target))
        {
            var recovered = FindStartMenuShortcut(
                sourceName: sourceName,
                sourceInfo: null,
                requiredTargetPath: null,
                preferredTargetPath: item.ResolvedTarget,
                requireName: true);
            if (recovered != null)
                ApplyStartMenuShortcut(item, recovered, target);
            return;
        }

        var chain = ReadShortcutChain(target);
        if (chain.Count == 0) return; // 读取失败不破坏已有配置和 fallback。

        var sourceInfo = chain[0].Info;
        var resolvedInfo = CombineShortcutChain(chain);

        // 非桌面 .lnk（开始菜单/用户自建稳定入口）保持原路径，只记录链路最终目标。
        if (!IsDesktopPath(target))
        {
            ApplyResolvedShortcut(item, resolvedInfo);
            return;
        }

        // 桌面 .lnk 可能指向开始菜单里的另一个 .lnk。这里逐层追踪，
        // 只要链路中出现开始菜单入口，就直接保存那个稳定入口，而不是保存桌面文件。
        var stableLink = chain
            .Skip(1)
            .FirstOrDefault(x => IsStartMenuPath(x.Path) && !IsStartupPath(x.Path));
        if (stableLink != null)
        {
            var stableInfo = ResolveShortcut(stableLink.Path) ?? stableLink.Info;
            item.Target = stableLink.Path;
            ApplyResolvedShortcut(item, stableInfo);
            RenameDefaultName(item, target, stableLink.Path);
            return;
        }

        var finalTarget = TrimPathText(resolvedInfo.TargetPath);

        if (finalTarget != null && PathExists(finalTarget))
        {
            var candidate = FindStartMenuShortcut(
                sourceName, sourceInfo, requiredTargetPath: finalTarget, preferredTargetPath: finalTarget,
                requireName: false);
            if (candidate != null)
            {
                ApplyStartMenuShortcut(item, candidate, target);
                return;
            }

            // 没有稳定 .lnk 时，保存最终真实目标；内嵌参数/工作目录只在原字段未显式配置时补充。
            item.Target = finalTarget;
            ClearResolved(item);
            if (item.Args == null)
                item.Args = string.IsNullOrWhiteSpace(resolvedInfo.Arguments) ? "" : resolvedInfo.Arguments.Trim();
            if (item.WorkDir == null)
                item.WorkDir = NullIfWhiteSpace(resolvedInfo.WorkingDirectory);
            return;
        }

        // 最终目标已失效（例如版本目录变化）时，按名字和强元数据反查当前有效的开始菜单入口。
        var fallbackCandidate = FindStartMenuShortcut(
            sourceName, sourceInfo, requiredTargetPath: null, preferredTargetPath: item.ResolvedTarget,
            requireName: true);
        if (fallbackCandidate != null)
        {
            ApplyStartMenuShortcut(item, fallbackCandidate, target);
            return;
        }

        // 没有任何稳定入口可用：保留桌面 .lnk 和旧 fallback，不把已失效路径写成 Target。
    }

    public static bool IsShortcut(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            return string.Equals(Path.GetExtension(path), ".lnk", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>启动时重新读取当前 .lnk 链路，保证版本目录更新后拿到最新目标。</summary>
    internal static ShortcutInfo? ResolveShortcut(string? path)
    {
        if (!IsShortcut(path) || !File.Exists(path!)) return null;
        var chain = ReadShortcutChain(path!);
        return chain.Count == 0 ? null : CombineShortcutChain(chain);
    }

    private static List<ShortcutChainLink> ReadShortcutChain(string sourcePath)
    {
        var links = new List<ShortcutChainLink>();
        var currentPath = sourcePath;
        var currentInfo = ReadShortcut(currentPath);
        if (currentInfo == null) return links;

        links.Add(new ShortcutChainLink(currentPath, currentInfo));

        for (var depth = 0; depth < MaxShortcutChainDepth; depth++)
        {
            var nextPath = TrimPathText(currentInfo.TargetPath);
            if (nextPath == null
                || !IsShortcut(nextPath)
                || !File.Exists(nextPath)
                || links.Any(x => PathEquals(x.Path, nextPath)))
            {
                break;
            }

            var nextInfo = ReadShortcut(nextPath);
            if (nextInfo == null) break;

            links.Add(new ShortcutChainLink(nextPath, nextInfo));
            currentPath = nextPath;
            currentInfo = nextInfo;
        }

        return links;
    }

    private static ShortcutInfo CombineShortcutChain(IReadOnlyList<ShortcutChainLink> links)
    {
        var targetPath = TrimPathText(links[^1].Info.TargetPath);
        var arguments = links
            .Select(x => NullIfWhiteSpace(x.Info.Arguments))
            .Where(x => x != null)
            .ToList();
        var workingDirectory = links
            .Select(x => NullIfWhiteSpace(x.Info.WorkingDirectory))
            .FirstOrDefault(x => x != null);

        return new ShortcutInfo(
            targetPath,
            arguments.Count == 0 ? null : string.Join(" ", arguments),
            workingDirectory,
            links[0].Info.Description);
    }

    private static StartMenuShortcut? FindStartMenuShortcut(
        string sourceName,
        ShortcutInfo? sourceInfo,
        string? requiredTargetPath,
        string? preferredTargetPath,
        bool requireName)
    {
        StartMenuShortcut? best = null;
        var bestScore = int.MinValue;

        foreach (var candidate in GetStartMenuShortcuts())
        {
            if (IsStartupPath(candidate.Path)) continue;

            var candidateTarget = TrimPathText(candidate.Info.TargetPath);
            if (candidateTarget == null || !PathExists(candidateTarget)) continue;

            var nameMatches = ShortcutNameEquals(candidate.Name, sourceName);
            if (requireName && !nameMatches) continue;
            if (requiredTargetPath != null && !PathEquals(candidateTarget, requiredTargetPath)) continue;
            if (!nameMatches && !MetadataMatches(candidate.Info, sourceInfo)) continue;

            var score = ScoreCandidate(candidate, sourceName, sourceInfo, preferredTargetPath);
            if (best == null
                || score > bestScore
                || (score == bestScore && candidate.Path.Length < best.Path.Length))
            {
                best = candidate;
                bestScore = score;
            }
        }

        return best;
    }

    private static bool MetadataMatches(ShortcutInfo candidate, ShortcutInfo? source)
    {
        if (source == null) return false;

        // 名称不一致时必须有强元数据佐证，避免把同目标的无关程序误配。
        return TextEquals(candidate.Description, source.Description)
            || TextEquals(candidate.Arguments, source.Arguments);
    }

    private static int ScoreCandidate(
        StartMenuShortcut candidate,
        string sourceName,
        ShortcutInfo? sourceInfo,
        string? preferredTargetPath)
    {
        var score = 0;
        if (ShortcutNameEquals(candidate.Name, sourceName)) score += 100;
        if (preferredTargetPath != null && PathEquals(candidate.Info.TargetPath, preferredTargetPath)) score += 80;
        if (sourceInfo != null)
        {
            if (TextEquals(candidate.Info.Description, sourceInfo.Description)) score += 30;
            if (TextEquals(candidate.Info.Arguments, sourceInfo.Arguments)) score += 20;
            if (TextEquals(candidate.Info.WorkingDirectory, sourceInfo.WorkingDirectory)) score += 10;
        }
        return score;
    }

    private static void ApplyStartMenuShortcut(LaunchItem item, StartMenuShortcut candidate, string sourceShortcutPath)
    {
        item.Target = candidate.Path;
        ApplyResolvedShortcut(item, ResolveShortcut(candidate.Path) ?? candidate.Info);
        RenameDefaultName(item, sourceShortcutPath, candidate.Path);
    }

    private static void RenameDefaultName(LaunchItem item, string sourceShortcutPath, string stableShortcutPath)
    {
        // 拖入“xxx - 快捷方式”时，把默认显示名恢复为稳定入口的正式名称；不覆盖用户自定义名。
        var sourceName = Path.GetFileNameWithoutExtension(sourceShortcutPath);
        if (string.IsNullOrWhiteSpace(item.Name)
            || string.Equals(item.Name, sourceName, StringComparison.CurrentCultureIgnoreCase))
        {
            item.Name = Path.GetFileNameWithoutExtension(stableShortcutPath);
        }
    }

    private static void ApplyResolvedShortcut(LaunchItem item, ShortcutInfo info)
    {
        item.ResolvedTarget = TrimPathText(info.TargetPath);
        item.ResolvedArgs = NullIfWhiteSpace(info.Arguments);
        item.ResolvedWorkDir = NullIfWhiteSpace(info.WorkingDirectory);
    }

    private static void ClearResolved(LaunchItem item)
    {
        item.ResolvedTarget = null;
        item.ResolvedArgs = null;
        item.ResolvedWorkDir = null;
    }

    private static List<StartMenuShortcut> GetStartMenuShortcuts()
    {
        lock (StartMenuCacheLock)
        {
            if (_startMenuCache != null && DateTime.UtcNow - _startMenuCacheTime < StartMenuCacheDuration)
                return _startMenuCache;

            var shortcuts = GetStartMenuDirectories()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .SelectMany(SafeEnumerateShortcutFiles)
                .Select(path => ReadShortcut(path) is { } info ? new StartMenuShortcut(path, info) : null)
                .Where(x => x != null)
                .Select(x => x!)
                .ToList();

            _startMenuCache = shortcuts;
            _startMenuCacheTime = DateTime.UtcNow;
            return shortcuts;
        }
    }

    private static IEnumerable<string> GetStartMenuDirectories()
    {
        yield return Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu);
    }

    private static IEnumerable<string> GetDesktopDirectories()
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (!string.IsNullOrWhiteSpace(desktop)) yield return desktop;
        var commonDesktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
        if (!string.IsNullOrWhiteSpace(commonDesktop)) yield return commonDesktop;

        // OneDrive 的“已知文件夹移动”会把桌面放到 OneDrive\Desktop。
        foreach (var name in new[] { "OneDrive", "OneDriveCommercial", "OneDriveConsumer" })
        {
            var root = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrWhiteSpace(root)) continue;
            var oneDriveDesktop = Path.Combine(root, "Desktop");
            if (Directory.Exists(oneDriveDesktop)) yield return oneDriveDesktop;
        }
    }

    private static IEnumerable<string> SafeEnumerateShortcutFiles(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) yield break;

        var pending = new Queue<string>();
        pending.Enqueue(root);

        while (pending.Count > 0)
        {
            var directory = pending.Dequeue();
            string[] files;
            string[] directories;
            try
            {
                files = Directory.EnumerateFiles(directory, "*.lnk").ToArray();
                directories = Directory.EnumerateDirectories(directory).ToArray();
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
                yield return file;

            foreach (var child in directories)
                pending.Enqueue(child);
        }
    }

    private static bool IsStartMenuPath(string path)
    {
        foreach (var startMenu in GetStartMenuDirectories())
        {
            if (IsUnderDirectory(path, startMenu)) return true;
        }
        return false;
    }

    private static bool IsStartupPath(string path)
    {
        foreach (var startMenu in GetStartMenuDirectories())
        {
            var startup = Path.Combine(startMenu, "Programs", "Startup");
            if (IsUnderDirectory(path, startup)) return true;
        }
        return false;
    }

    private static bool IsDesktopPath(string path)
    {
        foreach (var desktop in GetDesktopDirectories())
        {
            if (IsUnderDirectory(path, desktop)) return true;
        }
        return false;
    }

    private static bool IsUnderDirectory(string path, string directory)
    {
        try
        {
            var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                       + Path.DirectorySeparatorChar;
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool PathExists(string? path)
        => !string.IsNullOrWhiteSpace(path)
           && (File.Exists(path) || Directory.Exists(path));

    private static bool PathEquals(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return false;
        return string.Equals(NormalizeComparablePath(left), NormalizeComparablePath(right), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeComparablePath(string path)
    {
        var trimmed = path.Trim().Trim('"');
        try
        {
            return Path.GetFullPath(trimmed);
        }
        catch
        {
            return trimmed;
        }
    }

    private static string? TrimPathText(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim().Trim('"');

    private static string? NullIfWhiteSpace(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool TextEquals(string? left, string? right)
    {
        left = NullIfWhiteSpace(left);
        right = NullIfWhiteSpace(right);
        if (left == null || right == null) return false;
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShortcutNameEquals(string candidateName, string sourceName)
        => string.Equals(
            NormalizeShortcutName(candidateName),
            NormalizeShortcutName(sourceName),
            StringComparison.CurrentCultureIgnoreCase);

    private static string NormalizeShortcutName(string name)
    {
        name = name.Trim();
        foreach (var suffix in new[] { " - 快捷方式", " - Shortcut", "- 快捷方式", "- Shortcut" })
        {
            if (name.EndsWith(suffix, StringComparison.CurrentCultureIgnoreCase))
                return name[..^suffix.Length].Trim();
        }
        return name;
    }

    private sealed record ShortcutChainLink(string Path, ShortcutInfo Info);
    private sealed record StartMenuShortcut(string Path, ShortcutInfo Info)
    {
        public string Name => System.IO.Path.GetFileNameWithoutExtension(Path);
    }

    internal sealed record ShortcutInfo(string? TargetPath, string? Arguments, string? WorkingDirectory, string? Description);

    /// <summary>WScript.Shell COM 后期绑定读取 .lnk；失败返回 null，由调用方保留当前入口。</summary>
    private static ShortcutInfo? ReadShortcut(string path)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return null;
            var shell = Activator.CreateInstance(shellType);
            if (shell == null) return null;

            var shortcut = shellType.InvokeMember(
                "CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });
            if (shortcut == null) return null;

            return new ShortcutInfo(
                GetProperty(shortcut, "TargetPath"),
                GetProperty(shortcut, "Arguments"),
                GetProperty(shortcut, "WorkingDirectory"),
                GetProperty(shortcut, "Description"));
        }
        catch
        {
            return null;
        }
    }

    private static string? GetProperty(object instance, string name)
        => instance.GetType().InvokeMember(
            name, BindingFlags.GetProperty, null, instance, null) as string;
}