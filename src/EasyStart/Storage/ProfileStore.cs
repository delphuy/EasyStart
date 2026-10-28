using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using EasyStart.Models;
using EasyStart.SystemOps;

namespace EasyStart.Storage;

/// <summary>
/// Profile 存储：%LocalAppData%\EasyStart\（默认）或 程序目录\EasyStart\portable\（便携）。
/// 原子写（tmp+Move）+ 自动 *_backup（对齐 Rolan 实测 *_backup 行为）。
/// </summary>
public sealed class ProfileStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>便携模式（程序目录旁）</summary>
    public static bool Portable { get; set; }

    private static string Root
    {
        get
        {
            if (Portable)
            {
                var p = Path.Combine(System.AppContext.BaseDirectory, "EasyStart", "portable");
                Directory.CreateDirectory(p);
                return p;
            }
            var d = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "EasyStart");
            Directory.CreateDirectory(d);
            return d;
        }
    }

    public static string ProfilesDir => Path.Combine(Root, "profiles");
    public static string CacheDir => Path.Combine(Root, "cache");

    public static string ProfileDir(string id)
    {
        var p = Path.Combine(ProfilesDir, id);
        Directory.CreateDirectory(p);
        return p;
    }

    // ---------- Global misc ----------
    public static GlobalMisc LoadMisc()
    {
        var p = Path.Combine(CacheDir, "misc.json");
        if (File.Exists(p))
            return JsonSerializer.Deserialize<GlobalMisc>(File.ReadAllText(p), JsonOpts) ?? new GlobalMisc();
        return new GlobalMisc();
    }

    public static void SaveMisc(GlobalMisc m)
    {
        Directory.CreateDirectory(CacheDir);
        AtomicWrite(Path.Combine(CacheDir, "misc.json"), m, JsonOpts);
    }

    // ---------- Profile ----------
    public static Profile? LoadProfile(string id)
    {
        var p = Path.Combine(ProfileDir(id), "data.json");
        if (!File.Exists(p)) return null;
        var prof = JsonSerializer.Deserialize<Profile>(File.ReadAllText(p), JsonOpts);
        if (prof != null)
        {
            prof.Id = id;
            prof.Name = LoadMeta(id).Name;
        }
        return prof;
    }

    public static void SaveProfile(Profile p)
    {
        var dir = ProfileDir(p.Id);
        var path = Path.Combine(dir, "data.json");
        // 备份旧版
        if (File.Exists(path))
            File.Copy(path, path + "_backup", true);
        AtomicWrite(path, p, JsonOpts);

        // 备忘录独立存
        var memoPath = Path.Combine(dir, "memo.txt");
        File.WriteAllText(memoPath, p.Memo ?? "");
    }

    public static ProfileMeta LoadMeta(string id)
    {
        var p = Path.Combine(ProfileDir(id), "meta.json");
        if (File.Exists(p))
            return JsonSerializer.Deserialize<ProfileMeta>(File.ReadAllText(p), JsonOpts)
                   ?? new ProfileMeta { Name = "默认" };
        return new ProfileMeta { Name = "默认", CreationTime = DateTime.Now.ToString("o") };
    }

    public static void SaveMeta(ProfileMeta m, string id)
        => AtomicWrite(Path.Combine(ProfileDir(id), "meta.json"), m, JsonOpts);

    /// <summary>列出所有 profile</summary>
    public static List<(string Id, ProfileMeta Meta)> ListProfiles()
    {
        var list = new List<(string, ProfileMeta)>();
        if (Directory.Exists(ProfilesDir))
        {
            foreach (var d in Directory.GetDirectories(ProfilesDir))
            {
                var id = Path.GetFileName(d);
                list.Add((id, LoadMeta(id)));
            }
        }
        return list;
    }

    public static void DeleteProfile(string id)
        => Directory.Delete(ProfileDir(id), recursive: true);

    /// <summary>创建默认 profile（单页签 + 示例项）</summary>
    public static Profile CreateDefault()
    {
        var p = new Profile
        {
            Name = "默认",
            Tabs =
            {
                new PanelTab { Title = "常用" },
            }
        };
        p.Id = Guid.NewGuid().ToString("D");
        // 常用页签：桌面 + 系统操作
        p.Tabs[0].Items.Add(new LaunchItem
        {
            Type = LaunchItemType.Folder,
            Name = "桌面",
            Target = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        });
        p.Tabs[0].Items.AddRange(
        [
            new() { Type = LaunchItemType.System, Name = "锁定", Action = Models.ActionKey.Lock },
            new() { Type = LaunchItemType.System, Name = "关机", Action = Models.ActionKey.Shutdown },
            new() { Type = LaunchItemType.System, Name = "静音", Action = Models.ActionKey.VolumeMute },
            new() { Type = LaunchItemType.System, Name = "任务管理器", Action = Models.ActionKey.OpenTaskManager },
        ]);
        SaveMeta(new ProfileMeta { Name = p.Name, CreationTime = p.CreationTime }, p.Id);
        SaveProfile(p);
        return p;
    }

    private static void AtomicWrite<T>(string path, T obj, JsonSerializerOptions opts)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(obj, opts));
        File.Move(tmp, path, overwrite: true);
    }
}

public class ProfileMeta
{
    public string Name { get; set; } = "默认";
    public string CreationTime { get; set; } = DateTime.Now.ToString("o");
}
