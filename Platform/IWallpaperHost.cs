namespace TableNote;

/// <summary>
/// The signed-in user's desktop wallpaper. Windows: <see cref="WindowsWallpaperHost"/> (IDesktopWallpaper, one picture
/// per monitor, with SystemParametersInfo as the fallback). Other systems: <see cref="UnsupportedWallpaperHost"/>.
/// No method throws: failure is the return value plus an English detail for tablenote.log.
/// </summary>
public interface IWallpaperHost
{
    bool Supported { get; }

    /// <summary>Pictures per monitor, how they are fitted, the colour behind them, slideshow / spotlight / policy.</summary>
    WallpaperState? Read(out string? detail);

    /// <summary>Picture for one monitor (<paramref name="monitorId"/> null = every monitor).</summary>
    bool SetPicture(string? monitorId, string path, out string? detail);

    /// <summary>No picture on any monitor: the background colour shows (Settings: "Solid color").</summary>
    bool ClearPicture(out string? detail);

    /// <summary>Starts the slideshow again with the pictures or folder it had.</summary>
    bool SetSlideshow(IReadOnlyList<string> items, uint options, uint tick, out string? detail);
}

public static class WallpaperHosts
{
    public static IWallpaperHost ForCurrentMachine() =>
        OperatingSystem.IsWindows() ? new WindowsWallpaperHost() : new UnsupportedWallpaperHost();
}

public sealed class UnsupportedWallpaperHost : IWallpaperHost
{
    private const string Detail = "this system has no wallpaper support in TableNote (Windows only)";
    public bool Supported => false;
    public WallpaperState? Read(out string? detail) { detail = Detail; return null; }
    public bool SetPicture(string? monitorId, string path, out string? detail) { detail = Detail; return false; }
    public bool ClearPicture(out string? detail) { detail = Detail; return false; }
    public bool SetSlideshow(IReadOnlyList<string> items, uint options, uint tick, out string? detail) { detail = Detail; return false; }
}
