namespace TableNote;

public sealed class UnsupportedDesktopShell : IDesktopShell
{
    public DesktopAttachResult TryAttach(nint hwnd) =>
        new(false, DesktopHostKind.None, "this system has no Windows desktop layer");

    public void Detach(nint hwnd) { }
}
