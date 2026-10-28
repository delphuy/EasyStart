using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EasyStart.Launcher;
using EasyStart.Models;
using EasyStart.Storage;

namespace EasyStart.Panel;

/// <summary>
/// 添加快捷项（P2，辉哥哥 2026-10-28 规格）——三页签：
/// ① 已安装的应用程序（注册表+开始菜单合并去重，图标异步补齐）
/// ② 系统功能（20 个系统动作条列多选）
/// ③ 自定义（类型+路径+浏览 / 图标自动抓取 / 名称自动带出可改）
/// 确定后全部落入指定页签并即时落盘。
/// </summary>
public partial class AddItemWindow : Window
{
    private readonly SettingsService _settings;
    private readonly int _tabIndex;
    private readonly bool _dark;
    private readonly ObservableCollection<ItemRowVm> _apps = new();
    private readonly ObservableCollection<ItemRowVm> _sys = new();
    private bool _scanStarted;
    private int _appTotal;
    private string? _customIconPath;  // favicon 落盘路径或用户手选图标
    private bool _iconPickedByUser;   // 用户选过图就不再让 favicon 抢
    private int _faviconSeq;         // 让过期的 favicon 回调失效

    public AddItemWindow(SettingsService settings, int tabIndex, bool dark)
    {
        InitializeComponent();
        _settings = settings;
        _tabIndex = tabIndex;
        _dark = dark;
        DialogTheme.Apply(this, settings.Profile.Settings.Theme);

        AppList.ItemsSource = _apps;
        SysList.ItemsSource = _sys;

        var tabs = settings.Profile.Tabs;
        TargetTabText.Text = _tabIndex >= 0 && _tabIndex < tabs.Count
            ? $"确定后添加到页签：「{tabs[_tabIndex].Title}」"
            : "确定后添加到当前页签";

        // 系统功能条列：直接用 ActionKey.All（20 项），图标走系统动作字形渲染
        foreach (var (key, label, _) in ActionKey.All)
        {
            var row = new ItemRowVm
            {
                Item = new LaunchItem { Type = LaunchItemType.System, Action = key, Name = label },
                Icon = IconExtractor.Extract(key, 24, null, dark),
            };
            row.PropertyChanged += OnRowChecked;
            _sys.Add(row);
        }

        CustomType.SelectedIndex = 0;
        Loaded += (_, _) => StartScan();
    }

    private void OnHeaderDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try { DragMove(); } catch { }
        }
    }

    /// <summary>✕ / Esc 关闭（辉哥哥：要有返回/关闭功能，回到编辑窗）</summary>
    private void OnCloseRequest(object sender, RoutedEventArgs e) => Close();

    private void OnWinKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Escape) Close();
    }

    // ===== 页签 1：已安装的应用程序 =====

    private void StartScan()
    {
        if (_scanStarted) return;
        _scanStarted = true;

        Task.Run(() =>
        {
            var list = InstalledAppScanner.Scan();
            Dispatcher.Invoke(() =>
            {
                foreach (var it in list)
                {
                    var row = new ItemRowVm { Item = it }; // 图标先空，异步补齐
                    row.PropertyChanged += OnRowChecked;
                    _apps.Add(row);
                }
                _appTotal = list.Count;
                UpdateStatus();
            });

            // 图标后台逐个补（列表已先出，不卡首屏；提取结果 Freeze 后跨线程安全）
            foreach (var row in _apps.ToArray())
            {
                var icon = ItemRowVm.From(row.Item, _dark).Icon;
                Dispatcher.BeginInvoke(() =>
                {
                    row.Icon = icon;
                    if (!IsLoaded) return; // 窗口已关就别再刷了（后面的还会走完，无副作用）
                });
            }
        });
    }

    private void UpdateStatus()
    {
        var checkedCount = _apps.Count(r => r.Checked) + _sys.Count(r => r.Checked);
        StatusText.Text = checkedCount > 0
            ? $"共 {_appTotal} 个程序 · 已勾选 {checkedCount} 项"
            : $"共 {_appTotal} 个程序，勾选后点确定（图标在后台陆续加载）";
    }

    private void OnRowChecked(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ItemRowVm.Checked))
        {
            UpdateStatus();
            // 全选框联动（辉哥哥 2026-10-28：两页签都要全选/取消全选）。
            // 置同步护栏：行变化反推全选框时别再触发"设置所有行"，否则单向清空会误伤。
            _selectAllSyncing = true;
            AppSelectAll.IsChecked = _apps.Count > 0 && _apps.All(r => r.Checked);
            SysSelectAll.IsChecked = _sys.Count > 0 && _sys.All(r => r.Checked);
            _selectAllSyncing = false;
        }
    }

    private bool _selectAllSyncing;

    // Checked/Unchecked 接线（不用 Click：UIA Toggle 不走 Click，实测实锤）
    private void OnAppSelectAllChecked(object sender, RoutedEventArgs e)
    {
        if (_selectAllSyncing) return;
        var on = AppSelectAll.IsChecked == true;
        foreach (var r in _apps) r.Checked = on;
    }

    private void OnSysSelectAllChecked(object sender, RoutedEventArgs e)
    {
        if (_selectAllSyncing) return;
        var on = SysSelectAll.IsChecked == true;
        foreach (var r in _sys) r.Checked = on;
    }

    // ===== 页签 3：自定义 =====

    private void OnCustomTypeChanged(object sender, SelectionChangedEventArgs e)
    {
        var isUrl = CustomType.SelectedIndex == 2;
        CustomBrowse.IsEnabled = !isUrl;
        CustomPathLabel.Text = isUrl ? "网址" : "路径";
        CustomPath.ToolTip = isUrl ? "http:// 或 https://，缺省自动补 https://" : "目标路径";
    }

    private void OnCustomPathLostFocus(object sender, RoutedEventArgs e) => UpdateCustomPreview();

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Title = "选择目标" };
        dlg.Filter = CustomType.SelectedIndex == 0
            ? "程序|*.exe;*.lnk;*.bat;*.cmd;*.msc"
            : "所有文件|*.*";
        if (dlg.ShowDialog(this) == true)
        {
            CustomPath.Text = dlg.FileName;
            UpdateCustomPreview();
        }
    }

    /// <summary>路径变化：自动抓图标 + 名称自动带出（可改，不覆盖已填）</summary>
    private void UpdateCustomPreview()
    {
        var path = CustomPath.Text.Trim();
        if (path.Length == 0) return;

        if (CustomType.SelectedIndex == 2)
        {
            var url = NormalizeUrl(path);
            if (Uri.TryCreate(url, UriKind.Absolute, out var u) && (u.Scheme == "http" || u.Scheme == "https"))
            {
                // 立即出站点字母头像（不再是灰圈），用户没手动选图时再异步抓 favicon
                if (!_iconPickedByUser)
                {
                    _customIconPath = null;
                    CustomIcon.Source = IconExtractor.Extract(url, 32, null, _dark);
                }
                if (string.IsNullOrWhiteSpace(CustomName.Text))
                    CustomName.Text = u.Host;
                TryFetchFavicon(u.Host, u.Scheme);
            }
            return;
        }

        if (File.Exists(path) || Directory.Exists(path))
        {
            CustomIcon.Source = IconExtractor.Extract(path, 32, null, _dark);
            if (string.IsNullOrWhiteSpace(CustomName.Text))
            {
                var probe = ItemFactory.FromDrop(path, null);
                if (probe != null) CustomName.Text = probe.Name ?? "";
            }
        }
    }

    private static string NormalizeUrl(string s)
        => s.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
           || s.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? s
            : "https://" + s;

    // ===== 网址图标：favicon 异步抓取 + 手动选图（辉哥哥 2026-10-28）=====

    /// <summary>抓到就存到 %LOCALAPPDATA%\EasyStart\icons\<host>.ico 并作为该项图标落盘；
    /// 抓不到保持字母头像（预览与面板一致）。用户手动选过图则不打扰。</summary>
    private async void TryFetchFavicon(string host, string scheme)
    {
        if (_iconPickedByUser || string.IsNullOrEmpty(host)) return;
        var seq = ++_faviconSeq;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var bytes = await http.GetByteArrayAsync($"{scheme}://{host}/favicon.ico");
            if (bytes.Length < 8 || !LooksLikeImage(bytes)) return;

            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "EasyStart", "icons");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, host.Replace(':', '_') + ".ico");
            await File.WriteAllBytesAsync(file, bytes);

            await Dispatcher.InvokeAsync(() =>
            {
                if (seq != _faviconSeq || _iconPickedByUser || !IsLoaded) return;
                _customIconPath = file;
                try { CustomIcon.Source = IconExtractor.Extract(null, 32, file, _dark); }
                catch { /* 预览失败不致命，面板侧还有字母头像兜底 */ }
            });
        }
        catch
        {
            // 离线 / 404 / 超时 → 保持字母头像，不报错
        }
    }

    /// <summary>有些站对 favicon.ico 返回 404 HTML 页，按魔数过滤</summary>
    private static bool LooksLikeImage(byte[] b)
    {
        // ICO: 00 00 01 00；PNG: 89 50 4E 47；GIF: 47 49 46 38
        return (b[0] == 0 && b[1] == 0 && b[2] == 1 && b[3] == 0)
            || (b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47)
            || (b[0] == 0x47 && b[1] == 0x49 && b[2] == 0x46 && b[3] == 0x38);
    }

    private void OnPickIcon(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择图标",
            Filter = "图标文件|*.ico;*.png;*.jpg;*.bmp",
        };
        if (dlg.ShowDialog(this) == true)
        {
            _customIconPath = dlg.FileName;
            _iconPickedByUser = true;
            _faviconSeq++; // 让飞行中的 favicon 失效
            try { CustomIcon.Source = IconExtractor.Extract(null, 32, _customIconPath, _dark); }
            catch { ToastService.Show("这个文件当不了图标，换一个试试", isError: true); }
        }
    }

    private void OnResetIcon(object sender, RoutedEventArgs e)
    {
        _customIconPath = null;
        _iconPickedByUser = false;
        UpdateCustomPreview();
    }

    /// <summary>自定义页签 → LaunchItem；没填或非法返回 null</summary>
    private LaunchItem? BuildCustom()
    {
        var path = CustomPath.Text.Trim();
        var name = CustomName.Text.Trim();
        if (path.Length == 0) return null;

        if (CustomType.SelectedIndex == 2) // 网址
        {
            var url = NormalizeUrl(path);
            if (!Uri.TryCreate(url, UriKind.Absolute, out var u)
                || (u.Scheme != "http" && u.Scheme != "https"))
            {
                ToastService.Show("网址格式不对：" + path, isError: true);
                return null;
            }
            return new LaunchItem
            {
                Type = LaunchItemType.Uri,
                Uri = u.ToString(),
                Name = name.Length > 0 ? name : u.Host,
                IconPath = _customIconPath, // favicon / 手选图标落盘，面板直接用
            };
        }

        // 程序 / 文件（ItemFactory 自带分类：文件夹→Folder，exe/lnk/bat→Shortcut）
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            ToastService.Show("路径不存在：" + path, isError: true);
            return null;
        }
        var item = ItemFactory.FromDrop(path, null);
        if (item == null) return null;
        if (name.Length > 0) item.Name = name;
        item.IconPath = _customIconPath; // 手选图标同样落到程序/文件项
        return item;
    }

    // ===== 确定 / 取消 =====

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        var tabs = _settings.Profile.Tabs;
        if (_tabIndex < 0 || _tabIndex >= tabs.Count) return;
        var items = tabs[_tabIndex].Items;

        var added = 0;
        foreach (var r in _apps.Where(r => r.Checked))
        {
            items.Add(r.Item);
            added++;
        }
        foreach (var r in _sys.Where(r => r.Checked))
        {
            items.Add(new LaunchItem
            {
                Type = LaunchItemType.System,
                Action = r.Item.Action,
                Name = r.Item.Name,
            });
            added++;
        }
        var custom = BuildCustom();
        if (custom != null)
        {
            items.Add(custom);
            added++;
        }

        if (added == 0)
        {
            ToastService.Show("没有勾选任何项，也没有可用的自定义项", isError: true);
            return;
        }

        _settings.Save(); // 即时落盘 + 面板同步重渲染
        ToastService.Show($"已添加 {added} 项到「{tabs[_tabIndex].Title}」");
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();
}
