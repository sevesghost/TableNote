using System.Diagnostics;
using System.Reflection;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TableNote;

// 2.6.0: whole rows / columns (select, insert, delete, insert copied, move), Undo / Redo in the menu, Close vs Exit,
// and the Help window in topics. Core first, then the real window (headless, Thai).
var fail = 0;
void Ok(bool cond, string name, string detail = "")
{
    Console.WriteLine((cond ? "PASS " : "FAIL ") + name + (cond || detail.Length == 0 ? "" : " — " + detail));
    if (!cond) fail++;
}

var dir = Directory.CreateTempSubdirectory("tn-rowcol").FullName;
var note = Path.Combine(dir, "note.json");
Environment.SetEnvironmentVariable("TABLENOTE_DATA", note);
File.WriteAllText(Path.Combine(dir, "settings.json"), "{ \"language\": \"th\", \"uiScale\": 100 }");
AppLog.PathOverride = Path.Combine(dir, "tablenote.log");
Localizer.SetLanguage("th");

NoteDocument Sample(int rows)
{
    var d = new NoteDocument
    {
        Columns = ["ราย", "NID", "ตรวจ", "สถานะ"],
        ColumnRolesList = ["title", "", "", "status"],
        DueColumnOmitted = true,
        ColumnWidths = [220, 150, 150, 120],
        TrayNoticeShown = true
    };
    for (var i = 0; i < rows; i++) d.Rows.Add([$"บริษัท {i + 1}", $"N{i + 1}", i % 2 == 0 ? "วิเคราะห์แบบ" : "ตรวจเฉพาะประเด็น", i == 1 ? "doing" : ""]);
    d.EnsureRowIds();
    d.EnsureColumnIds();
    return d;
}
string Col(NoteDocument d, int c) => string.Join(",", d.Rows.Select(r => r.ElementAtOrDefault(c) ?? ""));

// ───────────────────────── core ─────────────────────────
{
    var d = Sample(4);
    var ids = d.RowIds.ToList();
    Ok(TableEdits.InsertRows(d, 1, 2) && d.Rows.Count == 6 && Col(d, 0) == "บริษัท 1,,,บริษัท 2,บริษัท 3,บริษัท 4" &&
       d.RowIds[0] == ids[0] && d.RowIds[3] == ids[1] && d.RowIds.Distinct().Count() == 6,
        "insert 2 rows at row 2: blank rows with their own ids, the rest move down", Col(d, 0));
    Ok(TableEdits.InsertRows(d, 0, 1) && d.Rows[0].All(c => c == "") && TableEdits.InsertRows(d, d.Rows.Count, 1) && d.Rows[^1].All(c => c == ""),
        "insert at the top and after the last row");
    var full = Sample(TableLimits.MaxRows - 1);
    Ok(!TableEdits.InsertRows(full, 0, 2) && full.Rows.Count == TableLimits.MaxRows - 1 && TableEdits.InsertRows(full, 0, 1),
        "past the row limit nothing is inserted");
    var r = Sample(5);
    Ok(TableEdits.RemoveRows(r, [3, 0, 3, 1]) == 3 && Col(r, 0) == "บริษัท 3,บริษัท 5", "remove rows in any order (duplicates ignored)", Col(r, 0));

    var c = Sample(2);
    var newIds = TableEdits.InsertColumns(c, 1, 2);
    Ok(newIds is { Count: 2 } && c.Columns.Count == 6 && c.Columns[0] == "ราย" && c.Columns[3] == "NID" && c.ColumnIds[1] == newIds[0] &&
       c.Rows.All(row => row.Count == 6 && row[1] == "" && row[2] == "") && c.StatusColumnIndex() == 5,
        "insert 2 columns at B: empty, the others move right, status still found", string.Join("|", c.Columns));
    Ok(TableEdits.UniqueName(c, "NID") == "NID (2)" && TableEdits.UniqueName(c, "ใหม่") == "ใหม่", "copied names get (2) when taken");
    var wide = Sample(1);
    while (wide.Columns.Count < NoteDocument.MaxUserColumns) wide.AddColumn();
    Ok(TableEdits.InsertColumns(wide, 0, 1) == null && wide.Columns.Count == NoteDocument.MaxUserColumns, "past the column limit nothing is inserted");
    var rc = Sample(1);
    Ok(TableEdits.RemoveColumns(rc, [0, 1, 2, 3]) == 3 && rc.Columns.Count == 1, "removing every column keeps one");

    // copies of rows: cells by column id, row colour, repeat, advance reminders, height
    var s = Sample(3);
    var src = s.RowIdAt(0);
    s.RowStyles[src] = new RowStyle { Color = "orange", Highlight = true };
    s.Recurrence[src] = Recurrence.Weekly;
    ReminderPlan.Info(s, src).Offsets = [15, 60];
    (s.RowHeights ??= new())[src] = 48;
    var clip = TableEdits.CopyRows(s, [0], "บริษัท 1\tN1\tวิเคราะห์แบบ\t");
    s.MoveColumn(2, 0); // the table changes between copy and insert: cells still go to their columns
    Ok(TableEdits.InsertRowCopies(s, 2, clip.Rows!) && s.Rows.Count == 4, "insert a copied row");
    var copyId = s.RowIdAt(2);
    Ok(copyId != src && s.GetCell(2, 0) == "วิเคราะห์แบบ" && s.GetCell(2, 1) == "บริษัท 1" && s.GetCell(2, 2) == "N1",
        "the copy's cells follow their columns after a column move", string.Join("|", s.Rows[2]));
    Ok(s.RowStyles.TryGetValue(copyId, out var st) && st.Color == "orange" && st.Highlight && s.Recurrence.GetValueOrDefault(copyId) == Recurrence.Weekly &&
       s.Reminders[copyId].Offsets.SequenceEqual([15, 60]) && s.RowHeights![copyId] == 48 && !ReferenceEquals(st, s.RowStyles[src]),
        "the copy keeps colour, repeat, advance reminders and height (its own objects)");
    Ok(clip.Matches("บริษัท 1\tN1\tวิเคราะห์แบบ\t\r\n") && !clip.Matches("something else"), "a copy matches its clipboard text (line ends aside)");

    // copies of columns: type, choices, width, cells by row id; status becomes a plain column with labels
    var k = Sample(3);
    k.ColumnSettings[k.ColumnIds[2]] = new ColumnSetting { Type = ColumnTypes.Choice, Options = ["วิเคราะห์แบบ", "ตรวจเฉพาะประเด็น"] };
    k.SetColumnWidth(2, 175);
    var colClip = TableEdits.CopyColumns(k, [2, 3], [0, 1, 2], "x");
    var inserted = TableEdits.InsertColumnCopies(k, 0, colClip.Columns!);
    Ok(inserted == 2 && k.Columns[0] == "ตรวจ (2)" && k.Columns[1] == "สถานะ (2)" && k.ColumnWidth(0) == 175,
        "copied columns go in with their names (made unique) and width", string.Join("|", k.Columns));
    Ok(k.ColumnSettings.TryGetValue(k.ColumnIds[0], out var set) && set.Type == ColumnTypes.Choice && set.Options.Count == 2 &&
       !ReferenceEquals(set, k.ColumnSettings[k.ColumnIds[4]]), "a copied choice column keeps its choices");
    Ok(Col(k, 0) == "วิเคราะห์แบบ,ตรวจเฉพาะประเด็น,วิเคราะห์แบบ" && Col(k, 1) == ",กำลังทำ," && k.StatusColumnIndex() == 5 &&
       k.ColumnRolesList[1] == "", "the copied status column is plain text with the labels; the table keeps one status column",
        Col(k, 1) + " status at " + k.StatusColumnIndex());

    // text from Excel
    var t = Sample(2);
    Ok(TableEdits.InsertTextRows(t, 1, 1, "a\tb\r\nc\td\r\n") == null && t.Rows.Count == 4 && t.GetCell(1, 1) == "a" && t.GetCell(1, 2) == "b" &&
       t.GetCell(2, 1) == "c" && t.GetCell(2, 2) == "d" && t.GetCell(1, 0) == "" && t.GetCell(3, 0) == "บริษัท 2",
        "text from Excel goes in as new rows from the chosen column", string.Join(" / ", t.Rows.Select(x => string.Join("|", x))));
    Ok(TableEdits.InsertTextRows(t, 0, 2, "เดียว") == null && t.Rows.Count == 5 && t.GetCell(0, 2) == "เดียว", "one value: one new row");
    Ok(TableEdits.InsertTextRows(t, 0, 0, "\r\n") != null && t.Rows.Count == 5, "nothing on the clipboard: nothing inserted");
    var tc = Sample(2);
    Ok(TableEdits.InsertTextColumns(tc, 1, [0, 1], "x\ty\nz\tw") == null && tc.Columns.Count == 6 && tc.GetCell(0, 1) == "x" && tc.GetCell(0, 2) == "y" &&
       tc.GetCell(1, 1) == "z" && tc.GetCell(1, 2) == "w" && tc.GetCell(0, 3) == "N1", "text as new columns goes down the rows on screen");

    // moving several rows
    var m = Sample(6);
    var order = m.RowIds.ToList();
    var vis = Enumerable.Range(0, 6).ToList();
    Ok(RowOps.MoveRowsToSlot(m, vis, [1, 2], 5) && Col(m, 0) == "บริษัท 1,บริษัท 4,บริษัท 5,บริษัท 2,บริษัท 3,บริษัท 6", "rows 2–3 dropped before row 6", Col(m, 0));
    Ok(!RowOps.MoveRowsToSlot(m, vis, [3, 4], 4) && !RowOps.MoveRowsToSlot(m, vis, [3, 4], 3), "dropped onto itself: nothing moves");
    var after = m.RowIds.ToList();
    var action = new RowOrderAction(order, after, "ย้ายแถว");
    action.Undo(m);
    Ok(m.RowIds.SequenceEqual(order) && Col(m, 0) == "บริษัท 1,บริษัท 2,บริษัท 3,บริษัท 4,บริษัท 5,บริษัท 6", "undo puts the order back");
    action.Redo(m);
    Ok(m.RowIds.SequenceEqual(after), "redo moves them again");
    var h = Sample(5);
    h.SetCellRaw(2, 3, "done");
    Ok(RowOps.MoveRowsToSlot(h, [0, 1, 3, 4], [3, 4], 0) && Col(h, 0) == "บริษัท 4,บริษัท 5,บริษัท 1,บริษัท 2,บริษัท 3", "a hidden row between stays where it was", Col(h, 0));

    var u = new UndoStack();
    u.Push(new RowOrderAction(order, after, "หนึ่ง"));
    Ok(u.LastRedoLabel == null && u.Undo(m) == "หนึ่ง" && u.LastRedoLabel == "หนึ่ง", "the redo step has a name for the menu");

    // help topics
    var (heading, sections) = HelpContent.Parse("App 1\n\n# A\nKey :: text\n- bullet\nplain\n# B\nx");
    Ok(heading == "App 1" && sections.Count == 2 && sections[0].Lines.Select(l => l.Kind).SequenceEqual([HelpLineKind.Pair, HelpLineKind.Bullet, HelpLineKind.Text]) &&
       sections[0].Lines[0].Key == "Key" && sections[0].Lines[0].Text == "text", "help markup: heading, topics, key / text, bullets");
    var th = HelpContent.Parse(AppInfo.HelpTextTh);
    var en = HelpContent.Parse(AppInfo.HelpTextEn);
    Ok(th.Sections.Count >= 14 && th.Sections.Count == en.Sections.Count && th.Sections.All(x => x.Lines.Count >= 2) &&
       th.Heading.Contains(AppInfo.Version) && en.Heading.Contains(AppInfo.Version),
        "help: the same topics in Thai and English, none empty", $"{th.Sections.Count} / {en.Sections.Count}");
    Ok(th.Sections.Any(x => x.Title.Contains("ปิดหน้าต่าง") && x.Lines.Any(l => (l.Key ?? "").Contains("ออกจากโปรแกรม"))) &&
       th.Sections.Any(x => x.Lines.Any(l => (l.Key ?? "").Contains("คลิกเลขแถว"))) &&
       th.Sections.Any(x => x.Lines.Any(l => l.Text.Contains("100"))) && en.Sections.Any(x => x.Title.Contains("Undo")),
        "help covers rows / columns, undo (100 steps), close vs exit");
}

// ───────────────────────── the window ─────────────────────────
var seed = Sample(6);
seed.Window.Width = 980; seed.Window.Height = 560;
NoteStore.Save(note, seed);
AppBuilder.Configure<App>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .With(AppFonts.ManagerOptions)
    .SetupWithoutStarting();

var window = new MainWindow { Width = 980, Height = 560 };
var windowClosed = false;
window.Closed += (_, _) => windowClosed = true;
ToolTip.SetServiceEnabled(window, false); // a tip popping up after a pause would take the next simulated click
window.Show();
Pump(400);
var session = typeof(MainWindow).GetField("_session", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
NoteDocument Doc() => (NoteDocument)session.GetType().GetProperty("Doc")!.GetValue(session)!;
int P(string name) => (int)session.GetType().GetProperty(name)!.GetValue(session)!;
string Sel() => $"{P("AnchorRow")},{P("AnchorCol")}→{P("ActiveRow")},{P("ActiveCol")}";

// row numbers
Click(RowLabel(1));
Ok(Sel() == "1,0→1,3", "a click on a row number selects the whole row", Sel());
Ok(RowLabel(1).Background != RowLabel(0).Background, "its row number is marked");
Pump(700);
Drag(RowLabel(1), RowLabel(3));
Ok(Sel() == "1,0→3,3", "dragging over row numbers selects several rows", Sel());
Pump(700);
Click(RowLabel(4), RawInputModifiers.Shift);
Ok(Sel() == "1,0→4,3", "Shift+click extends the rows", Sel());

var menu = OpenMenu(RowLabel(2));
var labels = Headers(menu.Items);
Ok(labels.Contains("แทรก 4 แถวด้านบน") && labels.Contains("แทรก 4 แถวด้านล่าง") && labels.Contains("ลบ 4 แถว") && labels.Contains("แทรกแถวที่คัดลอก") &&
   labels.Contains("เลื่อนขึ้น"), "right-click inside the selection: insert / delete count the selected rows", string.Join(" | ", labels));
menu.Close();
Ok(Sel() == "1,0→4,3", "right-click keeps the selected rows", Sel());

// insert 2 above, undo, redo
Pump(700);
Click(RowLabel(1)); Pump(700); Click(RowLabel(2), RawInputModifiers.Shift);
var before = Doc().RowIds.ToList();
menu = OpenMenu(RowLabel(1));
Press(menu, "แทรก 2 แถวด้านบน");
Pump(100);
var d0 = Doc();
Ok(d0.Rows.Count == 8 && d0.Rows[1].All(x => x == "") && d0.Rows[2].All(x => x == "") && d0.GetCell(3, 0) == "บริษัท 2" && Sel() == "1,0→2,3",
    "Insert 2 rows above: two blank rows, and they are selected", Sel() + " " + Col(d0, 0));
PressKey(Avalonia.Input.Key.Z, RawInputModifiers.Control);
Ok(Doc().Rows.Count == 6 && Doc().RowIds.SequenceEqual(before), "Ctrl+Z takes both rows out (one step)");
PressKey(Avalonia.Input.Key.Y, RawInputModifiers.Control);
Ok(Doc().Rows.Count == 8 && Doc().Rows[1].All(x => x == ""), "Ctrl+Y puts them back");
PressKey(Avalonia.Input.Key.Z, RawInputModifiers.Control);

// delete 2 rows with text: asks, then one undo step brings them back with their colour
Doc().RowStyles[Doc().RowIdAt(2)] = new RowStyle { Color = "green" };
Pump(700);
Click(RowLabel(1)); Pump(700); Click(RowLabel(2), RawInputModifiers.Shift);
menu = OpenMenu(RowLabel(2));
Press(menu, "ลบ 2 แถว");
Pump(150);
var ask = Confirm();
Ok(ask != null && Texts(ask).Any(x => x.Contains("ลบ 2 แถวที่เลือก")), "deleting rows with text asks first", ask == null ? "no dialog" : string.Join(" | ", Texts(ask)));
PressIn(ask, "ลบ");
Pump(150);
Ok(Col(Doc(), 0) == "บริษัท 1,บริษัท 4,บริษัท 5,บริษัท 6", "the two rows are gone", Col(Doc(), 0));
PressKey(Avalonia.Input.Key.Z, RawInputModifiers.Control);
Ok(Col(Doc(), 0) == "บริษัท 1,บริษัท 2,บริษัท 3,บริษัท 4,บริษัท 5,บริษัท 6" && Doc().RowStyles[Doc().RowIdAt(2)].Color == "green" &&
   Doc().RowIds.SequenceEqual(before), "Ctrl+Z brings both back, same ids and colour");

// F6 with three rows selected
Pump(700);
Click(RowLabel(3)); Pump(700); Click(RowLabel(5), RawInputModifiers.Shift);
PressKey(Avalonia.Input.Key.F6);
Pump(150);
PressIn(Confirm(), "ลบ");
Pump(150);
Ok(Col(Doc(), 0) == "บริษัท 1,บริษัท 2,บริษัท 3", "F6 deletes every selected row", Col(Doc(), 0));
PressKey(Avalonia.Input.Key.Z, RawInputModifiers.Control);
Ok(Doc().Rows.Count == 6, "and Ctrl+Z brings them back");

// copy a whole row, insert copied: colour, repeat and reminders come along
var srcId = Doc().RowIdAt(0);
Doc().RowStyles[srcId] = new RowStyle { Color = "orange" };
Doc().Recurrence[srcId] = Recurrence.Daily;
Pump(700);
Click(RowLabel(0));
PressKey(Avalonia.Input.Key.C, RawInputModifiers.Control);
Pump(100);
Ok(ClipboardText().StartsWith("บริษัท 1\tN1", StringComparison.Ordinal), "Ctrl+C on a row copies the row", ClipboardText());
Pump(700);
Click(RowLabel(4));
menu = OpenMenu(RowLabel(4));
Press(menu, "แทรกแถวที่คัดลอก");
Pump(200);
var newId = Doc().RowIdAt(4);
Ok(Doc().Rows.Count == 7 && Doc().GetCell(4, 0) == "บริษัท 1" && Doc().GetCell(5, 0) == "บริษัท 5" && newId != srcId &&
   Doc().RowStyles.GetValueOrDefault(newId)?.Color == "orange" && Doc().Recurrence.GetValueOrDefault(newId) == Recurrence.Daily && Sel() == "4,0→4,3",
    "Insert copied rows: a copy above row 5 with its colour and repeat, selected", Col(Doc(), 0) + " " + Sel());
PressKey(Avalonia.Input.Key.Z, RawInputModifiers.Control);
Ok(Doc().Rows.Count == 6 && !Doc().RowIds.Contains(newId), "one Ctrl+Z takes the copy out");

// text from Excel, inserted from a cell
SetClipboard("แถวใหม่ 1\tX1\r\nแถวใหม่ 2\tX2\r\n");
Pump(700);
Click(Cell(2, 0));
var cellMenu = OpenCellMenu(Cell(2, 0));
Ok(Headers(cellMenu.Items).Contains("แทรกเซลล์ที่คัดลอก") && Headers(cellMenu.Items).Contains("แทรก") && Headers(cellMenu.Items).Contains("ลบ"),
    "the cell menu has Insert copied cells, Insert ▸ and Delete ▸", string.Join(" | ", Headers(cellMenu.Items)));
Press(cellMenu, "แทรกเซลล์ที่คัดลอก");
Pump(200);
Ok(Doc().Rows.Count == 8 && Doc().GetCell(2, 0) == "แถวใหม่ 1" && Doc().GetCell(3, 1) == "X2" && Doc().GetCell(4, 0) == "บริษัท 3",
    "text from Excel goes in as new rows at the selected cell", Col(Doc(), 0));
PressKey(Avalonia.Input.Key.Z, RawInputModifiers.Control);
Ok(Doc().Rows.Count == 6, "Ctrl+Z takes them out");

// drag selected rows to move them
Pump(700);
Click(RowLabel(0)); Pump(700); Click(RowLabel(1), RawInputModifiers.Shift);
var orderBefore = Doc().RowIds.ToList();
Pump(700);
MoveDrag(RowLabel(1), RowLabel(4), below: true);
Pump(150);
Ok(Col(Doc(), 0) == "บริษัท 3,บริษัท 4,บริษัท 5,บริษัท 1,บริษัท 2,บริษัท 6" && Sel() == "3,0→4,3",
    "dragging the ⋮⋮ grip of a selected row moves the selected rows together", Col(Doc(), 0) + " " + Sel());
PressKey(Avalonia.Input.Key.Z, RawInputModifiers.Control);
Ok(Doc().RowIds.SequenceEqual(orderBefore), "one Ctrl+Z puts the order back");
Pump(700);
Click(RowLabel(2));
Pump(700);
MoveDrag(RowLabel(5), RowLabel(0), below: false);
Pump(150);
Ok(Col(Doc(), 0) == "บริษัท 6,บริษัท 1,บริษัท 2,บริษัท 3,บริษัท 4,บริษัท 5" && Sel().StartsWith("0,", StringComparison.Ordinal),
    "the grip of a row that is not selected moves just that row", Col(Doc(), 0) + " " + Sel());
PressKey(Avalonia.Input.Key.Z, RawInputModifiers.Control);
Ok(Doc().RowIds.SequenceEqual(orderBefore), "Ctrl+Z puts it back");
Pump(700);
Drag(RowLabel(1), RowLabel(2));
Ok(Sel() == "1,0→2,3" && Doc().RowIds.SequenceEqual(orderBefore), "dragging on the numbers (not the grip) selects, even from a selected row", Sel());

// column headers
Pump(700);
Click(Header(1));
Ok(Sel() == "0,1→5,1" && Header(1).Classes.Contains("selected") && !Header(2).Classes.Contains("selected") && Header(1).IsReadOnly,
    "a click on a header selects the whole column (header filled), not the name", Sel());
Pump(700);
Drag(Header(1), Header(2));
Ok(Sel() == "0,1→5,2" && Header(2).Classes.Contains("selected"), "dragging over headers selects several columns", Sel());
PressKey(Avalonia.Input.Key.Delete);
Ok(Col(Doc(), 1) == ",,,,," && Col(Doc(), 2) == ",,,,,", "Delete clears the selected columns", Col(Doc(), 1));
PressKey(Avalonia.Input.Key.Z, RawInputModifiers.Control);
Ok(Col(Doc(), 1) == "N1,N2,N3,N4,N5,N6", "Ctrl+Z puts them back");

Pump(700);
DoubleClick(Header(1));
Ok(!Header(1).IsReadOnly, "double-click on a header renames the column");
Header(1).Text = "เลขผู้เสียภาษี";
PressKey(Avalonia.Input.Key.Return);
Ok(Doc().Columns[1] == "เลขผู้เสียภาษี" && Header(1).IsReadOnly, "Enter keeps the new name", Doc().Columns[1]);
PressKey(Avalonia.Input.Key.Z, RawInputModifiers.Control);
Ok(Doc().Columns[1] == "NID", "Ctrl+Z gives the old name back", Doc().Columns[1]);

// insert / delete columns from the header menu
Pump(700);
Click(Header(1)); Pump(700); Click(Header(2), RawInputModifiers.Shift);
var colMenu = OpenMenu(Header(2));
var colLabels = Headers(colMenu.Items);
Ok(colLabels.Contains("แทรก 2 คอลัมน์ทางซ้าย") && colLabels.Contains("แทรก 2 คอลัมน์ทางขวา") && colLabels.Contains("ลบ 2 คอลัมน์") &&
   colLabels.Contains("แทรกคอลัมน์ที่คัดลอก") && colLabels.Contains("ย้ายไปทางซ้าย"), "the header menu counts the selected columns", string.Join(" | ", colLabels));
Press(colMenu, "แทรก 2 คอลัมน์ทางซ้าย");
Pump(150);
Ok(Doc().Columns.Count == 6 && Doc().Columns[3] == "NID" && Col(Doc(), 1) == ",,,,," && Sel() == "0,1→5,2",
    "Insert 2 columns left: two new columns, selected", string.Join("|", Doc().Columns) + " " + Sel());
PressKey(Avalonia.Input.Key.Z, RawInputModifiers.Control);
Ok(Doc().Columns.SequenceEqual(["ราย", "NID", "ตรวจ", "สถานะ"]), "Ctrl+Z takes them out", string.Join("|", Doc().Columns));

Pump(700);
Click(Header(1)); Pump(700); Click(Header(2), RawInputModifiers.Shift);
colMenu = OpenMenu(Header(1));
Press(colMenu, "ลบ 2 คอลัมน์");
Pump(150);
ask = Confirm();
Ok(ask != null && Texts(ask).Any(x => x.Contains("ลบ 2 คอลัมน์: NID, ตรวจ")), "deleting columns asks once, with their names", ask == null ? "no dialog" : string.Join(" | ", Texts(ask)));
PressIn(ask, "ลบ");
Pump(150);
Ok(Doc().Columns.SequenceEqual(["ราย", "สถานะ"]), "both columns are gone", string.Join("|", Doc().Columns));
PressKey(Avalonia.Input.Key.Z, RawInputModifiers.Control);
Ok(Doc().Columns.SequenceEqual(["ราย", "NID", "ตรวจ", "สถานะ"]) && Col(Doc(), 2).StartsWith("วิเคราะห์แบบ", StringComparison.Ordinal), "Ctrl+Z brings both back with their cells");

// copy a column, insert copied columns
Pump(700);
Click(Header(2));
PressKey(Avalonia.Input.Key.C, RawInputModifiers.Control);
Pump(100);
Pump(700);
Click(Header(0));
colMenu = OpenMenu(Header(0));
Press(colMenu, "แทรกคอลัมน์ที่คัดลอก");
Pump(200);
Ok(Doc().Columns.Count == 5 && Doc().Columns[0] == "ตรวจ (2)" && Col(Doc(), 0) == Col(Doc(), 3) && Sel() == "0,0→5,0",
    "Insert copied columns: the copy goes left of the selected column, with its cells", string.Join("|", Doc().Columns) + " " + Sel());
PressKey(Avalonia.Input.Key.Z, RawInputModifiers.Control);
Ok(Doc().Columns.Count == 4, "Ctrl+Z takes it out");

// Table menu: Undo / Redo with the step's name
var mainMenu = window.FindControl<Menu>("MainMenu")!;
var table = mainMenu.Items.OfType<MenuItem>().First(i => i.Header?.ToString() == "ตาราง");
table.Open();
Pump(100);
var undoItem = table.Items.OfType<MenuItem>().First(i => (i.Header?.ToString() ?? "").StartsWith("ย้อนกลับ", StringComparison.Ordinal));
var redoItem = table.Items.OfType<MenuItem>().First(i => (i.Header?.ToString() ?? "").StartsWith("ทำซ้ำ", StringComparison.Ordinal));
Ok(redoItem.IsEnabled && redoItem.Header?.ToString() == "ทำซ้ำ: แทรกที่คัดลอก" &&
   undoItem.InputGesture?.ToString() == "Ctrl+Z" && redoItem.InputGesture?.ToString() == "Ctrl+Y",
    "Table menu: Redo with the name of the step that was undone, and the shortcuts", $"{undoItem.Header} | {redoItem.Header}");
redoItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
table.Close();
Pump(150);
Ok(Doc().Columns.Count == 5, "Redo from the menu does it again");
table.Open();
Pump(100);
undoItem = table.Items.OfType<MenuItem>().First(i => (i.Header?.ToString() ?? "").StartsWith("ย้อนกลับ", StringComparison.Ordinal));
Ok(undoItem.IsEnabled && undoItem.Header?.ToString() == "ย้อนกลับ: แทรกที่คัดลอก", "then Undo names it", undoItem.Header?.ToString() ?? "");
undoItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
table.Close();
Pump(150);
Ok(Doc().Columns.Count == 4, "Undo from the menu takes it back");
// 12 separate edits, then 12 × Ctrl+Z: every one comes back (the stack keeps up to 100)
var original = Doc().Rows.Select(x => x.ToList()).ToList();
for (var i = 0; i < 12; i++)
{
    Pump(700);
    Click(Cell(i % 6, 1 + i / 6));
    window.KeyTextInput("แก้" + i);
    PressKey(Avalonia.Input.Key.Return);
}
Ok(Doc().GetCell(5, 2) == "แก้11" && UndoCount() >= 12, "12 edits are 12 undo steps", UndoCount().ToString());
for (var i = 0; i < 12; i++) PressKey(Avalonia.Input.Key.Z, RawInputModifiers.Control);
Ok(Doc().Rows.Select(x => string.Join("|", x)).SequenceEqual(original.Select(x => string.Join("|", x))) && UndoStack.MaxSteps >= 10,
    "12 × Ctrl+Z takes all of them back (undo keeps up to " + UndoStack.MaxSteps + " steps)", Col(Doc(), 1));
for (var i = 0; i < 12; i++) PressKey(Avalonia.Input.Key.Y, RawInputModifiers.Control);
Ok(Doc().GetCell(5, 2) == "แก้11" && Doc().GetCell(0, 1) == "แก้0", "and 12 × Ctrl+Y does them again");
for (var i = 0; i < 12; i++) PressKey(Avalonia.Input.Key.Z, RawInputModifiers.Control);

// Help: topics that open and close
var help = HelpWindow.Show("วิธีใช้", AppInfo.HelpText);
Pump(200);
Ok(help.Topics.Count >= 14 && help.Topics.All(t => !t.IsExpanded), "help opens with every topic closed", help.Topics.Count.ToString());
var toggle = help.Topics[2].GetVisualDescendants().OfType<ToggleButton>().First();
void ClickIn(Window w, Control c)
{
    var p = c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), w)!.Value;
    w.MouseMove(p); w.MouseDown(p, MouseButton.Left); w.MouseUp(p, MouseButton.Left);
    Pump(100);
}
ClickIn(help, toggle);
Ok(help.Topics[2].IsExpanded && help.Topics.Count(t => t.IsExpanded) == 1, "a click on a topic opens it");
SaveFrame(help, "help-one-open-th");
Pump(700);
ClickIn(help, toggle);
Ok(!help.Topics[2].IsExpanded, "a second click closes it");
help.GetLogicalDescendants().OfType<Button>().First(b => b.Content as string == "ขยายทั้งหมด").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
Pump(200);
Ok(help.Topics.All(t => t.IsExpanded), "Open all");
var foreign = ForeignRuns(help);
Ok(foreign.Count == 0, "the help text uses the bundled font", string.Join(" | ", foreign.Take(6)));
SaveFrame(help, "help-topics-th");
Ok(ReferenceEquals(HelpWindow.Show("วิธีใช้", AppInfo.HelpText), help), "Help again brings the same window forward");
help.GetLogicalDescendants().OfType<Button>().First(b => b.Content as string == "ยุบทั้งหมด").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
Pump(100);
Ok(help.Topics.All(t => !t.IsExpanded), "Close all");
SaveFrame(help, "help-closed-th");
help.Close();

// File menu: Close window (to the tray) and Exit
var file = mainMenu.Items.OfType<MenuItem>().First(i => i.Header?.ToString() == "ไฟล์");
typeof(MainWindow).GetField("_trayReady", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, true);
file.Open();
Pump(100);
var fileLabels = Headers(file.Items);
Ok(fileLabels.Contains("ปิดหน้าต่าง (เก็บไว้ที่ถาด)") && fileLabels.Contains("ออกจากโปรแกรม"), "File menu: Close window (tray) and Exit", string.Join(" | ", fileLabels));
var closeItem = file.Items.OfType<MenuItem>().First(i => i.Header?.ToString() == "ปิดหน้าต่าง (เก็บไว้ที่ถาด)");
Ok(closeItem.InputGesture?.ToString() == "Alt+F4", "Close window shows Alt+F4");
closeItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
file.Close();
Pump(150);
Ok(!window.IsVisible && !Closed(window), "Close window: hidden in the tray, TableNote still running");
window.Show();
Pump(200);
SaveFrame(window, "rowcol-after");
file.Open();
Pump(100);
var exitItem = file.Items.OfType<MenuItem>().First(i => i.Header?.ToString() == "ออกจากโปรแกรม");
exitItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
Pump(200);
Ok(Closed(window), "Exit: the window really closes (TableNote ends)");
Ok(File.ReadAllText(note).Contains("บริษัท 6"), "the table was saved on exit");

Console.WriteLine(fail == 0 ? "RowColTests: all passed" : $"RowColTests: {fail} failed");
return fail;

// ───────────────────────── helpers ─────────────────────────
Grid RowGrid(int row)
{
    Pump(10);
    // The list's own index → container map (after a rebuild, an old container can still be listed as realized).
    var visible = (List<int>)typeof(MainWindow).GetField("_visibleRows", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
    var list = window.FindControl<ItemsControl>("RowsList")!;
    if (list.ContainerFromIndex(visible.IndexOf(row)) is ContentPresenter { Child: Grid { Tag: int r } g } && r == row) return g;
    throw new InvalidOperationException($"row {row} not on screen");
}
Border RowLabel(int row) => (Border)RowGrid(row).Children[0];
TextBox Cell(int row, int column)
{
    var child = RowGrid(row).Children.First(ch => Grid.GetColumn(ch) == column + 1 && ch.GetType().Name != "CellOverlay");
    return child as TextBox ?? child.GetVisualDescendants().OfType<TextBox>().First();
}
TextBox Header(int column)
{
    Pump(10);
    var map = (Dictionary<int, TextBox>)typeof(MainWindow).GetField("_menuHeaderBoxes", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
    return map[column];
}
int UndoCount()
{
    var undo = typeof(MainWindow).GetField("_undo", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
    return (int)undo.GetType().GetProperty("Count")!.GetValue(undo)!;
}
Point Center(Control c) => c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), window) ?? throw new InvalidOperationException("no point");
void Click(Control c, RawInputModifiers mods = RawInputModifiers.None)
{
    var p = Center(c);
    window.MouseMove(p, mods);
    window.MouseDown(p, MouseButton.Left, mods);
    window.MouseUp(p, MouseButton.Left, mods);
    Pump(60);
}
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
void MoveDrag(Control from, Control to, bool below)
{
    var a = from.TranslatePoint(new Point(6, from.Bounds.Height / 2), window)!.Value; // the ⋮⋮ grip
    var b = to.TranslatePoint(new Point(to.Bounds.Width / 2, below ? to.Bounds.Height - 3 : 3), window)!.Value;
    window.MouseMove(a); window.MouseDown(a, MouseButton.Left);
    for (var i = 1; i <= 8; i++) window.MouseMove(new Point(a.X, a.Y + (b.Y - a.Y) * i / 8), RawInputModifiers.LeftMouseButton);
    window.MouseUp(b, MouseButton.Left);
    Pump(60);
}
ContextMenu OpenMenu(Control target)
{
    var p = Center(target);
    window.MouseMove(p);
    window.MouseDown(p, MouseButton.Right);
    window.MouseUp(p, MouseButton.Right);
    Pump(80);
    var m = target.ContextMenu ?? throw new InvalidOperationException("no context menu");
    if (!m.IsOpen) m.Open(target);
    Pump(80);
    return m;
}
MenuFlyout OpenCellMenu(Control target)
{
    var field = typeof(MainWindow).GetField("_lastCellMenu", BindingFlags.Instance | BindingFlags.NonPublic)!;
    field.SetValue(window, null);
    var p = Center(target);
    window.MouseMove(p);
    window.MouseDown(p, MouseButton.Right);
    window.MouseUp(p, MouseButton.Right);
    Pump(120);
    if (field.GetValue(window) is not MenuFlyout shown)
    {
        target.RaiseEvent(new ContextRequestedEventArgs()); // the platform did not raise it: ask as a right-click would
        Pump(120);
        shown = field.GetValue(window) as MenuFlyout ?? throw new InvalidOperationException("no cell menu");
    }
    return shown;
}
void Press(object menuOrFlyout, string header)
{
    var items = menuOrFlyout switch { ContextMenu c => c.Items, MenuFlyout f => f.Items, _ => throw new ArgumentException() };
    var item = items.OfType<MenuItem>().FirstOrDefault(i => i.Header?.ToString() == header)
        ?? throw new InvalidOperationException($"no item {header}: " + string.Join(" | ", Headers(items)));
    item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
    if (menuOrFlyout is ContextMenu cm) cm.Close(); else ((MenuFlyout)menuOrFlyout).Hide();
    Pump(60);
}
List<string> Headers(IEnumerable<object?> items) => items.OfType<MenuItem>().Select(i => i.Header?.ToString() ?? "").ToList();
Window? Confirm()
{
    Pump(60);
    return window.OwnedWindows.LastOrDefault(w => w.IsVisible && w.Title == Localizer.T("delete.title"));
}
void PressIn(Window? w, string text)
{
    if (w == null) { Ok(false, "dialog button " + text, "no dialog"); return; }
    var b = w.GetLogicalDescendants().OfType<Button>().First(x => x.Content as string == text);
    b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    Pump(80);
}
List<string> Texts(Window w) => w.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToList();
bool Closed(Window w) => w == window ? windowClosed : false;
void PressKey(Avalonia.Input.Key key, RawInputModifiers mods = RawInputModifiers.None)
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
List<string> ForeignRuns(Visual root)
{
    var bad = new List<string>();
    foreach (var v in root.GetVisualDescendants())
    {
        var layout = v switch
        {
            TextBlock tb when !string.IsNullOrEmpty(tb.Text) && tb.IsEffectivelyVisible => tb.TextLayout,
            _ => null
        };
        if (layout == null) continue;
        foreach (var line in layout.TextLines)
            foreach (var run in line.TextRuns)
                if (run is Avalonia.Media.TextFormatting.ShapedTextRun sr && sr.GlyphRun.GlyphTypeface.FamilyName != "Noto Sans Thai"
                    && new string(sr.Text.Span).EnumerateRunes().Any(r => Rune.IsLetterOrDigit(r)))
                    bad.Add(sr.GlyphRun.GlyphTypeface.FamilyName + "[" + new string(sr.Text.Span) + "]");
    }
    return bad;
}
void SaveFrame(Window w, string name)
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
