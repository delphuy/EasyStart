using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using EasyStart.Launcher;
using EasyStart.Models;

namespace EasyStart.Panel;

/// <summary>单个快捷项控件（图标 + 标签 + 右键菜单 + 拖拽换位/跨页签）</summary>
public class LaunchItemControl : UserControl
{
    private readonly Border _card;
    private readonly Image _icon;
    private readonly TextBlock _label;
    private int _iconSize = 48;

    public LaunchItemControl()
    {
        // 紧凑排版（辉哥哥要求）：固定行间距 6px，不随图标大小变化
        Margin = new Thickness(4, 0, 4, 6);
        Padding = new Thickness(0);
        Cursor = Cursors.Hand;
        Width = 108;
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
        // 高度由内容决定（图标 + 标签 + 卡片 padding）
        Height = double.NaN;

        // 去掉焦点虚线框（辉哥哥要求）：UserControl 在 ItemsControl 里会画 FocusVisualStyle 虚线
        Focusable = false;
        FocusVisualStyle = null;

        var ic = new Image
        {
            Width = 48,
            Height = 48,
            // 居中排版（辉哥哥 2026-10-28：图标和文字要居中，不要贴左参差）
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Stretch = Stretch.Uniform,
            Margin = new Thickness(0, 2, 0, 4),
        };
        var lb = new TextBlock
        {
            FontSize = 11,
            TextWrapping = TextWrapping.NoWrap,
            TextAlignment = TextAlignment.Center,
            // 过长名称省略号截断 + 悬停看全名（辉哥哥 2026-10-28：硬切半个字很难看）
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 96,
            // 标签颜色由 ApplyTheme 设置（默认浅灰，深色主题下换成浅色）
            Foreground = Brushes.DimGray,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 0),
        };
        _card = new Border
        {
            CornerRadius = new CornerRadius(8),
            Background = Brushes.Transparent,
            Padding = new Thickness(6, 8, 6, 6),
            // 卡片拉满控件宽度：hover 高亮盖满整个格位，内容（图标+文字）在卡内真正居中
            // （2026-10-28 实锤：卡片若贴靠内容+左对齐，内容居中也只居在左侧 60px 内，
            //  UIA 实测 7 个标签全部偏左 30px —— 辉哥哥肉眼正确）
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        _icon = ic;
        _label = lb;

        var sp = new StackPanel
        {
            // 内容整体居中（水平居中；卡片内垂直居中，上下留白对称）
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        sp.Children.Add(ic);
        sp.Children.Add(lb);
        _card.Child = sp;
        Content = _card;

        // 拖拽源（P1-F7 换位/跨页签）：按下+移动超阈值才起拖拽，纯点击不触发
        AllowDrop = true;
        DragOver += OnSelfDragOver;
        Drop += OnSelfDrop;
        MouseLeftButtonDown += OnSelfMouseLeftButtonDown;
        MouseMove += OnSelfMouseMove;

        // 事件
        MouseLeftButtonUp += OnSelfMouseLeftButtonUp;
        MouseRightButtonUp += (_, _) => ShowMenu();
        // hover 高亮：颜色由 ApplyTheme 按主题设置（浅色主题=蓝色微光，深色主题=白色微光）
        _hoverBrush = new SolidColorBrush(Color.FromArgb(0x28, 0x1A, 0x6C, 0xD8));
        MouseEnter += (_, _) => _card.Background = _hoverBrush;
        MouseLeave += (_, _) => _card.Background = Brushes.Transparent;
    }

    /// <summary>按主题调色板刷新本控件的颜色（标签文字 / hover 高亮 / 拖拽边框）</summary>
    public void ApplyTheme(ThemePalette pal)
    {
        _themePalette = pal;
        _label.Foreground = pal.TextSec();
        _hoverBrush.Color = pal.Hover;
        _dragBorderBrush = new SolidColorBrush(pal.Accent);
        // 主题变了重画图标（fallback 圆/emoji 底色随深浅切换）
        if (DataContext is LaunchItem it) ApplyItem(it);
    }

    private ThemePalette? _themePalette;
    private readonly System.Windows.Media.SolidColorBrush _hoverBrush;
    private System.Windows.Media.SolidColorBrush _dragBorderBrush;

    private bool _isDragging;
    private bool _mousePressed;
    private bool _dragStarted;
    private Point _downPoint;
    private const int DragThreshold = 4;

    /// <summary>图标尺寸（由 PanelWindow 根据 ItemIconSize 设置）</summary>
    public void SetIconSize(int size)
    {
        _iconSize = size;
        // 紧凑排版（辉哥哥要求）：卡片高度紧贴内容，固定行间距 6px（不随图标大小变）
        int cardH = size + 40; // 图标 size + 标签 16px + 卡片上下 padding 24px
        Height = cardH;
        // 行间距固定 6px：底部 margin 6，顶部 0（最后一行由 ScrollViewer padding 兜底）
        Margin = new Thickness(4, 0, 4, 6);
        _icon.Width = size;
        _icon.Height = size;
        Width = size + 60;
        // 标签可写宽度随图标放大（省略号截断的位置跟着卡片宽走）
        _label.MaxWidth = size + 48;
        if (DataContext is LaunchItem it) ApplyItem(it);
    }

    public event Action<LaunchItem>? LaunchRequested;
    public event Action<LaunchItem>? EditRequested;

    public Window? OwnerWindow { get; set; }

    /// <summary>源页签 index（拖拽换位用，PanelWindow 在生成 control 时设置）</summary>
    public int SourceTabIndex { get; set; }

    /// <summary>源 item 在页签里的 index（拖拽换位用）</summary>
    public int SourceItemIndex { get; set; }

    public new object DataContext
    {
        get => base.DataContext;
        set
        {
            base.DataContext = value;
            if (value is LaunchItem it) ApplyItem(it);
        }
    }

    private void ApplyItem(LaunchItem it)
    {
        int size = _iconSize;
        _icon.Width = size;
        _icon.Height = size;
        // 深色主题下 fallback 圆/emoji 底色用浅色调（辉哥哥：深色模式图标没适配）
        bool dark = _themePalette?.IsDark ?? false;
        _icon.Source = IconExtractor.Extract(PickTarget(it), size, it.IconPath, dark);
        _label.Text = it.Name ?? DisplayName(it);
        _label.ToolTip = _label.Text; // 过长被省略号截断时，悬停看全名
    }

    private static string DisplayName(LaunchItem it)
    {
        if (it.Type == LaunchItemType.Uri)
            return "网址";
        var name = it.Target ?? "未命名";
        return System.IO.Path.GetFileNameWithoutExtension(name);
    }

    private string PickTarget(LaunchItem it)
    {
        // 系统项：把 action 作为"target"传给 IconExtractor，走系统符号渲染
        if (it.Type == LaunchItemType.System)
            return it.Action ?? "";
        return it.Type == LaunchItemType.Uri ? it.Uri ?? "" : it.Target ?? "";
    }

    private void ShowMenu()
    {
        var it = DataContext as LaunchItem;
        if (it == null) return;

        var menu = new ContextMenu();
        void Add(string text, RoutedEventHandler onClick)
        {
            var mi = new MenuItem { Header = text };
            mi.Click += onClick;
            menu.Items.Add(mi);
        }
        Add("编辑", (_, _) => EditRequested?.Invoke(it));
        Add("定位文件", (_, _) => LaunchRunner.Reveal(it));
        Add("启动目录", (_, _) =>
        {
            if (System.IO.Directory.Exists(it.WorkDir))
                System.Diagnostics.Process.Start("explorer.exe", it.WorkDir);
        });
        menu.Items.Add(new Separator());
        Add("删除", (_, _) => DeleteItem(it));

        menu.PlacementTarget = this;
        menu.IsOpen = true;
    }

    private void DeleteItem(LaunchItem it)
    {
        var tabs = EasyStart.AppContext.Instance?.Profile;
        if (tabs == null) return;
        foreach (var t in tabs.Tabs)
        {
            var idx = t.Items.IndexOf(it);
            if (idx >= 0)
            {
                t.Items.RemoveAt(idx);
                // 先落盘再刷新（2026-10-28 修回归）：RaiseRefresh 内部会从磁盘重载档案，
                // 旧顺序（先刷新后保存）= 先重载把刚删的项复活、再把复活数据存回去，删除当场被抹掉。
                EasyStart.AppContext.Instance?.SettingsService.Save();
                if (OwnerWindow != null)
                {
                    OwnerWindow.Dispatcher.Invoke(() =>
                    {
                        var pw = OwnerWindow as PanelWindow;
                        pw?.RaiseRefresh();
                    });
                }
                break;
            }
        }
    }

    // ===== 拖拽换位（P1-F7）：阈值判定区分点击/拖拽 =====

    private void OnSelfMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _mousePressed = true;
        _dragStarted = false;
        _downPoint = e.GetPosition(this);
    }

    private void OnSelfMouseMove(object sender, MouseEventArgs e)
    {
        // 按下后移动超阈值 → 起拖拽
        if (_mousePressed
            && !_dragStarted
            && e.LeftButton == MouseButtonState.Pressed
            && DataContext is LaunchItem)
        {
            var p = e.GetPosition(this);
            if (Math.Abs(p.X - _downPoint.X) > DragThreshold
                || Math.Abs(p.Y - _downPoint.Y) > DragThreshold)
            {
                _dragStarted = true;
                _isDragging = true;
                var data = new DataObject();
                data.SetData("EasyStart.ItemMove", new ItemMoveData(SourceTabIndex, SourceItemIndex));
                try
                {
                    DragDrop.DoDragDrop(this, data, DragDropEffects.Move);
                }
                finally
                {
                    _isDragging = false;
                    _mousePressed = false;
                }
            }
        }
    }

    private void OnSelfMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // 纯点击（未按住移动）→ 启动；拖拽结束 → 不启动（换位已完成）
        if (_dragStarted)
        {
            _dragStarted = false;
            _mousePressed = false;
            return;
        }
        _mousePressed = false;
        if (DataContext is LaunchItem it)
            LaunchRequested?.Invoke(it);
    }

    private void OnSelfDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent("EasyStart.ItemMove"))
        {
            e.Effects = DragDropEffects.Move;
            _card.BorderThickness = new Thickness(2);
            _card.BorderBrush = _dragBorderBrush ?? new SolidColorBrush(Colors.DodgerBlue);
            e.Handled = true;
        }
    }

    private void OnSelfDrop(object sender, DragEventArgs e)
    {
        _card.BorderThickness = new Thickness(0);
        if (e.Data.GetData("EasyStart.ItemMove") is ItemMoveData data
            && OwnerWindow is PanelWindow pw)
        {
            pw.MoveItemBetween(data, SourceTabIndex, SourceItemIndex);
            e.Handled = true;
        }
    }

    /// <summary>拖拽数据载体</summary>
    public record ItemMoveData(int FromTab, int FromIndex);
}
