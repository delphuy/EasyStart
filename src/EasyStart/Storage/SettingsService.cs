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

    /// <summary>仅落盘不通知：窗口几何（位置/大小）等纯数据持久化专用。
    /// 2026-10-29 新增：此前失焦隐藏/拖动/resize/关闭每次都走 Save() → 触发
    /// 「重注册热键+重应用外观」整链——热键被反复注销重注（偶发被抢就整段失灵），
    /// 退出时还会在已关闭窗口上抛「关闭窗口后…」。几何变化不需要任何 UI 重应用。</summary>
    public void SaveSilent() => ProfileStore.SaveProfile(_profile);

    /// <summary>改一个设置项并保存</summary>
    public void Update(Action<PanelSettings> mutate)
    {
        mutate(_profile.Settings);
        Save();
    }
}
