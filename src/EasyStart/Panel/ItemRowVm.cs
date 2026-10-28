using System.ComponentModel;
using System.Windows.Media;
using EasyStart.Models;

namespace EasyStart.Panel;

/// <summary>
/// 列表行 VM（P2 编辑弹窗 / 添加窗口共用）：
/// 勾选框 + 图标 + 名称 + 类型 + 路径 五栏位。
/// </summary>
public class ItemRowVm : INotifyPropertyChanged
{
    private bool _checked;
    private ImageSource? _icon;

    public LaunchItem Item { get; init; } = null!;

    /// <summary>勾选框（批量删除/整块移动/多选添加的依据）</summary>
    public bool Checked
    {
        get => _checked;
        set { if (_checked != value) { _checked = value; OnPs(nameof(Checked)); } }
    }

    /// <summary>24px 图标；添加窗口里列表先出、图标异步补齐</summary>
    public ImageSource? Icon
    {
        get => _icon;
        set { _icon = value; OnPs(nameof(Icon)); }
    }

    public string NameText => Item.Name ?? "";

    public override string ToString() => NameText; // UIA/自动化友好名（默认是类名）

    public string TypeText => Item.Type switch
    {
        LaunchItemType.Shortcut => "程序",
        LaunchItemType.Uri => "网址",
        LaunchItemType.Folder => "文件夹",
        LaunchItemType.System => "系统",
        _ => "",
    };

    public string PathText => Item.Type switch
    {
        LaunchItemType.Uri => Item.Uri ?? "",
        LaunchItemType.System => ActionLabel(Item.Action),
        _ => Item.Target ?? "",
    };

    /// <summary>从 LaunchItem 建行（图标按面板同款规则提取：网址占位/系统字形/文件真图标）</summary>
    public static ItemRowVm From(LaunchItem it, bool dark, int size = 24)
    {
        var target = it.Type switch
        {
            LaunchItemType.Uri => it.Uri,
            LaunchItemType.System => it.Action,
            _ => it.Target,
        };
        return new ItemRowVm
        {
            Item = it,
            Icon = Launcher.IconExtractor.Extract(target, size, it.IconPath, dark),
        };
    }

    private static string ActionLabel(string? key)
    {
        foreach (var (k, label, _) in ActionKey.All)
            if (k == key) return label;
        return key ?? "";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPs(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
