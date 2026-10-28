using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EasyStart.Models;
using EasyStart.Storage;

namespace EasyStart.Panel;

/// <summary>设置窗口（P0-F12）：呼出/隐藏 + 标签切换 + 外观 + 系统</summary>
public partial class SettingsWindow : Window
{
    private SettingsService _settings = null!;
    private bool _capturingHotkey;   // 是否处于"按键捕获热键"状态
    private string _capturedHotkey = "";

    public SettingsWindow()
    {
        InitializeComponent();
    }

    public static void Show(SettingsService settings)
    {
        var w = new SettingsWindow();
        w.Init(settings);
        var owner = Application.Current?.MainWindow;
        if (owner != null) w.Owner = owner;
        w.Show();
    }

    private void Init(SettingsService settings)
    {
        _settings = settings;
        var s = settings.Profile.Settings;

        // 无边框弹窗换皮（辉哥哥 2026-10-28）：不透明背景/描边直接落到 DialogRoot
        DialogTheme.Apply(this, s.Theme);

        // 热键：显示当前值，点框后进入捕获态
        _capturedHotkey = s.ToggleHotkey ?? "";
        UpdateHotkeyDisplay();
        HotkeyBox.MouseLeftButtonDown += OnHotkeyBoxClick;
        PreviewKeyDown += OnWindowKeyDown;

        MiddleClickCheck.IsChecked = s.ShowOnMiddleClick;
        HideOnDeactivatedCheck.IsChecked = s.HideOnSmartDetect;
        CtrlHoldCheck.IsChecked = s.PreventHideOnCtrlHold;
        NumberCheck.IsChecked = s.NumberKeyLaunch;
        WheelCheck.IsChecked = s.SwitchTabsOnWheel;
        CirculateCheck.IsChecked = s.WheelCirculate;
        HoverCheck.IsChecked = s.SwitchTabsOnHover;
        IconSizeBox.SelectedIndex = Math.Clamp(s.ItemIconSize, 0, 3);
        OpacitySlider.Value = s.Opacity;
        OpacitySlider.ValueChanged += OnOpacitySliderChanged;
        UpdateOpacityText(s.Opacity);
        AutoStartCheck.IsChecked = s.AutoStart;
        PortableCheck.IsChecked = s.Portable;

        // 主题：辉哥哥要求只保留 纯色浅色/纯色深色 两套（默认浅色）
        ThemeBox.SelectedIndex = s.Theme switch
        {
            "SolidDark" => 1,
            _ => 0, // MicaDark/Acrylic/旧值 一律归为浅色（兼容老数据）
        };
    }

    /// <summary>不透明度数值实时显示（辉哥哥：要看得见当前值）</summary>
    private void OnOpacitySliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        => UpdateOpacityText(e.NewValue);

    private void UpdateOpacityText(double v)
        => OpacityValueText.Text = v.ToString("0.00");

    /// <summary>点轨道跳到点击位置（辉哥哥：点一下要到点的地方，不是最大/最小）。
    /// 用 PreviewMouseLeftButtonDown 在 Slider 自己的翻页命令之前拦截，
    /// 直接按点击 X 坐标比例算 Value 并 e.Handled=true 阻止默认行为。</summary>
    private void OnOpacitySliderClick(object sender, MouseButtonEventArgs e)
    {
        // 点在滑块（thumb）上 → 交给拖动，不拦截
        if (e.OriginalSource is System.Windows.Controls.Primitives.Thumb) return;
        var slider = sender as Slider;
        if (slider == null || slider.ActualWidth <= 0) return;
        double x = e.GetPosition(slider).X;
        double ratio = Math.Clamp(x / slider.ActualWidth, 0, 1);
        slider.Value = slider.Minimum + ratio * (slider.Maximum - slider.Minimum);
        e.Handled = true; // 阻止 Slider 轨道翻页（推回最大/最小）的默认行为
    }

    /// <summary>热键捕获：点框 → 进入捕获态 → 按下组合键 → 显示</summary>
    private void OnHotkeyBoxClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _capturingHotkey = true;
        HotkeyText.Text = "现在按下组合键…";
        HotkeyText.Foreground = System.Windows.Media.Brushes.DodgerBlue;
    }

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (!_capturingHotkey) return;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        // Esc = 清除
        if (key == Key.Escape)
        {
            _capturedHotkey = "";
            _capturingHotkey = false;
            UpdateHotkeyDisplay();
            e.Handled = true;
            return;
        }

        // 纯修饰键不算有效热键
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            e.Handled = true;
            return;
        }

        // 组合同步修饰键 + 主键
        var mods = Keyboard.Modifiers;
        var parts = new List<string>();
        if (mods.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (mods.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (mods.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(KeyToName(key));
        _capturedHotkey = string.Join(" + ", parts);

        _capturingHotkey = false;
        UpdateHotkeyDisplay();
        e.Handled = true;
    }

    private void UpdateHotkeyDisplay()
    {
        if (string.IsNullOrEmpty(_capturedHotkey))
        {
            HotkeyText.Text = "点我后按快捷键";
            HotkeyText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x60, 0x60, 0x60));
        }
        else
        {
            HotkeyText.Text = _capturedHotkey;
            HotkeyText.Foreground = System.Windows.Media.Brushes.Black;
        }
    }

    private static string KeyToName(Key k) => k switch
    {
        Key.D0 => "0", Key.D1 => "1", Key.D2 => "2", Key.D3 => "3", Key.D4 => "4",
        Key.D5 => "5", Key.D6 => "6", Key.D7 => "7", Key.D8 => "8", Key.D9 => "9",
        Key.A => "A", Key.B => "B", Key.C => "C", Key.D => "D", Key.E => "E",
        Key.F => "F", Key.G => "G", Key.H => "H", Key.I => "I", Key.J => "J",
        Key.K => "K", Key.L => "L", Key.M => "M", Key.N => "N", Key.O => "O",
        Key.P => "P", Key.Q => "Q", Key.R => "R", Key.S => "S", Key.T => "T",
        Key.U => "U", Key.V => "V", Key.W => "W", Key.X => "X", Key.Y => "Y", Key.Z => "Z",
        Key.Space => "Space",
        Key.Enter => "Enter",
        Key.Tab => "Tab",
        Key.Delete => "Delete",
        Key.Insert => "Insert",
        Key.Home => "Home",
        Key.End => "End",
        Key.PageUp => "PageUp",
        Key.PageDown => "PageDown",
        Key.Up => "Up",
        Key.Down => "Down",
        Key.Left => "Left",
        Key.Right => "Right",
        Key.OemMinus => "-",
        Key.OemPlus => "+",
        Key.LWin or Key.RWin => "Win",
        Key.NumPad0 => "Num0", Key.NumPad1 => "Num1", Key.NumPad2 => "Num2", Key.NumPad3 => "Num3",
        Key.NumPad4 => "Num4", Key.NumPad5 => "Num5", Key.NumPad6 => "Num6",
        Key.NumPad7 => "Num7", Key.NumPad8 => "Num8", Key.NumPad9 => "Num9",
        Key.PrintScreen => "Print",
        Key.Pause => "Pause",
        _ => k.ToString(),
    };

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var s = _settings.Profile.Settings;
        s.ToggleHotkey = _capturedHotkey;
        s.ShowOnMiddleClick = MiddleClickCheck.IsChecked == true;
        s.HideOnSmartDetect = HideOnDeactivatedCheck.IsChecked == true;
        s.PreventHideOnCtrlHold = CtrlHoldCheck.IsChecked == true;
        s.NumberKeyLaunch = NumberCheck.IsChecked == true;
        s.SwitchTabsOnWheel = WheelCheck.IsChecked == true;
        s.WheelCirculate = CirculateCheck.IsChecked == true;
        s.SwitchTabsOnHover = HoverCheck.IsChecked == true;
        s.ItemIconSize = int.Parse(((ComboBoxItem)IconSizeBox.SelectedItem).Tag!.ToString());
        s.Opacity = Math.Round(double.Parse(OpacitySlider.Value.ToString()), 2);
        s.AutoStart = AutoStartCheck.IsChecked == true;
        s.Portable = PortableCheck.IsChecked == true;
        // 主题：辉哥哥要求只要 纯色浅色/纯色深色 两套，默认浅色
        s.Theme = ThemeBox.SelectedIndex == 1 ? "SolidDark" : "SolidLight";

        // 保存（触发 onChanged → App 重注册热键 + 面板 ApplySettings 即时生效）
        _settings.Save();
        // 开机自启 + 便携模式
        Shell.AutoStartService.Apply(s.AutoStart);
        Storage.ProfileStore.Portable = s.Portable;
        Close();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    // ===== 无边框弹窗（辉哥哥 2026-10-28：设置窗与编辑/添加窗同风格） =====

    /// <summary>标题栏拖动</summary>
    private void OnHeaderDrag(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed)
        {
            try { DragMove(); } catch { }
        }
    }

    /// <summary>Esc 关窗。捕获热键时 Esc 归捕获逻辑（PreviewKeyDown 已 Handled，不会到这里）</summary>
    private void OnWinKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (!_capturingHotkey && e.Key == System.Windows.Input.Key.Escape) Close();
    }
}
