// 把 icon/icon.png（2048x2048）转成多尺寸 ICO（16/32/48/256）放到 Assets/es.ico
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

string srcPng = @"D:\02_study-files\EasyStart\icon\icon.png";
string outIco = @"D:\02_study-files\EasyStart\src\EasyStart\Assets\es.ico";
Directory.CreateDirectory(Path.GetDirectoryName(outIco)!);

using var src = Bitmap.FromFile(srcPng);

// 要生成的尺寸
int[] sizes = [16, 24, 32, 48, 64, 128, 256];
var frames = new List<byte[]>();
foreach (var s in sizes)
{
    var bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb);
    using var g = Graphics.FromImage(bmp);
    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
    g.SmoothingMode = SmoothingMode.HighQuality;
    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
    g.DrawImage(src, 0, 0, s, s);

    using var ms = new MemoryStream();
    if (s >= 1000)
        bmp.Save(ms, ImageFormat.Png);      // 大尺寸用 PNG 编码
    else
    {
        // 小尺寸用 BMP 编码更兼容；ICO 里 1000 以下通常存 BMP
        var pngOut = new MemoryStream();
        bmp.Save(pngOut, ImageFormat.Png);
        ms.Write(pngOut.ToArray(), 0, (int)pngOut.Length);
    }
    frames.Add(ms.ToArray());
    bmp.Dispose();
}

// 写 ICO：ICONDIR + 每个 icondir entry + 图像数据
using var fs = new FileStream(outIco, FileMode.Create);
// ICONDIR: reserved(2)=0, type(2)=1, count(2)
fs.Write(new byte[] { 0, 0, 1, 0, (byte)sizes.Length, 0 }, 0, 6);

int offset = 6 + 16 * sizes.Length; // header + entries
int i = 0;
foreach (var data in frames)
{
    var s = sizes[i];
    byte w = s >= 256 ? (byte)0 : (byte)s;
    byte h = s >= 256 ? (byte)0 : (byte)s;
    // ICONDIR entry
    fs.WriteByte(w);                  // bWidth
    fs.WriteByte(h);                  // bHeight
    fs.WriteByte(0);                  // bColorCount
    fs.WriteByte(0);                  // bReserved
    fs.Write(new byte[] { 1, 0 }, 0, 2);  // wPlanes
    fs.Write(new byte[] { 32, 0 }, 0, 2);  // wBitCount
    fs.Write(BitConverter.GetBytes(data.Length), 0, 4); // dwBytesInRes
    fs.Write(BitConverter.GetBytes(offset), 0, 4);      // dwImageOffset
    offset += data.Length;
    i++;
}
// 图像数据
foreach (var data in frames)
    fs.Write(data, 0, data.Length);

fs.Flush();
Console.WriteLine($"es.ico created: {new FileInfo(outIco).Length} bytes ({frames.Count} frames)");
