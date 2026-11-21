using System.Reflection;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TableNote;

/// <summary>The real window (headless, Thai): the days cell, its pop-up, notes, the start date, undo, the Table menu.</summary>
static class UiTests
{
    public static void Run(Action<bool, string, string> ok)
    {
        var dir = Directory.CreateTempSubdirectory("tn-days-ui").FullName;
        var note = Path.Combine(dir, "note.json");
        Environment.SetEnvironmentVariable("TABLENOTE_DATA", note);
        File.WriteAllText(Path.Combine(dir, "settings.json"), "{ \"language\": \"th\", \"uiScale\": 100 }");
        AppLog.PathOverride = Path.Combine(dir, "tablenote.log");

        Localizer.SetLanguage("th");
        var today = DateTime.Today;
        var seed = CoreTests.Doc(3, DueDates.FormatDate(today.AddDays(-150)));
        seed.Columns[0] = "ราย"; seed.Columns[1] = "ขั้นตอน"; seed.Columns[4] = "นับวัน";
        seed.MoveColumn(4, 2); // ราย | ขั้นตอน | นับวัน | สถานะ | กำหนดส่ง, as Table → Add days counter column puts it
        seed.SetCellRaw(0, 0, "บริษัท ทีเอชที จิวเวลรี่ จำกัด");
        seed.SetCellRaw(0, 1, "ต.6 เพื่อรับทราบผล");
        seed.SetCellRaw(0, seed.StatusColumnIndex(), TaskStatuses.DoingCode);
        seed.ColumnWidths = [260, 200, 120, 120, 200];
        var id0 = seed.RowIdAt(0);
        RowHistoryOps.Append(seed, id0, new RowEvent { Time = RowHistoryOps.FormatTime(today.AddDays(-150).AddHours(9)), Kind = RowEventKinds.Added });
        RowHistoryOps.Append(seed, id0, new RowEvent
        {
            Time = RowHistoryOps.FormatTime(today.AddDays(-120).AddHours(10)), Kind = RowEventKinds.Status,
            Column = seed.ColumnIdAt(seed.StatusColumnIndex()), Name = seed.Columns[seed.StatusColumnIndex()], From = "", To = TaskStatuses.DoingCode
        });
        RowHistoryOps.Append(seed, id0, new RowEvent
        {
            Time = RowHistoryOps.FormatTime(today.AddDays(-60).AddHours(14)), Kind = RowEventKinds.Edit,
            Column = seed.ColumnIdAt(1), Name = "ขั้นตอน", From = "วิเคราะห์แบบ", To = "ต.6 เพื่อรับทราบผล"
        });
        RowHistoryOps.AddNote(seed, id0, "โทรตามแล้ว ลูกค้าจะส่งเอกสารวันศุกร์", today.AddDays(-9).AddHours(14.5));
        seed.Window.Width = 1100; seed.Window.Height = 560;
        NoteStore.Save(note, seed);

        var window = new MainWindow { Width = 1100, Height = 560 };
        window.Show();
        Pump(window);

        var cells = DaysCells(window);
        ok(cells.Count == 3, "every row has a days cell", cells.Count.ToString());
        ok(CellText(cells[0]) == "150 วัน", "the cell shows the days since the start", CellText(cells[0]));
        Save(window, "days-table-th");

        Click(window, cells[0]);
        Pump(window);
        var dialog = window.OwnedWindows.OfType<DaysDialog>().FirstOrDefault();
        ok(dialog != null, "clicking the cell opens the pop-up", "");
        if (dialog == null) return;
        Pump(dialog);
        var texts = dialog.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToList();
        ok(texts.Contains("บริษัท ทีเอชที จิวเวลรี่ จำกัด") && texts.Contains("ค้างมา 150 วัน"), "pop-up: row title and how long it is open",
            string.Join(" | ", texts.Take(6)));
        ok(texts.Any(t => t.StartsWith("สถานะ: กำลังทำ ตั้งแต่", StringComparison.Ordinal)) && texts.Any(t => t.StartsWith("อัปเดตล่าสุด:", StringComparison.Ordinal) && t.Contains("(9 วันที่แล้ว)")),
            "pop-up: status since and last update", string.Join(" | ", texts.Where(t => t.StartsWith("สถานะ") || t.StartsWith("อัปเดต"))));
        var timeline = Timeline(dialog);
        ok(timeline.Count >= 4 && timeline[0].Contains("โทรตามแล้ว") && timeline[1].Contains("ขั้นตอน: วิเคราะห์แบบ → ต.6 เพื่อรับทราบผล")
           && timeline[2].Contains("สถานะ: ยังไม่เริ่ม → กำลังทำ") && timeline[3].Contains("เพิ่มแถวนี้"),
            "pop-up: history newest first", string.Join(" / ", timeline));
        ok(timeline[2].Contains("หลังรับงาน 30 วัน") && timeline[3].Contains("วันรับงาน"), "each entry says which day it was", timeline[2]);
        var foreign = ForeignRuns(dialog);
        ok(foreign.Count == 0, "the pop-up uses the bundled font", string.Join(" | ", foreign.Take(6)));
        Save(dialog, "days-popup-th");

        var noteBox = dialog.GetVisualDescendants().OfType<TextBox>().First(t => t.MaxLength == RowHistoryOps.MaxNoteChars);
        noteBox.Text = "ส่งเรื่องให้หัวหน้าแล้ว";
        Press(ButtonWithText(dialog, "เพิ่มบันทึก"));
        Pump(dialog);
        timeline = Timeline(dialog);
        ok(timeline[0].Contains("ส่งเรื่องให้หัวหน้าแล้ว") && noteBox.Text == "", "a typed note goes to the top of the history", timeline[0]);

        // ── editing a note: Edit (or double-click) → its text in a box; Enter / Save keep it, Esc / Cancel put it back ──
        var firstTime = timeline[0].Split(" | ")[0];
        Press(ButtonIn(Items(dialog)[0], "แก้ไข"));
        Pump(dialog, 60);
        var editBox = EditBox(dialog);
        ok(editBox != null && editBox.Text == "ส่งเรื่องให้หัวหน้าแล้ว" && editBox.IsFocused && editBox.CaretIndex == editBox.Text.Length,
            "Edit puts the note's text in a box, ready to type", $"{editBox?.Text} focus={editBox?.IsFocused}");
        ok(Items(dialog)[0].GetLogicalDescendants().OfType<Button>().Any(b => b.Content as string == "บันทึก") &&
           !Items(dialog)[0].GetLogicalDescendants().OfType<Button>().Any(b => b.Content as string is "แก้ไข" or "✕"),
            "while editing: Save and Cancel instead of Edit and ✕", "");
        editBox!.Text = "ส่งเรื่องให้หัวหน้าแล้ว รออนุมัติ";
        PressKey(dialog, Key.Return);
        timeline = Timeline(dialog);
        ok(EditBox(dialog) == null && timeline[0].Contains("ส่งเรื่องให้หัวหน้าแล้ว รออนุมัติ") && timeline[0].Contains("แก้ไขเมื่อ ")
           && timeline[0].StartsWith(firstTime, StringComparison.Ordinal),
            "Enter keeps the new text; the note keeps its time and says when it was edited", timeline[0]);
        ok(RowHistoryOps.Of(Doc(window), id0).Count(e => e.Text == "ส่งเรื่องให้หัวหน้าแล้ว รออนุมัติ" && e.Edited != null) == 1,
            "the table's history has the new text", "");

        Press(ButtonIn(Items(dialog)[0], "แก้ไข"));
        Pump(dialog, 60);
        EditBox(dialog)!.Text = "ไม่เอาข้อความนี้";
        PressKey(dialog, Key.Escape);
        timeline = Timeline(dialog);
        ok(dialog.IsVisible && EditBox(dialog) == null && timeline[0].Contains("ส่งเรื่องให้หัวหน้าแล้ว รออนุมัติ") && !timeline.Any(t => t.Contains("ไม่เอาข้อความนี้")),
            "Esc puts the text back and leaves the pop-up open", timeline[0]);

        var older = Items(dialog).First(b => Timeline1(b).Contains("โทรตามแล้ว"));
        Pump(dialog, 700);
        DoubleClick(dialog, older.GetVisualDescendants().OfType<TextBlock>().First(t => (t.Text ?? "").Contains("โทรตามแล้ว")));
        Pump(dialog, 60);
        editBox = EditBox(dialog);
        ok(editBox?.Text == "โทรตามแล้ว ลูกค้าจะส่งเอกสารวันศุกร์", "double-click on a note edits it", editBox?.Text ?? "no box");
        editBox!.Text = "";
        Pump(dialog);
        var saveButton = Items(dialog).SelectMany(b => b.GetLogicalDescendants().OfType<Button>()).First(b => b.Content as string == "บันทึก");
        ok(!saveButton.IsEnabled, "a blank note cannot be saved (✕ deletes a note)", "");
        PressKey(dialog, Key.Return);
        ok(EditBox(dialog) != null, "Enter on blank text keeps the box open", "");
        EditBox(dialog)!.Text = "โทรตามแล้ว ลูกค้าจะส่งเอกสารวันจันทร์";
        Press(ButtonIn(Items(dialog)[0], "แก้ไข")); // going to another note keeps this edit, like Excel
        Pump(dialog, 60);
        ok(RowHistoryOps.Of(Doc(window), id0).Any(e => e.Text == "โทรตามแล้ว ลูกค้าจะส่งเอกสารวันจันทร์") && EditBox(dialog)?.Text == "ส่งเรื่องให้หัวหน้าแล้ว รออนุมัติ",
            "editing another note keeps the first edit", EditBox(dialog)?.Text ?? "no box");
        EditBox(dialog)!.Text = "ส่งเรื่องให้หัวหน้าแล้ว รออนุมัติ (ตามอีกครั้งวันศุกร์)";
        Press(ButtonWithText(dialog, "วันนี้")); // the list is drawn again: the open edit keeps its typed text
        Pump(dialog, 60);
        ok(EditBox(dialog)?.Text == "ส่งเรื่องให้หัวหน้าแล้ว รออนุมัติ (ตามอีกครั้งวันศุกร์)", "a redraw keeps what was typed in the open edit", EditBox(dialog)?.Text ?? "no box");
        foreign = ForeignRuns(dialog);
        ok(foreign.Count == 0, "the note editor uses the bundled font", string.Join(" | ", foreign.Take(6)));
        Save(dialog, "days-popup-edit-th");

        texts = dialog.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToList();
        ok(texts.Contains("รับงานวันนี้"), "Today sets the start date", string.Join(" | ", texts.Take(4)));
        Press(ButtonWithText(dialog, "ปิด")); // closing with the edit still open keeps it
        Pump(window);
        cells = DaysCells(window);
        ok(!window.OwnedWindows.Any(w => w.IsVisible) && CellText(cells[0]) == "วันนี้", "closing refreshes the cell", CellText(cells[0]));
        var saved = File.ReadAllText(note);
        ok(saved.Contains("ส่งเรื่องให้หัวหน้าแล้ว รออนุมัติ (ตามอีกครั้งวันศุกร์)") && saved.Contains("โทรตามแล้ว ลูกค้าจะส่งเอกสารวันจันทร์") && saved.Contains("\"edited\""),
            "closing with an open edit keeps it; the notes and their edit times are in note.json", "");
        typeof(MainWindow).GetMethod("DoUndo", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
        Pump(window);
        cells = DaysCells(window);
        ok(CellText(cells[0]) == "150 วัน", "undo puts the start date back", CellText(cells[0]));
        var doc = Doc(window);
        var h0 = RowHistoryOps.Of(doc, id0);
        ok(h0.Count(e => e.Text == "ส่งเรื่องให้หัวหน้าแล้ว รออนุมัติ (ตามอีกครั้งวันศุกร์)") == 1, "undo does not remove or change the note", h0.Count.ToString());

        // Status → done: the row stops counting at once.
        var combo = RowOf(window, 1).GetVisualDescendants().OfType<ComboBox>().First(c => c.ItemCount == TaskStatuses.Codes.Length);
        combo.SelectedIndex = Array.IndexOf(TaskStatuses.Codes, TaskStatuses.DoneCode);
        Pump(window);
        cells = DaysCells(window);
        ok(CellText(cells[1]) == "150 วัน ✓", "a row set to done shows its final count", CellText(cells[1]));
        ok(RowHistoryOps.DoneAt(doc, doc.RowIdAt(1))?.Date == today, "the done time is recorded", "");

        // Table → Add days counter column.
        var menu = window.FindControl<Menu>("MainMenu")!;
        var table = menu.Items.OfType<MenuItem>().First(i => i.Header?.ToString() == "ตาราง");
        table.Open();
        Pump(window);
        var add = table.Items.OfType<MenuItem>().FirstOrDefault(i => i.Header?.ToString() == "เพิ่มคอลัมน์นับวัน");
        ok(add is { IsEnabled: true }, "Table menu has Add days counter column", "");
        add?.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        table.Close();
        Pump(window);
        var added = doc.Columns.IndexOf("นับวัน 2");
        ok(added >= 0 && DaysCounter.IsDaysColumn(doc, added), "the new column counts days and gets its own name", string.Join(",", doc.Columns));
        ok(added == doc.StatusColumnIndex() - 1 && doc.DueColumnIndex() == doc.Columns.Count - 1, "it goes just before the status column; the reminder date stays last",
            string.Join(",", doc.Columns));
        ok(doc.GetCell(0, added) == DaysCounter.TodayText(DateTime.Now) && doc.GetCell(1, added) == "", "open rows start today, done rows stay blank",
            doc.GetCell(0, added) + " / " + doc.GetCell(1, added));
        Save(window, "days-table-added-th");
        window.Close();
    }

    static NoteDocument Doc(MainWindow window)
    {
        var session = typeof(MainWindow).GetField("_session", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        return (NoteDocument)session.GetType().GetProperty("Doc")!.GetValue(session)!;
    }

    static List<Button> DaysCells(Window window) =>
        window.GetVisualDescendants().OfType<Button>().Where(b => b.Tag as string == "days").ToList();

    static Grid RowOf(Window window, int row) =>
        window.GetVisualDescendants().OfType<Grid>().First(g => g.Tag is int r && r == row);

    static string CellText(Button b) => (b.Content as TextBlock)?.Text ?? "";

    static List<string> Timeline(Window dialog) =>
        dialog.GetVisualDescendants().OfType<Border>()
            .Where(b => b.BorderThickness.Left == 4 && b.Child is Grid)
            .Select(b => string.Join(" | ", b.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text)))
            .ToList();

    static Button ButtonWithText(Window w, string text) =>
        w.GetLogicalDescendants().OfType<Button>().First(b => b.Content as string == text);

    /// <summary>The history entries (newest first), as their bordered boxes.</summary>
    static List<Border> Items(Window dialog) =>
        dialog.GetVisualDescendants().OfType<Border>().Where(b => b.BorderThickness.Left == 4 && b.Child is Grid).ToList();

    static string Timeline1(Border item) => string.Join(" | ", item.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));

    static Button ButtonIn(Control item, string text) =>
        item.GetLogicalDescendants().OfType<Button>().First(b => b.Content as string == text);

    /// <summary>The open note editor (a text box inside a history entry), or null.</summary>
    static TextBox? EditBox(Window dialog) => Items(dialog).SelectMany(b => b.GetVisualDescendants().OfType<TextBox>()).FirstOrDefault();

    static void PressKey(Window w, Key key)
    {
        w.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, "");
        w.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, "");
        Pump(w, 60);
    }

    static void DoubleClick(Window w, Control c)
    {
        var p = c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), w) ?? throw new InvalidOperationException("no point");
        w.MouseMove(p);
        w.MouseDown(p, MouseButton.Left); w.MouseUp(p, MouseButton.Left);
        w.MouseDown(p, MouseButton.Left); w.MouseUp(p, MouseButton.Left);
    }

    static void Press(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    static void Click(Window window, Control control)
    {
        var p = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)
            ?? throw new InvalidOperationException("no point");
        window.MouseMove(p);
        window.MouseDown(p, MouseButton.Left);
        window.MouseUp(p, MouseButton.Left);
    }

    static void Pump(Window window, int ms = 0)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        do
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
            if (ms > 0) Thread.Sleep(20);
        } while (sw.ElapsedMilliseconds < ms);
    }

    static List<string> ForeignRuns(Visual root)
    {
        var bad = new List<string>();
        foreach (var v in root.GetVisualDescendants())
        {
            var layout = v switch
            {
                TextBlock tb when !string.IsNullOrEmpty(tb.Text) && tb.IsEffectivelyVisible => tb.TextLayout,
                Avalonia.Controls.Presenters.TextPresenter tp when !string.IsNullOrEmpty(tp.Text) => tp.TextLayout,
                _ => null
            };
            if (layout == null) continue;
            foreach (var line in layout.TextLines)
                foreach (var run in line.TextRuns)
                    if (run is Avalonia.Media.TextFormatting.ShapedTextRun sr && sr.GlyphRun.GlyphTypeface.FamilyName != "Noto Sans Thai"
                        && !new string(sr.Text.Span).EnumerateRunes().All(r => Rune.IsWhiteSpace(r) || r.Value >= 0x1F000))
                        bad.Add(sr.GlyphRun.GlyphTypeface.FamilyName + "[" + new string(sr.Text.Span) + "]");
        }
        return bad;
    }

    static void Save(Window window, string name)
    {
        try
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
            var frame = window.CaptureRenderedFrame() ?? window.GetLastRenderedFrame();
            if (frame == null) { Console.WriteLine("screenshot skipped: no frame"); return; }
            var outDir = Environment.GetEnvironmentVariable("SHOTS") ?? Path.GetTempPath();
            Directory.CreateDirectory(outDir);
            var path = Path.Combine(outDir, name + ".png");
            frame.Save(path);
            Console.WriteLine($"screenshot {path}");
        }
        catch (Exception ex) { Console.WriteLine("screenshot skipped: " + ex.Message); }
    }
}
