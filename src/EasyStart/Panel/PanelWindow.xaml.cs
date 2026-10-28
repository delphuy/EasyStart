using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using EasyStart.Launcher;
using EasyStart.Models;
using EasyStart.Storage;

namespace EasyStart.Panel;

public partial class PanelWindow : Window
{
    private SettingsService _settings;
    private int _currentTab;
    private bool _isClosing;
    /// <summary>编辑/添加弹窗打开中：抑制失焦自动隐藏（P2，辉哥哥：选中菜单项后主界面不消失）</summary>
    private bool _editorOpen;
    public bool AllowClose { get; set; }

    // ===== 无边框窗口手动 resize（P1-F5 辉哥哥要求拖拽调整窗体大小）=====
    private enum ResizeDir { None, Left, Right, Top, Bottom, TopLeft, TopRight, BottomLeft, BottomRight }
    private ResizeDir _resizeDir = ResizeDir.None;
    private Point _resizeStartMouse;   // 屏幕坐标
    private Point _resizeStartPos;      // 窗口 Left/Top
    private Size _resizeStartSize;      // 窗口 Width/Height
    private const double ResizeThreshold = 10; // 边缘命中阈值（px）

    public PanelWindow(SettingsService settings)
    {
        InitializeComponent();
        _settings = settings;
        Loaded += OnLoaded;

        // 拖拽快捷方式/文件/文件夹/网址到面板 → 识别为快捷项（P0-F3）
        AllowDrop = true;
        DragOver += OnDragOver;
        Drop += OnDrop;

        // 无边框窗口手动 resize：监听窗口级鼠标事件
        PreviewMouseLeftButtonDown += OnResizeMouseLeftButtonDown;
        MouseMove += OnResizeMouseMove;
        MouseLeftButtonUp += OnResizeMouseLeftButtonUp;

        // + 号按钮：放进页签末尾的 TabRow StackPanel，无底色，随页签增删移动（P1-F1）
        CreateAddTabButton();
    }

    // ===== resize 事件 =====
    private void OnResizeMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_resizeDir != ResizeDir.None) return;
        var p = e.GetPosition(this);
        bool l = p.X < ResizeThreshold;
        bool r = p.X > ActualWidth - ResizeThreshold;
        bool t = p.Y < ResizeThreshold;
        bool b = p.Y > ActualHeight - ResizeThreshold;
        if (!l && !r && !t && !b) return;

        _resizeDir = ResizeDir.None;
        if (t && l) _resizeDir = ResizeDir.TopLeft;
        else if (t && r) _resizeDir = ResizeDir.TopRight;
        else if (b && l) _resizeDir = ResizeDir.BottomLeft;
        else if (b && r) _resizeDir = ResizeDir.BottomRight;
        else if (l) _resizeDir = ResizeDir.Left;
        else if (r) _resizeDir = ResizeDir.Right;
        else if (t) _resizeDir = ResizeDir.Top;
        else if (b) _resizeDir = ResizeDir.Bottom;

        _resizeStartMouse = PointToScreen(e.GetPosition(this));
        _resizeStartPos = new Point(Left, Top);
        _resizeStartSize = new Size(Width, Height);
        CaptureMouse();
        e.Handled = true;
    }

    private void OnResizeMouseMove(object sender, MouseEventArgs e)
    {
        if (_resizeDir == ResizeDir.None || !IsMouseCaptured)
        {
            UpdateResizeCursor(e);
            return;
        }
        var cur = PointToScreen(e.GetPosition(this));
        double dx = cur.X - _resizeStartMouse.X;
        double dy = cur.Y - _resizeStartMouse.Y;

        double newL = _resizeStartPos.X;
        double newT = _resizeStartPos.Y;
        double newW = _resizeStartSize.Width;
        double newH = _resizeStartSize.Height;

        switch (_resizeDir)
        {
            case ResizeDir.Left:
                newL = _resizeStartPos.X + dx;
                newW = _resizeStartSize.Width - dx;
                break;
            case ResizeDir.Right:
                newW = _resizeStartSize.Width + dx;
                break;
            case ResizeDir.Top:
                newT = _resizeStartPos.Y + dy;
                newH = _resizeStartSize.Height - dy;
                break;
            case ResizeDir.Bottom:
                newH = _resizeStartSize.Height + dy;
                break;
            case ResizeDir.TopLeft:
                newL = _resizeStartPos.X + dx;
                newT = _resizeStartPos.Y + dy;
                newW = _resizeStartSize.Width - dx;
                newH = _resizeStartSize.Height - dy;
                break;
            case ResizeDir.TopRight:
                newT = _resizeStartPos.Y + dy;
                newW = _resizeStartSize.Width + dx;
                newH = _resizeStartSize.Height - dy;
                break;
            case ResizeDir.BottomLeft:
                newL = _resizeStartPos.X + dx;
                newW = _resizeStartSize.Width - dx;
                newH = _resizeStartSize.Height + dy;
                break;
            case ResizeDir.BottomRight:
                newW = _resizeStartSize.Width + dx;
                newH = _resizeStartSize.Height + dy;
                break;
        }

        // 限制最小尺寸
        double minW = MinWidth > 0 ? MinWidth : 320;
        double minH = MinHeight > 0 ? MinHeight : 240;
        if (newW < minW)
        {
            newW = minW;
            if (_resizeDir is ResizeDir.Left or ResizeDir.BottomLeft or ResizeDir.TopLeft)
                newL = _resizeStartPos.X + _resizeStartSize.Width - minW;
        }
        if (newH < minH)
        {
            newH = minH;
            if (_resizeDir is ResizeDir.Top or ResizeDir.TopLeft or ResizeDir.TopRight)
                newT = _resizeStartPos.Y + _resizeStartSize.Height - minH;
        }

        Left = newL;
        Top = newT;
        Width = newW;
        Height = newH;
    }

    private void OnResizeMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_resizeDir != ResizeDir.None)
        {
            _resizeDir = ResizeDir.None;
            ReleaseMouseCapture();
            SaveGeometryToDisk(); // resize 完落盘（辉哥哥：记住上一次大小）
        }
    }
    private void UpdateResizeCursor(MouseEventArgs e)
    {
        var p = e.GetPosition(this);
        bool l = p.X < ResizeThreshold;
        bool r = p.X > ActualWidth - ResizeThreshold;
        bool t = p.Y < ResizeThreshold;
        bool b = p.Y > ActualHeight - ResizeThreshold;
        if (l && t || r && b) Cursor = System.Windows.Input.Cursors.SizeNWSE;
        else if (l && b || r && t) Cursor = System.Windows.Input.Cursors.SizeNESW;
        else if (l || r) Cursor = System.Windows.Input.Cursors.SizeWE;
        else if (t || b) Cursor = System.Windows.Input.Cursors.SizeNS;
    }

    /// <summary>持久化窗口几何到盘（失焦隐藏/关闭/手动 resize/拖动 结束时调用）</summary>
    private void SaveGeometryToDisk()
    {
        _settings.Profile.Window.X = Left;
        _settings.Profile.Window.Y = Top;
        _settings.Profile.Window.Width = Width;
        _settings.Profile.Window.Height = Height;
        _settings.Save();
    }

    /// <summary>创建 + 号按钮（P1-F1 无底色），作为页签条末尾的固定项</summary>
    private void CreateAddTabButton()
    {
        var btn = new Button
        {
            Content = "+",
            FontSize = 16,
            Width = 28,
            Height = 28,
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = "新增页签",
            // 无底色：透明背景 + 无边框 + 文字色随主题（RenderTabs 里会刷新）
            Background = System.Windows.Media.Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = System.Windows.Media.Brushes.DodgerBlue,
            BorderBrush = System.Windows.Media.Brushes.Transparent,
        };
        btn.Click += OnAddTabClick;
        _addTabButton = btn;
    }

    private Button? _addTabButton;

    /// <summary>窗口尺寸变化时重算列数（P1-F5 图标排列动态调整）</summary>
    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        ApplyColumnCount();
        ShowItems();
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.Copy;
        if (e.Data.GetDataPresent(DataFormats.FileDrop)
            || e.Data.GetDataPresent(DataFormats.Text))
        {
            e.Handled = true;
        }
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        // 拖到面板主体（非某个页签标签）→ 进当前页签
        var added = AddDropToTab(e, _currentTab);
        if (added > 0)
        {
            var tabs = _settings.Profile.Tabs;
            e.Handled = true;
            ToastService.Show($"已添加 {added} 项到「{tabs[_currentTab].Title}」");
        }
    }

    /// <summary>把拖入的外部文件/快捷方式/URL 识别成 LaunchItem 加进指定页签，返回添加数</summary>
    private int AddDropToTab(DragEventArgs e, int tabIndex)
    {
        var items = _settings.Profile.Tabs;
        if (tabIndex < 0 || tabIndex >= items.Count) return 0;
        if (items.Count == 0) items.Add(new PanelTab { Title = "常用" });

        var added = 0;
        string[] files = Array.Empty<string>();
        if (e.Data.GetData(DataFormats.FileDrop) is string[] f) files = f;
        string? text = e.Data.GetData(DataFormats.Text) as string;

        // 文件/文件夹/快捷方式
        foreach (var path in files)
        {
            var it = ItemFactory.FromDrop(path, null);
            if (it != null)
            {
                items[tabIndex].Items.Add(it);
                added++;
            }
        }
        // 纯文本 / URL（无文件时）
        if (!string.IsNullOrWhiteSpace(text) && files.Length == 0)
        {
            var it = ItemFactory.FromDrop(null, text);
            if (it != null)
            {
                items[tabIndex].Items.Add(it);
                added++;
            }
        }

        if (added > 0)
        {
            _settings.Save();
            // 刷新：如果拖到的是当前页签就重画，否则只重画标签条
            if (tabIndex == _currentTab) ShowItems();
            RenderTabs();
        }
        return added;
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyColumnCount();
        RenderTabs();
        ShowItems();
        ApplyGeometry();
        ApplySettings();
    }

    /// <summary>把当前设置真正应用到面板（辉哥哥：设置页保存后行为/外观要即时生效）</summary>
    public void ApplySettings()
    {
        var s = _settings.Profile.Settings;

        // 外观：主题全量（背景/文字/页签/hover/图标标签）+ 不透明度 + 图标尺寸
        ThemeService.Apply(s, this);
        ApplyColumnCount();

        // 行为开关
        // 鼠标中键呼出：唯一入口 = App 的 WH_MOUSE_LL 全局钩子（MiddleClickWakeService）。
        // 不再挂面板级中键处理器：钩子在消息到达窗口【之前】就触发，若面板也挂处理器，
        // 可见时点中键会"Hide 后又被钩子 Show 回来"双双抵消，表现为点了没反应。
        // （辉哥哥实测根因：钩子里 mouseData==0x0004 这道门永远不匹配——中键按钮号在消息号 0x0207 里，
        //  mouseData 不携带标准按钮号；门已删，见 MiddleClickWakeService）
        // 滚轮切换标签：用 PreviewMouseWheel（隧道）在 ScrollViewer 消费前拦截，
        // 否则 ItemHost 在 ScrollViewer 里，滚轮被 ScrollViewer 吞掉、切不了标签
        PreviewMouseWheel -= OnPanelPreviewMouseWheel;
        if (s.SwitchTabsOnWheel) PreviewMouseWheel += OnPanelPreviewMouseWheel;

        // 数字键切换标签/启动项
        PreviewKeyDown -= OnPanelPreviewKeyDown;
        if (s.NumberKeyLaunch) PreviewKeyDown += OnPanelPreviewKeyDown;

        // 重画（图标尺寸/主题可能变了）
        RenderTabs();
        ShowItems();
    }

    private void OnPanelPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!_settings.Profile.Settings.SwitchTabsOnWheel) return;
        var tabs = _settings.Profile.Tabs;
        if (tabs.Count == 0) return;
        int delta = e.Delta > 0 ? -1 : 1; // 滚上 = 上一个标签
        int next = _currentTab + delta;
        if (next < 0) next = _settings.Profile.Settings.WheelCirculate ? tabs.Count - 1 : 0;
        if (next >= tabs.Count) next = _settings.Profile.Settings.WheelCirculate ? 0 : tabs.Count - 1;
        if (next != _currentTab) SelectTab(next);
        // 切完标签后吞掉滚轮，避免 ScrollViewer 同时滚动图标网格
        e.Handled = true;
    }

    private void OnPanelPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_settings.Profile.Settings.NumberKeyLaunch) return;
        // 数字键 1-9：切到第 N 个标签
        if (e.Key >= Key.D0 && e.Key <= Key.D9)
        {
            int idx = e.Key == Key.D0 ? 9 : (int)e.Key - (int)Key.D1;
            var tabs = _settings.Profile.Tabs;
            if (idx < tabs.Count)
            {
                SelectTab(idx);
                e.Handled = true;
            }
        }
    }

    private System.Windows.Threading.DispatcherTimer? _hoverTimer;

    /// <summary>悬停切换标签：由每个页签标签的 MouseEnter/Leave 驱动（见 RenderTabs）</summary>

    /// <summary>应用窗口几何（window.json），并保证窗口不跑出工作区（辉哥哥：记住上一次大小/位置）</summary>
    private void ApplyGeometry()
    {
        var g = _settings.Profile.Window;
        Left = g.X;
        Top = g.Y;
        Width = g.Width;
        Height = g.Height;

        // 屏幕边界校验：上次的显示器拔了/分辨率变了时，窗口别跑出可见区
        try
        {
            var wa = SystemParameters.WorkArea;
            if (wa.Width > 0 && wa.Height > 0)
            {
                // 窗口至少 1/3 在工作区内才算正常，否则回落到默认居中
                double visibleW = Math.Max(0, Math.Min(Left + Width, wa.Left + wa.Width) - Math.Max(Left, wa.Left));
                double visibleH = Math.Max(0, Math.Min(Top + Height, wa.Top + wa.Height) - Math.Max(Top, wa.Top));
                if (visibleW < Width * 0.3 || visibleH < Height * 0.3)
                {
                    // 超出工作区：缩到工作区内（保持尺寸，位置移回可见区）
                    Left = Math.Clamp(Left, wa.Left, wa.Left + wa.Width - Width);
                    Top = Math.Clamp(Top, wa.Top, wa.Top + wa.Height - Height);
                }
            }
        }
        catch { }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!AllowClose)
        {
            e.Cancel = true;
            SaveGeometryToDisk(); // 隐藏前落盘
            Hide();
            return;
        }
        _isClosing = true;
        SaveGeometryToDisk(); // 真正关闭前也落盘
        base.OnClosing(e);
    }

    private void OnDeactivated(object sender, EventArgs e)
    {
        // 编辑/添加弹窗打开中 → 面板必须保持可见（弹窗抢焦点必触发失焦，这里拦下）
        if (_editorOpen) return;

        var s = _settings.Profile.Settings;
        // 失焦自动隐藏（对齐 Rolan hideOnSmartDetect + preventHideOnCtrlHold）
        if (!s.HideOnSmartDetect) return;

        // 辉哥哥实测（diagnostics.log 5 次失焦全 False）：只查 VK_LCONTROL/VK_RCONTROL 读不到他的 Ctrl。
        // 加 VK_CONTROL(0x11) 兜底 + 把三个键位的原始值落盘，有据可查。
        var (ctrl, raw) = Shell.HookDiagnostics.CtrlState();
        Shell.HookDiagnostics.Log($"失焦: PreventCtrl={s.PreventHideOnCtrlHold} 物理Ctrl={ctrl} ({raw})");
        if (s.PreventHideOnCtrlHold && ctrl)
            return; // 失焦那刻 Ctrl 按着 → 不隐藏（辉哥哥：按住 Ctrl 时不隐藏）

        SaveGeometryToDisk(); // 隐藏前落盘（辉哥哥：窗口位置/大小要记住上一次的）
        Shell.HookDiagnostics.Log("失焦→隐藏");
        Hide();
    }

    // ===== P2 面板右键菜单（编辑快捷项 / 重新加载图标，辉哥哥 2026-10-28 规格）=====

    /// <summary>面板空白处右键：编辑快捷项 / 重新加载图标（两项任一点击，面板都不消失）。
    /// 点在快捷项或页签上时直接放行——它们有自己的右键菜单，面板级菜单不弹。</summary>
    private void OnPanelPreviewMouseRightButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var d = e.OriginalSource as System.Windows.DependencyObject;
        while (d != null && !ReferenceEquals(d, this))
        {
            if (d is LaunchItemControl || ReferenceEquals(d, TabStrip))
                return;
            d = System.Windows.Media.VisualTreeHelper.GetParent(d);
        }

        Shell.HookDiagnostics.Log($"面板右键: 来源={e.OriginalSource?.GetType().Name ?? "null"} → 打开菜单");
        var menu = new ContextMenu();
        void Add(string text, RoutedEventHandler onClick)
        {
            var mi = new MenuItem { Header = text };
            mi.Click += onClick;
            menu.Items.Add(mi);
        }
        Add("编辑快捷项", (_, _) => OpenEditItems());
        Add("重新加载图标", (_, _) => ReloadIcons());
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        menu.IsOpen = true;
        e.Handled = true;
    }

    /// <summary>批量编辑弹窗：打开期间 _editorOpen 抑制失焦自动隐藏；关掉后把面板带回前台</summary>
    private void OpenEditItems()
    {
        _editorOpen = true;
        Shell.HookDiagnostics.Log("编辑弹窗: 打开");
        try
        {
            ModalScrim.Visibility = Visibility.Visible;   // 蒙版压暗面板内容（辉哥哥：不让面板干扰弹窗）
            var w = new EditItemsWindow(_settings, _currentTab) { Owner = this };
            w.ShowDialog();
        }
        catch (Exception ex)
        {
            Shell.HookDiagnostics.Log("编辑弹窗异常: " + ex.Message);
        }
        finally
        {
            ModalScrim.Visibility = Visibility.Collapsed;
            _editorOpen = false;
            Show();
            Activate();
            Shell.HookDiagnostics.Log("编辑弹窗: 已关闭，面板保持显示");
        }
    }

    /// <summary>重新加载图标：IconExtractor 无持久缓存，重渲染 = 图标全量重抓重绘
    /// （其余页签切换显示时本来就会重抓，这里把当前页签立即刷掉）</summary>
    private void ReloadIcons()
    {
        Shell.HookDiagnostics.Log("重新加载图标: 开始重渲染");
        RenderTabs();
        ShowItems();
        Shell.HookDiagnostics.Log("重新加载图标: 完成");
        ToastService.Show("图标已重新加载");
    }

    /// <summary>图标网格排版：固定行高/行间距（辉哥哥要求），宽度变化只影响列数</summary>
    private void ApplyColumnCount()
    {
        // WrapPanel：每行高度由内容（固定 cardH）决定，行间距固定 = 控件 margin；
        // 宽度变小 → 每行容纳的图标自动减少，往后排；高度变化不影响行位置
        ItemHost.ItemsPanel = new ItemsPanelTemplate(
            new System.Windows.FrameworkElementFactory(typeof(WrapPanel)));
    }

    /// <summary>渲染标签条（胶囊 + + 号按钮在 XAML 里）</summary>
    private void RenderTabs()
    {
        TabStrip.Items.Clear();
        var tabs = _settings.Profile.Tabs;
        var pal = ThemePalette.Get(_settings.Profile.Settings.Theme);
        for (int i = 0; i < tabs.Count; i++)
        {
            var t = tabs[i];
            var index = i;
            var border = new Border
            {
                Padding = new Thickness(14, 6, 14, 6),
                Margin = new Thickness(2),
                CornerRadius = new CornerRadius(20),
                // 辉哥哥要求：去掉蓝色椭圆底色。选中页签只用文字加粗+变色，不加背景
                Background = System.Windows.Media.Brushes.Transparent,
                Cursor = Cursors.Hand,
                ToolTip = "点击切换 · 右键删除/重命名 · 拖 item 到此处跨页签移动",
            };
            var label = new TextBlock
            {
                Text = t.Title,
                FontSize = 13,
                FontWeight = index == _currentTab ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = index == _currentTab
                    ? pal.AccentBrush()
                    : pal.TextSec(),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            // 选中页签底部加一条细线（代替底色，更简洁）
            if (index == _currentTab)
            {
                var underline = new System.Windows.Shapes.Rectangle
                {
                    Height = 2,
                    Fill = pal.AccentBrush(),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Width = 40,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(0, 4, 0, 0),
                };
                var gp = new System.Windows.Controls.Grid();
                gp.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });
                gp.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });
                System.Windows.Controls.Grid.SetRow(label, 0);
                System.Windows.Controls.Grid.SetRow(underline, 1);
                gp.Children.Add(label);
                gp.Children.Add(underline);
                border.Child = gp;
            }
            else
            {
                border.Child = label;
            }
            border.MouseLeftButtonUp += (_, _) => SelectTab(index);
            border.MouseRightButtonUp += (_, _) => ShowTabContextMenu(index, border);
            // 悬停切换标签（辉哥哥：悬停某页签停留 HoverSwitchDelayMs 就切到该页签）：每个页签自己开 timer
            border.MouseEnter += (_, _) =>
            {
                if (!_settings.Profile.Settings.SwitchTabsOnHover) return;
                _hoverTimer?.Stop();
                _hoverTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(_settings.Profile.Settings.HoverSwitchDelayMs)
                };
                _hoverTimer.Tick += (_, _) =>
                {
                    _hoverTimer?.Stop();
                    if (index != _currentTab) SelectTab(index);
                };
                _hoverTimer.Start();
            };
            border.MouseLeave += (_, _) =>
            {
                _hoverTimer?.Stop();
                _hoverTimer = null;
            };
            // 拖拽目标（辉哥哥要求）：拖内部图标到页签标签 → 进该页签；拖桌面文件/快捷方式到页签标签 → 加进该页签
            border.AllowDrop = true;
            border.DragOver += (s, e2) =>
            {
                if (e2.Data.GetDataPresent("EasyStart.ItemMove")
                    || e2.Data.GetDataPresent(DataFormats.FileDrop)
                    || e2.Data.GetDataPresent(DataFormats.Text))
                {
                    e2.Effects = DragDropEffects.Move;
                    // 高亮该页签（提示可放），用主题强调色
                    border.BorderThickness = new Thickness(2);
                    border.BorderBrush = pal.AccentBrush();
                }
            };
            border.DragLeave += (_, _) =>
            {
                border.BorderThickness = new Thickness(0);
            };
            border.Drop += (s, e2) =>
            {
                border.BorderThickness = new Thickness(0);
                if (e2.Data.GetData("EasyStart.ItemMove") is LaunchItemControl.ItemMoveData data)
                {
                    // 内部图标跨页签：进该页签末尾
                    MoveItemToTab(data, index);
                    e2.Handled = true;
                    return;
                }
                // 外部文件/快捷方式/URL → 识别后加进该页签
                var added = AddDropToTab(e2, index);
                if (added > 0)
                {
                    e2.Handled = true;
                    ToastService.Show($"已添加 {added} 项到「{tabs[index].Title}」");
                }
            };
            TabStrip.Items.Add(border);
        }
        // 标签条本身也允许 item 跨页签拖拽
        TabStrip.AllowDrop = true;

        // + 号按钮作为页签条末尾的固定项（P1-F1 随增删移动，无底色，文字色随主题）
        if (_addTabButton != null)
        {
            _addTabButton.Foreground = pal.AccentBrush();
            TabStrip.Items.Add(_addTabButton);
        }
    }

    /// <summary>页签右键菜单（P0-F12 页签增删改）</summary>
    private void ShowTabContextMenu(int tabIndex, FrameworkElement placement)
    {
        var tabs = _settings.Profile.Tabs;
        var menu = new ContextMenu();
        void Add(string text, RoutedEventHandler onClick, bool enabled = true)
        {
            var mi = new MenuItem { Header = text, IsEnabled = enabled };
            mi.Click += onClick;
            menu.Items.Add(mi);
        }
        Add("重命名", (_, _) => RenameTab(tabIndex));
        Add("删除此页签", (_, _) => DeleteTab(tabIndex), tabs.Count > 1);
        menu.Items.Add(new Separator());
        Add("清除此页签所有项", (_, _) => ClearTabItems(tabIndex));

        menu.PlacementTarget = placement;
        menu.IsOpen = true;
    }

    /// <summary>新增页签（+ 号按钮）</summary>
    private void OnAddTabClick(object sender, RoutedEventArgs e)
    {
        var tabs = _settings.Profile.Tabs;
        var t = new PanelTab
        {
            Title = $"新页签 {tabs.Count + 1}"
        };
        tabs.Add(t);
        _currentTab = tabs.Count - 1;
        _settings.Save();
        RenderTabs();
        ShowItems();
        ToastService.Show($"已新增「{t.Title}」");
    }

    /// <summary>重命名页签（简化输入框）</summary>
    private void RenameTab(int tabIndex)
    {
        var tabs = _settings.Profile.Tabs;
        if (tabIndex < 0 || tabIndex >= tabs.Count) return;
        var current = tabs[tabIndex].Title;
        var input = PromptInput($"重命名页签", current);
        if (!string.IsNullOrWhiteSpace(input))
        {
            tabs[tabIndex].Title = input.Trim();
            _settings.Save();
            RenderTabs();
        }
    }

    /// <summary>删除页签（至少留 1 个）</summary>
    private void DeleteTab(int tabIndex)
    {
        var tabs = _settings.Profile.Tabs;
        if (tabs.Count <= 1)
        {
            ToastService.Show("至少保留 1 个页签", isError: true);
            return;
        }
        if (tabIndex < 0 || tabIndex >= tabs.Count) return;
        var removed = tabs[tabIndex];
        tabs.RemoveAt(tabIndex);
        if (_currentTab >= tabs.Count) _currentTab = tabs.Count - 1;
        _settings.Save();
        RenderTabs();
        ShowItems();
        ToastService.Show($"已删除「{removed.Title}」");
    }

    /// <summary>清除页签所有项</summary>
    private void ClearTabItems(int tabIndex)
    {
        var tabs = _settings.Profile.Tabs;
        if (tabIndex < 0 || tabIndex >= tabs.Count) return;
        var removedCount = tabs[tabIndex].Items.Count;
        tabs[tabIndex].Items.Clear();
        _settings.Save();
        ShowItems();
        ToastService.Show($"已清除 {removedCount} 项");
    }

    /// <summary>简单输入框（替代 InputBox）</summary>
    private static string? PromptInput(string title, string initial)
    {
        var dlg = new Window
        {
            Title = title,
            Width = 320,
            Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Application.Current.MainWindow,
            ResizeMode = ResizeMode.NoResize,
        };
        var sp = new StackPanel { Margin = new Thickness(16) };
        var tb = new TextBox { Text = initial, FontSize = 14, Margin = new Thickness(0, 8, 0, 12) };
        var bb = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var ok = new Button { Content = "确定", Width = 72, Height = 30, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var cancel = new Button { Content = "取消", Width = 72, Height = 30, IsCancel = true };
        string? result = null;
        ok.Click += (_, _) => { result = tb.Text; dlg.Close(); };
        cancel.Click += (_, _) => { dlg.Close(); };
        bb.Children.Add(ok);
        bb.Children.Add(cancel);
        sp.Children.Add(new TextBlock { Text = "输入名称：", FontSize = 12 });
        sp.Children.Add(tb);
        sp.Children.Add(bb);
        dlg.Content = sp;
        dlg.ShowDialog();
        return result;
    }

    public void SelectTab(int index)
    {
        var tabs = _settings.Profile.Tabs;
        if (index < 0 || index >= tabs.Count) return;
        _currentTab = index;
        // 滚轮循环（circulate）
        if (_settings.Profile.Settings.WheelCirculate && index == tabs.Count)
            index = 0;
        ShowItems();
        RenderTabs();
    }

    /// <summary>渲染当前标签的图标网格（P1-F6 自动适应列数 + 紧凑排版 + 拖拽换位）</summary>
    private void ShowItems()
    {
        var tabs = _settings.Profile.Tabs;
        if (tabs.Count == 0) return;
        var items = tabs[_currentTab].Items;

        ItemHost.Items.Clear();
        int iconSize = _settings.Profile.Settings.ItemIconSize switch
        {
            0 => 32,
            1 => 48,
            2 => 64,
            3 => 96,
            _ => 48,
        };
        foreach (var (it, itemIdx) in items.Select((x, i) => (x, i)))
        {
            var c = new LaunchItemControl
            {
                DataContext = it,
                OwnerWindow = this,
                SourceTabIndex = _currentTab,
                SourceItemIndex = itemIdx,
                // 去掉焦点虚线框（辉哥哥要求）
                Focusable = false,
                FocusVisualStyle = null,
            };
            c.SetIconSize(iconSize);
            // 主题全量适配（辉哥哥：深色模式下图标/标签文字没适配）
            c.ApplyTheme(ThemePalette.Get(_settings.Profile.Settings.Theme));
            c.LaunchRequested += LaunchFromUi;
            c.EditRequested += EditFromUi;
            ItemHost.Items.Add(c);
        }
    }

    /// <summary>主题切换后重渲染：页签文字/线色 + 图标标签/hover 全部跟随新调色板</summary>
    public void RenderThemeApplied()
    {
        RenderTabs();
        ShowItems();
    }

    private void LaunchFromUi(LaunchItem item)
    {
        LaunchRunner.Launch(item,
            success: msg => ToastService.Show(msg),
            fail: (name, err) => ToastService.Show($"{name}：{err}", isError: true));
    }

    private void EditFromUi(LaunchItem item)
    {
        ItemEditorWindow.ShowFor(this, item, _settings, saveAfterEdit: true, isNew: false);
    }

    private void OnClosed(object sender, EventArgs e)
    {
        _settings.Save();
    }

    // ===== 面板拖动（P1-F8）=====
    private void OnDragAreaMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
            SaveGeometryToDisk(); // 拖完落盘（辉哥哥：记住上一次位置）
        }
    }

    // ===== 标签条跨页签拖拽（P1-F7）=====
    private void OnTabStripDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent("EasyStart.ItemMove"))
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }
    }

    private void OnTabStripDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData("EasyStart.ItemMove") is LaunchItemControl.ItemMoveData data)
        {
            // 拖到标签条整体 = 移到当前页签（标签条 AllowDrop 覆盖整个标签条区域）
            MoveItemToTab(data, _currentTab);
            e.Handled = true;
        }
    }

    /// <summary>跨页签移动：从 data.FromTab 移到 targetTab</summary>
    public void MoveItemToTab(LaunchItemControl.ItemMoveData data, int targetTab)
    {
        var tabs = _settings.Profile.Tabs;
        if (targetTab < 0 || targetTab >= tabs.Count) return;
        if (data.FromIndex < 0 || data.FromIndex >= tabs[data.FromTab].Items.Count) return;

        var it = tabs[data.FromTab].Items[data.FromIndex];
        tabs[data.FromTab].Items.RemoveAt(data.FromIndex);
        tabs[targetTab].Items.Add(it);
        _settings.Save();
        RenderTabs();
        ShowItems();
    }

    /// <summary>同页签换位：把 data.FromIndex 的 item 移到 toIndex 位置（跨页签时走 MoveItemToTab）</summary>
    public void MoveItemBetween(LaunchItemControl.ItemMoveData data, int toTabIndex, int toIndex)
    {
        var tabs = _settings.Profile.Tabs;
        if (toTabIndex != data.FromTab)
        {
            // 跨页签（拖到另一个页签的 item 上）
            MoveItemToTab(data, toTabIndex);
            return;
        }
        if (data.FromIndex < 0 || data.FromIndex >= tabs[data.FromTab].Items.Count) return;
        if (toIndex < 0 || toIndex >= tabs[data.FromTab].Items.Count) return;
        if (data.FromIndex == toIndex) return;

        var it = tabs[data.FromTab].Items[data.FromIndex];
        tabs[data.FromTab].Items.RemoveAt(data.FromIndex);
        int insertAt = toIndex > data.FromIndex ? toIndex - 1 : toIndex;
        tabs[data.FromTab].Items.Insert(insertAt, it);
        _settings.Save();
        ShowItems();
    }

    /// <summary>供 LaunchItemControl 删除/编辑后刷新当前视图</summary>
    public void RaiseRefresh()
    {
        AppContext.Instance?.Reload();
        ApplyColumnCount();
        RenderTabs();
        ShowItems();
    }
}
