using System.Text.Json;
using System.Text.Json.Serialization;
using EasyStart.Models;

namespace EasyStart.Storage;

/// <summary>
/// 配置中心：读写 Profile 设置 + 触发 UI 刷新（P0-F12 + P1-F15）。
/// </summary>
public sealed class SettingsService
{
    private readonly string _profileId;
    private readonly Action _onChanged;

    public SettingsService(string profileId, Action onChanged)
    {
        _profileId = profileId;
        _onChanged = onChanged;
        _profile = ProfileStore.LoadProfile(_profileId) ?? ProfileStore.CreateDefault();
    }

    private Profile _profile;
    public Profile Profile { get => _profile; set => _profile = value; }

    public void Save()
    {
        ProfileStore.SaveProfile(_profile);
        _onChanged();
    }

    /// <summary>改一个设置项并保存</summary>
    public void Update(Action<PanelSettings> mutate)
    {
        mutate(_profile.Settings);
        Save();
    }
}
