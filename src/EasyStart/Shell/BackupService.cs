using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using EasyStart.Models;
using EasyStart.Storage;

namespace EasyStart.Shell;

/// <summary>
/// 备份 / 恢复（辉哥哥 2026-10-28）：
/// 备份 = 当前档案 data.json（JSON 原样）+ meta/memo + 站点图标 icons\ + 手选图标 icons_custom\
///        + manifest.json 摘要，导出到「指定文件夹\EasyStart备份_时间戳\」，不覆盖旧备份；
///        导出路径记进设置（LastBackupFolder），下次选择器直接开在上次位置。
/// 恢复 = 选备份文件夹 → 先自动导出一份「恢复前」安全备份（双保险，恢复错了也能救回来）
///        → 图标归位 + 丢失的图标路径重定向 → data.json 存回当前档案（App 自身还会留 _backup）。
/// </summary>
public static class BackupService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>站点 favicon 图标目录（与 AddItemWindow 抓 favicon 的落盘位置一致）</summary>
    private static string IconsRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EasyStart", "icons");

    /// <summary>恢复出来的手选图标新家</summary>
    private static string IconsCustomRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EasyStart", "icons_custom");

    // ============ 备份 ============

    /// <summary>入口：弹文件夹选择器 → 导出 → 记住路径 → toast</summary>
    public static void Export(EasyStart.Storage.SettingsService settings, Window? owner)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "选择备份存放位置（将在此文件夹内创建时间戳子文件夹）",
            InitialDirectory = Directory.Exists(settings.Profile.Settings.LastBackupFolder)
                ? settings.Profile.Settings.LastBackupFolder!
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dlg.ShowDialog(owner ?? Application.Current?.MainWindow) != true) return;
        var chosen = dlg.FolderName;

        var target = Path.Combine(chosen, $"EasyStart备份_{DateTime.Now:yyyyMMdd_HHmmss}");
        var (tabs, items, icons) = DoExport(settings, target);

        // 下次记住：路径写入设置（Save 即写 data.json，并触发一次无害的外观/热键刷新链）
        settings.Profile.Settings.LastBackupFolder = chosen;
        settings.Save();

        Panel.ToastService.Show($"备份完成：{tabs} 个页签 / {items} 项 / {icons} 个图标 → {target}");
        HookDiagnostics.Log($"备份导出: {target}（{tabs} 页签 / {items} 项 / {icons} 图标）");
    }

    /// <summary>纯导出逻辑（恢复前的安全备份也复用这里）</summary>
    public static (int tabs, int items, int icons) DoExport(
        EasyStart.Storage.SettingsService settings, string target)
    {
        Directory.CreateDirectory(target);
        var ctx = EasyStart.AppContext.Instance
            ?? throw new InvalidOperationException("AppContext 未初始化");
        var srcDir = ProfileStore.ProfileDir(ctx.ProfileId);
        var prof = settings.Profile;

        // 1) data.json 原样导出（磁盘上的权威文件，避免内存序列化差异）
        File.Copy(Path.Combine(srcDir, "data.json"), Path.Combine(target, "data.json"), true);
        foreach (var extra in new[] { "meta.json", "memo.txt" })
        {
            var p = Path.Combine(srcDir, extra);
            if (File.Exists(p)) File.Copy(p, Path.Combine(target, extra), true);
        }

        // 2) 站点图标目录整体带走
        int icons = 0;
        if (Directory.Exists(IconsRoot))
        {
            Directory.CreateDirectory(Path.Combine(target, "icons"));
            foreach (var f in Directory.GetFiles(IconsRoot))
            {
                File.Copy(f, Path.Combine(target, "icons", Path.GetFileName(f)), true);
                icons++;
            }
        }

        // 3) 手选图标（散在磁盘各处的 IconPath）拷进 icons_custom\，备份自包含。
        //    文件名 = 页签_项名_原名（确定性命名：恢复端按同样规则重定向，天然防同名冲突）
        var customDir = Path.Combine(target, "icons_custom");
        foreach (var t in prof.Tabs)
        {
            int ti = prof.Tabs.IndexOf(t);
            foreach (var it in t.Items)
            {
                var ip = it.IconPath;
                if (string.IsNullOrWhiteSpace(ip) || !File.Exists(ip)) continue;
                if (ip.StartsWith(IconsRoot, StringComparison.OrdinalIgnoreCase)) continue; // 已在 icons 里
                Directory.CreateDirectory(customDir);
                var dest = Path.Combine(customDir, CustomIconName(ti, t.Title, it.Name, ip));
                File.Copy(ip, dest, true);
                icons++;
            }
        }

        // 4) manifest.json 摘要
        int items = prof.Tabs.Sum(t => t.Items.Count);
        var manifest = new
        {
            app = "EasyStart",
            format = 1,
            exportedAt = DateTime.Now.ToString("o"),
            profileId = ctx.ProfileId,
            profileName = prof.Name,
            tabCount = prof.Tabs.Count,
            itemCount = items,
            iconCount = icons,
        };
        File.WriteAllText(Path.Combine(target, "manifest.json"),
            JsonSerializer.Serialize(manifest, JsonOpts));

        return (prof.Tabs.Count, items, icons);
    }

    // ============ 恢复 ============

    /// <summary>手选图标在备份里的确定性文件名（导出与恢复两端按同一规则计算，天然防同名冲突）</summary>
    private static string CustomIconName(int tabIdx, string tabTitle, string itemName, string sourcePath)
    {
        var safe = new string((tabTitle + "_" + itemName)
            .Where(c => !Path.GetInvalidFileNameChars().Contains(c) && c != ' ')
            .ToArray());
        if (string.IsNullOrWhiteSpace(safe)) safe = "item";
        return $"{tabIdx}_{safe}_{Path.GetFileName(sourcePath)}";
    }

    /// <summary>入口：选备份文件夹 → 安全备份 → 应用 → toast。返回 true 时调用方需刷新面板/热键/外观。</summary>
    public static bool Restore(EasyStart.Storage.SettingsService settings, Window? owner)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "选择要恢复的备份文件夹（含 data.json 的那个）",
            InitialDirectory = Directory.Exists(settings.Profile.Settings.LastBackupFolder)
                ? settings.Profile.Settings.LastBackupFolder!
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dlg.ShowDialog(owner ?? Application.Current?.MainWindow) != true) return false;
        return RestoreFrom(settings, dlg.FolderName);
    }

    /// <summary>从指定文件夹恢复（拆出来便于测试与后续「点 manifest 恢复」扩展）</summary>
    public static bool RestoreFrom(EasyStart.Storage.SettingsService settings, string src)
    {
        var dataFile = Path.Combine(src, "data.json");
        if (!File.Exists(dataFile))
        {
            MessageBox.Show("这不是有效的 EasyStart 备份文件夹（缺少 data.json）",
                "EasyStart", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        var ctx = EasyStart.AppContext.Instance
            ?? throw new InvalidOperationException("AppContext 未初始化");

        // 双保险①：恢复前把「当前」数据先自动备份一份（安全备份失败 → 中止恢复，绝不裸奔）
        try
        {
            var parent = Directory.GetParent(src)?.FullName ?? src;
            var safety = Path.Combine(parent, $"EasyStart备份_恢复前_{DateTime.Now:yyyyMMdd_HHmmss}");
            DoExport(settings, safety);
            HookDiagnostics.Log("恢复前安全备份完成: " + safety);
        }
        catch (Exception ex)
        {
            HookDiagnostics.Log("恢复前安全备份失败: " + ex.Message);
            MessageBox.Show($"恢复前的安全备份失败，已中止恢复：\n{ex.Message}",
                "EasyStart", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        // 反序列化备份（Profile.Id/Name 是 JsonIgnore，落盘文件即全部数据）
        Profile prof;
        try
        {
            prof = JsonSerializer.Deserialize<Profile>(File.ReadAllText(dataFile), JsonOpts)
                   ?? throw new InvalidOperationException("data.json 反序列化为空");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"备份文件损坏，无法恢复：\n{ex.Message}",
                "EasyStart", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
        prof.Id = ctx.ProfileId; // 恢复到当前档案槽位（不产生新档案）

        // 站点图标归位（合并，同名覆盖）
        int iconsRestored = 0;
        var srcIcons = Path.Combine(src, "icons");
        if (Directory.Exists(srcIcons))
        {
            Directory.CreateDirectory(IconsRoot);
            foreach (var f in Directory.GetFiles(srcIcons))
            {
                File.Copy(f, Path.Combine(IconsRoot, Path.GetFileName(f)), true);
                iconsRestored++;
            }
        }

        // 手选图标归位 + 原路径已丢失的重定向到恢复出来的新家
        var srcCustom = Path.Combine(src, "icons_custom");
        if (Directory.Exists(srcCustom))
        {
            Directory.CreateDirectory(IconsCustomRoot);
            foreach (var f in Directory.GetFiles(srcCustom))
                File.Copy(f, Path.Combine(IconsCustomRoot, Path.GetFileName(f)), true);
        }
        foreach (var t in prof.Tabs)
        {
            int ti = prof.Tabs.IndexOf(t);
            foreach (var it in t.Items)
            {
                var ip = it.IconPath;
                if (string.IsNullOrWhiteSpace(ip) || File.Exists(ip)) continue; // 原路径还在：不动
                var cand = Path.Combine(srcCustom,
                    CustomIconName(ti, t.Title, it.Name, ip));
                if (File.Exists(cand))
                {
                    it.IconPath = Path.Combine(IconsCustomRoot,
                        Path.GetFileName(CustomIconName(ti, t.Title, it.Name, ip)));
                    iconsRestored++;
                }
            }
        }

        // meta / memo 跟随
        foreach (var extra in new[] { "meta.json", "memo.txt" })
        {
            var p = Path.Combine(src, extra);
            if (File.Exists(p))
                File.Copy(p, Path.Combine(ProfileStore.ProfileDir(prof.Id), extra), true);
        }

        // 存回当前档案（SaveProfile 自身会把旧 data.json 备成 _backup → 双保险②③）
        ProfileStore.SaveProfile(prof);

        int items = prof.Tabs.Sum(t => t.Items.Count);
        Panel.ToastService.Show($"恢复完成：{prof.Tabs.Count} 个页签 / {items} 项 / 图标 {iconsRestored} 个归位（当前数据已先安全备份）");
        HookDiagnostics.Log($"恢复完成: 来自 {src}（{prof.Tabs.Count} 页签 / {items} 项 / 图标归位 {iconsRestored}）");
        return true;
    }
}
