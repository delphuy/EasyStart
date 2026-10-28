using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;

namespace EasyStart.Launcher;

/// <summary>
/// 图标提取（P0-F4）：从 exe/lnk/文件夹/文件提取图标，4 档尺寸。
/// Win32 ShellExtractIcon + 回退，提取失败返回纯色占位。
/// </summary>
public static class IconExtractor
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int ExtractIconW(string lpszFile, int nIconIndex, IntPtr[] phicon, int nIcons);

    // ===== SHGetFileInfo（双保险，直接拿大图标句柄）=====
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFOW
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfoW(string pszPath, uint dwFileAttributes,
        ref SHFILEINFOW psfi, uint cbFileInfo, uint uFlags);

    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_LARGEICON = 0x000000000;
    private const uint SHGFI_SMALLICON = 0x000000001;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    /// <summary>提取指定文件的图标，size = 32/48/64/96。返回 WPF ImageSource。dark：深色主题下 fallback 圆用浅色</summary>
    public static System.Windows.Media.ImageSource Extract(string? target, int size, string? iconPathOverride, bool dark = false)
    {
        // 自定义图标优先（.ico 直接读，png/jpg/bmp 走 Image 解码）
        if (!string.IsNullOrEmpty(iconPathOverride) && System.IO.File.Exists(iconPathOverride))
        {
            try { return LoadIco(iconPathOverride, size); }
            catch
            {
                try { return LoadImageFile(iconPathOverride, size); } catch { /* fallthrough */ }
            }
        }

        if (string.IsNullOrEmpty(target)) return Fallback(size, dark);

        // 网址：离线不联网，按站点取色的字母头像占位（辉哥哥 2026-10-28：默认不该是灰圆）
        if (target.StartsWith("http://", System.StringComparison.OrdinalIgnoreCase)
            || target.StartsWith("https://", System.StringComparison.OrdinalIgnoreCase))
            return FallbackForHost(target, size);

        // 系统项：渲染动作对应的 emoji 字符作为图标（关机⏻/锁定🔒/静音🔇…），深色主题下用浅色符号
        if (IsSystemAction(target))
            return RenderGlyph(IconForAction(target), size, dark);

        // 文件/文件夹：双保险图标提取（P0-F4）
        if (System.IO.File.Exists(target) || System.IO.Directory.Exists(target))
        {
            // 方案 1：SHGetFileInfo（直接拿大图标句柄，最可靠）
            try
            {
                var shfi = new SHFILEINFOW();
                uint flags = SHGFI_ICON | (size >= 64 ? SHGFI_LARGEICON : SHGFI_SMALLICON);
                var hRes = SHGetFileInfoW(target, 0, ref shfi, (uint)Marshal.SizeOf<SHFILEINFOW>(), flags);
                if (shfi.hIcon != IntPtr.Zero)
                {
                    var h = shfi.hIcon;
                    try
                    {
                        using var ico = System.Drawing.Icon.FromHandle(h);
                        var bmp = ico.ToBitmap();
                        var resized = ResizeTo(bmp, size);
                        return ToBitmapSource(resized);
                    }
                    finally
                    {
                        DestroyIcon(h);
                    }
                }
            }
            catch { /* 落到方案 2 */ }

            // 方案 2：ExtractIconW（调用方预留数组，P/Invoke 经典写法）
            try
            {
                var arr = new IntPtr[1];
                int count = ExtractIconW(target, 0, arr, 1);
                if (count > 0 && arr[0] != IntPtr.Zero)
                {
                    var h = arr[0];
                    try
                    {
                        using var ico = System.Drawing.Icon.FromHandle(h);
                        var bmp = ico.ToBitmap();
                        var resized = ResizeTo(bmp, size);
                        return ToBitmapSource(resized);
                    }
                    finally
                    {
                        DestroyIcon(h);
                    }
                }
            }
            catch { /* fallthrough 到占位图 */ }
        }

        return Fallback(size, dark);
    }

    /// <summary>判断 target 是否为系统动作 key（关机/锁定/静音…）</summary>
    public static bool IsSystemAction(string? target)
    {
        if (string.IsNullOrEmpty(target)) return false;
        foreach (var (_, _, _) in EasyStart.Models.ActionKey.All)
        {
            // ActionKey.All 的 Key 字段是第一个元素
        }
        return EasyStart.Models.ActionKey.All.Any(a => a.Key == target);
    }

    /// <summary>系统动作 key → 对应的 emoji 字符</summary>
    public static string IconForAction(string? target)
    {
        var found = EasyStart.Models.ActionKey.All.FirstOrDefault(a => a.Key == target);
        return found.Icon;
    }

    /// <summary>把 emoji/符号字符渲染成位图（用于系统项图标）。dark：深色主题下用浅色符号+浅底圆</summary>
    public static System.Windows.Media.ImageSource RenderGlyph(string glyph, int size, bool dark = false)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            g.Clear(Color.Transparent);
            // 深色主题：浅蓝灰底圆 + 浅色符号；浅色主题：灰底圆 + 深色符号
            int pad = size / 8;
            if (dark)
            {
                using var bg = new SolidBrush(Color.FromArgb(90, 90, 130, 200));
                g.FillEllipse(bg, pad, pad, size - 2 * pad, size - 2 * pad);
            }
            else
            {
                using var bg = new SolidBrush(Color.FromArgb(60, 210, 210, 220));
                g.FillEllipse(bg, pad, pad, size - 2 * pad, size - 2 * pad);
            }
            using var font = new Font("Segoe UI Emoji", size * 0.55f, FontStyle.Regular, GraphicsUnit.Pixel);
            var sf = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
            var symbolColor = dark
                ? Color.FromArgb(240, 230, 240, 255)
                : Color.FromArgb(230, 50, 50, 80);
            g.DrawString(glyph ?? "●", font, new SolidBrush(symbolColor), new RectangleF(0, 0, size, size), sf);
        }
        using var ms = new System.IO.MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        ms.Position = 0;
        var bi = new BitmapImage();
        bi.BeginInit();
        bi.CacheOption = BitmapCacheOption.OnLoad;
        bi.StreamSource = ms;
        bi.EndInit();
        bi.Freeze();
        return bi;
    }

    private static Bitmap ResizeTo(Bitmap src, int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.SmoothingMode = SmoothingMode.HighQuality;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.CompositingMode = CompositingMode.SourceOver;
        g.DrawImage(src, 0, 0, size, size);
        return bmp;
    }

    private static System.Windows.Media.ImageSource ToBitmapSource(Bitmap bmp)
    {
        using var ms = new System.IO.MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        ms.Position = 0;
        var bi = new BitmapImage();
        bi.BeginInit();
        bi.CacheOption = BitmapCacheOption.OnLoad;
        bi.StreamSource = ms;
        bi.EndInit();
        bi.Freeze();
        return bi;
    }

    private static System.Windows.Media.ImageSource LoadIco(string path, int size)
    {
        using var ico = new System.Drawing.Icon(path, size, size);
        var bmp = ico.ToBitmap();
        using var ms = new System.IO.MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        ms.Position = 0;
        var bi = new BitmapImage();
        bi.BeginInit();
        bi.CacheOption = BitmapCacheOption.OnLoad;
        bi.StreamSource = ms;
        bi.EndInit();
        bi.Freeze();
        return bi;
    }

    /// <summary>网址占位：按站点取色的字母头像（辉哥哥 2026-10-28：不要灰圆，要像浏览器那样有辨识度）</summary>
    private static System.Windows.Media.ImageSource FallbackForHost(string url, int size)
    {
        var host = "";
        try
        {
            if (System.Uri.TryCreate(url, System.UriKind.Absolute, out var u)) host = u.Host;
        }
        catch { }
        if (string.IsNullOrEmpty(host)) host = url;

        var label = host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;
        var letter = label.Length > 0 ? char.ToUpperInvariant(label[0]) : 'W';

        // 站点稳定取色（手写 hash，不用随机化的 string.GetHashCode）
        int hash = 0;
        foreach (var c in label) hash = hash * 31 + c;
        var palette = new[]
        {
            Color.FromArgb(255, 0x1A, 0x6C, 0xD8),
            Color.FromArgb(255, 0xE8, 0x59, 0x0C),
            Color.FromArgb(255, 0x00, 0x90, 0x6E),
            Color.FromArgb(255, 0x8E, 0x44, 0xAD),
            Color.FromArgb(255, 0xC0, 0x39, 0x2B),
            Color.FromArgb(255, 0x0E, 0x7F, 0xA8),
            Color.FromArgb(255, 0xB7, 0x79, 0x1F),
            Color.FromArgb(255, 0x4C, 0x6E, 0xF5),
        };
        var bgc = palette[Math.Abs(hash) % palette.Length];

        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
        using (var brush = new SolidBrush(Color.FromArgb(bgc.A, bgc.R, bgc.G, bgc.B)))
            g.FillEllipse(brush, 1, 1, size - 2, size - 2);
        using var font = new Font("Segoe UI", size * 0.52f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var sf = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
        };
        using var white = new SolidBrush(Color.White);
        g.DrawString(letter.ToString(), font, white, new RectangleF(0, -size * 0.06f, size, size), sf);
        return ToBitmapSource(bmp);
    }

    /// <summary>png/jpg/bmp 图标文件解码（选图时 .ico 之外的兜底）</summary>
    private static System.Windows.Media.ImageSource LoadImageFile(string path, int size)
    {
        using var img = Image.FromFile(path);
        using var resized = ResizeTo(new Bitmap(img), size);
        return ToBitmapSource(resized);
    }

    private static System.Windows.Media.ImageSource Fallback(int size, bool dark = false)
    {
        // 辉哥哥：深色模式下灰圆图标看不出 → 深色主题用浅色圆 + 浅蓝描边
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        using var brush = new SolidBrush(dark
            ? Color.FromArgb(90, 120, 160, 220)   // 深底：浅蓝灰
            : Color.FromArgb(60, 90, 90, 110));  // 浅底：灰
        g.FillEllipse(brush, 2, 2, size - 4, size - 4);
        using var pen = new Pen(dark
            ? Color.FromArgb(180, 140, 180, 240)
            : Color.FromArgb(140, 70, 70, 90), 2f);
        g.DrawEllipse(pen, 2, 2, size - 5, size - 5);
        using var ms = new System.IO.MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        ms.Position = 0;
        var bi = new BitmapImage();
        bi.BeginInit();
        bi.CacheOption = BitmapCacheOption.OnLoad;
        bi.StreamSource = ms;
        bi.EndInit();
        bi.Freeze();
        return bi;
    }
}
