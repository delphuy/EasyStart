using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace EasyStart.Panel;

/// <summary>
/// 启动反馈 toast（P0-F16）：右上角浮层，成功绿边/失败红边，1.5s 自动消。
///
/// 实锤修复（crash.log 咬出来的雷）：
/// 旧版 WrapWithToast 直接把还在当 Window.Content 的根元素 Add 进新 Grid
/// ——"指定的元素已经是另一个元素的逻辑子元素"必然抛异常，首次弹 toast 必崩，
/// 还把调用链炸断（onChanged 里排在后面的 ApplySettings 全被跳过）。
/// 本版：先把 Window.Content 置 null 断开逻辑父子再重组；包一层缓存（只 wrap 一次）；
/// 内外双层 try/catch——toast 是提示，天塌了也不能带崩业务链。
/// </summary>
public static class ToastService
{
    private static Border? _toast;
    private static Grid? _wrapGrid;
    private static DispatcherTimer? _timer;

    public static void Show(string msg, bool isError = false)
    {
        try
        {
            var app = Application.Current;
            if (app == null) return;

            Window? target = null;
            foreach (Window w in app.Windows)
            {
                if (w is PanelWindow) { target = w; break; }
            }
            target ??= app.Windows.Count > 0 ? app.Windows[0] : null;
            if (target == null) return;

            target.Dispatcher.Invoke(() =>
            {
                try
                {
                    // 只 wrap 一次；若面板内容被换过（比如别的代码改了 Content）才重新组装
                    if (_toast == null || _wrapGrid == null || !ReferenceEquals(target.Content, _wrapGrid))
                    {
                        var old = target.Content;
                        target.Content = null; // 先断开逻辑父子关系，否则 Add 必抛"已是另一个元素的逻辑子元素"

                        var grid = new Grid();
                        if (old is FrameworkElement root) grid.Children.Add(root);

                        if (_toast == null)
                        {
                            _toast = new Border
                            {
                                Width = 320,
                                Height = 44,
                                CornerRadius = new CornerRadius(10),
                                Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x20, 0x20, 0x24)),
                                HorizontalAlignment = HorizontalAlignment.Right,
                                VerticalAlignment = VerticalAlignment.Top,
                                Margin = new Thickness(0, 56, 16, 0),
                                Padding = new Thickness(12, 8, 12, 8),
                                IsHitTestVisible = false,
                                Opacity = 0,
                            };
                            _toast.Child = new TextBlock
                            {
                                FontSize = 13,
                                Foreground = Brushes.White,
                                VerticalAlignment = VerticalAlignment.Center,
                            };
                        }
                        if (_toast.Parent is Grid prev) prev.Children.Remove(_toast); // 复挂兜底
                        grid.Children.Add(_toast);

                        _wrapGrid = grid;
                        target.Content = grid;
                    }

                    var tb = (TextBlock)_toast.Child!;
                    tb.Text = msg;
                    _toast.BorderBrush = isError
                        ? new SolidColorBrush(Color.FromRgb(0xE8, 0x4B, 0x4B))
                        : new SolidColorBrush(Color.FromRgb(0x3A, 0xD2, 0x6A));

                    // 淡入
                    var anim = new DoubleAnimation(0, 1,
                        new Duration(TimeSpan.FromMilliseconds(160)))
                    { FillBehavior = FillBehavior.HoldEnd };
                    _toast.BeginAnimation(UIElement.OpacityProperty, anim);

                    // 自动消
                    _timer?.Stop();
                    _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
                    _timer.Tick += (_, _) =>
                    {
                        _timer.Stop();
                        _toast.BeginAnimation(UIElement.OpacityProperty,
                            new DoubleAnimation(1, 0,
                                new Duration(TimeSpan.FromMilliseconds(240))));
                    };
                    _timer.Start();
                }
                catch (Exception ex)
                {
                    EasyStart.Shell.HookDiagnostics.Log("toast内部异常: " + ex.Message);
                }
            });
        }
        catch (Exception ex)
        {
            EasyStart.Shell.HookDiagnostics.Log("toast异常: " + ex.Message);
        }
    }
}
