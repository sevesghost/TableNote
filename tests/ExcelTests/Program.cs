using System.Diagnostics;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TableNote;

// 2.6.0 Excel-style cells in the real window (headless, Thai).
var fail = 0;
void Ok(bool cond, string name, string detail = "")
{
    Console.WriteLine((cond ? "PASS " : "FAIL ") + name + (cond || detail.Length == 0 ? "" : " — " + detail));
    if (!cond) fail++;
}

var dir = Directory.CreateTempSubdirectory("tn-excel").FullName;
var note = Path.Combine(dir, "note.json");
Environment.SetEnvironmentVariable("TABLENOTE_DATA", note);
File.WriteAllText(Path.Combine(dir, "settings.json"), "{ \"language\": \"th\", \"uiScale\": 100 }");
AppLog.PathOverride = Path.Combine(dir, "tablenote.log");
Localizer.SetLanguage("th");
var seed = new NoteDocument
{
    Columns = ["ราย", "NID", "ตรวจ", "สถานะ"],
    ColumnRolesList = ["title", "", "", "status"],
    DueColumnOmitted = true,
    ColumnWidths = [260, 150, 150, 120],
    TrayNoticeShown = true
};
seed.Rows.Add(["บริษัท ทีเอชที จำกัด", "0105557185479", "ตรวจเฉพาะประเด็น", ""]);
seed.Rows.Add(["บริษัท เวิลด์ คลาส จำกัด", "0105548094300", "วิเคราะห์แบบ", "doing"]);
seed.Rows.Add(["บริษัท จีอีที จำกัด", "0105565111721", "วิเคราะห์แบบ", ""]);
seed.Rows.Add(["บริษัท สเตชั่น โซน จำกัด", "0125556027527", "เลิก (non vat)", ""]);
seed.EnsureRowIds();
seed.EnsureColumnIds();
seed.Window.Width = 900; seed.Window.Height = 520;
NoteStore.Save(note, seed);

AppBuilder.Configure<App>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .With(AppFonts.ManagerOptions)
    .SetupWithoutStarting();

var window = new MainWindow { Width = 900, Height = 520 };
window.Show();
Pump(400);
var session = typeof(MainWindow).GetField("_session", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
NoteDocument Doc() => (NoteDocument)session.GetType().GetProperty("Doc")!.GetValue(session)!;
int P(string name) => (int)session.GetType().GetProperty(name)!.GetValue(session)!;
bool HasSel() => (bool)session.GetType().GetProperty("HasSelection")!.GetValue(session)!;
var nameBox = window.FindControl<TextBlock>("NameBox")!;
var bar = window.FindControl<TextBox>("FormulaBox")!;

Ok(window.Icon != null, "the window carries the Table Note icon");
Ok(HasSel() && P("AnchorRow") == 0 && P("AnchorCol") == 0 && nameBox.Text == "A1", "the editor opens with A1 selected", nameBox.Text ?? "");
Ok(bar.Text == "บริษัท ทีเอชที จำกัด", "the formula bar shows A1's text", bar.Text ?? "");

// ── click selects; typing replaces; Esc puts it back ──
var b2 = Cell(1, 1);
Click(b2);
Ok(P("AnchorRow") == 1 && P("AnchorCol") == 1 && nameBox.Text == "B2" && b2.IsReadOnly && b2.IsFocused,
    "a click selects the cell (no caret, nothing typed yet)", $"{nameBox.Text} ro={b2.IsReadOnly} focus={b2.IsFocused}");
Ok(b2.Classes.Contains("selected"), "the selected cell is filled");
window.KeyTextInput("999");
Pump(50);
Ok(Doc().GetCell(1, 1) == "999" && !b2.IsReadOnly, "typing replaces the cell, like Excel", Doc().GetCell(1, 1));
Ok(bar.Text == "999", "the formula bar follows the typing", bar.Text ?? "");
Key(Avalonia.Input.Key.Escape);
Ok(Doc().GetCell(1, 1) == "0105548094300" && b2.IsReadOnly, "Esc puts the text back", Doc().GetCell(1, 1));

// ── double-click edits the text that is there; Enter keeps it and goes down ──
Pump(700); // the earlier click on this cell must not count as the first click of the double-click
DoubleClick(b2);
Ok(!b2.IsReadOnly && b2.CaretIndex == (b2.Text ?? "").Length, "double-click edits with the caret at the end", $"ro={b2.IsReadOnly} caret={b2.CaretIndex}");
window.KeyTextInput("-1");
Key(Avalonia.Input.Key.Return);
Ok(Doc().GetCell(1, 1) == "0105548094300-1" && P("AnchorRow") == 2 && P("AnchorCol") == 1, "Enter keeps the edit and moves down",
    $"{Doc().GetCell(1, 1)} → row {P("AnchorRow")}");
Key(Avalonia.Input.Key.Z, RawInputModifiers.Control);
Ok(Doc().GetCell(1, 1) == "0105548094300", "Ctrl+Z undoes the edit", Doc().GetCell(1, 1));

// ── F2, arrows, Tab, Shift+arrows ──
Click(Cell(0, 0));
Key(Avalonia.Input.Key.F2);
Ok(!Cell(0, 0).IsReadOnly, "F2 edits the selected cell");
Key(Avalonia.Input.Key.Escape);
Key(Avalonia.Input.Key.Right);
Ok(P("AnchorRow") == 0 && P("AnchorCol") == 1, "→ moves to the next cell", $"{P("AnchorRow")},{P("AnchorCol")}");
Key(Avalonia.Input.Key.Down);
Key(Avalonia.Input.Key.Tab);
Ok(P("AnchorRow") == 1 && P("AnchorCol") == 2, "↓ and Tab move like Excel", $"{P("AnchorRow")},{P("AnchorCol")}");
Key(Avalonia.Input.Key.Down, RawInputModifiers.Shift);
Key(Avalonia.Input.Key.Left, RawInputModifiers.Shift);
Ok(P("AnchorRow") == 1 && P("AnchorCol") == 2 && P("ActiveRow") == 2 && P("ActiveCol") == 1 && nameBox.Text == "B2:C3",
    "Shift+arrows grow a block; the active cell stays", nameBox.Text ?? "");

// ── drag a block, copy, delete, undo ──
Drag(Cell(0, 0), Cell(2, 2));
Ok(P("AnchorRow") == 0 && P("AnchorCol") == 0 && P("ActiveRow") == 2 && P("ActiveCol") == 2 && nameBox.Text == "A1:C3",
    "dragging selects a block", nameBox.Text ?? "");
Ok(Enumerable.Range(0, 3).All(r => Enumerable.Range(0, 3).All(c => Cell(r, c).Classes.Contains("selected"))) &&
   !Cell(3, 0).Classes.Contains("selected"), "every cell of the block is filled, the others not");
Key(Avalonia.Input.Key.C, RawInputModifiers.Control);
Pump(100);
var clip = ClipboardText();
Ok(clip.Replace("\r\n", "\n").TrimEnd('\n') ==
   "บริษัท ทีเอชที จำกัด\t0105557185479\tตรวจเฉพาะประเด็น\nบริษัท เวิลด์ คลาส จำกัด\t0105548094300\tวิเคราะห์แบบ\nบริษัท จีอีที จำกัด\t0105565111721\tวิเคราะห์แบบ",
   "Ctrl+C copies the block tab-separated (pastes into Excel)", clip);
Key(Avalonia.Input.Key.Delete);
Ok(Enumerable.Range(0, 3).All(r => Enumerable.Range(0, 3).All(c => Doc().GetCell(r, c) == "")) && Doc().GetCell(3, 0) != "",
    "Delete clears the block only");
Ok(HasSel() && P("ActiveRow") == 2 && P("ActiveCol") == 2, "the block stays selected after Delete");
Key(Avalonia.Input.Key.Z, RawInputModifiers.Control);
Ok(Doc().GetCell(0, 0) == "บริษัท ทีเอชที จำกัด" && Doc().GetCell(2, 2) == "วิเคราะห์แบบ", "one Ctrl+Z brings the whole block back");

// ── paste: one value fills the block; a block from Excel goes in from the top-left ──
Drag(Cell(1, 2), Cell(2, 2));
SetClipboard("ปิดงาน");
Key(Avalonia.Input.Key.V, RawInputModifiers.Control);
Pump(150);
Ok(Doc().GetCell(1, 2) == "ปิดงาน" && Doc().GetCell(2, 2) == "ปิดงาน" && Doc().GetCell(0, 2) == "ตรวจเฉพาะประเด็น",
    "a single value pasted fills every selected cell", $"{Doc().GetCell(1, 2)} / {Doc().GetCell(2, 2)}");
Click(Cell(2, 0));
SetClipboard("X1\tX2\nY1\tY2\n");
Key(Avalonia.Input.Key.V, RawInputModifiers.Control);
Pump(150);
Ok(Doc().GetCell(2, 0) == "X1" && Doc().GetCell(2, 1) == "X2" && Doc().GetCell(3, 0) == "Y1" && Doc().GetCell(3, 1) == "Y2",
    "a block copied from Excel pastes from the selected cell", $"{Doc().GetCell(2, 0)} {Doc().GetCell(3, 1)}");
Pump(700); // not a double-click with the click before the paste
Drag(Cell(2, 0), Cell(3, 1));
Key(Avalonia.Input.Key.X, RawInputModifiers.Control);
Pump(150);
Ok(Doc().GetCell(2, 0) == "" && Doc().GetCell(3, 1) == "" && ClipboardText().StartsWith("X1\tX2", StringComparison.Ordinal),
    "Ctrl+X copies and clears", ClipboardText());
Key(Avalonia.Input.Key.Z, RawInputModifiers.Control);
Key(Avalonia.Input.Key.Z, RawInputModifiers.Control);
Key(Avalonia.Input.Key.Z, RawInputModifiers.Control);
Ok(Doc().GetCell(2, 0) == "บริษัท จีอีที จำกัด" && Doc().GetCell(1, 2) == "วิเคราะห์แบบ", "undo takes back cut, paste and fill",
    $"{Doc().GetCell(2, 0)} / {Doc().GetCell(1, 2)}");

// ── status cells: arrows move, they do not change the status ──
Click(Cell(1, 2));
Key(Avalonia.Input.Key.Right);
Key(Avalonia.Input.Key.Down);
Key(Avalonia.Input.Key.Up);
Ok(Doc().GetCell(1, 3) == "doing" && P("AnchorRow") == 1 && P("AnchorCol") == 3, "arrows over a status cell move, the status stays",
    $"{Doc().GetCell(1, 3)} at {P("AnchorRow")},{P("AnchorCol")}");

// ── select all and clear selection ──
var corner = window.FindControl<Grid>("TableGrid")!.Children.OfType<Border>().First();
ClickAt(corner);
Ok(P("AnchorRow") == 0 && P("AnchorCol") == 0 && P("ActiveRow") == 3 && P("ActiveCol") == 3 && nameBox.Text == "A1:D4",
    "the top-left corner selects every cell", nameBox.Text ?? "");
Key(Avalonia.Input.Key.Escape);
Key(Avalonia.Input.Key.A, RawInputModifiers.Control);
Ok(nameBox.Text == "A1:D4", "Ctrl+A selects every cell too", nameBox.Text ?? "");
var rows = window.FindControl<ItemsControl>("RowsList")!;
var empty = new Point(rows.Bounds.Width / 2, rows.Bounds.Height - 6);
var at = rows.TranslatePoint(empty, window)!.Value;
window.MouseDown(at, MouseButton.Left);
window.MouseUp(at, MouseButton.Left);
Pump(80);
Ok(!HasSel() && !Cell(0, 0).Classes.Contains("selected") && nameBox.Text == "", "a click on an empty spot clears the selection");

// ── formula bar ──
Click(Cell(3, 2));
ClickAt(bar);
bar.Text = "แก้จากแถบสูตร";
Key(Avalonia.Input.Key.Return);
Ok(Doc().GetCell(3, 2) == "แก้จากแถบสูตร" && Cell(3, 2).IsFocused, "Enter in the formula bar writes the cell and goes back to it", Doc().GetCell(3, 2));
Key(Avalonia.Input.Key.Z, RawInputModifiers.Control);
Ok(Doc().GetCell(3, 2) == "เลิก (non vat)", "the formula-bar edit is one undo step", Doc().GetCell(3, 2));
Click(CellControl(1, 3));
Pump(50);
if (CellControl(1, 3) is ComboBox open) open.IsDropDownOpen = false;
Ok(bar.IsReadOnly && bar.Text == "กำลังทำ", "a status cell shows its label in the bar (edited with its list)", bar.Text ?? "");

// ── find ──
Click(Cell(0, 0));
Key(Avalonia.Input.Key.F, RawInputModifiers.Control);
var findBar = window.FindControl<Border>("FindBar")!;
var findBox = window.FindControl<TextBox>("FindBox")!;
var count = window.FindControl<TextBlock>("FindCount")!;
Ok(findBar.IsVisible && findBox.IsFocused, "Ctrl+F opens the find bar");
findBox.Text = "วิเคราะห์";
Pump(80);
Ok(count.Text == "1 / 2" && P("AnchorRow") == 1 && P("AnchorCol") == 2, "matches are counted and the first one is selected",
    $"{count.Text} at {P("AnchorRow")},{P("AnchorCol")}");
Key(Avalonia.Input.Key.Return);
Ok(count.Text == "2 / 2" && P("AnchorRow") == 2, "Enter goes to the next match", $"{count.Text} at {P("AnchorRow")}");
Key(Avalonia.Input.Key.Return, RawInputModifiers.Shift);
Ok(count.Text == "1 / 2" && P("AnchorRow") == 1, "Shift+Enter goes back", count.Text ?? "");
Pump(700);
Click(Cell(0, 1));
window.KeyTextInput("วิเคราะห์ใหม่");
Key(Avalonia.Input.Key.Return);
Pump(60);
Ok(findBar.IsVisible && count.Text?.EndsWith("/ 3") == true && Cell(0, 1).Classes.Contains("match"),
    "an edit while the find bar is open: the marks follow", $"{count.Text} classes={string.Join(",", Cell(0, 1).Classes)}");
Key(Avalonia.Input.Key.Z, RawInputModifiers.Control);
Pump(60);
Ok(Doc().GetCell(0, 1) == "0105557185479" && count.Text?.EndsWith("/ 2") == true && !Cell(0, 1).Classes.Contains("match"),
    "undo: the marks follow again", $"{Doc().GetCell(0, 1)} {count.Text}");
Ok(Cell(1, 2).Classes.Contains("match") && Cell(2, 2).Classes.Contains("match"), "the other matches stay marked",
    $"sel {P("AnchorRow")},{P("AnchorCol")}");
findBox.Text = "กำลังทำ";
Pump(60);
Ok(count.Text == "1 / 1" && P("AnchorCol") == 3, "a status is found by its label", count.Text ?? "");
findBox.Text = "ไม่มีคำนี้";
Pump(60);
Ok(count.Text == "ไม่พบ", "no match says so", count.Text ?? "");
Key(Avalonia.Input.Key.Escape);
Ok(!findBar.IsVisible, "Esc closes the find bar");

SaveFrame("excel-block");
window.Close();
Console.WriteLine(fail == 0 ? "ExcelTests: all passed" : $"ExcelTests: {fail} failed");
return fail;

// ───────── helpers ─────────
Control CellControl(int row, int column)
{
    Pump(10);
    foreach (var c in window.FindControl<ItemsControl>("RowsList")!.GetRealizedContainers())
        if (c is ContentPresenter { Child: Grid { Tag: int r } g } && r == row)
            return g.Children.First(ch => Grid.GetColumn(ch) == column + 1 && ch.GetType().Name != "CellOverlay");
    throw new InvalidOperationException($"row {row} not on screen");
}

TextBox Cell(int row, int column)
{
    Pump(10);
    foreach (var c in window.FindControl<ItemsControl>("RowsList")!.GetRealizedContainers())
        if (c is ContentPresenter { Child: Grid { Tag: int r } g } && r == row)
        {
            var child = g.Children.First(ch => Grid.GetColumn(ch) == column + 1 && ch.GetType().Name != "CellOverlay");
            return child as TextBox ?? child.GetVisualDescendants().OfType<TextBox>().First();
        }
    throw new InvalidOperationException($"row {row} not on screen");
}

Point Center(Control c) => c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), window) ?? throw new InvalidOperationException("no point");
void Click(Control c) { var p = Center(c); window.MouseMove(p); window.MouseDown(p, MouseButton.Left); window.MouseUp(p, MouseButton.Left); Pump(60); }
void ClickAt(Control c) => Click(c);
void DoubleClick(Control c)
{
    var p = Center(c);
    window.MouseDown(p, MouseButton.Left); window.MouseUp(p, MouseButton.Left);
    window.MouseDown(p, MouseButton.Left); window.MouseUp(p, MouseButton.Left);
    Pump(60);
}
void Drag(Control from, Control to)
{
    var a = Center(from); var b = Center(to);
    window.MouseMove(a); window.MouseDown(a, MouseButton.Left);
    for (var i = 1; i <= 6; i++) window.MouseMove(new Point(a.X + (b.X - a.X) * i / 6, a.Y + (b.Y - a.Y) * i / 6), RawInputModifiers.LeftMouseButton);
    window.MouseUp(b, MouseButton.Left);
    Pump(60);
}
void Key(Avalonia.Input.Key key, RawInputModifiers mods = RawInputModifiers.None)
{
    window.KeyPress(key, mods, PhysicalKey.None, "");
    window.KeyRelease(key, mods, PhysicalKey.None, "");
    Pump(60);
}
string ClipboardText()
{
    var t = window.Clipboard!.TryGetTextAsync();
    var sw = Stopwatch.StartNew();
    while (!t.IsCompleted && sw.ElapsedMilliseconds < 2000) Pump(10);
    return t.Result ?? "";
}
void SetClipboard(string text)
{
    var t = window.Clipboard!.SetTextAsync(text);
    var sw = Stopwatch.StartNew();
    while (!t.IsCompleted && sw.ElapsedMilliseconds < 2000) Pump(10);
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
