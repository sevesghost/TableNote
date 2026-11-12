// v2.4.2 NotifyTests: run with `dotnet run -c Release --project tests/NotifyTests` (no display needed).
// Reproduces "several rows due in the same minute → popup + sound + toast together" and the popup's
// re-entrant / failing callbacks with fakes, and checks nothing blocks the UI thread or escapes to the dispatcher.
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using TableNote;

var dir = Path.Combine(Path.GetTempPath(), "tn-notify-" + Guid.NewGuid().ToString("N")[..8]);
Directory.CreateDirectory(dir);
Environment.SetEnvironmentVariable("TABLENOTE_DATA", Path.Combine(dir, "note.json"));
AppLog.PathOverride = Path.Combine(dir, "tablenote.log");

AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions()).SetupWithoutStarting();
var uiUnhandled = new List<Exception>();
CrashLog.Install();
// Count what reaches the dispatcher (CrashLog marks it handled; a test must still see it).
Dispatcher.UIThread.UnhandledException += (_, e) => uiUnhandled.Add(e.Exception);

int fail = 0, pass = 0;
void Ok(bool c, string n) { Console.WriteLine((c ? "PASS " : "FAIL ") + n); if (c) pass++; else fail++; }
void Pump(int ms) { var sw = Stopwatch.StartNew(); do { Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); } while (sw.ElapsedMilliseconds < ms); }
string Log() => File.Exists(AppLog.PathOverride) ? File.ReadAllText(AppLog.PathOverride) : "";
void ClearLog() { try { File.Delete(AppLog.PathOverride!); } catch { } }
List<DueTask> Make(int n, string p) => Enumerable.Range(0, n).Select(i => new DueTask(i, p + i, "Task " + p + i, "", DateTime.Now.AddSeconds(-5))).ToList();
List<Button> Buttons() => ReminderPopup.CurrentWindow?.GetLogicalDescendants().OfType<Button>().ToList() ?? [];
void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

int sounds = 0, toasts = 0;
void Reset()
{
    ReminderPopup.CloseSilently(); Pump(30);
    ReminderPopup.ResetForTests(); WindowsToast.ResetForTests(); NotificationSound.ResetForTests();
    ReminderPopup.WorkAreaHeight = _ => 900;
    NotificationSound.PlayOverride = () => Interlocked.Increment(ref sounds);
    WindowsToast.ShowCoreOverride = _ => Interlocked.Increment(ref toasts);
    WindowsToast.StuckAfter = TimeSpan.FromSeconds(20);
    sounds = 0; toasts = 0; uiUnhandled.Clear(); ClearLog();
}
var released = new HashSet<string>();
void Show(IReadOnlyList<DueTask> tasks, Action<DueTask>? done = null, Action<DueTask>? snooze = null, Action<DueTask>? ack = null, Action<IReadOnlyList<DueTask>>? onX = null)
    => ReminderPopup.ShowBatch(tasks, snooze ?? (_ => { }), done ?? (_ => { }), ack ?? (_ => { }), onX ?? (_ => { }), () => { }, null, id => released.Add(id));

// ── 1. 5 and 30 rows due in the same minute: ONE window, one sound, one toast ──
foreach (var n in new[] { 5, 30 })
{
    Reset();
    var sw = Stopwatch.StartNew();
    Show(Make(n, $"same{n}-"));
    var ms = sw.ElapsedMilliseconds;
    Pump(150);
    Ok(ms < 2000, $"{n} rows: ShowBatch returned in {ms} ms");
    Ok(ReminderPopup.CurrentWindow is { IsVisible: true } && ReminderPopup.OpenRowCount == n, $"{n} rows: one visible window with {ReminderPopup.OpenRowCount} rows");
    Ok(sounds == 1 && toasts == 1, $"{n} rows: sound={sounds} toast={toasts} (1 each)");
    Ok(ReminderPopup.CurrentWindow!.Height <= 900, $"{n} rows: window height {ReminderPopup.CurrentWindow!.Height} fits the work area");
}

// ── 2. the same minute arriving as 5 separate batches (per-row CheckNow) before the window is shown ──
Reset();
for (int b = 0; b < 5; b++) Show(Make(1, $"seq{b}-"));
Pump(150);
Ok(ReminderPopup.OpenRowCount == 5, $"5 batches in one tick → one window, {ReminderPopup.OpenRowCount} rows");
Ok(sounds == 1, $"5 batches in one tick → one sound (got {sounds})");
Ok(toasts <= 5 && toasts >= 1, $"5 batches in one tick → toasts={toasts} (never in parallel, see 3)");

// ── 3. toast never returns (COM / shell hang): UI never waits, no pile-up, turned off after StuckAfter ──
Reset();
var gate = new ManualResetEventSlim(false); int toastCalls = 0;
WindowsToast.ShowCoreOverride = _ => { Interlocked.Increment(ref toastCalls); gate.Wait(); };
WindowsToast.StuckAfter = TimeSpan.FromMilliseconds(300);
var sw3 = Stopwatch.StartNew();
for (int b = 0; b < 10; b++) Show(Make(5, $"hang{b}-"));
Ok(sw3.ElapsedMilliseconds < 2000, $"blocking toast: 10 × ShowBatch(5 rows) returned in {sw3.ElapsedMilliseconds} ms");
Pump(400);
Show(Make(1, "hang-late-"));
Pump(100);
Ok(Volatile.Read(ref toastCalls) == 1, $"blocking toast: only one toast call in flight (got {toastCalls})");
Ok(WindowsToast.IsDisabled && Log().Contains("toasts are off"), "blocking toast: turned off for the session and logged");
Ok(ReminderPopup.CurrentWindow is { IsVisible: true } && ReminderPopup.OpenRowCount == 51, $"blocking toast: reminder window still shows all rows ({ReminderPopup.OpenRowCount})");
gate.Set(); Pump(100);

// ── 4. toast throws ──
Reset();
WindowsToast.ShowCoreOverride = _ => throw new InvalidOperationException("Failed initializing notifications");
var sw4 = Stopwatch.StartNew(); Show(Make(5, "tthrow-")); Pump(200);
Ok(sw4.ElapsedMilliseconds < 2000 && ReminderPopup.OpenRowCount == 5, "throwing toast: window shown, returned promptly");
Ok(Log().Contains("toast: failed: InvalidOperationException"), "throwing toast: logged to tablenote.log");
Ok(uiUnhandled.Count == 0, "throwing toast: nothing reached the dispatcher");

// ── 5. sound throws / blocks ──
Reset();
NotificationSound.PlayOverride = () => throw new System.ComponentModel.Win32Exception(5, "beep failed");
Show(Make(5, "sthrow-")); Pump(200);
Ok(ReminderPopup.OpenRowCount == 5 && Log().Contains("sound: failed"), "throwing sound: window shown, logged");
Reset();
var sgate = new ManualResetEventSlim(false); int soundCalls = 0;
NotificationSound.PlayOverride = () => { Interlocked.Increment(ref soundCalls); sgate.Wait(); };
NotificationSound.MinGap = TimeSpan.Zero;
var sw5 = Stopwatch.StartNew(); for (int b = 0; b < 10; b++) Show(Make(3, $"sblock{b}-")); var ms5 = sw5.ElapsedMilliseconds; Pump(100);
Ok(ms5 < 2000 && soundCalls == 1, $"blocking sound: returned in {ms5} ms, one call in flight (got {soundCalls})");
sgate.Set(); Pump(50); NotificationSound.MinGap = TimeSpan.FromSeconds(2);

// ── 6. MainWindow callbacks throw (save failed, etc.): every row still handled, app keeps running ──
Reset(); released.Clear();
var tasks6 = Make(6, "cbthrow-");
Show(tasks6, done: _ => throw new IOException("done"), snooze: _ => throw new IOException("snooze"),
     ack: _ => throw new IOException("ack"), onX: _ => throw new IOException("snooze on X"));
Pump(100);
var b6 = Buttons(); Click(b6[0]); Pump(30); // snooze row 0
b6 = Buttons(); Click(b6[1]); Pump(30);     // done row 1
b6 = Buttons(); Click(b6[2]); Pump(30);     // ack row 2
Ok(ReminderPopup.OpenRowCount == 3, $"throwing callbacks: rows removed after clicks ({ReminderPopup.OpenRowCount} left)");
ReminderPopup.CurrentWindow!.Close(); Pump(100); // X with 3 rows → snooze-on-X throws
Ok(ReminderPopup.CurrentWindow == null, "throwing callbacks: X closes the window");
Ok(uiUnhandled.Count == 0, $"throwing callbacks: nothing reached the dispatcher ({uiUnhandled.Count})");
Ok(tasks6.All(t => released.Contains(t.RowId)), "throwing callbacks: every row released back to the reminder service");
Ok(Log().Contains("popup: snooze on X failed: IOException") && Log().Contains("popup: Done failed"), "throwing callbacks: logged to tablenote.log");
Reset(); released.Clear();
Show(Make(5, "closeall-"), ack: _ => throw new IOException("ack"));
Pump(100); Click(Buttons().Last()); Pump(100);
Ok(ReminderPopup.CurrentWindow == null && uiUnhandled.Count == 0, "throwing ack: 'close (acknowledge all)' still closes, no crash");

// ── 7. re-entry: Done → ApplyDone → CheckNow → a NEW batch while the popup's own handler runs ──
foreach (var n in new[] { 5, 30 })
{
    Reset(); released.Clear();
    int nested = 0;
    Action<DueTask> doneNested = null!;
    Action<IReadOnlyList<DueTask>> onXNested = r => Show(Make(2, $"afterX{n}-"));
    // like ApplyDone → _reminders.CheckNow() (MainWindow always passes the same callbacks)
    doneNested = t => Show(Make(1, $"nested{n}-{nested++}-"), done: doneNested, onX: onXNested);
    Show(Make(n, $"re{n}-"), done: doneNested, onX: onXNested);
    Pump(100);
    for (int i = 0; i < n; i++) { var bb = Buttons(); if (bb.Count < 2) break; Click(bb[1]); Pump(5); }
    Pump(100);
    Ok(uiUnhandled.Count == 0, $"re-entry {n} rows: Done on every row with a nested batch each time, nothing escaped");
    Ok(ReminderPopup.CurrentWindow is { IsVisible: true } && ReminderPopup.OpenRowCount == n && nested == n, $"re-entry {n} rows: one window, each click removed its row and added the nested one ({ReminderPopup.OpenRowCount} open)");
    ReminderPopup.CurrentWindow!.Close(); Pump(150);
    Ok(uiUnhandled.Count == 0 && ReminderPopup.CurrentWindow is { IsVisible: true } && ReminderPopup.OpenRowCount == 2,
        $"re-entry {n} rows: batch raised from the X-close handler opens a fresh window ({ReminderPopup.OpenRowCount} rows)");
}
// last row Done + nested batch: the window must not close under the new row
Reset();
Show(Make(1, "last-"), done: _ => Show(Make(1, "last-nested-")));
Pump(100); Click(Buttons()[1]); Pump(100);
Ok(ReminderPopup.CurrentWindow is { IsVisible: true } && ReminderPopup.OpenRowCount == 1 && uiUnhandled.Count == 0, "re-entry on the last row: nested task shows (no closing-window access)");

// ── 8. screen size unavailable before the window is shown (ReminderPopup.cs:162 in 2.4.1) ──
Reset();
ReminderPopup.WorkAreaHeight = _ => throw new NullReferenceException("Screens");
Show(Make(5, "scr-")); Pump(100);
Ok(ReminderPopup.CurrentWindow is { IsVisible: true } && ReminderPopup.OpenRowCount == 5 && Log().Contains("screen size unavailable"), "screen query throws: window still shows with default size");

// ── 9. app quitting: a deferred batch is dropped (no window after quit) and its rows released ──
Reset(); released.Clear();
Show(Make(2, "quit-"), done: _ => { Show(Make(1, "quit-deferred-")); ReminderPopup.CloseSilently(); });
Pump(100); Click(Buttons()[1]); Pump(100);
Ok(ReminderPopup.CurrentWindow == null && released.Contains("quit-deferred-0"), "quit during a click: deferred batch dropped and released");

// ── 10. CrashLog ──
Reset();
Dispatcher.UIThread.Post(() => throw new InvalidOperationException("boom from a UI handler"));
Pump(100);
Ok(uiUnhandled.Count == 1 && Log().Contains("crash: UI (kept running") && Log().Contains("boom from a UI handler"), "CrashLog: UI-thread exception logged and handled");
ClearLog();
_ = Task.Run(() => throw new ApplicationException("lost task"));
Thread.Sleep(200); GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); Thread.Sleep(100);
Ok(Log().Contains("crash: background task: ApplicationException: lost task"), "CrashLog: unobserved task exception logged");
ClearLog(); CrashLog.OnUnhandled(new IOException("fatal one"), true);
Ok(Log().Contains("crash: fatal (app ends): IOException: fatal one"), "CrashLog: AppDomain unhandled exception logged");

// ── 11. whole app (MainWindow): 5 and 30 rows due in the same minute, click through the popup ──
foreach (var n in new[] { 5, 30 })
{
    Reset();
    WindowsToast.ShowCoreOverride = null; NotificationSound.PlayOverride = null; // real code paths (no toast on Linux)
    var d = Path.Combine(dir, "app" + n); Directory.CreateDirectory(d);
    Environment.SetEnvironmentVariable("TABLENOTE_DATA", Path.Combine(d, "note.json"));
    var doc = NoteDocument.CreateDefault(); doc.Rows.Clear();
    var due = DueDates.Format(DateTime.Now);
    for (int i = 0; i < n; i++) { var r = Enumerable.Repeat("", doc.Columns.Count).ToList(); r[0] = "Task " + i; r[doc.DueColumnIndex()] = due; doc.Rows.Add(r); }
    doc.EnsureRowIds(); doc.Window.Layer = DesktopLayerKind.Window;
    NoteStore.Save(Path.Combine(d, "note.json"), doc);
    var mw = new MainWindow(); mw.Show();
    var t0 = Stopwatch.StartNew();
    while (ReminderPopup.CurrentWindow == null && t0.ElapsedMilliseconds < 15000) Pump(50);
    Ok(ReminderPopup.OpenRowCount == n, $"app {n} rows: one reminder window with {ReminderPopup.OpenRowCount} rows after {t0.ElapsedMilliseconds / 1000.0:0.0} s");
    int clicks = 0;
    while (ReminderPopup.CurrentWindow != null && clicks < n - 2) { var bb = Buttons(); Click(bb[clicks % 3]); clicks++; Pump(20); } // snooze/done/ack (Done → Rebuild + CheckNow)
    ReminderPopup.CurrentWindow?.Close(); Pump(300); // X on the rest
    Ok(uiUnhandled.Count == 0 && ReminderPopup.CurrentWindow == null, $"app {n} rows: clicked {clicks} buttons + X, no exception, popup closed");
    ReminderPopup.CloseSilently(); mw.Close(); Pump(50);
}

Console.WriteLine(fail == 0 ? $"NotifyTests: all {pass} passed" : $"NotifyTests: {fail} failed, {pass} passed");
Environment.Exit(fail == 0 ? 0 : 1);
