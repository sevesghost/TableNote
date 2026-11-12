namespace TableNote;

public sealed class WindowMemory
{
    public int Width { get; set; } = 720;
    public int Height { get; set; } = 480;
    public int? X { get; set; }
    public int? Y { get; set; }
    public bool Topmost { get; set; }
    public DesktopLayerKind Layer { get; set; }
    public bool Locked { get; set; }
}
