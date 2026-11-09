# FitVerify：验证 FitToFrame（裁留白+等比铺满）对"图标太小"的修复效果（2026-11-10）
# 对同一张 SHIL_JUMBO(256) 系统位图分别模拟：
#   旧算法：256 画布直接等比缩到 phys → 内容 = bboxMax * phys/256（留白被忠实保留）
#   新算法：CropToContent(1px安全边, alpha>8) + 等比缩放居中到 phys → 内容实测
param()

$src = @'
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class FitVerify
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SHFILEINFOW
    {
        public IntPtr hIcon; public int iIcon; public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr SHGetFileInfoW(string pszPath, uint dwFileAttributes, ref SHFILEINFOW psfi, uint cbFileInfo, uint uFlags);

    [ComImport, Guid("46EB5926-582E-4017-9FDF-E8998DAA0950"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IImageList
    {
        void _Add(IntPtr a, IntPtr b, int i);
        void _ReplaceIcon(int i, IntPtr h, int pi);
        void _SetOverlayImage(int i, int o);
        void _Replace(int i, IntPtr a, IntPtr b);
        void _AddMasked(IntPtr b, int c, int pi);
        void _Draw(IntPtr p);
        void _Remove(int i);
        [PreserveSig] int GetIcon(int i, int flags, out IntPtr picon);
    }

    [DllImport("shell32.dll", EntryPoint = "SHGetImageList")]
    static extern int SHGetImageList(int iImageList, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IImageList ppv);

    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);

    static Bitmap GetJumbo(string path)
    {
        var shfi = new SHFILEINFOW();
        var hr = SHGetFileInfoW(path, 0, ref shfi, (uint)Marshal.SizeOf(typeof(SHFILEINFOW)), 0x4000);
        if (hr == IntPtr.Zero) return null;
        Guid iid = typeof(IImageList).GUID;
        IImageList il;
        if (SHGetImageList(4, ref iid, out il) != 0) return null;
        IntPtr hIcon;
        if (il.GetIcon(shfi.iIcon, 1, out hIcon) != 0) return null;
        Icon ic = (Icon)Icon.FromHandle(hIcon).Clone();
        DestroyIcon(hIcon);
        using (ic) return ic.ToBitmap();
    }

    static Rectangle ContentBBox(Bitmap bmp)
    {
        var r = new Rectangle(0, 0, bmp.Width, bmp.Height);
        var data = bmp.LockBits(r, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        try
        {
            int stride = data.Stride;
            byte[] px = new byte[Math.Abs(stride) * bmp.Height];
            Marshal.Copy(data.Scan0, px, 0, px.Length);
            for (int y = 0; y < bmp.Height; y++)
            {
                int baseOff = y * stride;
                for (int x = 0; x < bmp.Width; x++)
                {
                    if (px[baseOff + x * 4 + 3] > 8)
                    {
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                }
            }
        }
        finally { bmp.UnlockBits(data); }
        if (maxX < 0) return Rectangle.Empty;
        return Rectangle.FromLTRB(minX, minY, maxX, maxY);
    }

    // 旧算法：整画布等比缩到 phys，留白保留 → 框内内容尺寸（按比例推算）
    public static string OldAlgo(string path, int phys)
    {
        using (var bmp = GetJumbo(path))
        {
            if (bmp == null) return "ERR";
            var b = ContentBBox(bmp);
            if (b.IsEmpty) return "空";
            int content = Math.Max(b.Width, b.Height);
            double inFrame = content * (double)phys / bmp.Width;
            return string.Format("{0}px({1}x{2}源)", (int)Math.Round(inFrame), b.Width, b.Height);
        }
    }

    // 新算法：CropToContent(1px安全边) + 等比缩放居中 phys —— 实测输出位图的内容
    public static string NewAlgo(string path, int phys)
    {
        using (var bmp = GetJumbo(path))
        {
            if (bmp == null) return "ERR";
            var b = ContentBBox(bmp);
            if (b.IsEmpty) return "空";
            int x1 = Math.Max(0, b.X - 1), y1 = Math.Max(0, b.Y - 1);
            int x2 = Math.Min(bmp.Width - 1, b.Right), y2 = Math.Min(bmp.Height - 1, b.Bottom);
            int w = x2 - x1 + 1, h = y2 - y1 + 1;

            using (var cropped = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(cropped))
                    g.DrawImage(bmp, new Rectangle(0, 0, w, h), new Rectangle(x1, y1, w, h), GraphicsUnit.Pixel);

                double s = Math.Min((double)phys / w, (double)phys / h);
                int w2 = Math.Max(1, (int)Math.Round(w * s));
                int h2 = Math.Max(1, (int)Math.Round(h * s));
                using (var outp = new Bitmap(phys, phys, PixelFormat.Format32bppArgb))
                {
                    using (var g = Graphics.FromImage(outp))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.SmoothingMode = SmoothingMode.HighQuality;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.CompositingQuality = CompositingQuality.HighQuality;
                        g.DrawImage(cropped, (phys - w2) / 2.0f, (phys - h2) / 2.0f, w2, h2);
                    }
                    var rb = ContentBBox(outp);
                    if (rb.IsEmpty) return "空";
                    return string.Format("{0}px({1}x{2})", Math.Max(rb.Width, rb.Height), rb.Width, rb.Height);
                }
            }
        }
    }
}
'@
Add-Type -TypeDefinition $src -ReferencedAssemblies System.Drawing

$phys = 60   # 48px 逻辑 × 125% DPI → 物理 60

$tests = @(
    @{ n = 'Notepad--';          p = 'C:\Users\dehui\AppData\Local\Programs\Notepad--\Notepad--.exe' }
    @{ n = 'IT_Help';            p = 'C:\Users\youdh\AppData\Local\IT_Help\IT_Help.exe' }
    @{ n = 'OpenVPN';            p = 'C:\Program Files\DigiWin OpenVPN\bin\openvpn-gui.exe' }
    @{ n = 'DsInfo(自己人)';     p = 'D:\03_software\IT_tools\自己人\DsInfo\DsInfo.exe' }
    @{ n = '万彩办公大师';       p = 'C:\Program Files (x86)\万彩办公大师OfficeBox\officebox.exe' }
    @{ n = 'digiwin_vpn(Mars)';  p = 'D:\00 工作文件\00.平台环境\测试环境\AIGP_E10\Digiwin.Mars.Deployment.Client.exe' }
    @{ n = '昆山天气';           p = 'D:\02_study-files\webweather\昆山天气.exe' }
    @{ n = 'QQ(对照)';           p = 'C:\Program Files\Tencent\QQNT\QQ.exe' }
    @{ n = 'QQ音乐(对照)';       p = 'C:\Program Files (x86)\Tencent\QQMusic\QQMusic.exe' }
)

Write-Output ("物理目标: {0}px  (48px 逻辑 x 125% DPI)" -f $phys)
Write-Output ("{0,-20} {1,-26} {2,-22}" -f '项', '旧算法(框内内容)', '新算法(框内内容)')
foreach ($t in $tests) {
    $old = [FitVerify]::OldAlgo($t.p, $phys)
    $new = [FitVerify]::NewAlgo($t.p, $phys)
    Write-Output ("{0,-20} {1,-26} {2,-22}" -f $t.n, $old, $new)
}
