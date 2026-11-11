using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;

namespace EasyStart.Launcher;

/// <summary>
/// 图标提取：从 exe/lnk/文件夹/文件提取图标。
/// 2026-11-09 重构（辉哥哥：面板图标锯齿 + 切页签卡顿）：
/// ① 高清主通道：SHGetFileInfo 拿系统图标索引 + SHGetImageList 直接取 Explorer 同款大图标
///    （SHIL_JUMBO 256×256 / SHIL_EXTRALARGE 48 / SHIL_LARGE 32），HICON→32bpp ARGB 不丢 alpha；
///    彻底废除旧版「SHGFI_LARGEICON 最多 32×32、48 档错拿 16×16 再硬放大」的锯齿源头。
/// ② 提取尺寸乘 DPI 缩放比（面板控件把 PixelsPerDip 传进来），高分屏不再发糊。
/// ③ 只缩不放：源图小于目标时不做 GDI 放大，交给 WPF 高质量缩放；大于目标用 HighQualityBicubic 缩小。
/// ④ 内存缓存：target+覆盖图标+尺寸+DPI+深浅主题 为键，ImageSource 已 Freeze 可跨线程复用；
///    切页签/改窗口大小的全量重渲染直接命中缓存，不再重复抓图（「重新加载图标」菜单会清缓存）。
/// Win32 ShellExtractIcon + 双保险回退，提取失败返回纯色占位。
/// </summary>
public static class IconExtractor
{
    // ===== 内存缓存（2026-11-09：切页签全量重建控件时不再重复提取）=====
    private static readonly ConcurrentDictionary<string, System.Windows.Media.ImageSource> _cache = new();

    /// <summary>清空图标缓存（「重新加载图标」菜单用）</summary>
    public static void ClearCache() => _cache.Clear();

    /// <summary>只读缓存不触发提取：UI 层用它在「同步显示缓存 / 后台提取回填」之间做选择</summary>
    public static System.Windows.Media.ImageSource? GetCached(string? target, string? iconPathOverride,
        int size, bool dark = false, double dpiScale = 1.0)
        => _cache.TryGetValue(CacheKey(target, iconPathOverride, size, dark, dpiScale), out var v) ? v : null;

    private static string CacheKey(string? target, string? iconPathOverride,
        int size, bool dark, double dpiScale)
        => $"{target}|{iconPathOverride}|{size}|{dpiScale:0.###}|{dark}";

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int ExtractIconW(string lpszFile, int nIconIndex, IntPtr[] phicon, int nIcons);

    // ===== SHGetFileInfo（拿系统图标索引 + 旧路径兜底）=====
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
    private const uint SHGFI_SYSICONINDEX = 0x00004000; // 只拿系统图标索引（配 SHGetImageList 取大图）

    // ===== SHGetImageList + IImageList（Explorer 同款大图标：48/256）=====
    // vtable 顺序（IImageList）：Add→ReplaceIcon→SetOverlayImage→Replace→AddMasked→Draw→Remove→GetIcon
    // 前 7 个不调用也必须占位，GetIcon 才落在正确槽位。
    [ComImport, Guid("46EB5926-582E-4017-9FDF-E8998DAA0950"),
        InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IImageList
    {
        void Add(IntPtr hbmImage, IntPtr hbmMask, int i);
        void ReplaceIcon(int i, IntPtr hicon, int pi);
        void SetOverlayImage(int iImage, int iOverlay);
        void Replace(int iImage, IntPtr hbmImage, IntPtr hbmMask);
        void AddMasked(IntPtr hbmImage, int crMask, int pi);
        void Draw(IntPtr pimldp);
        void Remove(int iImage);
        [PreserveSig] int GetIcon(int i, int flags, out IntPtr picon);
    }

    [DllImport("shell32.dll", EntryPoint = "SHGetImageList")]
    [return: MarshalAs(UnmanagedType.Error)]
    private static extern int SHGetImageList(int iImageList, in Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IImageList ppv);

    private const int SHIL_LARGE = 0x0;       // 32×32
    private const int SHIL_SMALL = 0x1;       // 16×16
    private const int SHIL_EXTRALARGE = 0x2;  // 48×48
    private const int SHIL_JUMBO = 0x4;       // 256×256
    private const int ILD_TRANSPARENT = 0x0001;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    /// <summary>提取指定文件的图标。size 为逻辑尺寸（WPF DIU），dpiScale 为监视器缩放比；
    /// 内部按物理像素取图并写缓存。dark：深色主题下 fallback 圆用浅色。</summary>
    public static System.Windows.Media.ImageSource Extract(string? target, int size,
        string? iconPathOverride, bool dark = false, double dpiScale = 1.0)
    {
        var key = CacheKey(target, iconPathOverride, size, dark, dpiScale);
        if (_cache.TryGetValue(key, out var hit)) return hit;
        var src = ExtractCore(target, Physical(size, dpiScale), iconPathOverride, dark);
        _cache[key] = src;
        return src;
    }

    /// <summary>逻辑尺寸 → 物理像素（16~256，越界钳制）</summary>
    private static int Physical(int logicalSize, double dpiScale)
    {
        if (double.IsNaN(dpiScale) || dpiScale <= 0) dpiScale = 1.0;
        int px = (int)Math.Ceiling(logicalSize * dpiScale);
        return Math.Clamp(px, 16, 256);
    }

    private static System.Windows.Media.ImageSource ExtractCore(string? target, int phys,
        string? iconPathOverride, bool dark)
    {
        // 自定义图标优先：.ico 走多帧选帧；png/jpg/jpeg/bmp 走等比高清方形化
        if (!string.IsNullOrEmpty(iconPathOverride) && System.IO.File.Exists(iconPathOverride))
        {
            var ext = System.IO.Path.GetExtension(iconPathOverride).ToLowerInvariant();
            try
            {
                return ext == ".ico"
                    ? LoadIco(iconPathOverride, phys)
                    : LoadImageFile(iconPathOverride, phys);
            }
            catch
            {
                try
                {
                    return ext == ".ico"
                        ? LoadImageFile(iconPathOverride, phys)
                        : LoadIco(iconPathOverride, phys);
                }
                catch { /* fallthrough */ }
            }
        }

        if (string.IsNullOrEmpty(target)) return Fallback(phys, dark);

        // 网址：离线不联网，按站点取色的字母头像占位（辉哥哥 2026-10-28：默认不该是灰圆）
        if (target.StartsWith("http://", System.StringComparison.OrdinalIgnoreCase)
            || target.StartsWith("https://", System.StringComparison.OrdinalIgnoreCase))
            return FallbackForHost(target, phys);

        // 系统项：渲染动作对应的 emoji 字符作为图标（关机⏻/锁定🔒/静音🔇…），深色主题下用浅色符号
        if (IsSystemAction(target))
            return RenderGlyph(IconForAction(target), phys, dark);

        // 文件/文件夹：三级提取链
        if (System.IO.File.Exists(target) || System.IO.Directory.Exists(target))
        {
            // 方案 1：SHGetImageList 大图标（Explorer 同款，256/48 原生档，锯齿根治主通道）
            var bmp = ShellImageListIcon(target, phys)
                // 方案 2：SHGetFileInfo LARGE（32×32，老系统/列表接口异常时兜底）
                ?? LegacyShGetFileInfo(target)
                // 方案 3：ExtractIconW（32×32，P/Invoke 经典写法兜底）
                ?? LegacyExtractIconW(target);
            if (bmp != null)
            {
                using (bmp)
                {
                    return ToBitmapSource(FitToFrame(bmp, phys));
                }
            }
        }

        return Fallback(phys, dark);
    }

    /// <summary>SHGetImageList 主通道：系统图标索引 → 对应尺寸档的大图标 HICON → 32bpp ARGB 位图。
    /// 在后台线程调用时若 COM 出错（罕见），外层 catch 返回 null 落到兜底链，不炸不卡。</summary>
    private static Bitmap? ShellImageListIcon(string target, int phys)
    {
        try
        {
            var shfi = new SHFILEINFOW();
            var hRes = SHGetFileInfoW(target, 0, ref shfi, (uint)Marshal.SizeOf<SHFILEINFOW>(), SHGFI_SYSICONINDEX);
            if (hRes == IntPtr.Zero) return null;
            int index = shfi.iIcon;

            int shil = phys <= 16 ? SHIL_SMALL
                : phys <= 32 ? SHIL_LARGE
                : phys <= 48 ? SHIL_EXTRALARGE
                : SHIL_JUMBO;

            var iid = typeof(IImageList).GUID;
            if (SHGetImageList(shil, iid, out var il) != 0) return null;
            try
            {
                if (il.GetIcon(index, ILD_TRANSPARENT, out IntPtr hIcon) != 0 || hIcon == IntPtr.Zero)
                    return null;
                try
                {
                    // ToBitmap 拷贝像素后再销毁 HICON（FromHandle 不拥有句柄，必须手动 DestroyIcon）
                    using var ico = System.Drawing.Icon.FromHandle(hIcon);
                    return ico.ToBitmap();
                }
                finally
                {
                    DestroyIcon(hIcon);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(il);
            }
        }
        catch
        {
            return null; // 兜底链接手
        }
    }

    /// <summary>兜底方案 2：SHGetFileInfo 直接拿 32×32 图标句柄（旧主通道，保留）</summary>
    private static Bitmap? LegacyShGetFileInfo(string target)
    {
        try
        {
            var shfi = new SHFILEINFOW();
            var hRes = SHGetFileInfoW(target, 0, ref shfi, (uint)Marshal.SizeOf<SHFILEINFOW>(),
                SHGFI_ICON | SHGFI_LARGEICON);
            if (hRes != IntPtr.Zero && shfi.hIcon != IntPtr.Zero)
            {
                var h = shfi.hIcon;
                try
                {
                    using var ico = System.Drawing.Icon.FromHandle(h);
                    return ico.ToBitmap();
                }
                finally
                {
                    DestroyIcon(h);
                }
            }
        }
        catch { /* fallthrough */ }
        return null;
    }

    /// <summary>兜底方案 3：ExtractIconW（调用方预留数组，P/Invoke 经典写法）</summary>
    private static Bitmap? LegacyExtractIconW(string target)
    {
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
                    return ico.ToBitmap();
                }
                finally
                {
                    DestroyIcon(h);
                }
            }
        }
        catch { /* fallthrough */ }
        return null;
    }

    /// <summary>判断 target 是否为系统动作 key（关机/锁定/静音…）</summary>
    public static bool IsSystemAction(string? target)
    {
        if (string.IsNullOrEmpty(target)) return false;
        return EasyStart.Models.ActionKey.All.Any(a => a.Key == target);
    }

    /// <summary>系统动作 key → 对应的 emoji 字符</summary>
    public static string IconForAction(string? target)
    {
        var found = EasyStart.Models.ActionKey.All.FirstOrDefault(a => a.Key == target);
        return found.Icon;
    }

    /// <summary>把 emoji/符号字符渲染成位图（用于系统项图标）。dark：深色主题下用浅色符号+浅底圆。
    /// size 直接按物理像素画，不经过缩放。</summary>
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
        return ToBitmapSource(bmp);
    }

    /// <summary>2026-11-09：裁留白 + 等比铺满（FitToFrame）。
    /// 实测（iconprobe 全量探测）：系统图像列表对低清图标（源最高 32–48px）在 256 大槽位
    /// 不放大、只居中留白，内容仅占画布 16–18%；旧链路"只缩不放"把留白原样缩下来，
    /// 在 125% DPI + 48px 设置（物理 60px）下图标仅 ~11px，即"部分图标太小"的根因。
    /// 本函数：① CropToContent 裁掉四周透明留白；② 等比缩放到目标物理尺寸
    /// （低清源允许 HighQualityBicubic 放大，与 Explorer 大图标视图行为一致）；
    /// ③ 居中放回方形画布。全帧高清图标（内容≈画布）裁剪无损、行为不变。</summary>
    private static Bitmap FitToFrame(Bitmap src, int size)
    {
        var content = CropToContent(src);
        double s = Math.Min((double)size / content.Width, (double)size / content.Height);
        int w2 = Math.Max(1, (int)Math.Round(content.Width * s));
        int h2 = Math.Max(1, (int)Math.Round(content.Height * s));

        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.DrawImage(content, (size - w2) / 2.0f, (size - h2) / 2.0f, w2, h2);
        }
        return bmp;
    }

    /// <summary>扫描 alpha 边界（阈值 8）裁掉四周透明留白，各留 1px 安全边防切到抗锯齿边缘。
    /// 全透明或位图无法锁定时返回原图，不影响显示主流程。</summary>
    private static Bitmap CropToContent(Bitmap src)
    {
        try
        {
            var rect = new Rectangle(0, 0, src.Width, src.Height);
            var data = src.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            int stride = data.Stride;
            var buf = new byte[Math.Abs(stride) * src.Height];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, buf, 0, buf.Length);
            src.UnlockBits(data);

            int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
            for (int y = 0; y < src.Height; y++)
            {
                int rowOff = y * stride;
                for (int x = 0; x < src.Width; x++)
                {
                    if (buf[rowOff + x * 4 + 3] > 8)
                    {
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                }
            }
            if (maxX < 0) return src; // 全透明：无内容可裁

            const int pad = 1;
            int x1 = Math.Max(0, minX - pad), y1 = Math.Max(0, minY - pad);
            int x2 = Math.Min(src.Width - 1, maxX + pad), y2 = Math.Min(src.Height - 1, maxY + pad);
            int w = x2 - x1 + 1, h = y2 - y1 + 1;
            if (w >= src.Width && h >= src.Height) return src; // 无留白：原样返回

            var cropped = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(cropped))
            {
                g.DrawImage(src, new Rectangle(0, 0, w, h), new Rectangle(x1, y1, w, h), GraphicsUnit.Pixel);
            }
            return cropped;
        }
        catch
        {
            return src; // 锁定位图失败等异常：退回原图，绝不让图标显示炸掉
        }
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

    /// <summary>自定义 .ico：WPF 解码全部内嵌帧，选「不小于目标的最小帧」；都小于目标时选最大帧
    /// （不做 GDI 放大）。解码异常时回退 GDI Icon 构造。</summary>
    private static System.Windows.Media.ImageSource LoadIco(string path, int size)
    {
        try
        {
            using var fs = new System.IO.FileStream(path, System.IO.FileMode.Open,
                System.IO.FileAccess.Read, System.IO.FileShare.Read);
            var decoder = BitmapDecoder.Create(fs, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var frames = decoder.Frames;
            if (frames.Count > 0)
            {
                var best = frames.Where(f => f.PixelWidth >= size)
                    .OrderBy(f => f.PixelWidth)
                    .FirstOrDefault()
                    ?? frames.OrderByDescending(f => f.PixelWidth).First();
                var conv = new FormatConvertedBitmap(best, System.Windows.Media.PixelFormats.Bgra32, null, 0);
                conv.Freeze();
                return conv;
            }
        }
        catch { /* 回退 GDI */ }

        using var ico = new System.Drawing.Icon(path, size, size);
        return ToBitmapSource(FitToFrame(ico.ToBitmap(), size));
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

    /// <summary>png/jpg/bmp 图标文件解码（选图时 .ico 之外的兜底）；大于目标高质量缩小，小于则原样返回</summary>
    private static System.Windows.Media.ImageSource LoadImageFile(string path, int size)
    {
        using var img = Image.FromFile(path);
        using var src = new Bitmap(img);
        return ToBitmapSource(FitToFrame(src, size));
    }

    private static System.Windows.Media.ImageSource Fallback(int size, bool dark = false)
    {
        // 辉哥哥：深色模式下灰圆图标看不出 → 深色主题用浅色圆 + 浅蓝描边
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        using (var brush = new SolidBrush(dark
            ? Color.FromArgb(90, 120, 160, 220)   // 深底：浅蓝灰
            : Color.FromArgb(60, 90, 90, 110)))   // 浅底：灰
        {
            g.FillEllipse(brush, 2, 2, size - 4, size - 4);
        }
        using (var pen = new Pen(dark
            ? Color.FromArgb(180, 140, 180, 240)
            : Color.FromArgb(140, 70, 70, 90), Math.Max(1f, size / 48f)))
        {
            g.DrawEllipse(pen, 2, 2, size - 5, size - 5);
        }
        return ToBitmapSource(bmp);
    }
}
