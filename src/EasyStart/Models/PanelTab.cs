namespace EasyStart.Models;

/// <summary>标签页（一个 LaunchPad）</summary>
public class PanelTab
{
    public string Id { get; set; } = Guid.NewGuid().ToString("D");
    /// <summary>标签名</summary>
    public string Title { get; set; } = "新标签";
    /// <summary>该标签下的快捷项</summary>
    public List<LaunchItem> Items { get; set; } = new();
}
