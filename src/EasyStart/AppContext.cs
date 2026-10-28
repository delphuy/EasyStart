using EasyStart.Models;
using EasyStart.Storage;

namespace EasyStart;

/// <summary>
/// 全局状态容器：当前 Profile + SettingsService（供 UI 各层访问，避免层层传参）。
/// 命名空间 EasyStart.AppContext，与 System.AppContext 区分。
/// </summary>
public sealed class AppContext
{
    public static AppContext? Instance { get; private set; }

    public Profile Profile { get; private set; } = null!;
    public SettingsService SettingsService { get; } = null!;
    public string ProfileId { get; } = "";

    public static AppContext Init(string profileId, Action onChanged)
    {
        var svc = new SettingsService(profileId, onChanged);
        var inst = new AppContext(svc, profileId);
        Instance = inst;
        return inst;
    }

    private AppContext(SettingsService svc, string profileId)
    {
        SettingsService = svc;
        ProfileId = profileId;
        Profile = svc.Profile;
    }

    public void Reload()
    {
        Profile = ProfileStore.LoadProfile(ProfileId) ?? Profile;
        SettingsService.Profile = Profile;
    }
}
