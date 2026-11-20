using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TableNote;

// 2.6.0 start-up: splash screen, "another copy is running" check, start-up records in tablenote.log.
var fail = 0;
void Ok(bool cond, string name, string detail = "")
{
    Console.WriteLine((cond ? "PASS " : "FAIL ") + name + (cond || detail.Length == 0 ? "" : " — " + detail));
    if (!cond) fail++;
}
var dir = Directory.CreateTempSubdirectory("tn-start").FullName;
var note = Path.Combine(dir, "note.json");
Environment.SetEnvironmentVariable("TABLENOTE_DATA", note);
File.WriteAllText(Path.Combine(dir, "settings.json"), "{ \"language\": \"th\", \"uiScale\": 100 }");
AppLog.PathOverride = Path.Combine(dir, "tablenote.log");
AppBuilder.Configure<App>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .With(AppFonts.ManagerOptions)
    .SetupWithoutStarting();
Localizer.SetLanguage("th");

void Pump(int ms)
{
    var sw = Stopwatch.StartNew();
    do
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
        Thread.Sleep(10);
    } while (sw.ElapsedMilliseconds < ms);
}

// ── splash ──
SplashWindow.MinimumTime = TimeSpan.FromMilliseconds(400);
var splash = new SplashWindow();
splash.Show();
Pump(60);
var image = splash.GetVisualDescendants().OfType<Image>().FirstOrDefault();
ok(image?.Source is Bitmap { PixelSize.Width: 520 }, "the splash shows the Table Note logo", (image?.Source as Bitmap)?.PixelSize.ToString() ?? "no image");
var texts = splash.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
ok(texts.Contains("กำลังเปิดตาราง…") && texts.Contains("รุ่น " + AppInfo.Version), "Thai loading text and version", string.Join(" | ", texts));
ok(splash.WindowDecorations == WindowDecorations.None && splash.Topmost && !splash.ShowInTaskbar &&
   splash.WindowStartupLocation == WindowStartupLocation.CenterScreen, "borderless, on top, centred, no taskbar button");
ok(splash.Bounds.Width is > 300 and < 460 && splash.Bounds.Height is > 340 and < 520, "a compact window", splash.Bounds.Size.ToString());
SaveFrame(splash, "splash-headless");
var next = false;
splash.WhenDrawn(() => next = true);
Pump(250);
ok(next, "the editor is built once the splash has drawn");
SplashWindow.MinimumTime = TimeSpan.FromMilliseconds(1200);
var timed = new SplashWindow();
timed.Show();
Pump(50);
var early = false;
timed.AfterMinimumTime(() => early = true);
Pump(300);
ok(!early, "the editor waits until the splash has been up for the minimum time");
Pump(1200);
ok(early, "then it is shown");
timed.Close();
var main = new Window { Width = 300, Height = 200, Content = new TextBlock { Text = "editor" } };
main.Show();
var closed = false;
splash.Closed += (_, _) => closed = true;
var after = false;
splash.CloseAfterFirstFrameOf(main, () => after = true);
Pump(300);
ok(closed && after, "the splash closes once the editor has drawn, then the editor is activated");
main.Close();
var never = new SplashWindow();
never.Show();
var hidden = new Window { Width = 100, Height = 100 }; // wallpaper mode hides the editor: no frames come
var gone = false;
never.Closed += (_, _) => gone = true;
SplashWindow.FirstFrameTimeout = TimeSpan.FromMilliseconds(200);
never.CloseAfterFirstFrameOf(hidden);
Pump(450);
ok(gone, "without editor frames the splash still closes (time-out)");

// ── another copy running ──
var me = @"C:\Apps\TableNote 2.6.0\TableNote.exe";
var same = new OtherInstance.Running(10, @"c:\apps\tablenote 2.6.0\TableNote.EXE", "2.6.0");
var older = new OtherInstance.Running(11, @"C:\Users\x\Downloads\TableNote.exe", "2.5.1");
ok(OtherInstance.Pick(me, [same]) == null, "the same file again: just bring it forward (no question)");
ok(OtherInstance.Pick(me, [same, older]) == older, "a copy from another file is offered for closing");
ok(OtherInstance.Pick(null, [older]) == null, "unknown own path: no question");
var q = OtherInstance.Question(older);
ok(q.Contains("2.5.1") && q.Contains(AppInfo.Version) && q.Contains(Path.GetDirectoryName(older.Path)!), "the question names both versions and the folder", q);

// ── start records ──
var window = new MainWindow { Width = 800, Height = 500 };
window.Show();
Pump(300);
var log = File.Exists(AppLog.PathOverride) ? File.ReadAllText(AppLog.PathOverride) : "";
ok(log.Contains("start: TableNote " + AppInfo.Version + ": start, saved mode Edit"), "every start writes a line to tablenote.log", log);
ok(log.Contains("start: editor drawn ") && log.Contains(" ms after it was shown"), "and when the editor was drawn", log);
ok(!log.Contains("nothing was drawn"), "no drawing problem reported");
window.Close();

Console.WriteLine(fail == 0 ? "StartTests: all passed" : $"StartTests: {fail} failed");
return fail;

void ok(bool c, string n, string d = "") => Ok(c, n, d);

static void SaveFrame(Window w, string name)
{
    try
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
        var frame = w.CaptureRenderedFrame() ?? w.GetLastRenderedFrame();
        if (frame == null) return;
        var outDir = Environment.GetEnvironmentVariable("SHOTS") ?? Path.GetTempPath();
        Directory.CreateDirectory(outDir);
        frame.Save(Path.Combine(outDir, name + ".png"));
    }
    catch (Exception ex) { Console.WriteLine("screenshot skipped: " + ex.Message); }
}
