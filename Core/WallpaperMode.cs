using System.Security.Cryptography;
using Avalonia;

namespace TableNote;

/// <summary>What the render step gets: the picture's area, and what Windows shows under the table card.</summary>
/// <param name="Area">The picture in screen pixels: one monitor, or the box around every monitor (span).</param>
/// <param name="Picture">File to draw under the card; null = only the background colour.</param>
/// <param name="Screen">Monitor the window is on: scale factor and work area for the card.</param>
/// <param name="Window">The editor window's rectangle in screen pixels (where the card goes).</param>
public sealed record WallpaperScene(PixelRect Area, WallpaperFit Fit, string? Picture, uint BackgroundColor, WallpaperScreen Screen, PixelRect Window);

/// <param name="Message">Text for the user (already in the UI language), or null.</param>
/// <param name="Detail">English detail for tablenote.log.</param>
/// <param name="NeedsConfirm">Windows spotlight is on: ask before turning it off (nothing changed yet).</param>
/// <param name="Changed">The desktop picture changed.</param>
public sealed record WallpaperOutcome(bool Ok, string? Message = null, string? Detail = null, bool NeedsConfirm = false, bool Changed = false)
{
    public static readonly WallpaperOutcome Done = new(true);
}

/// <summary>
/// Table-on-the-wallpaper mode. Draws the table over the user's own wallpaper on the monitor the window is on, sets
/// it as that monitor's wallpaper, redraws it when the table changes, and puts the user's wallpaper back.
/// Everything needed to put it back is written to wallpaper.json BEFORE the wallpaper changes, so a crash, a
/// sign-out or a failed restore is put right by the next start. Windows is reached through
/// <see cref="IWallpaperHost"/> and drawing through a callback, so tests run all of this with fakes.
/// </summary>
public sealed class WallpaperMode
{
    private readonly IWallpaperHost _host;
    private string? _appliedHash;

    public WallpaperMode(IWallpaperHost host, string folder)
    {
        _host = host;
        Folder = folder;
        Record = WallpaperRecordStore.Load(folder);
    }

    public string Folder { get; }
    public WallpaperRecord? Record { get; private set; }
    public bool Supported => _host.Supported;

    /// <summary>A table picture may be on the desktop: there is a wallpaper to put back.</summary>
    public bool Pending => Record != null;

    private sealed record Target(string Id, PixelRect Area, string Picture, WallpaperScreen Screen);

    /// <summary>
    /// Shows the table on the wallpaper, or redraws it. The first call keeps the current wallpaper (record + copy);
    /// later calls keep that one, unless the user picked a new wallpaper meanwhile, which then becomes the one to
    /// put back. A picture identical to the one already up is not set again.
    /// </summary>
    public WallpaperOutcome Apply(PixelRect window, IReadOnlyList<WallpaperScreen> screens, Func<WallpaperScene, byte[]?> render, bool confirmed = false)
    {
        if (!_host.Supported) return Fail(Localizer.T("wallpaper.unsupported"), "not supported on this system");
        var state = _host.Read(out var detail);
        if (state == null) return Fail(Localizer.T("wallpaper.read_failed", detail ?? ""), detail);
        if (state.PolicyLocked) return Fail(Localizer.T("wallpaper.policy"), "wallpaper is fixed by a policy");
        var target = Resolve(state, window, screens);
        if (target == null) return Fail(Localizer.T("wallpaper.no_monitor"), "no monitor");

        if (Record is { } old && old.Monitor != target.Id)
        {
            // The window is on another monitor now (displays changed): put the old one back, start again here.
            if (!RestoreCore(old, out var d)) AppLog.Write("wallpaper", "previous monitor not put back: " + d);
            DropRecord();
        }
        else if (Record is { Applied: not null } kept && !WallpaperFiles.SamePath(target.Picture, kept.Applied)
                 && !WallpaperFiles.IsTablePicture(target.Picture, Folder))
        {
            AppLog.Write("wallpaper", "the wallpaper was changed outside TableNote; that one is now the one to put back");
            DropRecord();
        }

        if (Record == null)
        {
            if (state.Spotlight && !confirmed)
                return new WallpaperOutcome(false, Detail: "Windows spotlight is on", NeedsConfirm: true);
            if (Capture(state, target, out var error) == null)
                return Fail(Localizer.T("wallpaper.backup_failed", error ?? ""), error);
        }
        var record = Record!;

        byte[]? bytes = null;
        try
        {
            bytes = render(new WallpaperScene(target.Area, state.Fit, DrawSource(record), state.BackgroundColor, target.Screen, window));
        }
        catch (Exception ex) { detail = ex.GetType().Name + ": " + ex.Message; }
        if (bytes == null || bytes.Length == 0)
        {
            if (record.Applied == null) DropRecord(); // nothing changed yet
            return Fail(Localizer.T("wallpaper.render_failed", detail ?? "empty picture"), detail ?? "empty picture");
        }

        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        if (hash == _appliedHash && WallpaperFiles.SamePath(target.Picture, record.Applied))
            return WallpaperOutcome.Done;

        string file;
        try { file = WriteTable(bytes, record.Applied); }
        catch (Exception ex)
        {
            if (record.Applied == null) DropRecord();
            return Fail(Localizer.T("wallpaper.render_failed", ex.Message), ex.Message);
        }
        if (!_host.SetPicture(target.Id.Length == 0 ? null : target.Id, file, out detail))
        {
            if (record.Applied == null) DropRecord();
            return Fail(Localizer.T("wallpaper.set_failed", detail ?? ""), detail);
        }
        record.Applied = file;
        _appliedHash = hash;
        try { WallpaperRecordStore.Save(Folder, record); }
        catch (Exception ex) { AppLog.Write("wallpaper", "wallpaper.json not updated: " + ex.Message); }
        AppLog.Write("wallpaper", $"table picture set ({target.Area.Width}×{target.Area.Height}, {state.Fit}, monitor {(target.Id.Length == 0 ? "all" : "1 of " + state.Monitors.Count)})");
        return new WallpaperOutcome(true, Changed: true);
    }

    /// <summary>
    /// Puts the user's wallpaper back and forgets the record. If the desktop no longer shows a TableNote picture
    /// (never set, or the user chose another wallpaper), nothing is changed. On failure the record stays, so the
    /// next start tries again.
    /// </summary>
    public WallpaperOutcome Restore()
    {
        var record = Record;
        if (record == null) return WallpaperOutcome.Done;
        if (!_host.Supported)
        {
            DropRecord();
            return WallpaperOutcome.Done;
        }
        var state = _host.Read(out _);
        if (state != null && record.Monitor.Length > 0 && state.PerMonitor && state.Monitors.All(m => m.Id != record.Monitor))
        {
            // That monitor is not connected: Windows keeps its picture until it is. Keep the record for later.
            AppLog.Write("wallpaper", "the monitor with the table picture is not connected; it is put back when it is");
            return WallpaperOutcome.Done;
        }
        var current = state == null ? null : PictureOn(state, record.Monitor);
        if (current != null && !WallpaperFiles.IsTablePicture(current, Folder))
        {
            var replaced = record.Applied != null;
            DropRecord();
            Cleanup(state, null);
            if (replaced) AppLog.Write("wallpaper", "no table picture on the desktop any more (wallpaper changed outside TableNote): kept");
            return new WallpaperOutcome(true, replaced ? Localizer.T("wallpaper.kept_user") : null);
        }
        if (!RestoreCore(record, out var detail))
            return Fail(Localizer.T("wallpaper.restore_failed", detail ?? ""), detail);
        AppLog.Write("wallpaper", "previous wallpaper put back");
        DropRecord();
        Cleanup(_host.Read(out _), null);
        return new WallpaperOutcome(true, record.Spotlight ? Localizer.T("wallpaper.spotlight_off") : null, Changed: true);
    }

    private static Target? Resolve(WallpaperState state, PixelRect window, IReadOnlyList<WallpaperScreen> screens)
    {
        WallpaperScreen ScreenFor(PixelRect area)
        {
            foreach (var s in screens)
                if (s.Bounds == area) return s;
            var i = WallpaperGeometry.Pick(screens.Select(s => s.Bounds).ToList(), area);
            return i >= 0 ? screens[i] : new WallpaperScreen(area, area, 1);
        }

        var monitors = state.Monitors.Where(m => m.Bounds.Width > 0 && m.Bounds.Height > 0).ToList();
        if (!state.PerMonitor || monitors.Count == 0)
        {
            // One picture for every monitor: draw it for the window's monitor.
            if (screens.Count == 0) return null;
            var i = WallpaperGeometry.Pick(screens.Select(s => s.Bounds).ToList(), window);
            return new Target("", screens[i].Bounds, state.Monitors.FirstOrDefault()?.Picture ?? "", screens[i]);
        }
        var k = WallpaperGeometry.Pick(monitors.Select(m => m.Bounds).ToList(), window);
        var monitor = monitors[k];
        if (state.Fit == WallpaperFit.Span)
            return new Target("", WallpaperGeometry.Union(monitors.Select(m => m.Bounds)), monitor.Picture, ScreenFor(monitor.Bounds));
        return new Target(monitor.Id, monitor.Bounds, monitor.Picture, ScreenFor(monitor.Bounds));
    }

    private static string? PictureOn(WallpaperState state, string monitorId) =>
        monitorId.Length == 0
            ? state.Monitors.FirstOrDefault()?.Picture
            : state.Monitors.FirstOrDefault(m => m.Id == monitorId)?.Picture;

    /// <summary>Keeps the current wallpaper: a copy of the picture, then wallpaper.json. Null = don't go on.</summary>
    private WallpaperRecord? Capture(WallpaperState state, Target target, out string? error)
    {
        error = null;
        var current = target.Picture ?? "";
        var original = current;
        string? backup = null;
        if (WallpaperFiles.IsTablePicture(current, Folder))
        {
            // A TableNote picture with no record of the real wallpaper (wallpaper.json lost): Windows' own picture.
            var fallback = WallpaperFiles.WindowsDefaultPicture();
            original = File.Exists(fallback) ? fallback : "";
            AppLog.Write("wallpaper", "found a table picture but no record of the previous wallpaper; Windows' default picture is put back later");
        }
        else if (WallpaperFiles.IsBackup(current, Folder))
        {
            backup = current; // already TableNote's own copy
        }
        else if (current.Length > 0)
        {
            var source = File.Exists(current) ? current : WindowsCachedPicture();
            if (source != null)
            {
                try { backup = CopyBackup(source); }
                catch (Exception ex)
                {
                    // A picture that only exists in Windows' cache is lost as soon as the wallpaper changes.
                    if (WallpaperFiles.IsWindowsCache(current) || !File.Exists(current))
                    {
                        error = ex.Message;
                        return null;
                    }
                    AppLog.Write("wallpaper", "no copy of the wallpaper (" + ex.Message + "); the file itself is put back");
                }
            }
        }
        var record = new WallpaperRecord
        {
            Monitor = target.Id,
            Original = original,
            Backup = backup,
            Fit = state.Fit.ToString(),
            Slideshow = state.Slideshow,
            SlideshowItems = [.. state.SlideshowItems],
            SlideshowOptions = state.SlideshowOptions,
            SlideshowTick = state.SlideshowTick,
            Spotlight = state.Spotlight
        };
        try { WallpaperRecordStore.Save(Folder, record); }
        catch (Exception ex)
        {
            error = WallpaperFiles.RecordName + ": " + ex.Message;
            return null;
        }
        Record = record;
        _appliedHash = null;
        Cleanup(state, backup);
        return record;
    }

    /// <summary>Windows' cached copy of the picture on screen, for a wallpaper whose file was deleted.</summary>
    private static string? WindowsCachedPicture()
    {
        if (!OperatingSystem.IsWindows()) return null;
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft", "Windows", "Themes", "TranscodedWallpaper");
        return File.Exists(path) ? path : null;
    }

    private string CopyBackup(string source)
    {
        Directory.CreateDirectory(Folder);
        var name = WallpaperFiles.BackupPrefix + DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture)
                   + "-" + Guid.NewGuid().ToString("N")[..4] + WallpaperFiles.ExtensionFor(source);
        var path = Path.Combine(Folder, name);
        var tmp = path + ".tmp";
        File.Copy(source, tmp, overwrite: true);
        File.Move(tmp, path, overwrite: true);
        return path;
    }

    /// <summary>The picture drawn under the card: TableNote's copy if there is one, else the file itself.</summary>
    private string? DrawSource(WallpaperRecord record)
    {
        if (!string.IsNullOrEmpty(record.Backup) && File.Exists(record.Backup)) return record.Backup;
        if (!string.IsNullOrEmpty(record.Original) && File.Exists(record.Original) && !WallpaperFiles.IsTablePicture(record.Original, Folder))
            return record.Original;
        return null;
    }

    /// <summary>The user's own file when it is still there (Settings then shows its name), else TableNote's copy.</summary>
    private string? RestorePath(WallpaperRecord record)
    {
        bool Usable(string? p) => !string.IsNullOrEmpty(p) && File.Exists(p) && !WallpaperFiles.IsTablePicture(p, Folder);
        if (Usable(record.Original) && !WallpaperFiles.IsWindowsCache(record.Original)) return record.Original;
        if (Usable(record.Backup)) return record.Backup;
        return null;
    }

    private bool RestoreCore(WallpaperRecord record, out string? detail)
    {
        detail = null;
        if (record.Slideshow && record.SlideshowItems.Count > 0)
        {
            if (_host.SetSlideshow(record.SlideshowItems, record.SlideshowOptions, record.SlideshowTick, out detail)) return true;
            AppLog.Write("wallpaper", "slideshow not restarted (" + detail + "); putting the picture back");
        }
        var path = RestorePath(record);
        if (path != null) return _host.SetPicture(record.Monitor.Length == 0 ? null : record.Monitor, path, out detail);
        if (record.Original.Length > 0)
            AppLog.Write("wallpaper", "the previous picture and its copy are both gone; no picture is put back");
        return _host.ClearPicture(out detail);
    }

    private string WriteTable(byte[] bytes, string? previous)
    {
        Directory.CreateDirectory(Folder);
        // Two names in turn: setting the path that is already up may not make Windows read the file again.
        var first = Path.Combine(Folder, WallpaperFiles.TablePrefix + "1.png");
        var path = WallpaperFiles.SamePath(previous, first) ? Path.Combine(Folder, WallpaperFiles.TablePrefix + "2.png") : first;
        var tmp = path + ".tmp";
        File.WriteAllBytes(tmp, bytes);
        File.Move(tmp, path, overwrite: true);
        return path;
    }

    private void DropRecord()
    {
        Record = null;
        _appliedHash = null;
        WallpaperRecordStore.Delete(Folder);
    }

    /// <summary>Deletes TableNote's pictures and copies that no monitor shows and the record doesn't need.</summary>
    private void Cleanup(WallpaperState? state, string? keep)
    {
        try
        {
            if (!Directory.Exists(Folder)) return;
            var shown = state?.Monitors.Select(m => m.Picture).Where(p => p.Length > 0).ToList() ?? [];
            foreach (var file in Directory.GetFiles(Folder))
            {
                var name = Path.GetFileName(file);
                if (!name.StartsWith(WallpaperFiles.TablePrefix, StringComparison.OrdinalIgnoreCase) &&
                    !name.StartsWith(WallpaperFiles.BackupPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                if (WallpaperFiles.SamePath(file, keep) || shown.Any(p => WallpaperFiles.SamePath(p, file))) continue;
                if (Record != null && (WallpaperFiles.SamePath(file, Record.Backup) || WallpaperFiles.SamePath(file, Record.Original) ||
                                       WallpaperFiles.SamePath(file, Record.Applied))) continue;
                // While the record exists, its slideshow pictures stay too (they may be TableNote copies).
                try { File.Delete(file); }
                catch (Exception ex) { AppLog.Write("wallpaper", "old file not deleted: " + name + " (" + ex.Message + ")"); }
            }
        }
        catch (Exception ex) { AppLog.Write("wallpaper", "cleanup failed: " + ex.Message); }
    }

    private static WallpaperOutcome Fail(string message, string? detail)
    {
        AppLog.Write("wallpaper", "failed: " + (detail ?? message));
        return new WallpaperOutcome(false, message, detail);
    }
}
