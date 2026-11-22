using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TableNote;

// 2.6.0 start-up: splash screen, "another copy is running" check, start-up records in tablenote.log.
// 2.6.1: faster start (publish settings, a shorter splash wait, the start timeline) and lighter table cells.
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

// ── 2.6.1: how the Windows exe is built, and the start timeline ──
var csproj = FindRepoFile("TableNote.csproj");
var win = System.Text.RegularExpressions.Regex.Match(csproj, @"<PropertyGroup Condition=""'\$\(RuntimeIdentifier\)' == 'win-x64'"">(.*?)</PropertyGroup>",
    System.Text.RegularExpressions.RegexOptions.Singleline).Groups[1].Value;
bool Prop(string name, string value) => win.Contains($"<{name}>{value}</{name}>");
ok(Prop("PublishReadyToRun", "true") && Prop("PublishReadyToRunComposite", "true"),
    "the Windows exe carries precompiled code (ReadyToRun, one composite image)", win);
ok(Prop("EnableCompressionInSingleFile", "false"), "the exe is not compressed inside (no unpacking into memory at every start)");
ok(Prop("TieredCompilation", "false"), "the precompiled code is not compiled again in the background");
ok(Prop("PublishTrimmed", "true") && Prop("PublishSingleFile", "true") && Prop("SelfContained", "true"),
    "still one self-contained, trimmed file");
ok(SplashWindow.MinimumTime == TimeSpan.FromMilliseconds(300), "the splash waits at most 0.3 s for a start that is already done",
    SplashWindow.MinimumTime.ToString());

StartClock.Reset();
StartClock.Begin();
StartClock.Mark("splash drawn");
Thread.Sleep(15);
StartClock.Mark("table built");
StartClock.Mark("splash drawn"); // a second mark of a step does not move it
ok(StartClock.At("code") is >= 0 && StartClock.At("splash drawn") is { } sd && StartClock.At("table built") is { } tb && tb >= sd + 10,
    "steps are counted from the process start, each once", StartClock.Timeline());
var line = StartClock.Timeline();
ok(line.StartsWith("code ") && line.IndexOf("splash drawn", StringComparison.Ordinal) < line.IndexOf("table built", StringComparison.Ordinal) &&
   line.Contains(" ms · "), "the timeline lists the steps in order", line);
StartClock.Reset();

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
var timeline = log.Split('\n').FirstOrDefault(l => l.Contains("start timeline (ms after the process started): ")) ?? "";
ok(timeline.Contains("editor opened") && timeline.Contains("editor drawn"), "2.6.1: the start timeline is written once the editor has drawn", log);
var again = new MainWindow { Width = 400, Height = 300 };
again.Show();
Pump(200);
again.Close();
log = File.ReadAllText(AppLog.PathOverride!);
ok(log.Split('\n').Count(l => l.Contains("start timeline")) == 1, "only one timeline per start");

// 2.6.1: lighter cells — the same border, padding and text, without the parts a cell never shows.
var cells = window.GetVisualDescendants().OfType<TextBox>().Where(t => t.Classes.Contains("cell")).ToList();
var heads = window.GetVisualDescendants().OfType<TextBox>().Where(t => t.Classes.Contains("colhead")).ToList();
var cellTheme = window.FindResource("TnCellTextBox");
ok(cells.Count >= 4 && heads.Count >= 4 && cells.Concat(heads).All(c => c.Theme != null && ReferenceEquals(c.Theme, cellTheme)),
    "table cells and column headers use the light cell template", $"{cells.Count} cells, {heads.Count} headers");
var parts = cells[0].GetVisualDescendants().ToList();
ok(parts.Count <= 8 && !parts.OfType<Avalonia.Controls.Primitives.ScrollBar>().Any() && !parts.OfType<DataValidationErrors>().Any(),
    "a cell has a handful of parts and no scroll bars", string.Join(", ", parts.Select(v => v.GetType().Name)));
ok(parts.OfType<Border>().Any(b => b.Name == "PART_BorderElement") && parts.OfType<Avalonia.Controls.Presenters.TextPresenter>().Any(t => t.Name == "PART_TextPresenter") &&
   parts.OfType<ScrollViewer>().Any(s => s.Name == "PART_ScrollViewer"),
    "it keeps the border the selection colours paint, the text, and the scroller that keeps the caret in view");
var formula = window.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(t => !t.Classes.Contains("cell") && !t.Classes.Contains("colhead") && t.IsVisible);
ok(formula == null || formula.Theme == null, "other text boxes (formula bar, find) keep the standard look");
ToolTip.SetServiceEnabled(window, false); // a tooltip must not cover the click
var target = cells[1];
var center = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window)!.Value;
window.MouseMove(center);
window.MouseDown(center, Avalonia.Input.MouseButton.Left);
window.MouseUp(center, Avalonia.Input.MouseButton.Left);
Pump(80);
window.KeyTextInput("ทดสอบ");
Pump(80);
ok(target.Text == "ทดสอบ" && target.IsFocused, "a click and typing still fill a light cell", $"'{target.Text}' focus={target.IsFocused}");
window.KeyPress(Avalonia.Input.Key.Escape, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.Escape, null);
Pump(60);

// 2.6.1: row and column menus are made when they open (not for every row on screen).
var withMenus = window.GetVisualDescendants().OfType<Control>().Where(c => c.ContextMenu != null).ToList();
ok(withMenus.Count >= 4 && withMenus.All(c => c.ContextMenu!.Items.Count == 0), "no menu items are made before a menu opens",
    string.Join(", ", withMenus.Select(c => c.ContextMenu!.Items.Count)));
var rowLabel = withMenus.First();
rowLabel.RaiseEvent(new Avalonia.Input.ContextRequestedEventArgs());
Pump(120);
ok(rowLabel.ContextMenu!.Items.Count > 3, "a right-click fills the menu", rowLabel.ContextMenu.Items.Count.ToString());
rowLabel.ContextMenu.Close();
var byCode = withMenus.Last().ContextMenu!;
byCode.Open(withMenus.Last());
Pump(120);
ok(byCode.Items.Count > 3, "a menu opened from code fills itself too", byCode.Items.Count.ToString());
byCode.Close();
window.Close();

Console.WriteLine(fail == 0 ? "StartTests: all passed" : $"StartTests: {fail} failed");
return fail;

void ok(bool c, string n, string d = "") => Ok(c, n, d);

static string FindRepoFile(string name)
{
    var d = new DirectoryInfo(AppContext.BaseDirectory);
    while (d != null && !File.Exists(Path.Combine(d.FullName, name))) d = d.Parent;
    return File.ReadAllText(Path.Combine(d?.FullName ?? throw new Exception(name + " not found"), name));
}

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
