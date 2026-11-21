// 2.6.0 wallpaper mode. Run: dotnet run -c Release --project tests/WallpaperTests (no display needed).
// The Windows side is a fake desktop (FakeDesktop) and a fake IDesktopWallpaper vtable (FakeCom), so everything but
// the real Windows calls runs here: what is kept, what is set on which monitor, what is put back, the picture itself,
// and the editor window going to the tray and back.
using System.Reflection;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using SkiaSharp;
using TableNote;

var root = Directory.CreateTempSubdirectory("tn-wallpaper-").FullName;
Environment.SetEnvironmentVariable("TABLENOTE_DATA", Path.Combine(root, "note.json"));
AppLog.PathOverride = Path.Combine(root, "tablenote.log");
AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
Localizer.SetLanguage("en");

int pass = 0, fail = 0;
void Ok(bool ok, string name, string detail = "")
{
    Console.WriteLine((ok ? "PASS " : "FAIL ") + name + (ok || detail.Length == 0 ? "" : " — " + detail));
    if (ok) pass++; else fail++;
}

try { Geometry(Ok); } catch (Exception ex) { Ok(false, "geometry harness", ex.ToString()); }
try { Files(Ok, root); } catch (Exception ex) { Ok(false, "files harness", ex.ToString()); }
try { Mode(Ok, root); } catch (Exception ex) { Ok(false, "mode harness", ex.ToString()); }
try { Com(Ok); } catch (Exception ex) { Ok(false, "COM harness", ex.ToString()); }
try { Picture(Ok, root); } catch (Exception ex) { Ok(false, "picture harness", ex.ToString()); }
try { WindowFlow(Ok, root); } catch (Exception ex) { Ok(false, "window harness", ex.ToString()); }

Console.WriteLine($"\nWallpaperTests: {pass} passed, {fail} failed");
return fail == 0 ? 0 : 1;

static string Join(IEnumerable<string> items) => string.Join(" | ", items);

static void Geometry(Check ok)
{
    var fill = WallpaperGeometry.Place(WallpaperFit.Fill, 1000, 500, 800, 600);
    ok(fill == new Rect(-200, 0, 1200, 600), "fill: covers the screen, cuts the sides", fill.ToString());
    var fit = WallpaperGeometry.Place(WallpaperFit.Fit, 1000, 500, 800, 600);
    ok(fit == new Rect(0, 100, 800, 400), "fit: whole picture, bands above and below", fit.ToString());
    ok(WallpaperGeometry.Place(WallpaperFit.Stretch, 1000, 500, 800, 600) == new Rect(0, 0, 800, 600), "stretch: the screen");
    ok(WallpaperGeometry.Place(WallpaperFit.Center, 200, 100, 800, 600) == new Rect(300, 250, 200, 100), "center: actual size in the middle");
    ok(WallpaperGeometry.Place(WallpaperFit.Tile, 64, 32, 800, 600) == new Rect(0, 0, 64, 32), "tile: first tile at the top left");
    var span = WallpaperGeometry.Place(WallpaperFit.Span, 1000, 500, 3840, 1080);
    ok(span == new Rect(0, -420, 3840, 1920), "span: fills the box around the monitors", span.ToString());

    ok(WallpaperGeometry.DecodeWidth(WallpaperFit.Fill, 6000, 4000, 1920, 1080) == 1920, "a 6000 px photo is decoded at the 1920 px it is drawn");
    ok(WallpaperGeometry.DecodeWidth(WallpaperFit.Fit, 6000, 4000, 1920, 1080) == 1620, "fit decodes at its drawn width");
    ok(WallpaperGeometry.DecodeWidth(WallpaperFit.Fill, 800, 600, 1920, 1080) == 800, "a small picture is never decoded larger than it is");
    ok(WallpaperGeometry.DecodeWidth(WallpaperFit.Center, 6000, 4000, 1920, 1080) == 6000, "center keeps the actual size");

    var work = new PixelRect(0, 0, 1920, 1040);
    ok(WallpaperGeometry.CardBox(new PixelRect(100, 100, 800, 500), work) == new PixelRect(100, 100, 800, 500), "card box = the window");
    ok(WallpaperGeometry.CardBox(new PixelRect(1800, 900, 800, 500), work) == new PixelRect(1760, 880, 160, 160),
        "a window hanging off the corner keeps a card on screen, above the taskbar",
        WallpaperGeometry.CardBox(new PixelRect(1800, 900, 800, 500), work).ToString());
    ok(WallpaperGeometry.CardBox(new PixelRect(-500, -50, 800, 500), work) == new PixelRect(0, 0, 800, 500), "a window above / left of the screen is moved in");
    var second = new PixelRect(1920, 0, 2560, 1400);
    ok(WallpaperGeometry.CardBox(new PixelRect(2000, 200, 700, 400), second) == new PixelRect(2000, 200, 700, 400), "second monitor coordinates");

    var monitors = new List<PixelRect> { new(0, 0, 1920, 1080), new(1920, 0, 2560, 1440) };
    ok(WallpaperGeometry.Pick(monitors, new PixelRect(2500, 300, 600, 400)) == 1, "the monitor the window is on");
    ok(WallpaperGeometry.Pick(monitors, new PixelRect(1700, 100, 600, 400)) == 1, "a window across two monitors: the one with more of it");
    ok(WallpaperGeometry.Pick(monitors, new PixelRect(-3000, 0, 100, 100)) == 0, "a window on no monitor: the nearest");
    ok(WallpaperGeometry.Pick([], new PixelRect(0, 0, 1, 1)) == -1, "no monitors");
    ok(WallpaperGeometry.Union(monitors) == new PixelRect(0, 0, 4480, 1440), "span box");
    ok(WallpaperGeometry.Union([new PixelRect(-1280, 0, 1280, 1024), new PixelRect(0, 0, 1920, 1080)]) == new PixelRect(-1280, 0, 3200, 1080), "span box with a monitor on the left");
    var c = WallpaperGeometry.FromColorRef(0x00332211);
    ok(c.R == 0x11 && c.G == 0x22 && c.B == 0x33, "COLORREF is 0x00BBGGRR");
    ok(WallpaperGeometry.ParseFit("Span") == WallpaperFit.Span && WallpaperGeometry.ParseFit("bogus") == WallpaperFit.Fill
        && WallpaperGeometry.ParseFit("7") == WallpaperFit.Fill, "fit names from wallpaper.json");
}

static void Files(Check ok, string root)
{
    var note = Path.Combine(root, "note.json");
    var folder = WallpaperFiles.Folder(note);
    ok(folder == Path.Combine(root, "wallpaper"), "with TABLENOTE_DATA the pictures live next to note.json", folder);
    ok(WallpaperFiles.IsTablePicture(Path.Combine(folder, "table-1.png"), folder)
       && !WallpaperFiles.IsTablePicture(Path.Combine(folder, "original-1.jpg"), folder)
       && !WallpaperFiles.IsTablePicture(Path.Combine(root, "table-1.png"), folder), "table pictures are TableNote's own, in its folder");
    ok(WallpaperFiles.IsBackup(Path.Combine(folder, "original-20261120.jpg"), folder), "copies are original-*");
    ok(WallpaperFiles.IsWindowsCache(@"C:\Users\a\AppData\Roaming\Microsoft\Windows\Themes\TranscodedWallpaper")
       && WallpaperFiles.IsWindowsCache("/x/Microsoft/Windows/Themes/CachedFiles/CachedImage_1920_1080_POS4.jpg")
       && !WallpaperFiles.IsWindowsCache(@"C:\Users\a\Pictures\beach.jpg"), "Windows' own wallpaper cache is recognised");
    ok(WallpaperFiles.Sniff([0xFF, 0xD8, 0xFF, 0xE0]) == ".jpg" && WallpaperFiles.Sniff([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]) == ".png"
       && WallpaperFiles.Sniff("BM\0\0"u8) == ".bmp" && WallpaperFiles.Sniff("GIF89a"u8) == ".gif"
       && WallpaperFiles.Sniff("RIFF\0\0\0\0WEBPVP8 "u8) == ".webp" && WallpaperFiles.Sniff([0x49, 0x49, 0x2A, 0]) == ".tif"
       && WallpaperFiles.Sniff("hello"u8) == null, "picture type from the first bytes");
    Directory.CreateDirectory(Path.Combine(root, "sniff"));
    var transcoded = Path.Combine(root, "sniff", "TranscodedWallpaper");
    File.WriteAllBytes(transcoded, [0xFF, 0xD8, 0xFF, 0xE1, 1, 2, 3]);
    ok(WallpaperFiles.ExtensionFor(transcoded) == ".jpg", "a copy of TranscodedWallpaper gets .jpg so Windows can open it");
    File.WriteAllBytes(transcoded, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
    ok(WallpaperFiles.ExtensionFor(transcoded) == ".png", "…or .png");
    ok(WallpaperFiles.ExtensionFor("/x/Photo.JPEG") == ".jpeg", "a picture extension is kept");
    ok(WallpaperFiles.SamePath("/a/b/../c.png", "/a/c.png") && WallpaperFiles.SamePath(null, "") && !WallpaperFiles.SamePath("x", null), "path comparison");

    var store = Path.Combine(root, "store");
    var record = new WallpaperRecord
    {
        Monitor = @"\\?\DISPLAY#GSM5B09#5&1", Original = @"C:\pics\beach.jpg", Backup = Path.Combine(store, "original-1.jpg"),
        Fit = "Span", Slideshow = true, SlideshowItems = [@"C:\Slides"], SlideshowOptions = 1, SlideshowTick = 600000,
        Spotlight = true, Applied = Path.Combine(store, "table-2.png")
    };
    WallpaperRecordStore.Save(store, record);
    var json = File.ReadAllText(Path.Combine(store, "wallpaper.json"));
    var back = WallpaperRecordStore.Load(store);
    ok(back != null && back.Monitor == record.Monitor && back.Original == record.Original && back.Backup == record.Backup && back.Fit == "Span"
       && back.Slideshow && back.SlideshowItems.SequenceEqual(record.SlideshowItems) && back.SlideshowOptions == 1 && back.SlideshowTick == 600000
       && back.Spotlight && back.Applied == record.Applied && back.Version == 1,
        "wallpaper.json round trip (source-generated JSON, reflection off)", json);
    ok(json.Contains("\"monitor\"") && json.Contains("\"original\"") && json.Contains("\"slideshowItems\"") && json.Contains("\"applied\""), "camelCase names", json);
    File.WriteAllText(Path.Combine(store, "wallpaper.json"), "{ not json");
    ok(WallpaperRecordStore.Load(store) == null, "a broken wallpaper.json reads as no record");
    WallpaperRecordStore.Delete(store);
    ok(!File.Exists(Path.Combine(store, "wallpaper.json")), "delete");
}

static void Mode(Check ok, string root)
{
    var pics = Path.Combine(root, "pictures");
    Directory.CreateDirectory(pics);
    var beach = Pictures.Halves(pics, "beach.jpg", 64, 32);
    var city = Pictures.Halves(pics, "city.png", 64, 32);
    var n = 0;
    string NewFolder() => Path.Combine(root, "mode-" + ++n);
    var screens = new List<WallpaperScreen>
    {
        new(new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1040), 1.25),
        new(new PixelRect(1920, 0, 2560, 1440), new PixelRect(1920, 0, 2560, 1400), 1.5)
    };
    List<WallpaperMonitor> Two() => [new("MON-A", screens[0].Bounds, beach), new("MON-B", screens[1].Bounds, city)];
    var onA = new PixelRect(100, 100, 900, 600);
    var onB = new PixelRect(2200, 300, 1000, 700);
    WallpaperScene? seen = null;
    var version = 1;
    byte[] Render(WallpaperScene s) { seen = s; return Encoding.UTF8.GetBytes("table v" + version); }

    // 1. Enter: only the window's monitor, the record is written first, the user's picture is kept.
    var folder = NewFolder();
    var desk = new FakeDesktop { Monitors = Two() };
    var recordAtSet = false;
    desk.BeforeSet = () => recordAtSet = File.Exists(Path.Combine(folder, "wallpaper.json"));
    var mode = new WallpaperMode(desk, folder);
    var r = mode.Apply(onB, screens, Render);
    ok(r.Ok && r.Changed, "enter: the table picture is set", r.Message ?? "");
    ok(desk.Calls.SequenceEqual(["set MON-B table-1.png"]), "only the window's monitor gets the table picture", Join(desk.Calls));
    ok(desk.Monitors[0].Picture == beach, "the other monitor keeps its wallpaper");
    ok(recordAtSet, "wallpaper.json is written before the wallpaper changes");
    ok(seen is { } s1 && s1.Area == screens[1].Bounds && s1.Screen == screens[1] && s1.Fit == WallpaperFit.Fill && s1.Window == onB
       && s1.BackgroundColor == desk.Color, "the scene: that monitor, its scale, the fit, the colour, the window");
    ok(seen?.Picture is { } drawn && WallpaperFiles.IsBackup(drawn, folder) && File.ReadAllBytes(drawn).SequenceEqual(File.ReadAllBytes(city)),
        "the picture under the card is TableNote's copy of the user's wallpaper");
    var rec = WallpaperRecordStore.Load(folder)!;
    ok(rec.Monitor == "MON-B" && rec.Original == city && rec.Applied!.EndsWith("table-1.png") && rec.Fit == "Fill",
        "the record names the monitor, the user's picture and the table picture");

    // 2. Redraw.
    desk.Calls.Clear();
    r = mode.Apply(onB, screens, Render);
    ok(r.Ok && !r.Changed && desk.Calls.Count == 0, "the same picture is not set again");
    version = 2;
    r = mode.Apply(onB, screens, Render);
    ok(r.Ok && r.Changed && desk.Calls.SequenceEqual(["set MON-B table-2.png"]), "a changed table goes up under the other name", Join(desk.Calls));
    version = 3;
    desk.Calls.Clear();
    mode.Apply(onB, screens, Render);
    ok(desk.Calls.SequenceEqual(["set MON-B table-1.png"]), "the two names alternate", Join(desk.Calls));

    // 3. A new start (crash, sign-out) resumes from wallpaper.json: the table picture is never taken for the user's.
    var again = new WallpaperMode(desk, folder);
    ok(again.Pending, "a new start finds the record");
    version = 4;
    r = again.Apply(onB, screens, Render);
    ok(r.Ok && WallpaperRecordStore.Load(folder)!.Original == city, "resuming keeps the user's picture as the one to put back");

    // 4. Leave.
    desk.Calls.Clear();
    r = again.Restore();
    ok(r.Ok && desk.Calls.SequenceEqual(["set MON-B city.png"]), "leaving puts the user's own file back on that monitor", Join(desk.Calls));
    ok(!again.Pending && !File.Exists(Path.Combine(folder, "wallpaper.json")), "the record is gone after the restore");
    ok(Directory.GetFiles(folder, "table-*").Length == 0 && Directory.GetFiles(folder, "original-*").Length == 0,
        "table pictures and the copy are deleted when the user's own file went back", Join(Directory.GetFiles(folder).Select(Path.GetFileName)!));

    // 5. A wallpaper that only exists in Windows' cache comes back from TableNote's copy.
    var themes = Path.Combine(root, "AppData", "Roaming", "Microsoft", "Windows", "Themes");
    Directory.CreateDirectory(themes);
    var transcoded = Path.Combine(themes, "TranscodedWallpaper");
    File.Copy(beach, transcoded, overwrite: true);
    folder = NewFolder();
    desk = new FakeDesktop { Monitors = [new("MON-A", screens[0].Bounds, transcoded)] };
    mode = new WallpaperMode(desk, folder);
    mode.Apply(onA, screens, Render);
    File.WriteAllText(transcoded, "Windows rewrote its cache with the table"); // what Windows does
    desk.Calls.Clear();
    r = mode.Restore();
    var putBack = desk.Monitors[0].Picture;
    ok(r.Ok && desk.Calls.Count == 1 && WallpaperFiles.IsBackup(putBack, folder) && putBack.EndsWith(".jpg")
       && File.ReadAllBytes(putBack).SequenceEqual(File.ReadAllBytes(beach)), "a wallpaper only in Windows' cache comes back from the copy (.jpg)", Join(desk.Calls));
    ok(File.Exists(putBack), "that copy stays: Windows points at it now");

    // 6. The user's file was deleted while the table was up.
    var gone = Pictures.Halves(pics, "gone.jpg", 64, 32);
    folder = NewFolder();
    desk = new FakeDesktop { Monitors = [new("MON-A", screens[0].Bounds, gone)] };
    mode = new WallpaperMode(desk, folder);
    mode.Apply(onA, screens, Render);
    var goneBytes = File.ReadAllBytes(gone);
    File.Delete(gone);
    desk.Calls.Clear();
    mode.Restore();
    ok(desk.Calls.Count == 1 && WallpaperFiles.IsBackup(desk.Monitors[0].Picture, folder)
       && File.ReadAllBytes(desk.Monitors[0].Picture).SequenceEqual(goneBytes), "a deleted wallpaper file comes back from the copy", Join(desk.Calls));

    // 7. The user picked another wallpaper while the table was up: theirs stays.
    folder = NewFolder();
    desk = new FakeDesktop { Monitors = [new("MON-A", screens[0].Bounds, beach)] };
    mode = new WallpaperMode(desk, folder);
    mode.Apply(onA, screens, Render);
    desk.Monitors[0] = desk.Monitors[0] with { Picture = city };
    desk.Calls.Clear();
    r = mode.Restore();
    ok(r.Ok && desk.Calls.Count == 0 && r.Message == Localizer.T("wallpaper.kept_user") && !mode.Pending, "a wallpaper the user picked meanwhile is kept", Join(desk.Calls));

    // 8. …and if the table is redrawn after that, the new wallpaper is the one to put back.
    folder = NewFolder();
    desk = new FakeDesktop { Monitors = [new("MON-A", screens[0].Bounds, beach)] };
    mode = new WallpaperMode(desk, folder);
    mode.Apply(onA, screens, Render);
    desk.Monitors[0] = desk.Monitors[0] with { Picture = city };
    version++;
    r = mode.Apply(onA, screens, Render);
    ok(r.Ok && WallpaperRecordStore.Load(folder)!.Original == city && WallpaperFiles.IsTablePicture(desk.Monitors[0].Picture, folder),
        "a redraw over a newly picked wallpaper keeps that one to put back");
    desk.Calls.Clear();
    mode.Restore();
    ok(desk.Calls.SequenceEqual(["set MON-A city.png"]), "…and puts it back", Join(desk.Calls));

    // 9. Crash between the record and the picture: the desktop never changed, nothing to do.
    folder = NewFolder();
    WallpaperRecordStore.Save(folder, new WallpaperRecord { Monitor = "MON-A", Original = beach });
    desk = new FakeDesktop { Monitors = [new("MON-A", screens[0].Bounds, beach)] };
    mode = new WallpaperMode(desk, folder);
    r = mode.Restore();
    ok(r.Ok && r.Message == null && desk.Calls.Count == 0 && !mode.Pending, "a record whose picture never went up is just dropped");

    // 9b. Crash after the picture but before the record noted it: the table is up, the record has no Applied.
    folder = NewFolder();
    desk = new FakeDesktop { Monitors = [new("MON-A", screens[0].Bounds, beach)] };
    mode = new WallpaperMode(desk, folder);
    mode.Apply(onA, screens, Render);
    var raw = WallpaperRecordStore.Load(folder)!;
    raw.Applied = null;
    WallpaperRecordStore.Save(folder, raw);
    desk.Calls.Clear();
    r = new WallpaperMode(desk, folder).Restore();
    ok(r.Ok && desk.Calls.SequenceEqual(["set MON-A beach.jpg"]), "a table picture the record didn't note yet is still put back", Join(desk.Calls));

    // 10. Windows spotlight: ask first.
    folder = NewFolder();
    desk = new FakeDesktop { Spotlight = true, Monitors = [new("MON-A", screens[0].Bounds, beach)] };
    mode = new WallpaperMode(desk, folder);
    r = mode.Apply(onA, screens, Render);
    ok(!r.Ok && r.NeedsConfirm && desk.Calls.Count == 0 && !mode.Pending && !Directory.Exists(folder),
        "Windows spotlight: asks first, changes and writes nothing");
    r = mode.Apply(onA, screens, Render, confirmed: true);
    ok(r.Ok && desk.Calls.Count == 1 && WallpaperRecordStore.Load(folder)!.Spotlight, "after a yes the table goes up");
    r = mode.Restore();
    ok(r.Ok && r.Message == Localizer.T("wallpaper.spotlight_off"), "leaving says spotlight has to be turned on again");

    // 11. Slideshow: it is started again with its folder and settings.
    folder = NewFolder();
    desk = new FakeDesktop { Slideshow = true, Items = [pics], Options = 1, Tick = 600000, Monitors = [new("MON-A", screens[0].Bounds, beach)] };
    mode = new WallpaperMode(desk, folder);
    mode.Apply(onA, screens, Render);
    ok(!desk.Slideshow, "(the fake stops the slideshow like Windows)");
    desk.Calls.Clear();
    mode.Restore();
    ok(desk.Calls.SequenceEqual(["slideshow pictures 1 600000"]) && desk.Slideshow, "leaving restarts the slideshow", Join(desk.Calls));

    // 12. Solid colour.
    folder = NewFolder();
    desk = new FakeDesktop { Monitors = [new("MON-A", screens[0].Bounds, "")] };
    mode = new WallpaperMode(desk, folder);
    mode.Apply(onA, screens, Render);
    ok(seen?.Picture == null, "solid colour: only the colour under the card");
    mode.Restore();
    ok(desk.Calls.SequenceEqual(["set MON-A table-1.png", "clear"]), "solid colour comes back as no picture", Join(desk.Calls));

    // 13. Organization policy.
    folder = NewFolder();
    desk = new FakeDesktop { Policy = true, Monitors = [new("MON-A", screens[0].Bounds, beach)] };
    r = new WallpaperMode(desk, folder).Apply(onA, screens, Render);
    ok(!r.Ok && r.Message == Localizer.T("wallpaper.policy") && desk.Calls.Count == 0 && !Directory.Exists(folder), "a wallpaper fixed by policy is left alone");

    // 14. Windows refuses: nothing left behind.
    folder = NewFolder();
    desk = new FakeDesktop { FailSet = true, Monitors = [new("MON-A", screens[0].Bounds, beach)] };
    mode = new WallpaperMode(desk, folder);
    r = mode.Apply(onA, screens, Render);
    ok(!r.Ok && r.Message == Localizer.T("wallpaper.set_failed", "nope") && !mode.Pending && !File.Exists(Path.Combine(folder, "wallpaper.json")),
        "a refused picture leaves no record", r.Message ?? "");

    // 15. Span: one picture over the box around every monitor.
    folder = NewFolder();
    desk = new FakeDesktop { Fit = WallpaperFit.Span, Monitors = [new("MON-A", screens[0].Bounds, beach), new("MON-B", screens[1].Bounds, beach)] };
    mode = new WallpaperMode(desk, folder);
    mode.Apply(onB, screens, Render);
    ok(desk.Calls.SequenceEqual(["set * table-1.png"]) && seen?.Area == new PixelRect(0, 0, 4480, 1440) && seen.Screen == screens[1],
        "span: one picture for all monitors, the card on the window's monitor", Join(desk.Calls));
    desk.Calls.Clear();
    mode.Restore();
    ok(desk.Calls.SequenceEqual(["set * beach.jpg"]), "span: put back on all monitors", Join(desk.Calls));

    // 16. No IDesktopWallpaper: one picture for every monitor, drawn for the window's monitor.
    folder = NewFolder();
    desk = new FakeDesktop { PerMonitor = false, Monitors = [new("", default, beach)] };
    mode = new WallpaperMode(desk, folder);
    mode.Apply(onB, screens, Render);
    ok(desk.Calls.SequenceEqual(["set * table-1.png"]) && seen?.Area == screens[1].Bounds, "classic API: drawn for the window's screen", Join(desk.Calls));
    mode.Restore();

    // 17. The window ends up on another monitor (displays changed): the old one goes back first.
    folder = NewFolder();
    desk = new FakeDesktop { Monitors = Two() };
    mode = new WallpaperMode(desk, folder);
    mode.Apply(onB, screens, Render);
    desk.Calls.Clear();
    version++;
    mode.Apply(onA, screens, Render);
    ok(desk.Calls.SequenceEqual(["set MON-B city.png", "set MON-A table-1.png"]) && WallpaperRecordStore.Load(folder)!.Original == beach,
        "moving monitors: old one restored, new one kept and drawn", Join(desk.Calls));
    mode.Restore();

    // 18. Leaving while that monitor is unplugged: kept for later.
    folder = NewFolder();
    desk = new FakeDesktop { Monitors = Two() };
    mode = new WallpaperMode(desk, folder);
    mode.Apply(onB, screens, Render);
    var unplugged = desk.Monitors[1];
    desk.Monitors.RemoveAt(1);
    desk.Calls.Clear();
    r = mode.Restore();
    ok(r.Ok && desk.Calls.Count == 0 && mode.Pending, "a monitor that is not connected keeps its record");
    desk.Monitors.Add(unplugged);
    r = new WallpaperMode(desk, folder).Restore();
    ok(r.Ok && desk.Calls.SequenceEqual(["set MON-B city.png"]), "…and is put back once it is", Join(desk.Calls));

    // 19. Can't read the wallpaper; 20. not Windows.
    folder = NewFolder();
    desk = new FakeDesktop { FailRead = true };
    r = new WallpaperMode(desk, folder).Apply(onA, screens, Render);
    ok(!r.Ok && r.Message == Localizer.T("wallpaper.read_failed", "fake read failure") && desk.Calls.Count == 0, "unreadable wallpaper: nothing changes");
    folder = NewFolder();
    WallpaperRecordStore.Save(folder, new WallpaperRecord { Monitor = "X", Original = beach, Applied = "x" });
    var other = new WallpaperMode(new UnsupportedWallpaperHost(), folder);
    ok(!other.Apply(onA, screens, Render).Ok, "other systems: not supported");
    ok(other.Restore().Ok && !other.Pending, "other systems: a stray record is dropped");
}

static void Com(Check ok)
{
    FakeCom.Build();
    var calls = FakeCom.Calls;
    var com = new DesktopWallpaperCom(FakeCom.Desk);
    string Last() => calls.Count == 0 ? "" : calls[^1];

    calls.Clear();
    ok(com.GetMonitorDevicePathCount(out var count) == 0 && count == 3 && Last() == "GetMonitorDevicePathCount", "slot 6 GetMonitorDevicePathCount", Last());
    ok(com.GetMonitorDevicePathAt(1, out var id) == 0 && id == "MON-1" && Last() == "GetMonitorDevicePathAt", "slot 5 GetMonitorDevicePathAt + string", Last());
    ok(com.GetMonitorRect("MON-1", out var rect) == 0 && rect == new PixelRect(1920, 0, 1920, 1080) && Last() == "GetMonitorRECT", "slot 7 GetMonitorRECT", rect.ToString());
    ok(com.GetMonitorRect("MON-2", out var none) == 1 && none.Width == 0, "a monitor that isn't connected (S_FALSE, empty)");
    ok(com.GetWallpaper("MON-0", out var path) == 0 && path == @"C:\pics\MON-0.jpg" && Last() == "GetWallpaper", "slot 4 GetWallpaper", path);
    ok(com.SetWallpaper(null, @"C:\t\table-1.png") == 0 && Last() == "SetWallpaper" && FakeCom.LastMonitor == null && FakeCom.LastPath == @"C:\t\table-1.png",
        "slot 3 SetWallpaper; null monitor = every monitor");
    ok(com.SetWallpaper("MON-1", "x.png") == 0 && FakeCom.LastMonitor == "MON-1" && FakeCom.LastPath == "x.png", "SetWallpaper for one monitor");
    ok(com.GetPosition(out var position) == 0 && position == 3 && Last() == "GetPosition", "slot 11 GetPosition", Last());
    ok(com.GetBackgroundColor(out var colour) == 0 && colour == 0x00112233 && Last() == "GetBackgroundColor", "slot 9 GetBackgroundColor", Last());
    ok(com.GetStatus(out var status) == 0 && status == 3 && Last() == "GetStatus", "slot 17 GetStatus", Last());
    ok(com.GetSlideshow(out var array) == 0 && array == FakeCom.Array && Last() == "GetSlideshow", "slot 13 GetSlideshow", Last());
    calls.Clear();
    var items = DesktopWallpaperCom.ItemPaths(array);
    ok(items.SequenceEqual([@"C:\Slides", @"D:\More pictures"]), "slideshow folders through IShellItemArray / IShellItem", Join(items));
    ok(calls.SequenceEqual(["Array.GetCount", "Array.GetItemAt", "Item.GetDisplayName:80058000", "Release:item0", "Array.GetItemAt", "Item.GetDisplayName:80058000", "Release:item1"]),
        "GetCount (7), GetItemAt (8), GetDisplayName (5) with SIGDN_FILESYSPATH, each item released, the array left to the caller", Join(calls));
    ok(com.GetSlideshowOptions(out var options, out var tick) == 0 && options == 1 && tick == 900000 && Last() == "GetSlideshowOptions", "slot 15 GetSlideshowOptions");
    ok(com.SetSlideshow(array) == 0 && Last() == "SetSlideshow:array", "slot 12 SetSlideshow", Last());
    ok(com.SetSlideshowOptions(1, 900000) == 0 && Last() == "SetSlideshowOptions:1:900000", "slot 14 SetSlideshowOptions", Last());
    ok(DesktopWallpaperCom.Release(FakeCom.Desk) == 0 && Last() == "Release:desk", "slot 2 Release");
    var expected = new[] { "GetMonitorDevicePathCount", "GetMonitorDevicePathAt", "GetMonitorRECT", "GetWallpaper", "SetWallpaper", "GetPosition",
        "GetBackgroundColor", "GetStatus", "GetSlideshow", "Array.GetCount", "Array.GetItemAt", "GetSlideshowOptions", "SetSlideshow",
        "SetSlideshowOptions", "Release", "Item.GetDisplayName" };
    var stray = calls.Where(c => !expected.Any(e => c == e || c.StartsWith(e + ":", StringComparison.Ordinal))).ToList();
    ok(stray.Count == 0, "no other slot was called (SetPosition, Enable, AddRef, …)", Join(stray));
}

static void Picture(Check ok, string root)
{
    var dir = Path.Combine(root, "picture");
    Directory.CreateDirectory(dir);
    var png = Pictures.Halves(dir, "halves.png", 400, 200);
    var jpg = Pictures.Halves(dir, "halves.jpg", 400, 200);
    using (var s = File.OpenRead(png)) ok(WallpaperImage.ReadSize(s) == (400, 200), "PNG size from the header");
    using (var s = File.OpenRead(jpg)) ok(WallpaperImage.ReadSize(s) == (400, 200), "JPEG size from the frame header");
    var bmp = new byte[30]; "BM"u8.CopyTo(bmp); BitConverter.GetBytes(640).CopyTo(bmp, 18); BitConverter.GetBytes(-480).CopyTo(bmp, 22);
    ok(WallpaperImage.ReadSize(new MemoryStream(bmp)) == (640, 480), "BMP size (top-down height)");
    var gif = new byte[16]; "GIF89a"u8.CopyTo(gif); gif[6] = 0x40; gif[7] = 0x01; gif[8] = 0xF0;
    ok(WallpaperImage.ReadSize(new MemoryStream(gif)) == (320, 240), "GIF size");
    var webp = new byte[32]; "RIFF"u8.CopyTo(webp); "WEBPVP8X"u8.CopyTo(webp.AsSpan(8)); webp[24] = 0xE7; webp[25] = 0x03; webp[27] = 0xF3; webp[28] = 0x01;
    ok(WallpaperImage.ReadSize(new MemoryStream(webp)) == (1000, 500), "WebP (VP8X) size");
    ok(WallpaperImage.ReadSize(new MemoryStream("not a picture at all, sorry"u8.ToArray())) == null, "unknown data: no size");

    var screen = new WallpaperScreen(new PixelRect(0, 0, 800, 600), new PixelRect(0, 0, 800, 560), 1);
    SKBitmap Draw(WallpaperFit fit, string? picture, Control? card = null, PixelPoint at = default, double scale = 1) =>
        Pictures.Decode(WallpaperImage.Compose(new WallpaperScene(new PixelRect(0, 0, 800, 600), fit, picture, 0x00008000, screen, default), card, at, scale));
    bool Green(SKColor c) => Pictures.Near(c, 0, 0x80, 0);

    using (var b = Draw(WallpaperFit.Fill, png))
        ok(b.Width == 800 && b.Height == 600 && Pictures.IsRed(b.GetPixel(100, 300)) && Pictures.IsRed(b.GetPixel(390, 300)) && Pictures.IsBlue(b.GetPixel(410, 300))
           && Pictures.IsBlue(b.GetPixel(790, 10)), "fill: the picture covers the screen, cut at the sides");
    using (var b = Draw(WallpaperFit.Fit, png))
        ok(Green(b.GetPixel(400, 50)) && Green(b.GetPixel(400, 550)) && Pictures.IsRed(b.GetPixel(100, 300)) && Pictures.IsBlue(b.GetPixel(700, 300)),
            "fit: bands of the background colour above and below");
    using (var b = Draw(WallpaperFit.Center, png))
        ok(Green(b.GetPixel(100, 100)) && Pictures.IsRed(b.GetPixel(250, 300)) && Pictures.IsBlue(b.GetPixel(550, 300)) && Green(b.GetPixel(650, 300)),
            "center: actual size in the middle");
    using (var b = Draw(WallpaperFit.Tile, png))
        ok(Pictures.IsRed(b.GetPixel(100, 100)) && Pictures.IsBlue(b.GetPixel(300, 100)) && Pictures.IsRed(b.GetPixel(500, 300)) && Pictures.IsBlue(b.GetPixel(700, 500)),
            "tile: repeated from the top left");
    using (var b = Draw(WallpaperFit.Stretch, jpg))
        ok(Pictures.IsRed(b.GetPixel(300, 300)) && Pictures.IsBlue(b.GetPixel(500, 300)), "stretch (and JPEG decoding)");
    using (var b = Draw(WallpaperFit.Fill, Path.Combine(dir, "missing.jpg")))
        ok(Green(b.GetPixel(400, 300)) && Green(b.GetPixel(10, 10)), "a picture that can't be read: the colour only");
    var big = Pictures.Halves(dir, "big.jpg", 4000, 2000);
    using (var bitmap = WallpaperImage.LoadPicture(big, WallpaperFit.Fill, 800, 600))
        ok(bitmap != null && bitmap.PixelSize.Width == 1200, "a big picture is decoded at the size it is drawn", bitmap?.PixelSize.ToString() ?? "null");

    // The card: alert boxes, as many rows as fit, "+ N more rows"; on the picture at the window's place.
    var doc = NoteDocument.CreateDefault();
    while (doc.Rows.Count < 30) doc.Rows.Add(["Task " + doc.Rows.Count, "detail", "", ""]);
    doc.EnsureRowIds();
    doc.AlertBoxes = [AlertBoxOps.Create("Read me", "A pinned note"), AlertBoxOps.Create("", "")];
    var cardBg = new SolidColorBrush(Color.Parse("#FFF6C2"));
    var card = WallpaperImage.BuildCard(doc, new ViewImageOptions { Now = DateTime.Now }, new Size(600, 400), cardBg, _ => null);
    card.Measure(Size.Infinity);
    card.Arrange(new Rect(card.DesiredSize));
    var texts = card.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToList();
    var more = texts.FirstOrDefault(t => t.StartsWith("+ ", StringComparison.Ordinal)) ?? "";
    var hidden = int.TryParse(more.Split(' ').ElementAtOrDefault(1), out var h) ? h : -1;
    ok(hidden is > 0 and < 30 && more == Localizer.T("wallpaper.more_rows", hidden), "rows that don't fit become \"+ N more rows\"", more);
    ok(card.DesiredSize.Height <= 401 && card.DesiredSize.Width <= 601, "the card stays inside the window's size", card.DesiredSize.ToString());
    ok(texts.Contains("Read me") && texts.Contains("A pinned note") && texts.Count(t => t.Length == 0) == 0, "alert boxes are on the card; empty ones are left out", Join(texts));

    var fewDoc = NoteDocument.CreateDefault();
    var few = WallpaperImage.BuildCard(fewDoc, new ViewImageOptions { Now = DateTime.Now }, new Size(1400, 900), cardBg, _ => null);
    few.Measure(Size.Infinity);
    ok(few.DesiredSize.Height < 400 && !few.GetLogicalDescendants().OfType<TextBlock>().Any(t => (t.Text ?? "").StartsWith("+ ")),
        "a short table makes a short card, with no \"more\" line", few.DesiredSize.ToString());

    // A table wider than the window: shrunk (not below 60 %), starting at the row numbers, cut only on the right.
    var wide = NoteDocument.CreateDefault();
    wide.ColumnWidths = [400, 400, 300, 300];
    var wideCard = WallpaperImage.BuildCard(wide, new ViewImageOptions { Now = DateTime.Now }, new Size(700, 400), cardBg, _ => null);
    var shrink = wideCard.GetLogicalDescendants().OfType<LayoutTransformControl>().FirstOrDefault()?.LayoutTransform as ScaleTransform;
    ok(shrink != null && Math.Abs(shrink.ScaleX - Math.Max(WallpaperImage.MinTableScale, (700 - 24) / 1444.0)) < 0.01,
        "a table wider than the window is drawn smaller", shrink?.ScaleX.ToString("0.000") ?? "none");
    using (var b = Pictures.Decode(WallpaperImage.Compose(new WallpaperScene(new PixelRect(0, 0, 800, 600), WallpaperFit.Fill, png, 0, screen, default),
               wideCard, new PixelPoint(20, 20), 1)))
    {
        var label = b.GetPixel(20 + 12 + 10, 20 + 12 + 40); // first column: the row numbers
        ok(Pictures.Near(label, 0xF8, 0xE7, 0xA4, 12) || Pictures.Near(label, 0xF6, 0xE0, 0x8A, 12), "the wide table starts at the row numbers (cut on the right, never centred)", label.ToString());
    }

    var composed = WallpaperImage.Compose(new WallpaperScene(new PixelRect(0, 0, 800, 600), WallpaperFit.Fill, png, 0x00008000, screen, default),
        WallpaperImage.BuildCard(doc, new ViewImageOptions { Now = DateTime.Now }, new Size(300, 260), cardBg, _ => null), new PixelPoint(420, 40), 1.5);
    File.WriteAllBytes(Path.Combine(dir, "composed.png"), composed);
    using (var b = Pictures.Decode(composed))
    {
        var inCard = b.GetPixel(420 + 30, 40 + 8); // top padding of the card
        ok(Pictures.Near(inCard, 0xFF, 0xF6, 0xC2, 6), "the card is drawn at the window's place", inCard.ToString());
        ok(Pictures.IsRed(b.GetPixel(100, 300)) && Pictures.IsBlue(b.GetPixel(790, 590)), "the wallpaper shows around the card");
        var below = b.GetPixel(420 + 100, 40 + (int)(262 * 1.5) + 30);
        ok(Pictures.IsBlue(below), "the card ends at the window's height (scaled)", below.ToString());
        var dark = 0;
        for (var y = 40; y < 40 + 390; y++)
            for (var x = 420; x < 420 + 450 && x < 800; x++)
                if (b.GetPixel(x, y) is var p && p.Red < 120 && p.Green < 120 && p.Blue < 120) dark++;
        ok(dark > 500, "the card has text on it", dark.ToString());
    }
}

static void WindowFlow(Check ok, string root)
{
    var dir = Path.Combine(root, "window");
    Directory.CreateDirectory(dir);
    var note = Path.Combine(dir, "note.json");
    Environment.SetEnvironmentVariable("TABLENOTE_DATA", note);
    File.WriteAllText(Path.Combine(dir, "settings.json"), "{ \"language\": \"th\", \"uiScale\": 100 }");
    var beach = Pictures.Halves(dir, "beach.jpg", 1280, 720);
    var folder = WallpaperFiles.Folder(note);

    // The way back must exist before the editor hides: the tray (headless has one) and the start-again signal.
    var program = typeof(MainWindow).Assembly.GetType("TableNote.Program")!;
    var signal = new EditorSignal("TableNote.Test." + Guid.NewGuid().ToString("N")[..8]);
    signal.TryStart(() => { });
    program.GetField("_signal", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, signal);
    program.GetField("_signalStarted", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, true);

    var desk = new FakeDesktop();
    MainWindow.WallpaperHostOverride = () => desk;
    var view = typeof(MainWindow).GetField("_view", BindingFlags.Instance | BindingFlags.NonPublic)!;
    void Pump(int ms = 50)
    {
        var until = DateTime.Now.AddMilliseconds(ms);
        do { Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); } while (DateTime.Now < until);
    }
    string SavedMode() => JsonDocument.Parse(File.ReadAllText(note)).RootElement.GetProperty("viewMode").GetString() ?? "";
    void Call(MainWindow w, string method, params object[] args) =>
        typeof(MainWindow).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(w, args);

    var window = new MainWindow { Width = 900, Height = 600 };
    window.Show();
    Pump(300);
    var screen = window.Screens!.All[0];
    desk.Monitors = [new("MON-A", screen.Bounds, beach)];
    var button = window.FindControl<Button>("WallpaperButton")!;
    ok(button.Content?.ToString() == "วอลเปเปอร์" && button.IsVisible, "the toolbar has a Wallpaper button (Thai)", button.Content?.ToString() ?? "");
    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    Pump(400);
    ok(!window.IsVisible && (TableNote.ViewMode)view.GetValue(window)! == TableNote.ViewMode.Background, "the editor goes to the tray");
    ok(desk.Calls.SequenceEqual(["set MON-A table-1.png"]), "the table picture is set on the monitor", Join(desk.Calls));
    ok(SavedMode() == "background", "wallpaper mode is saved in note.json (as the old background mode)", SavedMode());
    ok(File.ReadAllText(Path.Combine(dir, "settings.json")).Contains("\"wallpaperTipShown\": true"), "the first-time tip is shown once (settings.json)");
    var picture = Path.Combine(folder, "table-1.png");
    using (var b = Pictures.Decode(File.ReadAllBytes(picture)))
    {
        ok(b.Width == screen.Bounds.Width && b.Height == screen.Bounds.Height, "full screen picture", $"{b.Width}x{b.Height}");
        var x = Math.Max(0, window.Position.X) + 40;
        var y = Math.Max(0, window.Position.Y) + 6;
        ok(Pictures.Near(b.GetPixel(x, y), 0xFF, 0xF6, 0xC2, 6), "the card is where the window was", b.GetPixel(x, y).ToString());
        ok(Pictures.IsBlue(b.GetPixel(b.Width - 5, b.Height - 5)), "the user's wallpaper is around it");
        File.Copy(picture, Path.Combine(root, "window-wallpaper.png"), overwrite: true);
    }

    // A reminder marked done from the popup changes the table: the wallpaper follows.
    var session = typeof(MainWindow).GetField("_session", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
    var doc = (NoteDocument)session.GetType().GetProperty("Doc")!.GetValue(session)!;
    desk.Calls.Clear();
    Call(window, "ApplyDone", doc.RowIdAt(0));
    Pump(2200);
    ok(desk.Calls.SequenceEqual(["set MON-A table-2.png"]), "a change while on the wallpaper redraws it (debounced)", Join(desk.Calls));
    var saved = File.GetLastWriteTimeUtc(note);
    desk.Calls.Clear();
    Call(window, "OnColourClock", null!, EventArgs.Empty);
    Pump(3200);
    ok(desk.Calls.Count == 0, "the minute clock redraws nothing when nothing changed", Join(desk.Calls));
    ok(File.GetLastWriteTimeUtc(note) == saved, "no saves while nothing changes on the wallpaper");

    // Tray → Edit: the editor comes back, the user's wallpaper too.
    Call(window, "ShowEditor");
    Pump(300);
    ok(window.IsVisible && (TableNote.ViewMode)view.GetValue(window)! == TableNote.ViewMode.Edit, "the tray brings the editor back");
    ok(desk.Calls.SequenceEqual(["set MON-A beach.jpg"]) && !File.Exists(Path.Combine(folder, "wallpaper.json")), "…and the user's wallpaper", Join(desk.Calls));
    ok(SavedMode() == "edit", "edit mode saved", SavedMode());
    Pump(300);
    ok(window.IsVisible, "the editor stays open");

    // Crash while on the wallpaper, then a new start: it comes back on the wallpaper, the user's picture still known.
    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    Pump(400);
    view.SetValue(window, TableNote.ViewMode.Edit); // the old window is "dead": no more redraws from it
    desk.Calls.Clear();
    var second = new MainWindow { Width = 900, Height = 600 };
    second.Show();
    Pump(500);
    ok(!second.IsVisible && desk.Calls.Count == 1 && desk.Calls[0].StartsWith("set MON-A table-", StringComparison.Ordinal),
        "a new start goes back to the wallpaper and redraws it", Join(desk.Calls));
    ok(WallpaperRecordStore.Load(folder)?.Original == beach, "the user's wallpaper is still the one to put back");

    // Exit from the tray: the user's wallpaper comes back, wallpaper mode stays on for the next start.
    var exits = 0;
    MainWindow.ExitAfterCloseOverride = () => exits++;
    desk.Calls.Clear();
    Call(second, "QuitApp");
    Pump(300);
    ok(desk.Calls.SequenceEqual(["set MON-A beach.jpg"]) && SavedMode() == "background", "Exit puts the user's wallpaper back and keeps the mode", Join(desk.Calls) + " / " + SavedMode());

    // --edit with a table picture still up (e.g. after a crash): the editor opens and the wallpaper goes back.
    var third = new MainWindow { Width = 900, Height = 600 };
    third.Show();
    Pump(500);
    ok(!third.IsVisible, "(a normal start is on the wallpaper again)");
    view.SetValue(third, TableNote.ViewMode.Edit);
    program.GetProperty("ForceEdit", BindingFlags.Static | BindingFlags.Public)!.SetValue(null, true);
    desk.Calls.Clear();
    var forced = new MainWindow { Width = 900, Height = 600 };
    forced.Show();
    Pump(500);
    ok(forced.IsVisible && desk.Calls.SequenceEqual(["set MON-A beach.jpg"]) && SavedMode() == "edit", "--edit opens the editor and puts the wallpaper back", Join(desk.Calls));
    program.GetProperty("ForceEdit", BindingFlags.Static | BindingFlags.Public)!.SetValue(null, false);

    // Regression (2.5.1): after a sign-in start in the tray, opening the editor hid it again at once.
    view.SetValue(forced, TableNote.ViewMode.Edit);
    program.GetProperty("StartHidden", BindingFlags.Static | BindingFlags.Public)!.SetValue(null, true);
    var signIn = new MainWindow { Width = 900, Height = 600 };
    signIn.Show();
    Pump(400);
    ok(!signIn.IsVisible, "sign-in start: in the tray");
    Call(signIn, "ShowEditor");
    Pump(600);
    ok(signIn.IsVisible, "the tray opens the editor and it stays open");
    program.GetProperty("StartHidden", BindingFlags.Static | BindingFlags.Public)!.SetValue(null, false);
    MainWindow.ExitAfterCloseOverride = null;
    MainWindow.WallpaperHostOverride = null;
    signal.Dispose();
}
