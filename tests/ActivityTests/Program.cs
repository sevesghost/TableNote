using System.Diagnostics;
using System.Reflection;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TableNote;

// 2.6.0: the activity bar — what TableNote is doing now and did lately (optional, can be hidden).
var fail = 0;
void Ok(bool cond, string name, string detail = "")
{
    Console.WriteLine((cond ? "PASS " : "FAIL ") + name + (cond || detail.Length == 0 ? "" : " — " + detail));
    if (!cond) fail++;
}

var dir = Directory.CreateTempSubdirectory("tn-activity").FullName;
var note = Path.Combine(dir, "note.json");
Environment.SetEnvironmentVariable("TABLENOTE_DATA", note);
File.WriteAllText(Path.Combine(dir, "settings.json"), "{ \"language\": \"th\", \"uiScale\": 100 }");
AppLog.PathOverride = Path.Combine(dir, "tablenote.log");
Localizer.SetLanguage("th");

// ───────────────────────── the log ─────────────────────────
{
    var log = new ActivityLog();
    var changes = 0;
    log.Changed += () => Interlocked.Increment(ref changes);
    var t0 = new DateTime(2026, 11, 21, 10, 0, 0);
    var now = t0;
    log.Clock = () => now;
    var job = log.Begin("กำลังส่งออก a.xlsx…");
    Ok(log.Busy && log.Current?.Text == "กำลังส่งออก a.xlsx…" && log.Current?.Progress == null && log.Entries.Count == 0,
        "Begin: running, no progress known yet, nothing finished");
    job.Report(0.4);
    Ok(log.Current?.Progress == 0.4, "Report sets how far it is");
    now = t0.AddSeconds(2.5);
    job.Done("ส่งออก a.xlsx แล้ว");
    job.Dispose();
    var e = log.Last!;
    Ok(!log.Busy && log.Entries.Count == 1 && e.Kind == ActivityLevel.Done && e.Text == "ส่งออก a.xlsx แล้ว" && e.Duration == TimeSpan.FromSeconds(2.5),
        "Done: one entry with what came out and how long it took (Dispose after Done adds nothing)", $"{log.Entries.Count} {e.Kind} {e.Duration}");
    log.Begin("ก").Fail("พัง");
    log.Begin("ข").Warn("ต้องถามก่อน");
    using (log.Begin("ค")) { }
    Ok(log.Entries.Select(x => x.Kind).SequenceEqual([ActivityLevel.Done, ActivityLevel.Error, ActivityLevel.Warning, ActivityLevel.Done]) && log.Last!.Text == "ค",
        "Fail / Warn / using: error, warning, done");
    log.Add(ActivityLevel.Done, "บันทึก note.json แล้ว", "save");
    now = t0.AddSeconds(10);
    log.Add(ActivityLevel.Done, "บันทึก note.json แล้ว", "save");
    Ok(log.Entries.Count == 5 && log.Last!.Count == 2 && log.Last.Time == now && log.LastOf("save")!.Count == 2,
        "saves one after another are one line, counted, with the newest time");
    log.Add(ActivityLevel.Info, "ย้อนกลับแล้ว");
    log.Add(ActivityLevel.Done, "บันทึก note.json แล้ว", "save");
    log.Add(ActivityLevel.Error, "บันทึกไม่สำเร็จ", "save");
    Ok(log.Entries.Count == 8 && log.Last!.Kind == ActivityLevel.Error && log.LastOf("save")!.Kind == ActivityLevel.Error,
        "something else in between, or another kind: a new line");
    for (var i = 0; i < 100; i++) log.Add(ActivityLevel.Info, "x" + i);
    Ok(log.Entries.Count == ActivityLog.MaxEntries && log.Entries[^1].Text == "x99", "the list keeps the newest " + ActivityLog.MaxEntries);
    Ok(changes > 100, "every change is announced", changes.ToString());

    var shared = new ActivityLog();
    var threads = Enumerable.Range(0, 6).Select(k => new Thread(() =>
    {
        for (var i = 0; i < 300; i++)
        {
            var r = shared.Begin("t" + k);
            shared.Add(ActivityLevel.Info, "n" + i, i % 3 == 0 ? "same" : null);
            if (i % 2 == 0) r.Done(); else r.Fail("f");
        }
    })).ToList();
    foreach (var t in threads) t.Start();
    foreach (var t in threads) t.Join();
    Ok(!shared.Busy && shared.Entries.Count == ActivityLog.MaxEntries, "6 threads at once: nothing left running, the list stays at its size");

    Ok(ActivityLog.Describe("save note.json") == "บันทึก note.json" && ActivityLog.Describe("rebuild table") == "วาดตารางใหม่" &&
       ActivityLog.Describe("start: tray and shortcut") == "เปิดโปรแกรม" && ActivityLog.Describe("idle") == "ทำงานอื่น" && ActivityLog.Describe("xyz") == "xyz",
        "the watchdog's breadcrumbs in plain words");

    var s = AppSettings.Load(note);
    Ok(s.ShowActivity, "the bar is on unless hidden");
    s.ShowActivity = false;
    s.Save(note);
    Ok(File.ReadAllText(AppSettings.PathFor(note)).Contains("\"showActivity\": false") && !AppSettings.Load(note).ShowActivity, "hidden is remembered in settings.json");
    s.ShowActivity = true;
    s.Save(note);
    Ok(!File.ReadAllText(AppSettings.PathFor(note)).Contains("showActivity") && AppSettings.Load(note).ShowActivity, "shown again: nothing written (the default)");
}

// ───────────────────────── the window ─────────────────────────
var seed = new NoteDocument
{
    Columns = ["ราย", "NID", "สถานะ"], ColumnRolesList = ["title", "", "status"], DueColumnOmitted = true, TrayNoticeShown = true,
    ColumnWidths = [240, 150, 120]
};
for (var i = 0; i < 3; i++) seed.Rows.Add([$"บริษัท {i + 1}", $"N{i + 1}", ""]);
seed.EnsureRowIds(); seed.EnsureColumnIds();
NoteStore.Save(note, seed);
AppBuilder.Configure<App>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .With(AppFonts.ManagerOptions)
    .SetupWithoutStarting();
ActivityLog.Shared.Clear();

var window = new MainWindow { Width = 900, Height = 480 };
ToolTip.SetServiceEnabled(window, false);
window.Show();
Pump(400);
var bar = window.FindControl<Border>("ActivityBar")!;
var text = window.FindControl<TextBlock>("ActivityText")!;
var saved = window.FindControl<TextBlock>("ActivitySaved")!;
var progress = window.FindControl<ProgressBar>("ActivityProgress")!;
var dot = window.FindControl<Avalonia.Controls.Shapes.Ellipse>("ActivityDot")!;
string Hex(IBrush? b) => b is ISolidColorBrush c ? c.Color.ToString() : "";
Ok(bar.IsVisible && ActivityLog.Shared.Entries.FirstOrDefault()?.Text.StartsWith("เปิดตาราง 3 แถว (ใช้เวลา ", StringComparison.Ordinal) == true &&
   ((text.Text ?? "").StartsWith("เปิดตาราง 3 แถว", StringComparison.Ordinal) || text.Text == "รอบันทึกอัตโนมัติ…") && !progress.IsVisible,
    "on open the bar is there; the first entry says the table was opened and how long it took", text.Text ?? "");

// typing: waiting for the autosave, then saved
Click(Cell(0, 1));
window.KeyTextInput("ใหม่");
Pump(50);
Ok(text.Text == "รอบันทึกอัตโนมัติ…" && Hex(dot.Fill) == "#ffd9a21b", "after typing: edits wait for the autosave", text.Text ?? "");
Call("FlushSave");
Pump(50);
var clock = DateTime.Now.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
Ok((text.Text ?? "").StartsWith("บันทึก note.json แล้ว · " + clock, StringComparison.Ordinal) && (saved.Text ?? "").StartsWith("บันทึกล่าสุด " + clock, StringComparison.Ordinal),
    "after the save: saved, with the time; the last save on the right", $"{text.Text} | {saved.Text}");
var savesBefore = ActivityLog.Shared.LastOf("save")!.Count;
PressKey(Key.Return);
window.KeyTextInput("อีก");
Call("FlushSave");
Pump(50);
Ok(ActivityLog.Shared.LastOf("save")!.Count == savesBefore + 1 && (text.Text ?? "").EndsWith($"({savesBefore + 1} ครั้ง)", StringComparison.Ordinal),
    "the next save is counted on the same line", text.Text ?? "");

// running work: the progress bar
var run = ActivityLog.Shared.Begin("ทดสอบงานยาว…");
Pump(50);
Ok(progress.IsVisible && progress.IsIndeterminate && text.Text == "ทดสอบงานยาว…" && Hex(dot.Fill) == "#ffd9a21b", "running work: the bar moves and says what it is");
SaveFrame("activity-busy");
run.Report(0.5);
Pump(50);
Ok(!progress.IsIndeterminate && Math.Abs(progress.Value - 50) < 0.01, "known progress fills the bar", progress.Value.ToString());
run.Done("ทดสอบเสร็จ");
Pump(50);
Ok(!progress.IsVisible && (text.Text ?? "").StartsWith("ทดสอบเสร็จ · ", StringComparison.Ordinal), "done: the bar goes, the result stays", text.Text ?? "");
ActivityLog.Shared.Add(ActivityLevel.Error, "ผิดพลาดทดสอบ");
Pump(50);
Ok(Hex(dot.Fill) == "#ffc0392b", "an error shows red");

// the watchdog: a stall in plain words
UiWatchdog.ReportRecovered(3.2, "save note.json");
Pump(50);
Ok((text.Text ?? "").StartsWith("โปรแกรมไม่ตอบสนอง 3.2 วินาที ขณะบันทึก note.json", StringComparison.Ordinal) && Hex(dot.Fill) == "#ffe07a1f",
    "a stall the watchdog saw shows in the bar (orange)", text.Text ?? "");
Ok(File.ReadAllText(Path.Combine(dir, "tablenote.log")).Contains("UI thread responsive again after 3.2 s"), "and in tablenote.log");

// undo shows too
PressKey(Key.Z, RawInputModifiers.Control);
Pump(50);
Ok(ActivityLog.Shared.Entries.Any(x => x.Text.StartsWith("ย้อนกลับ", StringComparison.Ordinal) || x.Text.Contains("ย้อน", StringComparison.Ordinal)), "undo is listed",
    string.Join(" | ", ActivityLog.Shared.Entries.TakeLast(3).Select(x => x.Text)));

// History
var historyButton = window.FindControl<Button>("ActivityHistoryButton")!;
Ok(historyButton.Content as string == "ประวัติ", "the History button");
var flyout = (Flyout)historyButton.Flyout!;
flyout.ShowAt(historyButton);
Pump(150);
var panel = (StackPanel)flyout.Content!;
var lines = panel.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").Where(t => t.Length > 0).ToList();
var iStall = lines.FindIndex(l => l.StartsWith("โปรแกรมไม่ตอบสนอง", StringComparison.Ordinal));
var iDone = lines.FindIndex(l => l == "ทดสอบเสร็จ");
var iOpen = lines.FindIndex(l => l.StartsWith("เปิดตาราง 3 แถว", StringComparison.Ordinal));
Ok(lines.Contains("สิ่งที่ตารางโน้ตทำล่าสุด") && iStall >= 0 && iDone > iStall && iOpen > iDone &&
   lines.Any(l => System.Text.RegularExpressions.Regex.IsMatch(l, @"^บันทึก note\.json แล้ว \(\d+ ครั้ง\)$")),
    "History lists what happened, newest first", string.Join(" | ", lines.Take(10)));
Ok(panel.GetLogicalDescendants().OfType<Button>().Any(b => b.Content as string == "เปิดโฟลเดอร์") && lines.Any(l => l.Contains("tablenote.log")),
    "History points to tablenote.log and can open the folder");
var foreign = ForeignRuns(bar).Concat(ForeignRuns(panel)).ToList();
Ok(foreign.Count == 0, "the bar and History use the bundled font", string.Join(" | ", foreign.Take(6)));
SaveFrame("activity-history");
flyout.Hide();
Pump(50);

// hide and show
var hide = window.FindControl<Button>("ActivityHideButton")!;
hide.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
Pump(50);
var status = window.FindControl<TextBlock>("StatusText")!;
Ok(!bar.IsVisible && File.ReadAllText(AppSettings.PathFor(note)).Contains("\"showActivity\": false") && (status.Text ?? "").Contains("มุมมอง → แถบการทำงาน"),
    "✕ hides the bar, remembers it, and says where to get it back", status.Text ?? "");
var view = window.FindControl<Menu>("MainMenu")!.Items.OfType<MenuItem>().First(i => i.Header?.ToString() == "มุมมอง");
view.Open();
Pump(80);
var item = view.Items.OfType<MenuItem>().First(i => (i.Header?.ToString() ?? "").Contains("แถบการทำงาน"));
Ok(item.Header?.ToString() == "แถบการทำงาน (ด้านล่าง)", "View menu: Activity bar, not ticked", item.Header?.ToString() ?? "");
item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
view.Close();
Pump(80);
Ok(bar.IsVisible && !File.ReadAllText(AppSettings.PathFor(note)).Contains("showActivity") && item.Header?.ToString() == "✓ แถบการทำงาน (ด้านล่าง)",
    "View → Activity bar shows it again (ticked)", item.Header?.ToString() ?? "");
SaveFrame("activity-bar");

// a new start keeps it hidden
hide.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
Pump(50);
window.Close();
Pump(100);
var second = new MainWindow { Width = 900, Height = 480 };
second.Show();
Pump(300);
Ok(!second.FindControl<Border>("ActivityBar")!.IsVisible, "after a restart the bar stays hidden");
second.Close();

Console.WriteLine(fail == 0 ? "ActivityTests: all passed" : $"ActivityTests: {fail} failed");
return fail;

// ───────────────────────── helpers ─────────────────────────
void Call(string method) => typeof(MainWindow).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
TextBox Cell(int row, int column)
{
    Pump(10);
    var visible = (List<int>)typeof(MainWindow).GetField("_visibleRows", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
    var g = (Grid)((ContentPresenter)window.FindControl<ItemsControl>("RowsList")!.ContainerFromIndex(visible.IndexOf(row))!).Child!;
    var child = g.Children.First(ch => Grid.GetColumn(ch) == column + 1 && ch.GetType().Name != "CellOverlay");
    return child as TextBox ?? child.GetVisualDescendants().OfType<TextBox>().First();
}
void Click(Control c)
{
    var p = c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), window)!.Value;
    window.MouseMove(p); window.MouseDown(p, MouseButton.Left); window.MouseUp(p, MouseButton.Left);
    Pump(60);
}
void PressKey(Key key, RawInputModifiers mods = RawInputModifiers.None)
{
    window.KeyPress(key, mods, PhysicalKey.None, "");
    window.KeyRelease(key, mods, PhysicalKey.None, "");
    Pump(60);
}
void Pump(int ms)
{
    var sw = Stopwatch.StartNew();
    do
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
        Thread.Sleep(5);
    } while (sw.ElapsedMilliseconds < ms);
}
List<string> ForeignRuns(Visual root)
{
    var bad = new List<string>();
    foreach (var v in root.GetVisualDescendants().Prepend(root))
    {
        if (v is not TextBlock { Text.Length: > 0 } tb || !tb.IsEffectivelyVisible) continue;
        foreach (var line in tb.TextLayout.TextLines)
            foreach (var r in line.TextRuns)
                if (r is Avalonia.Media.TextFormatting.ShapedTextRun sr && sr.GlyphRun.GlyphTypeface.FamilyName != "Noto Sans Thai"
                    && new string(sr.Text.Span).EnumerateRunes().Any(Rune.IsLetterOrDigit))
                    bad.Add(sr.GlyphRun.GlyphTypeface.FamilyName + "[" + new string(sr.Text.Span) + "]");
    }
    return bad;
}
void SaveFrame(string name)
{
    try
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
        var frame = window.CaptureRenderedFrame() ?? window.GetLastRenderedFrame();
        if (frame == null) return;
        var outDir = Environment.GetEnvironmentVariable("SHOTS") ?? Path.GetTempPath();
        Directory.CreateDirectory(outDir);
        frame.Save(Path.Combine(outDir, name + ".png"));
    }
    catch (Exception ex) { Console.WriteLine("screenshot skipped: " + ex.Message); }
}
