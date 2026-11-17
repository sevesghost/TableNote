using System.Text.Json;
using TableNote;

public static partial class T
{
    static void Lang(string code, Action act)
    {
        var prev = Localizer.Current;
        Localizer.SetLanguage(code);
        try { act(); }
        finally { Localizer.SetLanguage(prev); }
    }

    public static void TestSizeValidation()
    {
        const int lo = TableLimits.MinColumnWidth, hi = TableLimits.MaxColumnWidth;
        Ok("column range is 72–800, default 140", lo == 72 && hi == 800 && TableLimits.DefaultColumnWidth == 140);
        Ok("row range is 32–400", SizeSpec.MinRowHeight == 32 && SizeSpec.MaxRowHeight == 400);

        foreach (var bad in new[] { "71", "801", "0", "-5", "140.5", "", "abc", "๑๔๐" })
            Ok($"column rejects {Json(bad)}", !SizeSpec.CanApply(bad, lo, hi) && !SizeSpec.IsAccepted(bad, lo, hi, out _),
                SizeSpec.ValidationMessage(bad, lo, hi) ?? "");
        Ok("column accepts trimmed 140", SizeSpec.IsAccepted(" 140 ", lo, hi, out var n) && n == 140 && SizeSpec.CanApply(" 140 ", lo, hi));
        Ok("column accepts the new max 800 and the old max 420",
            SizeSpec.IsAccepted("800", lo, hi, out var a) && a == 800 && SizeSpec.IsAccepted("420", lo, hi, out _));
        Ok("column rejects 801 with the range", SizeSpec.ValidationMessage("801", lo, hi) == Localizer.T("size.out_of_range", lo, hi));
        Ok("letters are not a number", SizeSpec.ValidationMessage("abc", lo, hi) == Localizer.T("size.not_number"));
        Ok("empty asks for a number", SizeSpec.ValidationMessage("", lo, hi) == Localizer.T("size.empty"));
        Ok("Thai digits are not a number", SizeSpec.ValidationMessage("๑๔๐", lo, hi) == Localizer.T("size.not_number"));

        Ok("row accepts 71 and 140 and 400", SizeSpec.CanApply("71", 32, 400) && SizeSpec.CanApply(" 140 ", 32, 400) && SizeSpec.CanApply("400", 32, 400));
        Ok("row rejects 31, 401, 801, 0, -5, decimals, letters, empty, Thai digits",
            new[] { "31", "401", "801", "0", "-5", "140.5", "", "abc", "๑๔๐" }.All(s => !SizeSpec.CanApply(s, 32, 400)));
        Ok("Auto enables OK even when the box is empty or not a number",
            SizeSpec.CanApply("", 32, 400, auto: true) && SizeSpec.CanApply("abc", 32, 400, auto: true) && !SizeSpec.CanApply("abc", 32, 400));

        var doc = NoteDocument.CreateDefault();
        var before = doc.ColumnWidth(0);
        Ok("71 is not written and not clamped", !SizeSpec.ApplyColumnWidths(doc, [0], 71) && doc.ColumnWidth(0) == before);
        Ok("801 is not written and not clamped", !SizeSpec.ApplyColumnWidths(doc, [0], 801) && doc.ColumnWidth(0) == before);
        Ok("0 is not written", !SizeSpec.ApplyColumnWidths(doc, [0], 0) && doc.ColumnWidth(0) == before);
        Ok("row 31 and 401 are not written", !SizeSpec.ApplyRowHeights(doc, [0], 31) && !SizeSpec.ApplyRowHeights(doc, [0], 401) && SizeSpec.StoredRowHeight(doc, 0) is null);

        Lang("en", () =>
        {
            Ok("en column label", Localizer.T("size.col_label") == "Column width (pixels)");
            Ok("en row label", Localizer.T("size.row_label") == "Row height (pixels)");
            Ok("en multiple", Localizer.T("size.multiple") == "Multiple values");
        });
        Lang("th", () =>
        {
            Ok("th column label", Localizer.T("size.col_label") == "ความกว้างคอลัมน์ (พิกเซล)");
            Ok("th row label", Localizer.T("size.row_label") == "ความสูงแถว (พิกเซล)");
            Ok("th multiple", Localizer.T("size.multiple") == "หลายค่า");
        });
    }

    static string Json(string s) => s.Length == 0 ? "empty" : s;

    public static void TestSizeTargetsAndHiddenColumns()
    {
        var doc = NoteDocument.CreateDefault();
        Ok("one column when the click is outside the selection",
            SizeSpec.TargetColumns(doc, 3, 0, 1, true).SequenceEqual([3]));
        Ok("every column in the shift-selection",
            SizeSpec.TargetColumns(doc, 1, 0, 2, true).SequenceEqual([0, 1, 2]));
        Ok("a single selected cell is just that column",
            SizeSpec.TargetColumns(doc, 1, 1, 1, true).SequenceEqual([1]));
        doc.SetColumnWidth(0, 200);
        doc.SetColumnWidth(1, 200);
        doc.SetColumnWidth(2, 360);
        var same = SizeSpec.Describe([200, 200]);
        var mixed = SizeSpec.Describe([200, 360]);
        Ok("same widths prefill that number", !same.Mixed && same.Value == 200);
        Ok("different widths stay blank", mixed.Mixed && mixed.Value is null);
        var rows = SizeSpec.Describe(new int?[] { null, null });
        var rowMix = SizeSpec.Describe(new int?[] { null, 48 });
        Ok("all-auto prefill is auto", !rows.Mixed && rows.Value is null);
        Ok("auto and a number is mixed", rowMix.Mixed);

        doc.SetColumnWidth(1, 220);
        SizeSpec.IsHiddenOverride = (_, col) => col == 1;
        try
        {
            Ok("hidden column is not a target", SizeSpec.TargetColumns(doc, 1, 0, 2, true).Count == 0);
            Ok("a selection skips the hidden column", SizeSpec.TargetColumns(doc, 0, 0, 2, true).SequenceEqual([0, 2]));
            Ok("shown width is the stored width, not 0", SizeSpec.StoredColumnWidth(doc, 1) == 220);
            SizeSpec.ApplyColumnWidths(doc, [0, 1, 2], 300);
            Ok("hidden stored width is not overwritten", doc.ColumnWidth(1) == 220 && doc.ColumnWidth(0) == 300 && doc.ColumnWidth(2) == 300);
            doc.ColumnWidths[1] = 0;
            Ok("a stored 0 is not what the dialog shows", SizeSpec.StoredColumnWidth(doc, 1) == TableLimits.DefaultColumnWidth);
        }
        finally { SizeSpec.IsHiddenOverride = null; doc.SetColumnWidth(1, 220); }
        Ok("without 2.1 nothing is hidden", Enumerable.Range(0, doc.Columns.Count).All(i => !SizeSpec.IsHiddenColumn(doc, i)));
    }

    public static void TestSizeRoundTrip()
    {
        var p = Path.Combine(Dir("size"), "note.json");
        var d = Sample(4);
        var id1 = d.RowIdAt(1);
        var id2 = d.RowIdAt(2);
        Ok("width 640 (above the old 420 cap)", SizeSpec.ApplyColumnWidths(d, [0], 640) && d.ColumnWidth(0) == 640);
        Ok("width 800", SizeSpec.ApplyColumnWidths(d, [1], 800));
        d.SetColumnWidth(2, 120);
        Ok("row height 80", SizeSpec.ApplyRowHeights(d, [1], 80));
        RowStyleOps.SetColor(d, id1, "green");
        RowStyleOps.SetHighlight(d, id2, true);
        var cell = d.Rows[1][0];
        NoteStore.Save(p, d);
        using (var j = JsonDocument.Parse(File.ReadAllText(p)))
        {
            Ok("version stays 5", j.RootElement.GetProperty("version").GetInt32() == NoteDocument.CurrentVersion);
            var widths = j.RootElement.GetProperty("columnWidths");
            Ok("columnWidths saved", widths[0].GetInt32() == 640 && widths[1].GetInt32() == 800 && widths[2].GetInt32() == 120);
            var heights = j.RootElement.GetProperty("rowHeights");
            Ok("only the set row is stored", heights.EnumerateObject().Count() == 1 && heights.GetProperty(id1).GetInt32() == 80);
            Ok("auto row has no height entry", !heights.TryGetProperty(id2, out _));
        }
        var r = NoteStore.Load(p).Document;
        Ok("widths reload", r.ColumnWidth(0) == 640 && r.ColumnWidth(1) == 800 && r.ColumnWidth(2) == 120);
        Ok("height reloads and the other rows stay auto", SizeSpec.StoredRowHeight(r, 1) == 80 && SizeSpec.StoredRowHeight(r, 0) is null && SizeSpec.StoredRowHeight(r, 2) is null);
        Ok("colour on the sized row survived", RowStyleOps.Get(r, id1).Color == "green" && r.Rows[1][0] == cell);
        Ok("highlight-only row has no height", RowStyleOps.Get(r, id2).Highlight && !r.RowHeights.ContainsKey(id2));
        Ok("rowHeights is not an unknown extra field", !r.ExtraFields.ContainsKey("rowHeights"));

        var moved = r.RowIds.IndexOf(id1);
        RowOps.MoveRow(r, moved, 0);
        Ok("height follows the row id", SizeSpec.StoredRowHeight(r, r.RowIds.IndexOf(id1)) == 80);
        NoteStore.Save(p, r);
        var again = NoteStore.Load(p).Document;
        Ok("moved height reloads", SizeSpec.StoredRowHeight(again, again.RowIds.IndexOf(id1)) == 80 && again.ColumnWidth(0) == 640);
    }

    public static void TestSizeOldFileLoads()
    {
        var (r, _) = LoadJson("oldsize", """
            {"version":3,"columns":["หัวข้อ","รายละเอียด","สถานะ","กำหนดส่ง"],"rows":[["keep","me","",""]],"rowIds":["r1"],
             "columnRoles":["title","detail","status","due"],"columnWidths":[90,140,140,180]}
            """);
        Ok("old file cells unchanged", r.Document.Rows[0][0] == "keep" && r.Document.Rows[0][1] == "me" && r.Document.Rows.Count == 1);
        Ok("old widths kept (no rowHeights invented)",
            r.Document.ColumnWidth(0) == 90 && r.Document.ColumnWidth(1) == 140 && r.Document.ColumnWidth(3) == 180 && r.Document.RowHeights.Count == 0);
        Ok("no warning for a file that simply has no sizes", r.Warning == null);

        var (bare, dir) = LoadJson("baresize", """
            {"version":2,"columns":["หัวข้อ","รายละเอียด","สถานะ","กำหนดส่ง"],"rows":[["hello","world","",""]]}
            """);
        Ok("file with no width or height fields opens", bare.Document.Rows[0][0] == "hello" && bare.Document.Rows[0][1] == "world" && bare.Document.RowHeights.Count == 0);
        Ok("missing widths still get the usual defaults",
            bare.Document.ColumnWidth(0) == 140 && bare.Document.ColumnWidth(2) == 120 && bare.Document.ColumnWidth(3) == 200);
        var p = Path.Combine(dir, "note.json");
        NoteStore.Save(p, bare.Document);
        Ok("saving an auto-only note does not add rowHeights", !File.ReadAllText(p).Contains("\"rowHeights\""));
        Ok("saved version is still 5", NoteStore.Load(p).Document.Version == 5 && NoteDocument.CurrentVersion == 5);

        var (bad, _) = LoadJson("badheight", """
            {"version":5,"columns":["A","B","C","D"],"columnRoles":["title","detail","status","due"],
             "columnIds":["c0","c1","c2","c3"],"rowIds":["r1","r2"],"rows":[["one","two","",""],["three","four","",""]],
             "columnWidths":[140,140,120,200],
             "rowHeights":{"r1":"tall","r2":9999,"r1b":40}}
            """);
        Ok("bad heights are skipped, the table still opens",
            bad.Warning == null && bad.Document.Rows[0][0] == "one" && bad.Document.Rows[1][0] == "three"
            && SizeSpec.StoredRowHeight(bad.Document, 0) is null && SizeSpec.StoredRowHeight(bad.Document, 1) is null
            && NoteStore.LastLoadIssues.Any(i => i.StartsWith("rowHeights", StringComparison.Ordinal)));
    }

    public static void TestSizeUndo()
    {
        var d = Sample(3);
        var undo = new UndoStack();
        var before = Snapshot.CloneLite(d);
        SizeSpec.ApplyColumnWidths(d, [0, 1], 500);
        undo.Push(new SnapshotAction("w", before, Snapshot.CloneLite(d)));
        Ok("two columns set in one step", d.ColumnWidth(0) == 500 && d.ColumnWidth(1) == 500);
        undo.Undo(d);
        Ok("undo restores both widths", d.ColumnWidth(0) == TableLimits.DefaultColumnWidth && d.ColumnWidth(1) == TableLimits.DefaultColumnWidth);
        undo.Redo(d);
        Ok("redo applies both widths", d.ColumnWidth(0) == 500 && d.ColumnWidth(1) == 500);

        var id = d.RowIdAt(0);
        RowStyleOps.SetColor(d, id, "blue");
        var b2 = Snapshot.CloneLite(d);
        SizeSpec.ApplyRowHeights(d, [0], 64);
        undo.Push(new SnapshotAction("h", b2, Snapshot.CloneLite(d)));
        Ok("height stored beside the colour", SizeSpec.StoredRowHeight(d, 0) == 64 && RowStyleOps.Get(d, id).Color == "blue");
        undo.Undo(d);
        Ok("undo height returns to auto and keeps the colour", SizeSpec.StoredRowHeight(d, 0) is null && RowStyleOps.Get(d, id).Color == "blue");
        undo.Redo(d);
        Ok("redo height", SizeSpec.StoredRowHeight(d, 0) == 64);

        SizeSpec.ResetColumnWidths(d, [0]);
        Ok("reset width is the default 140", d.ColumnWidth(0) == 140 && d.ColumnWidth(1) == 500);
        SizeSpec.ApplyRowHeights(d, [0], null);
        Ok("auto removes the entry", !d.RowHeights.ContainsKey(id));

        d.RowHeights[id] = 72;
        var captured = RowDeleteAction.Capture(d, 0);
        d.RemoveRow(0);
        Ok("delete drops the height", !d.RowHeights.ContainsKey(id));
        captured.Undo(d);
        Ok("undo delete brings the height back", d.RowIds.Contains(id) && d.RowHeights.TryGetValue(id, out var back) && back == 72);
    }
}
