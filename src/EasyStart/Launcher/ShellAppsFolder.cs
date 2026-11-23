using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace EasyStart.Launcher;

/// <summary>
/// shell:AppsFolder 的原生枚举（2026-11-23 辉哥哥：商店应用要进「已安装」列表）。
/// 选型为本机实测结论：非打包进程调 WinRT PackageManager 全部 E_ACCESSDENIED，
/// net8.0-windows10.0.19041.0 投影的 AppListEntry 也没有 DisplayName/Logo 成员；
/// IExtractIconW、IShellItemImageFactory 对 AppsFolder 子项一律 E_NOINTERFACE。
/// 可行路线：桌面 IShellFolder.ParseDisplayName("shell:AppsFolder") → BindToObject
/// → EnumObjects；子项 SHGDN_INFOLDER|FORPARSING 得 AUMID（如 OpenAI.Codex_…!App），
/// SHGDN_INFOLDER|NORMAL 得本地化显示名（如 ChatGPT / 记事本）。
/// </summary>
internal static class ShellAppsFolder
{
    // 商店应用 AUMID 固定形态：包名_13位小写哈希!应用Id（系统自带 MSIX 同格式，
    // 记事本/计算器/设置都在内）；AppsFolder 里的桌面 win32 条目不带该形态，正则天然过滤。
    private static readonly Regex StoreAppPattern =
        new(@"^[A-Za-z0-9.\-]+_[a-z0-9]{13}!", RegexOptions.Compiled);

    private const uint SHGDN_INFOLDER = 0x0001;
    private const uint SHGDN_FORPARSING = 0x8000;
    private const uint SHCONTF_EVERYTHING = 0x00E0; // FOLDERS | NONFOLDERS | INCLUDEHIDDEN

    /// <summary>枚举全部商店应用（显示名 + AUMID）。原生层任何意外都安静返回空列表，扫描不因此中断。</summary>
    public static List<(string Name, string Aumid)> EnumStoreApps()
    {
        var result = new List<(string, string)>();
        try
        {
            if (SHGetDesktopFolder(out var desktopPtr) != 0) return result;
            var desktop = (IShellFolder)Marshal.GetObjectForIUnknown(desktopPtr);
            Marshal.Release(desktopPtr); // RCW 已持有引用，这里释放原始引用计数
            try
            {
                desktop.ParseDisplayName(IntPtr.Zero, IntPtr.Zero, "shell:AppsFolder",
                    out _, out var pidlApps, 0, out _);
                try
                {
                    var guid = typeof(IShellFolder).GUID;
                    if (desktop.BindToObject(pidlApps, IntPtr.Zero, ref guid, out var appsPtr) != 0)
                        return result;
                    var apps = (IShellFolder)Marshal.GetObjectForIUnknown(appsPtr);
                    Marshal.Release(appsPtr);
                    try
                    {
                        CollectStoreApps(apps, result);
                    }
                    finally { Marshal.ReleaseComObject(apps); }
                }
                finally { Marshal.FreeCoTaskMem(pidlApps); }
            }
            finally { Marshal.ReleaseComObject(desktop); }
        }
        catch
        {
            // 只是一路补充数据源，绝不让扫描失败
        }
        return result;
    }

    private static void CollectStoreApps(IShellFolder apps, List<(string, string)> result)
    {
        if (apps.EnumObjects(IntPtr.Zero, SHCONTF_EVERYTHING, out var enumPtr) != 0) return;
        var e = (IEnumIDList)Marshal.GetObjectForIUnknown(enumPtr);
        Marshal.Release(enumPtr);
        try
        {
            while (true)
            {
                if (e.Next(1, out var pidl, out _) != 0) break;
                try
                {
                    var aumid = GetName(apps, pidl, SHGDN_INFOLDER | SHGDN_FORPARSING);
                    if (aumid == null || !StoreAppPattern.IsMatch(aumid)) continue;
                    var name = GetName(apps, pidl, SHGDN_INFOLDER);
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    result.Add((name!, aumid));
                }
                finally { Marshal.FreeCoTaskMem(pidl); }
            }
        }
        finally { Marshal.ReleaseComObject(e); }
    }

    /// <summary>
    /// GetDisplayNameOf + StrRetToStr。STRRET 的联合体偏移 x64 是 8、x86 是 4，
    /// 手写结构体两头都对不齐就有一头越界写；干脆不定义结构体——
    /// 手工开 272 字节缓冲（两种位宽的原生 sizeof 上限）传裸指针，交给 StrRetToStr 原生解读。
    /// </summary>
    private static string? GetName(IShellFolder apps, IntPtr pidl, uint flags)
    {
        var buf = Marshal.AllocCoTaskMem(272);
        try
        {
            if (apps.GetDisplayNameOf(pidl, flags, buf) != 0) return null;
            if (StrRetToStr(buf, pidl, out var ppsz) != 0) return null;
            try { return Marshal.PtrToStringUni(ppsz); }
            finally { Marshal.FreeCoTaskMem(ppsz); }
        }
        finally { Marshal.FreeCoTaskMem(buf); }
    }

    [DllImport("shell32.dll", PreserveSig = true)]
    private static extern int SHGetDesktopFolder(out IntPtr ppshf);

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int StrRetToStr(IntPtr pstr, IntPtr pidl, out IntPtr ppsz);

    [ComImport, Guid("000214E6-0000-0000-C000-000000000046"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellFolder
    {
        // vtable 槽位必须与原生完全一致（BindToStorage 不能省，否则 GetUIObjectOf
        // 之后的槽位全部错一格，之前探测时 E_INVALIDARG 的根因）。
        void ParseDisplayName(IntPtr hwnd, IntPtr pbc, [MarshalAs(UnmanagedType.LPWStr)] string pszDisplayName,
            out uint pchEaten, out IntPtr ppidl, uint rgfIn, out uint pdwAttributes);
        [PreserveSig] int EnumObjects(IntPtr hwnd, uint grfFlags, out IntPtr ppenumIDList);
        [PreserveSig] int BindToObject(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int BindToStorage(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int CompareIDs(IntPtr lcid, IntPtr pidl1, IntPtr pidl2);
        [PreserveSig] int CreateViewObject(IntPtr hwnd, ref Guid riid, out IntPtr ppv);
        void GetAttributesOf(uint cidl, IntPtr apidl, ref uint rgfInOut);
        [PreserveSig] int GetUIObjectOf(IntPtr hwnd, uint cidl, IntPtr[] apidl, ref Guid riid,
            IntPtr reserved, [MarshalAs(UnmanagedType.Interface)] out object ppv);
        [PreserveSig] int GetDisplayNameOf(IntPtr pidl, uint uFlags, IntPtr pName);
        void SetNameOf(IntPtr hwnd, IntPtr pidl, [MarshalAs(UnmanagedType.LPWStr)] string pszName,
            uint uFlags, out IntPtr ppidlOut);
    }

    [ComImport, Guid("000214F2-0000-0000-C000-000000000046"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEnumIDList
    {
        [PreserveSig] int Next(uint celt, out IntPtr rgelt, out uint pceltFetched);
        void Skip(uint celt);
        void Reset();
        void Clone(out IntPtr ppenum);
    }
}
