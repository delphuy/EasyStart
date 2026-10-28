namespace EasyStart.Models;

/// <summary>窗口几何（对齐 Rolan window.json 实测 {x:399,y:112,w:956,h:600}）</summary>
public class WindowGeometry
{
    public double X { get; set; } = 399;
    public double Y { get; set; } = 112;
    public double Width { get; set; } = 956;
    public double Height { get; set; } = 600;

    /// <summary>吸附角：None/TopLeft/TopRight/BottomLeft/BottomRight（Rolan 四角吸附）</summary>
    public string? DockCorner { get; set; }
}
