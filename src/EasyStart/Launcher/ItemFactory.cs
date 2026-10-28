using EasyStart.Models;

namespace EasyStart.Launcher;

/// <summary>
/// 拖拽识别（P0-F3）：把拖入的文件/文件夹/网址识别为 LaunchItem。
/// 支持 .lnk/.exe/.bat/.url/文件夹/纯文本 URL。
/// </summary>
public static class ItemFactory
{
    public static LaunchItem? FromDrop(string path, string? urlText)
    {
        // 纯文本（拖入文本或剪贴板 URL）
        if (!string.IsNullOrWhiteSpace(urlText))
        {
            var t = urlText.Trim();
            if (Uri.TryCreate(t, UriKind.Absolute, out var u)
                && (u.Scheme == "http" || u.Scheme == "https"))
                return new LaunchItem
                {
                    Type = LaunchItemType.Uri,
                    Name = u.Host,
                    Uri = t,
                };
        }

        if (string.IsNullOrEmpty(path)) return null;

        // 文件夹
        if (System.IO.Directory.Exists(path))
            return new LaunchItem
            {
                Type = LaunchItemType.Folder,
                Name = System.IO.Path.GetFileName(path.TrimEnd('\\')),
                Target = path,
            };

        var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        switch (ext)
        {
            case ".lnk":
                return new LaunchItem
                {
                    Type = LaunchItemType.Shortcut,
                    Name = System.IO.Path.GetFileNameWithoutExtension(path),
                    Target = path,
                };
            case ".url":
                // 解析 .url 文件
                try
                {
                    var lines = System.IO.File.ReadAllLines(path);
                    var url = lines.FirstOrDefault(l => l.StartsWith("URL=", System.StringComparison.OrdinalIgnoreCase));
                    if (url != null)
                    {
                        var u = url[4..].Trim();
                        return new LaunchItem
                        {
                            Type = LaunchItemType.Uri,
                            Name = System.IO.Path.GetFileNameWithoutExtension(path),
                            Uri = u,
                        };
                    }
                }
                catch { }
                return new LaunchItem
                {
                    Type = LaunchItemType.Shortcut,
                    Name = System.IO.Path.GetFileNameWithoutExtension(path),
                    Target = path,
                };
            case ".exe":
            case ".bat":
            case ".cmd":
                return new LaunchItem
                {
                    Type = LaunchItemType.Shortcut,
                    Name = System.IO.Path.GetFileNameWithoutExtension(path),
                    Target = path,
                };
            default:
                // 其他文件：作为"打开文件"项
                return new LaunchItem
                {
                    Type = LaunchItemType.Shortcut,
                    Name = System.IO.Path.GetFileNameWithoutExtension(path),
                    Target = path,
                };
        }
    }
}
