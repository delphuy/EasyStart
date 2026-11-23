using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Win32;

namespace EasyStart.Launcher;

/// <summary>
/// 商店应用 logo 定位（2026-11-23 辉哥哥：商店应用进「已安装」列表，图标不缺席）。
/// 全程只读、普通权限：HKCU AppModel\Repository\Packages 每个包都登记了
/// PackageRootFolder（实测无需 PackageManager，WinRT 非打包进程没有查询权限）；
/// 包内 AppxManifest.xml 的 VisualElements/@Square44x44Logo 给出 logo 相对路径；
/// 清单常写无后缀引用而实际文件带 .scale-200 / .targetsize-48 等限定符，按 MSIX
/// 命名规则逐个兜底。任何一步失败安静返回 null，列表走现有灰圈兜底。
/// </summary>
internal static class StoreAppIconLocator
{
    private const string RepoKey =
        @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages";

    // 包全名 OpenAI.Codex_26.1117.4957.0_x64__2p2nqsd0c76g0
    //   → PFN OpenAI.Codex_2p2nqsd0c76g0（AUMID 的 "!" 前半段）
    private static readonly Regex FullNamePattern =
        new(@"^(?<name>.+?)_[\d.]+_(?:x64|x86|arm|arm64|neutral)__(?<hash>[a-z0-9]{13})$", RegexOptions.Compiled);

    // 注册表扫一次全表，按 ShortcutResolver 的缓存节奏短暂缓存（包更新后不久即自愈）
    private static readonly object Gate = new();
    private static readonly TimeSpan MapCacheDuration = TimeSpan.FromSeconds(5);
    private static Dictionary<string, string>? _pfnToRoot;
    private static DateTime _pfnMapTime = DateTime.MinValue;

    /// <summary>AUMID（如 OpenAI.Codex_2p2nqsd0c76g0!App）→ logo PNG 物理路径；定位不到返回 null。</summary>
    public static string? Locate(string aumid)
    {
        try
        {
            var bang = aumid.IndexOf('!');
            if (bang <= 0) return null;
            var pfn = aumid[..bang];
            var appId = aumid[(bang + 1)..];
            if (appId.Length == 0) return null;

            var root = GetRootByPfn(pfn);
            if (root == null) return null;

            var manifestPath = Path.Combine(root, "AppxManifest.xml");
            if (!File.Exists(manifestPath)) return null;

            // 清单默认命名空间随 SDK 版本漂移，按 LocalName 匹配最稳
            var manifest = XDocument.Load(manifestPath);
            var applications = manifest.Root?.Descendants()
                .Where(x => x.Name.LocalName == "Application")
                .ToList();
            if (applications is not { Count: > 0 }) return null;

            var app = applications.FirstOrDefault(x =>
                        string.Equals((string?)x.Attribute("Id"), appId, StringComparison.Ordinal))
                    ?? applications[0];
            var logo = app.Elements()
                .FirstOrDefault(x => x.Name.LocalName == "VisualElements")
                ?.Attribute("Square44x44Logo")?.Value;
            if (string.IsNullOrWhiteSpace(logo)) return null;

            return ResolveLogoFile(root, logo);
        }
        catch
        {
            return null; // 图标只是增强，失败不进异常路径
        }
    }

    /// <summary>
    /// logo 引用解析：精确路径优先；清单常写 "assets/Square44x44Logo.png" 而实际
    /// 文件是 Square44x44Logo.scale-200.png / .targetsize-48_altform-unplated.png，
    /// 按优先级试限定符（非镀层 48px 最贴近列表 24px 图标，透明背景）；
    /// 再不行同目录同名前缀兜底，取限定符数值最大且非镀层的。
    /// </summary>
    private static string? ResolveLogoFile(string packageRoot, string logoRel)
    {
        var full = Path.GetFullPath(Path.Combine(packageRoot, logoRel.Replace('/', '\\')));
        if (File.Exists(full)) return full;

        var dir = Path.GetDirectoryName(full) ?? packageRoot;
        var name = Path.GetFileNameWithoutExtension(full);
        var ext = Path.GetExtension(full);

        string[] qualifiers =
        [
            ".targetsize-48_altform-unplated",
            ".targetsize-48",
            ".scale-200",
            ".targetsize-256_altform-unplated",
            ".targetsize-256",
            ".scale-100",
        ];
        foreach (var q in qualifiers)
        {
            var candidate = Path.Combine(dir, name + q + ext);
            if (File.Exists(candidate)) return candidate;
        }

        try
        {
            return Directory.EnumerateFiles(dir, name + "*" + ext)
                .OrderByDescending(f => f.Contains("_altform-unplated", StringComparison.Ordinal))
                .ThenByDescending(ParseQualifierNumber)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static int ParseQualifierNumber(string file)
    {
        var m = Regex.Match(Path.GetFileName(file), @"(?:scale|targetsize)-(\d+)");
        return m.Success ? int.Parse(m.Groups[1].Value) : 0;
    }

    private static string? GetRootByPfn(string pfn)
    {
        var map = GetPfnMap();
        return map.TryGetValue(pfn, out var root) ? root : null;
    }

    private static Dictionary<string, string> GetPfnMap()
    {
        lock (Gate)
        {
            if (_pfnToRoot != null && DateTime.UtcNow - _pfnMapTime < MapCacheDuration)
                return _pfnToRoot;

            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RepoKey);
                if (key != null)
                {
                    foreach (var fullName in key.GetSubKeyNames())
                    {
                        try
                        {
                            var m = FullNamePattern.Match(fullName);
                            if (!m.Success) continue;
                            var pfn = m.Groups["name"].Value + "_" + m.Groups["hash"].Value;
                            if (map.ContainsKey(pfn)) continue;

                            using var sk = key.OpenSubKey(fullName);
                            var root = sk?.GetValue("PackageRootFolder") as string;
                            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) continue;
                            map[pfn] = root;
                        }
                        catch { }
                    }
                }
            }
            catch { }

            _pfnToRoot = map;
            _pfnMapTime = DateTime.UtcNow;
            return map;
        }
    }
}
