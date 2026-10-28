using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Hardcodet.Wpf.TaskbarNotification;

namespace EasyStart.Shell;

/// <summary>
/// 托盘图标 + 菜单（P0-F11）：显示面板 / 编辑 / 设置 / 退出
/// （备忘录、任务计划已按辉哥哥要求去掉）
/// </summary>
public sealed class TrayIconService
{
    private TaskbarIcon? _icon;

    public void Create(Action onShowPanel, Action onSettings, Action onBackup, Action onRestore, Action onExit)
    {
        _icon = new TaskbarIcon
        {
            ToolTipText = "EasyStart",
        };

        // 菜单（P2：「编辑快捷项」迁到面板右键，托盘菜单不再放）
        // 2026-10-28 新增：备份数据 / 恢复数据（辉哥哥：数据可导出，路径下次记住）
        _icon.ContextMenu = new ContextMenu();

        void Add(string header, Action onClick, bool isSeparator = false)
        {
            if (isSeparator)
            {
                _icon.ContextMenu.Items.Add(new Separator());
                return;
            }
            var mi = new MenuItem { Header = header };
            mi.Click += (_, _) => onClick();
            _icon.ContextMenu.Items.Add(mi);
        }

        Add("显示面板", onShowPanel);
        Add("设置", onSettings);
        Add("备份数据", onBackup);
        Add("恢复数据", onRestore);
        Add("分隔", () => { }, isSeparator: true);
        Add("退出", onExit);

        // 双击托盘 = 显示面板
        _icon.TrayMouseDoubleClick += (_, _) => onShowPanel();
    }

    public void SetIcon(System.Drawing.Icon? icon)
    {
        if (_icon != null && icon != null) _icon.Icon = icon;
    }

    public void Dispose()
    {
        _icon?.Dispose();
        _icon = null;
    }
}
