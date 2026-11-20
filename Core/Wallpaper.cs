using System.Text.Json;
using Avalonia;
using Avalonia.Media;

namespace TableNote;

/// <summary>How Windows fits the picture (IDesktopWallpaper DESKTOP_WALLPAPER_POSITION, same numbers).</summary>
public enum WallpaperFit { Center = 0, Tile = 1, Stretch = 2, Fit = 3, Fill = 4, Span = 5 }

/// <summary>One connected monitor: its device id, rectangle in screen pixels, and picture ("" = none).</summary>
public sealed record WallpaperMonitor(string Id, PixelRect Bounds, string Picture);

/// <summary>A monitor as Avalonia sees it: rectangle and work area in screen pixels, and its scale factor.</summary>
public readonly record struct WallpaperScreen(PixelRect Bounds, PixelRect WorkArea, double Scaling);

/// <summary>What the desktop shows now (<see cref="IWallpaperHost.Read"/>).</summary>
public sealed class WallpaperState
{
    public IReadOnlyList<WallpaperMonitor> Monitors { get; init; } = [];
    public WallpaperFit Fit { get; init; } = WallpaperFit.Fill;
    /// <summary>COLORREF (0x00BBGGRR) shown where no picture covers the screen.</summary>
    public uint BackgroundColor { get; init; }
    /// <summary>False: one picture for every monitor (SystemParametersInfo); the single entry has no rectangle.</summary>
    public bool PerMonitor { get; init; } = true;
    public bool Slideshow { get; init; }
    public IReadOnlyList<string> SlideshowItems { get; init; } = [];
    public uint SlideshowOptions { get; init; }
    public uint SlideshowTick { get; init; }
    public bool Spotlight { get; init; }
    public bool PolicyLocked { get; init; }
}

/// <summary>
/// wallpaper.json: what to put back. Written before TableNote changes the wallpaper, deleted once it is back.
/// A start that finds it (after a crash, a sign-out, or a failed restore) resumes or restores from it.
/// </summary>
public sealed class WallpaperRecord
{
    public int Version { get; set; } = 1;
    /// <summary>Monitor TableNote draws on; "" = every monitor (span, or the one-picture fallback).</summary>
    public string Monitor { get; set; } = "";
    /// <summary>Picture the monitor had ("" = solid colour).</summary>
    public string Original { get; set; } = "";
    /// <summary>TableNote's copy of <see cref="Original"/>, used when that file is gone or is Windows' own cache.</summary>
    public string? Backup { get; set; }
    public string Fit { get; set; } = "Fill";
    public bool Slideshow { get; set; }
    public List<string> SlideshowItems { get; set; } = [];
    public uint SlideshowOptions { get; set; }
    public uint SlideshowTick { get; set; }
    public bool Spotlight { get; set; }
    /// <summary>The table picture TableNote set last (null until the first one is set).</summary>
    public string? Applied { get; set; }
}

/// <summary>Placement maths for the wallpaper picture and the table card. Pure, so tests cover it without Windows.</summary>
public static class WallpaperGeometry
{
    /// <summary>Smallest card TableNote keeps on screen when the window sits near an edge.</summary>
    public const int MinCard = 160;

    /// <summary>Where a picture goes in the area for this fit (Tile: the first tile at the top left).</summary>
    public static Rect Place(WallpaperFit fit, double imageWidth, double imageHeight, double areaWidth, double areaHeight)
    {
        if (imageWidth <= 0 || imageHeight <= 0 || areaWidth <= 0 || areaHeight <= 0)
            return new Rect(0, 0, Math.Max(0, areaWidth), Math.Max(0, areaHeight));
        Rect Centered(double w, double h) => new((areaWidth - w) / 2, (areaHeight - h) / 2, w, h);
        switch (fit)
        {
            case WallpaperFit.Stretch:
                return new Rect(0, 0, areaWidth, areaHeight);
            case WallpaperFit.Center:
                return Centered(imageWidth, imageHeight);
            case WallpaperFit.Tile:
                return new Rect(0, 0, imageWidth, imageHeight);
            case WallpaperFit.Fit:
            {
                var s = Math.Min(areaWidth / imageWidth, areaHeight / imageHeight);
                return Centered(imageWidth * s, imageHeight * s);
            }
            default: // Fill, Span (span fills the box around every monitor)
            {
                var s = Math.Max(areaWidth / imageWidth, areaHeight / imageHeight);
                return Centered(imageWidth * s, imageHeight * s);
            }
        }
    }

    /// <summary>Width to decode a big picture at: enough for its drawn size, never more than it has.</summary>
    public static int DecodeWidth(WallpaperFit fit, int imageWidth, int imageHeight, int areaWidth, int areaHeight)
    {
        if (imageWidth <= 0) return 1;
        if (fit is WallpaperFit.Center or WallpaperFit.Tile) return imageWidth;
        var drawn = Place(fit, imageWidth, imageHeight, areaWidth, areaHeight);
        return Math.Clamp((int)Math.Ceiling(drawn.Width), 1, imageWidth);
    }

    /// <summary>The card's box on the wallpaper: the window's rectangle, kept inside its monitor's work area.</summary>
    public static PixelRect CardBox(PixelRect window, PixelRect workArea)
    {
        if (workArea.Width <= 0 || workArea.Height <= 0) return window;
        var x = Math.Clamp(window.X, workArea.X, Math.Max(workArea.X, workArea.Right - Math.Min(MinCard, workArea.Width)));
        var y = Math.Clamp(window.Y, workArea.Y, Math.Max(workArea.Y, workArea.Bottom - Math.Min(MinCard, workArea.Height)));
        var w = Math.Min(Math.Max(window.Width, 1), workArea.Right - x);
        var h = Math.Min(Math.Max(window.Height, 1), workArea.Bottom - y);
        return new PixelRect(x, y, Math.Max(1, w), Math.Max(1, h));
    }

    /// <summary>Index of the rectangle that holds most of the window; the nearest one when it is on none.</summary>
    public static int Pick(IReadOnlyList<PixelRect> rects, PixelRect window)
    {
        if (rects.Count == 0) return -1;
        var best = -1;
        long bestArea = 0;
        for (var i = 0; i < rects.Count; i++)
        {
            var cut = rects[i].Intersect(window);
            long area = (long)cut.Width * cut.Height;
            if (area > bestArea) { bestArea = area; best = i; }
        }
        if (best >= 0) return best;
        var cx = window.X + window.Width / 2.0;
        var cy = window.Y + window.Height / 2.0;
        var nearest = 0;
        var nearestDistance = double.MaxValue;
        for (var i = 0; i < rects.Count; i++)
        {
            var r = rects[i];
            var dx = Math.Max(0, Math.Max(r.X - cx, cx - r.Right));
            var dy = Math.Max(0, Math.Max(r.Y - cy, cy - r.Bottom));
            var d = dx * dx + dy * dy;
            if (d < nearestDistance) { nearestDistance = d; nearest = i; }
        }
        return nearest;
    }

    public static PixelRect Union(IEnumerable<PixelRect> rects)
    {
        int? left = null, top = null, right = null, bottom = null;
        foreach (var r in rects)
        {
            if (r.Width <= 0 || r.Height <= 0) continue;
            left = Math.Min(left ?? r.X, r.X);
            top = Math.Min(top ?? r.Y, r.Y);
            right = Math.Max(right ?? r.Right, r.Right);
            bottom = Math.Max(bottom ?? r.Bottom, r.Bottom);
        }
        return left is int l && top is int t && right is int rr && bottom is int b ? new PixelRect(l, t, rr - l, b - t) : default;
    }

    /// <summary>COLORREF 0x00BBGGRR → colour.</summary>
    public static Color FromColorRef(uint value) =>
        Color.FromRgb((byte)(value & 0xFF), (byte)((value >> 8) & 0xFF), (byte)((value >> 16) & 0xFF));

    public static WallpaperFit ParseFit(string? value) =>
        Enum.TryParse<WallpaperFit>(value, ignoreCase: true, out var fit) && Enum.IsDefined(fit) ? fit : WallpaperFit.Fill;
}

/// <summary>Where wallpaper files live, and which pictures are TableNote's own.</summary>
public static class WallpaperFiles
{
    public const string RecordName = "wallpaper.json";
    public const string TablePrefix = "table-";
    public const string BackupPrefix = "original-";

    private static readonly string[] PictureExtensions =
        [".jpg", ".jpeg", ".jfif", ".png", ".bmp", ".dib", ".gif", ".tif", ".tiff", ".webp", ".heic", ".heif", ".avif", ".jxr", ".wdp"];

    /// <summary>
    /// Windows (default data folder): %LOCALAPPDATA%\TableNote\wallpaper, so the pictures don't roam with the
    /// profile. With TABLENOTE_DATA, or on other systems: a "wallpaper" folder next to note.json.
    /// </summary>
    public static string Folder(string notePath)
    {
        if (OperatingSystem.IsWindows() && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TABLENOTE_DATA")))
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrEmpty(local)) return Path.Combine(local, "TableNote", "wallpaper");
        }
        return Path.Combine(Path.GetDirectoryName(Path.GetFullPath(notePath)) ?? ".", "wallpaper");
    }

    public static bool SamePath(string? a, string? b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return string.IsNullOrEmpty(a) && string.IsNullOrEmpty(b);
        try
        {
            a = Path.GetFullPath(a);
            b = Path.GetFullPath(b);
        }
        catch { /* compare as given */ }
        return string.Equals(a, b, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private static bool InFolder(string? path, string folder, string prefix)
    {
        if (string.IsNullOrEmpty(path)) return false;
        try
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(path));
            var name = Path.GetFileName(path);
            return SamePath(dir, folder) && name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    /// <summary>A table picture TableNote drew (never a wallpaper to put back).</summary>
    public static bool IsTablePicture(string? path, string folder) => InFolder(path, folder, TablePrefix);

    /// <summary>TableNote's copy of a user's wallpaper (a real picture: fine to put back).</summary>
    public static bool IsBackup(string? path, string folder) => InFolder(path, folder, BackupPrefix);

    /// <summary>
    /// Windows' wallpaper cache (Themes\TranscodedWallpaper, Transcoded_000, CachedFiles). Windows overwrites it as
    /// soon as the wallpaper changes, so it is copied first and never used to restore.
    /// </summary>
    public static bool IsWindowsCache(string? path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        var p = path.Replace('/', '\\');
        return p.Contains(@"\Microsoft\Windows\Themes\", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Extension for a copy: the file's own picture extension, else one from its first bytes (".jpg" if unknown).</summary>
    public static string ExtensionFor(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (PictureExtensions.Contains(ext)) return ext;
        try
        {
            Span<byte> head = stackalloc byte[16];
            using var fs = File.OpenRead(path);
            var n = fs.Read(head);
            return Sniff(head[..n]) ?? ".jpg";
        }
        catch { return ".jpg"; }
    }

    public static string? Sniff(ReadOnlySpan<byte> h)
    {
        if (h.Length >= 3 && h[0] == 0xFF && h[1] == 0xD8 && h[2] == 0xFF) return ".jpg";
        if (h.Length >= 8 && h[0] == 0x89 && h[1] == 0x50 && h[2] == 0x4E && h[3] == 0x47) return ".png";
        if (h.Length >= 2 && h[0] == 0x42 && h[1] == 0x4D) return ".bmp";
        if (h.Length >= 4 && h[0] == 0x47 && h[1] == 0x49 && h[2] == 0x46 && h[3] == 0x38) return ".gif";
        if (h.Length >= 12 && h[0] == 0x52 && h[1] == 0x49 && h[2] == 0x46 && h[3] == 0x46 && h[8] == 0x57 && h[9] == 0x45 && h[10] == 0x42 && h[11] == 0x50) return ".webp";
        if (h.Length >= 4 && ((h[0] == 0x49 && h[1] == 0x49 && h[2] == 0x2A && h[3] == 0) || (h[0] == 0x4D && h[1] == 0x4D && h[2] == 0 && h[3] == 0x2A))) return ".tif";
        return null;
    }

    /// <summary>The picture Windows ships (used when TableNote finds its own picture and no record of the real one).</summary>
    public static string WindowsDefaultPicture() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Web", "Wallpaper", "Windows", "img0.jpg");
}

public static class WallpaperRecordStore
{
    private static readonly TableNoteJson Json = new(new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    });

    public static string PathIn(string folder) => Path.Combine(folder, WallpaperFiles.RecordName);

    /// <summary>The record, or null when there is none or it can't be read (logged).</summary>
    public static WallpaperRecord? Load(string folder)
    {
        var path = PathIn(folder);
        try
        {
            if (!File.Exists(path)) return null;
            var record = JsonSerializer.Deserialize(File.ReadAllText(path), Json.WallpaperRecord);
            return record is { Version: >= 1 } ? record : null;
        }
        catch (Exception ex)
        {
            AppLog.Write("wallpaper", "wallpaper.json unreadable: " + ex.Message);
            return null;
        }
    }

    public static void Save(string folder, WallpaperRecord record)
    {
        Directory.CreateDirectory(folder);
        var path = PathIn(folder);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(record, Json.WallpaperRecord));
        File.Move(tmp, path, overwrite: true);
    }

    public static void Delete(string folder)
    {
        try { File.Delete(PathIn(folder)); }
        catch (Exception ex) { AppLog.Write("wallpaper", "wallpaper.json not deleted: " + ex.Message); }
    }
}
