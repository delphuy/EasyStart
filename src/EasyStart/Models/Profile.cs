using System.Text.Json;
using System.Text.Json.Serialization;

namespace EasyStart.Models;

/// <summary>一个 Profile = 一套标签页 + 设置 + 窗口几何（对齐 Rolan 5 实测 Profiles/&lt;uuid&gt;/data）</summary>
public class Profile
{
    [JsonIgnore] public string Id { get; set; } = Guid.NewGuid().ToString("D");
    [JsonIgnore] public string Name { get; set; } = "默认";
    [JsonIgnore] public string CreationTime { get; set; } = DateTime.Now.ToString("o");

    /// <summary>标签页（LaunchPad）列表</summary>
    public List<PanelTab> Tabs { get; set; } = new();

    /// <summary>面板设置（对齐 Rolan settings 字段）</summary>
    public PanelSettings Settings { get; set; } = new();

    /// <summary>窗口几何（对齐 Rolan window.json）</summary>
    public WindowGeometry Window { get; set; } = new();

    /// <summary>备忘录文本（P2，独立存 memo.txt 或并入此处）</summary>
    public string Memo { get; set; } = "";
}
