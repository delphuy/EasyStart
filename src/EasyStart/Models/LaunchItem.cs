using System.Text.Json.Serialization;

namespace EasyStart.Models;

/// <summary>快捷项类型（对齐 Rolan 5 实测 $type）</summary>
public enum LaunchItemType
{
    /// <summary>可执行程序 / .lnk / .bat / 文件</summary>
    Shortcut,
    /// <summary>网址（uri 字段）</summary>
    Uri,
    /// <summary>文件夹</summary>
    Folder,
    /// <summary>系统操作（action 字段，SystemActions 枚举）</summary>
    System,
}

/// <summary>单个快捷项，序列化 $type 对齐 Rolan</summary>
public class LaunchItem
{
    [JsonIgnore] public string Id { get; set; } = Guid.NewGuid().ToString("D");

    [JsonPropertyName("$type")]
    public LaunchItemType Type { get; set; } = LaunchItemType.Shortcut;

    /// <summary>显示名（可空，缺省取文件名/主机名）</summary>
    public string? Name { get; set; }

    /// <summary>目标路径（exe/lnk/文件夹）</summary>
    public string? Target { get; set; }

    /// <summary>启动参数</summary>
    public string? Args { get; set; }

    /// <summary>工作目录</summary>
    public string? WorkDir { get; set; }

    /// <summary>自定义图标路径（.ico），缺省自动提取</summary>
    public string? IconPath { get; set; }

    /// <summary>网址（Type=Uri）</summary>
    public string? Uri { get; set; }

    /// <summary>系统动作 key（Type=System，见 SystemActions）</summary>
    public string? Action { get; set; }

    /// <summary>面板内数字键直接启动（Rolan shortcutKey 实测，"3"=第3项）</summary>
    public string? ShortcutKey { get; set; }

    /// <summary>全局热键（P1，"Ctrl+Alt+D" 格式）</summary>
    public string? GlobalHotkey { get; set; }

    /// <summary>以管理员启动（Rolan preferPrivileged 实测）</summary>
    public bool PreferPrivileged { get; set; }
}
