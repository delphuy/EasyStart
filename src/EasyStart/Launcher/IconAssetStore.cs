using System.IO;
using System.Security.Cryptography;

namespace EasyStart.Launcher;

/// <summary>
/// 自定义图标资产存储。
/// 用户手选的图片不直接引用原路径（桌面/下载目录里的文件很容易被删掉），
/// 保存时复制到 %LOCALAPPDATA%\EasyStart\icons，并用内容哈希命名：
/// 同一张图反复保存不会堆积，多个快捷项共用同一文件也能自然复用。
/// </summary>
public static class IconAssetStore
{
    private static readonly string[] SupportedExtensions = { ".png", ".jpg", ".jpeg", ".ico", ".bmp" };

    public static bool IsSupported(string path)
        => SupportedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>复制自定义图标到稳定目录，返回可长期保存的绝对路径。</summary>
    public static string Store(string sourcePath)
    {
        var fullPath = Path.GetFullPath(sourcePath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("图标文件不存在", fullPath);

        var ext = Path.GetExtension(fullPath).ToLowerInvariant();
        if (!IsSupported(fullPath))
            throw new NotSupportedException($"不支持的图标格式：{ext}");

        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EasyStart", "icons");
        Directory.CreateDirectory(dir);

        string hash;
        using (var stream = File.OpenRead(fullPath))
            hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();

        var dest = Path.Combine(dir, $"custom-{hash}{ext}");
        if (string.Equals(fullPath, dest, StringComparison.OrdinalIgnoreCase))
            return dest;

        if (!File.Exists(dest))
            File.Copy(fullPath, dest, overwrite: false);
        return dest;
    }
}