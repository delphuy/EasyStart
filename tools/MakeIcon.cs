// 生成一个最小 es.ico 占位（32x32 单帧），构建后可替换为正式图标
using System.Drawing;
using System.Drawing.Imaging;

var ico = new Bitmap(32, 32);
using (var g = Graphics.FromImage(ico))
{
    g.Clear(System.Drawing.Color.FromArgb(0x0A, 0x6C, 0xF1));
    g.FillEllipse(Brushes.White, 6, 6, 20, 20);
}
// 保存为 ico（简化：存 png 命名 ico，多数场景可用；正式图标后续替换）
// 这里生成真正的 .ico 格式
using var ms = new MemoryStream();
ico.Save(ms, ImageFormat.Png);
// 写 ico 头 + png 数据
var icoPath = "D:\\02_study-files\\EasyStart\\src\\EasyStart\\Assets\\es.ico";
Directory.CreateDirectory(System.IO.Path.GetDirectoryName(icoPath)!);
using var fs = new FileStream(icoPath, FileMode.Create);
// ICO header
fs.Write(new byte[] { 0,0, 1,0, 1,0 }, 0, 6);
var pngData = ms.ToArray();
fs.Write(BitConverter.GetBytes(0), 0, 2); // 宽 0=256
fs.Write(BitConverter.GetBytes(0), 0, 2); // 高 0=256
fs.WriteByte(0); // 颜色数
fs.WriteByte(0); // 保留
fs.Write(BitConverter.GetBytes(1), 0, 2); // 平面
fs.Write(BitConverter.GetBytes(32), 0, 2); // bpp
fs.Write(BitConverter.GetBytes(pngData.Length), 0, 4); // 数据大小
fs.Write(BitConverter.GetBytes(22), 0, 4); // 偏移 6+16
fs.Write(pngData, 0, pngData.Length);
fs.Close();
System.Console.WriteLine("es.ico created: " + new FileInfo(icoPath).Length + " bytes");
