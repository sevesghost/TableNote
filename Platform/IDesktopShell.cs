namespace TableNote;

public interface IDesktopShell
{
    DesktopAttachResult TryAttach(nint hwnd);
    void Detach(nint hwnd);

    /// <summary>Win+D-proof fallback when no desktop host accepts the window. False = not supported.</summary>
    bool EnterBottomFallback(nint hwnd) => false;
    void KeepBottomFallbackAlive(nint hwnd) { }
    void ExitBottomFallback(nint hwnd) { }
    bool InBottomFallback => false;
    /// <summary>False when the parent host vanished (e.g. Explorer restarted).</summary>
    bool HostAlive() => true;
    /// <summary>Off-UI-thread preparation (Windows: Progman 0x052C + window scan). Never blocks the caller.</summary>
    Task PrepareAsync() => Task.CompletedTask;
}
