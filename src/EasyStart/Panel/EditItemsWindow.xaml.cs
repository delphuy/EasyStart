using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using EasyStart.Storage;

namespace EasyStart.Panel;

/// <summary>
/// 编辑快捷项（P2，辉哥哥 2026-10-28 规格）：
/// 左页签列表 / 右 5 栏位清单（勾选框·图标·名称·类型·路径）；
/// 右上角 删除（先确认）· 上移 · 下移 · 添加 4 个图标按钮，未勾选时删除/移动置灰；
/// 每个操作即时落盘（_settings.Save() → 面板同步重渲染）。
/// </summary>
public partial class EditItemsWindow : Window
{
    private readonly SettingsService _settings;
    private readonly bool _dark;
    private readonly ObservableCollection<string> _tabTitles = new();
    private readonly ObservableCollection<ItemRowVm> _rows = new();
    private int _tabIndex;

    public EditItemsWindow(SettingsService settings, int initialTab)
    {
        InitializeComponent();
        _settings = settings;
        _dark = settings.Profile.Settings.Theme.Contains("Dark", StringComparison.OrdinalIgnoreCase);
        DialogTheme.Apply(this, settings.Profile.Settings.Theme);

        var tabs = settings.Profile.Tabs;
        _tabIndex = Math.Clamp(initialTab, 0, Math.Max(0, tabs.Count - 1));
        foreach (var t in tabs) _tabTitles.Add(t.Title);

        TabList.ItemsSource = _tabTitles;
        TabList.SelectionChanged += (_, _) =>
        {
            _tabIndex = TabList.SelectedIndex;
            RebuildRows();
        };
        TabList.SelectedIndex = _tabIndex;

        ItemList.ItemsSource = _rows;
        RebuildRows();
    }

    private void OnHeaderDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try { DragMove(); } catch { }
        }
    }

    /// <summary>✕ / Esc 关闭（辉哥哥：要有返回/关闭功能，回到主面板）</summary>
    private void OnCloseRequest(object sender, RoutedEventArgs e) => Close();

    private void OnWinKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Escape) Close();
    }

    // ===== 行清单 =====

    /// <summary>按当前页签重建行；keepChecked 里的项恢复勾选（移动后保持勾选便于连续操作）</summary>
    private void RebuildRows(HashSet<Models.LaunchItem>? keepChecked = null)
    {
        foreach (var r in _rows) r.PropertyChanged -= OnRowChecked;
        _rows.Clear();

        var tabs = _settings.Profile.Tabs;
        if (_tabIndex < 0 || _tabIndex >= tabs.Count) return;
        foreach (var it in tabs[_tabIndex].Items)
        {
            var row = ItemRowVm.From(it, _dark);
            if (keepChecked != null && keepChecked.Contains(it)) row.Checked = true;
            row.PropertyChanged += OnRowChecked;
            _rows.Add(row);
        }
        UpdateStates();
    }

    private void OnRowChecked(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ItemRowVm.Checked)) UpdateStates();
    }

    private void UpdateStates()
    {
        var checkedCount = _rows.Count(r => r.Checked);
        var any = checkedCount > 0;
        DeleteBtn.IsEnabled = any;
        MoveUpBtn.IsEnabled = any;
        MoveDownBtn.IsEnabled = any;

        var tabs = _settings.Profile.Tabs;
        var title = _tabIndex >= 0 && _tabIndex < tabs.Count ? tabs[_tabIndex].Title : "";
        CountText.Text = $"{title} · 共 {_rows.Count} 项 · 已勾选 {checkedCount}";
    }

    // ===== 删除（先确认，辉哥哥定的口径）=====
    private void OnDelete(object sender, RoutedEventArgs e)
    {
        var checkedItems = _rows.Where(r => r.Checked).Select(r => r.Item).ToHashSet();
        if (checkedItems.Count == 0) return;

        if (!MiniDialogs.Confirm(this, "删除快捷项",
                $"将删除 {checkedItems.Count} 个快捷项，删除后立即保存，确定？", okText: "删除",
                theme: _settings.Profile.Settings.Theme))
            return;

        var tabs = _settings.Profile.Tabs;
        if (_tabIndex < 0 || _tabIndex >= tabs.Count) return;
        tabs[_tabIndex].Items.RemoveAll(checkedItems.Contains);
        _settings.Save();                 // 即时落盘 + 面板同步重渲染
        RebuildRows();
        ToastService.Show($"已删除 {checkedItems.Count} 项");
    }

    // ===== 上移 / 下移（勾选项整块移动，保持相对顺序）=====
    private void OnMoveUp(object sender, RoutedEventArgs e) => MoveChecked(-1);
    private void OnMoveDown(object sender, RoutedEventArgs e) => MoveChecked(1);

    private void MoveChecked(int dir)
    {
        var tabs = _settings.Profile.Tabs;
        if (_tabIndex < 0 || _tabIndex >= tabs.Count) return;
        var items = tabs[_tabIndex].Items;
        var checkedItems = _rows.Where(r => r.Checked).Select(r => r.Item).ToHashSet();
        if (checkedItems.Count == 0) return;

        if (dir < 0)
        {
            // 上移：每个勾选项与其前一个未勾选项交换（正序扫描 → 勾选块整体上移一格）
            for (var i = 1; i < items.Count; i++)
                if (checkedItems.Contains(items[i]) && !checkedItems.Contains(items[i - 1]))
                    (items[i - 1], items[i]) = (items[i], items[i - 1]);
        }
        else
        {
            // 下移：逆序扫描
            for (var i = items.Count - 2; i >= 0; i--)
                if (checkedItems.Contains(items[i]) && !checkedItems.Contains(items[i + 1]))
                    (items[i + 1], items[i]) = (items[i], items[i + 1]);
        }

        _settings.Save();
        RebuildRows(checkedItems); // 保持勾选，便于连续点

        // 移动审计（2026-10-28 E2E 实锤：一次 UIA Invoke 移了两格，驱动侧单发已证清白——
        // 留痕定位是产品双触发还是外部物理点击：每次移动记录方向/勾选数/新顺序）
        Shell.HookDiagnostics.Log(
            $"移动: dir={(dir < 0 ? "上移" : "下移")} 勾选={string.Join(",", checkedItems.Select(x => x.Name))} " +
            $"新顺序={string.Join(",", items.Select(x => x.Name))}");
    }

    // ===== 添加（三页签添加窗口，落点 = 当前选中页签）=====
    private void OnAdd(object sender, RoutedEventArgs e)
    {
        var tabs = _settings.Profile.Tabs;
        if (_tabIndex < 0 || _tabIndex >= tabs.Count) return;

        var w = new AddItemWindow(_settings, _tabIndex, _dark) { Owner = this };
        w.ShowDialog();
        RebuildRows(); // 添加窗口确定时已落盘，这里刷新清单
    }

    private void OnDone(object sender, RoutedEventArgs e) => Close();
}
