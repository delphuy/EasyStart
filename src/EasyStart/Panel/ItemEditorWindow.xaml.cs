using System.Windows;
using System.Windows.Controls;
using EasyStart.Models;
using EasyStart.Storage;

namespace EasyStart.Panel;

/// <summary>编辑/新增快捷项（P0-F2）</summary>
public partial class ItemEditorWindow : Window
{
    private LaunchItem _item = null!;
    private SettingsService _settings = null!;
    private bool _saveAfterEdit;
    private bool _isNew;

    public ItemEditorWindow()
    {
        InitializeComponent();
    }

    /// <summary>新增或编辑</summary>
    public static void ShowFor(Window owner, LaunchItem item, SettingsService settings, bool saveAfterEdit, bool isNew = false)
    {
        var win = new ItemEditorWindow { Owner = owner };
        win.Init(item, settings, saveAfterEdit, isNew);
        win.Show();
    }

    private void Init(LaunchItem item, SettingsService settings, bool saveAfterEdit, bool isNew)
    {
        _item = item;
        _settings = settings;
        _saveAfterEdit = saveAfterEdit;
        _isNew = isNew;
        DialogTheme.Apply(this, settings.Profile.Settings.Theme);

        NameBox.Text = item.Name ?? "";
        TargetBox.Text = item.Target ?? item.Uri ?? "";
        ArgsBox.Text = item.Args ?? "";
        ShortcutKeyBox.Text = item.ShortcutKey ?? "";
        PrivilegedBox.IsChecked = item.PreferPrivileged;

        switch (item.Type)
        {
            case LaunchItemType.Shortcut: TypeBox.SelectedIndex = 0; break;
            case LaunchItemType.Uri: TypeBox.SelectedIndex = 1; break;
            case LaunchItemType.Folder: TypeBox.SelectedIndex = 2; break;
            case LaunchItemType.System: TypeBox.SelectedIndex = 3; break;
        }
        ToggleFields();
    }

    private void OnTypeChanged(object sender, SelectionChangedEventArgs e) => ToggleFields();

    private void ToggleFields()
    {
        var isUri = TypeBox.SelectedIndex == 1;
        var isSystem = TypeBox.SelectedIndex == 3;
        ArgsBox.IsEnabled = !isUri && !isSystem;
        PrivilegedBox.IsEnabled = !isUri && !isSystem;
        TargetBox.ToolTip = isUri ? "http:// 或 https://" : "目标路径";
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        _item.Name = NameBox.Text.Trim();
        _item.Args = ArgsBox.Text.Trim();
        _item.ShortcutKey = ShortcutKeyBox.Text.Trim();
        _item.PreferPrivileged = PrivilegedBox.IsChecked == true;
        switch (TypeBox.SelectedIndex)
        {
            case 0:
                _item.Type = LaunchItemType.Shortcut;
                _item.Target = TargetBox.Text.Trim();
                _item.Uri = null;
                break;
            case 1:
                _item.Type = LaunchItemType.Uri;
                _item.Uri = TargetBox.Text.Trim();
                _item.Target = null;
                break;
            case 2:
                _item.Type = LaunchItemType.Folder;
                _item.Target = TargetBox.Text.Trim();
                break;
            case 3:
                _item.Type = LaunchItemType.System;
                _item.Action = TargetBox.Text.Trim();
                break;
        }

        if (_isNew)
        {
            var tabs = _settings.Profile.Tabs;
            tabs[0].Items.Add(_item);
        }

        if (_saveAfterEdit)
        {
            _settings.Save();
            (Owner as PanelWindow)?.RaiseRefresh();
        }
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();

    // ===== 无边框弹窗补齐（辉哥哥 2026-10-28：要有 ✕/Esc，与别的弹窗一致） =====

    /// <summary>标题栏拖动</summary>
    private void OnHeaderDrag(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed)
        {
            try { DragMove(); } catch { }
        }
    }

    private void OnCloseRequest(object sender, RoutedEventArgs e) => Close();

    /// <summary>Esc 关窗（取消 = 关窗，语义一致）</summary>
    private void OnWinKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Escape) Close();
    }
}
