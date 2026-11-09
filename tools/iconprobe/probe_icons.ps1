# IconProbe：诊断面板图标「太小」问题（2026-11-09 辉哥哥报告部分图标小）
# 逐项测量：
#   1. 源 exe/.ico 内嵌图标的真实尺寸清单（解析 RT_GROUP_ICON 头，0=256）
#   2. 我们的提取链输出：SHIL_JUMBO(256) / SHIL_EXTRALARGE(48) 的画布尺寸 + alpha 内容包围盒
#      （内容包围盒 << 画布 = 图标在框里显得小；≈画布 = 满框显示）
param()

$src = @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

public static class IconProbe
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

    static string BBox(Bitmap bmp)
    {
        var r = new Rectangle(0, 0, bmp.Width, bmp.Height);
        var data = bmp.LockBits(r, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        try
        {
            int stride = data.Stride;
            byte[] px = new byte[Math.Abs(stride) * bmp.Height];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, px, 0, px.Length);
            int rowOff = stride < 0 ? -stride : 0; // 简化：TopDown 位图 stride 为正
            for (int y = 0; y < bmp.Height; y++)
            {
                int baseOff = y * stride + rowOff;
                for (int x = 0; x < bmp.Width; x++)
                {
                    byte a = px[baseOff + x * 4 + 3];
                    if (a > 8) { if (x < minX) minX = x; if (x > maxX) maxX = x; if (y < minY) minY = y; if (y > maxY) maxY = y; }
                }
            }
        }
        finally { bmp.UnlockBits(data); }
        if (maxX < 0) return "空";
        int w = maxX - minX + 1, h = maxY - minY + 1;
        return string.Format("{0}x{1}({2}%)", w, h, w * 100 / bmp.Width);
    }

    public static string ShellMeasure(string path)
    {
        var shfi = new SHFILEINFOW();
        var hr = SHGetFileInfoW(path, 0, ref shfi, (uint)Marshal.SizeOf(typeof(SHFILEINFOW)), 0x4000);
        if (hr == IntPtr.Zero) return "ERR:SHGetFileInfo";
        int idx = shfi.iIcon;
        Guid iid = typeof(IImageList).GUID;
        string result = "";
        int[] shils = new int[] { 4, 2 }; // JUMBO, EXTRALARGE
        string[] names = new string[] { "J256", "X48" };
        for (int k = 0; k < shils.Length; k++)
        {
            IImageList il;
            if (SHGetImageList(shils[k], ref iid, out il) != 0) { result += names[k] + "=ERR "; continue; }
            try
            {
                IntPtr h;
                if (il.GetIcon(idx, 1, out h) != 0 || h == IntPtr.Zero) { result += names[k] + "=FAIL "; continue; }
                try
                {
                    Icon ico = Icon.FromHandle(h);
                    Bitmap bmp = ico.ToBitmap();
                    try { result += names[k] + " 画布" + bmp.Width + " 内容" + BBox(bmp) + "  "; }
                    finally { bmp.Dispose(); ico.Dispose(); }
                }
                finally { DestroyIcon(h); }
            }
            finally { Marshal.ReleaseComObject(il); }
        }
        return "idx=" + idx + " " + result;
    }

    // ===== 源文件真实图标尺寸 =====
    delegate bool EnumResProc(IntPtr h, IntPtr type, IntPtr name, IntPtr l);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern IntPtr LoadLibraryExW(string f, IntPtr h, uint flags);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool EnumResourceNamesW(IntPtr h, IntPtr t, EnumResProc cb, IntPtr l);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool FreeLibrary(IntPtr h);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr FindResourceW(IntPtr h, IntPtr name, IntPtr type);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr LoadResource(IntPtr h, IntPtr r);
    [DllImport("kernel32.dll")]
    static extern IntPtr LockResource(IntPtr h);

    static string ParseGroupDir(IntPtr p)
    {
        int count = Marshal.ReadInt16(p, 4);
        var sb = new StringBuilder();
        int off = 6;
        for (int i = 0; i < count && i < 16; i++)
        {
            int w = Marshal.ReadByte(p, off);
            if (w == 0) w = 256;
            sb.Append(w).Append(',');
            off += 14;
        }
        return sb.ToString().TrimEnd(',');
    }

    public static string SourceSizes(string path)
    {
        try
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".ico")
            {
                byte[] b = File.ReadAllBytes(path);
                int count = BitConverter.ToUInt16(b, 4);
                var sb2 = new StringBuilder();
                for (int i = 0; i < count && i < 16; i++)
                {
                    int w = b[6 + i * 16];
                    if (w == 0) w = 256;
                    sb2.Append(w).Append(',');
                }
                return "ico帧[" + sb2.ToString().TrimEnd(',') + "]";
            }
            if (ext != ".exe" && ext != ".dll") return "(shell标准图标)";
            IntPtr h = LoadLibraryExW(path, IntPtr.Zero, 0x22); // LOAD_LIBRARY_AS_DATAFILE|AS_IMAGE_RESOURCE
            if (h == IntPtr.Zero) return "(加载失败)";
            try
            {
                var sizes = new System.Collections.Generic.List<string>();
                EnumResProc cb = delegate(IntPtr hMod, IntPtr type, IntPtr name, IntPtr l)
                {
                    IntPtr r = FindResourceW(hMod, name, (IntPtr)14 /*RT_GROUP_ICON*/);
                    if (r != IntPtr.Zero)
                    {
                        IntPtr lr = LoadResource(hMod, r);
                        if (lr != IntPtr.Zero) sizes.Add(ParseGroupDir(LockResource(lr)));
                    }
                    return true;
                };
                EnumResourceNamesW(h, (IntPtr)14, cb, IntPtr.Zero);
                GC.KeepAlive(cb);
                if (sizes.Count == 0) return "(无内嵌图标)";
                return "源[" + string.Join(" / ", sizes.ToArray()) + "]";
            }
            finally { FreeLibrary(h); }
        }
        catch (Exception ex) { return "(异常:" + ex.Message + ")"; }
    }
}
'@

Add-Type -TypeDefinition $src -ReferencedAssemblies System.Drawing

$dataPath = Join-Path $env:LOCALAPPDATA 'EasyStart\profiles\3e699fd1-45f8-4029-a1a5-b906a363ecc0\data.json'
$raw = Get-Content $dataPath -Raw -Encoding UTF8
$profile = $raw | ConvertFrom-Json

# 当前图标尺寸设置（供换算参考）
Write-Host ("ItemIconSize 设置: {0}  主题: {1}" -f $profile.Settings.ItemIconSize, $profile.Settings.Theme)

$targets = @()
foreach ($t in $profile.Tabs) {
    foreach ($it in $t.Items) {
        $tg = $it.Target
        if ($it.PSObject.Properties['Uri']) { $tg = $it.Uri }
        if ($tg) { $targets += [pscustomobject]@{ Name = $it.Name; Target = $tg; IconPath = $it.IconPath } }
    }
}

$report = foreach ($item in $targets) {
    $p = $item.Target
    if ($p -match '^https?://') { continue }
    $exists = Test-Path -LiteralPath $p
    if (-not $exists) {
        [pscustomobject]@{ 名称 = $item.Name; 路径 = $p; 存在 = '否'; 提取链输出 = '(fallback 圆)'; 源图标 = '' }
        continue
    }
    $shell = ''
    $srcSz = ''
    try { $shell = [IconProbe]::ShellMeasure($p) } catch { $shell = '探测异常:' + $_.Exception.Message }
    if ($item.IconPath) { $srcSz = '自定义:' + $item.IconPath }
    else { try { $srcSz = [IconProbe]::SourceSizes($p) } catch { $srcSz = '' } }
    [pscustomobject]@{ 名称 = $item.Name; 路径 = $p; 存在 = '是'; 提取链输出 = $shell; 源图标 = $srcSz }
}
$report | Format-Table -AutoSize -Wrap | Out-String -Width 260
