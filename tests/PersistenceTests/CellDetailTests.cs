using System.Text.Json;
using TableNote;

public static partial class T
{
    static void En() => Localizer.SetLanguage("en");

    static NoteDocument Doc()
    {
        En();
        var d = NoteDocument.CreateDefault();
        d.Rows.Clear();
        d.RowIds.Clear();
        d.Rows.Add(["Task", "body", "", ""]);
        d.Rows.Add(["Next", "", "done", ""]);
        d.EnsureRowIds();
        d.EnsureColumnIds();
        return d;
    }

    static CellDetail Grid(params string[] cells) => new()
    {
        Kind = CellDetailKinds.Table,
        Columns = ["Item", "Qty"],
        Rows = [cells.ToList()]
    };

    public static void TestCellDetailButtonChoice()
    {
        var d = Doc();
        Ok("no button until a cell or column is chosen", !CellDetailOps.ShowsButton(d, 0, 1) && !CellDetailOps.ShowsButton(d, 1, 1));
        Ok("one cell can show the button", CellDetailOps.SetCellButton(d, 0, 1, true) && CellDetailOps.ShowsButton(d, 0, 1) && !CellDetailOps.ShowsButton(d, 1, 1));
        Ok("the column shows it on every cell", CellDetailOps.SetColumnButton(d, 1, true) && CellDetailOps.ShowsButton(d, 0, 1) && CellDetailOps.ShowsButton(d, 1, 1));
        Ok("one cell of that column can hide it", CellDetailOps.SetCellButton(d, 1, 1, false) && CellDetailOps.ShowsButton(d, 0, 1) && !CellDetailOps.ShowsButton(d, 1, 1));
        Ok("turning the column off keeps the earlier per-cell button", CellDetailOps.SetColumnButton(d, 1, false) && CellDetailOps.ShowsButton(d, 0, 1) && !CellDetailOps.ShowsButton(d, 1, 1));
        Ok("turning the same cell off removes the button", CellDetailOps.SetCellButton(d, 0, 1, false) && !CellDetailOps.ShowsButton(d, 0, 1));
    }

    public static void TestCellDetailRoundTripAndOldFile()
    {
        En();
        var dir = Dir("detail");
        var p = Path.Combine(dir, "note.json");
        var d = Doc();
        var rowId = d.RowIdAt(0);
        var colId = d.ColumnIdAt(1);
        CellDetailOps.SetColumnButton(d, 1, true);
        CellDetailOps.SetCellButton(d, 1, 0, true);
        var table = Grid("nails", "10");
        table.Extra["note"] = JsonDocument.Parse("\"keep\"").RootElement.Clone();
        CellDetailOps.Set(d, rowId, colId, table);
        d.Reminders[rowId] = new ReminderInfo { Offsets = [15] };
        NoteStore.Save(p, d);

        using (var json = JsonDocument.Parse(File.ReadAllText(p)))
        {
            Ok("detail save keeps file version 5", json.RootElement.GetProperty("version").GetInt32() == NoteDocument.CurrentVersion);
            var key = CellDetailOps.Key(rowId, colId);
            var stored = json.RootElement.GetProperty("cellDetails").GetProperty("tables").GetProperty(key);
            Ok("detail grid is in note.json", stored.GetProperty("rows")[0][0].GetString() == "nails" && stored.GetProperty("rows")[0][1].GetString() == "10");
            Ok("unknown detail property is kept", stored.GetProperty("note").GetString() == "keep");
            Ok("column button id is stored", json.RootElement.GetProperty("cellDetails").GetProperty("columns").EnumerateArray().Any(e => e.GetString() == colId));
        }

        var loaded = NoteStore.Load(p).Document;
        var again = CellDetailOps.Get(loaded, rowId, colId);
        Ok("reload restores the detail grid", again != null && again!.Rows[0][0] == "nails" && again.Rows[0][1] == "10");
        Ok("reload restores the column button", CellDetailOps.ShowsButton(loaded, 0, 1) && CellDetailOps.ColumnButtonOn(loaded, 1));
        Ok("reload restores the per-cell button", CellDetailOps.ShowsButton(loaded, 1, 0));
        Ok("reload keeps reminders beside details", loaded.Reminders.TryGetValue(rowId, out var rem) && rem.Offsets.Contains(15));
        Ok("reload keeps the main cell text", loaded.Rows[0][0] == "Task" && loaded.Rows[0][1] == "body");

        var oldPath = Path.Combine(Dir("detail-old"), "note.json");
        const string oldFile = """{"version":5,"columns":["Title","Detail","Status","Due"],"columnRoles":["title","detail","status","due"],"columnIds":["c1","c2","c3","c4"],"rows":[["kept","body","",""]],"rowIds":["r1"],"reminders":{"r1":{"offsets":[60],"acked":[]}},"futureBox":{"x":1}}""";
        File.WriteAllText(oldPath, oldFile);
        var old = NoteStore.Load(oldPath);
        Ok("v2.4.2 file opens with its rows", old.Warning == null && old.Document.Rows[0][0] == "kept" && old.Document.RowIds[0] == "r1");
        Ok("v2.4.2 file has no detail buttons", CellDetailOps.IsEmpty(old.Document.CellDetails));
        Ok("unknown top-level field is kept", old.Document.ExtraFields.ContainsKey("futureBox"));
        NoteStore.Save(oldPath, old.Document);
        using (var saved = JsonDocument.Parse(File.ReadAllText(oldPath)))
        {
            Ok("saving an old file does not invent cellDetails", !saved.RootElement.TryGetProperty("cellDetails", out _));
            Ok("saving an old file does not bump version", saved.RootElement.GetProperty("version").GetInt32() == 5);
            Ok("saving an old file keeps the unknown field", saved.RootElement.TryGetProperty("futureBox", out var box) && box.GetProperty("x").GetInt32() == 1);
            Ok("saving an old file keeps the row", saved.RootElement.GetProperty("rows")[0][0].GetString() == "kept");
        }
    }

    public static void TestCellDetailForwardCompatible()
    {
        En();
        var dir = Dir("detail-fwd");
        var p = Path.Combine(dir, "note.json");
        const string file = """
        {"version":5,"columns":["Title","Detail","Status","Due"],"columnRoles":["title","detail","status","due"],
         "columnIds":["c1","c2","c3","c4"],"rows":[["t","","",""]],"rowIds":["r1"],
         "cellDetails":{"schema":1,"columns":["c2"],"futureAnchor":["r1"],"tables":{
            "r1/c2":{"kind":"chart","axis":"y","series":[1,2]},
            "r1/c1":{"kind":"table","columns":["A"],"rows":[["ok"]],"marker":true},
            "bad":{"kind":"table"},
            "r1/c9":5
         }}}
        """;
        File.WriteAllText(p, file);
        var loaded = NoteStore.Load(p);
        Ok("a bad detail block does not drop the table", loaded.Document.Rows[0][0] == "t" && loaded.Warning == null);
        var chart = CellDetailOps.Get(loaded.Document, "r1", "c2");
        Ok("unknown detail kind is kept", chart != null && chart!.Kind == "chart" && !CellDetailOps.IsTable(chart));
        var table = CellDetailOps.Get(loaded.Document, "r1", "c1");
        Ok("table kind still loads beside an unknown kind", table != null && table!.Rows[0][0] == "ok");
        Ok("bad detail keys are reported, not fatal", NoteStore.LastLoadIssues.Any(i => i.StartsWith("cellDetails", StringComparison.Ordinal)));
        NoteStore.Save(p, loaded.Document);
        using var json = JsonDocument.Parse(File.ReadAllText(p));
        var details = json.RootElement.GetProperty("cellDetails");
        Ok("future anchor on cellDetails is written back", details.GetProperty("futureAnchor")[0].GetString() == "r1");
        var savedChart = details.GetProperty("tables").GetProperty("r1/c2");
        Ok("unknown kind keeps axis and series", savedChart.GetProperty("kind").GetString() == "chart"
            && savedChart.GetProperty("axis").GetString() == "y" && savedChart.GetProperty("series").GetArrayLength() == 2);
        Ok("table marker is written back", details.GetProperty("tables").GetProperty("r1/c1").GetProperty("marker").GetBoolean());
        Ok("version stays 5 when details are present", json.RootElement.GetProperty("version").GetInt32() == 5);
    }

    public static void TestCellDetailBadBlockAndMoves()
    {
        En();
        var p = Path.Combine(Dir("detail-bad"), "note.json");
        File.WriteAllText(p, """{"version":5,"columns":["Title","Detail","Status","Due"],"columnRoles":["title","detail","status","due"],"rows":[["stay","","",""]],"cellDetails":"nope"}""");
        var bad = NoteStore.Load(p);
        Ok("a wrong cellDetails type does not wipe the note", bad.Document.Rows[0][0] == "stay" && CellDetailOps.IsEmpty(bad.Document.CellDetails));
        Ok("a wrong cellDetails type is logged", NoteStore.LastLoadIssues.Any(i => i.StartsWith("cellDetails:", StringComparison.Ordinal)));

        var d = Doc();
        var rowId = d.RowIdAt(0);
        var colId = d.ColumnIdAt(0);
        CellDetailOps.Set(d, rowId, colId, Grid("keep", "1"));
        CellDetailOps.SetCellButton(d, 0, 0, true);
        var movedRow = d.RowIds[0];
        RowOps.MoveRow(d, 0, 1);
        Ok("moving a row keeps its detail", CellDetailOps.Get(d, movedRow, colId)?.Rows[0][0] == "keep" && CellDetailOps.ShowsButton(d, 1, 0));
        ColumnOps.MoveColumn(d, 0, 2);
        var col = d.ColumnIds.IndexOf(colId);
        Ok("moving a column keeps its detail", col == 2 && CellDetailOps.Get(d, movedRow, colId)?.Rows[0][1] == "1" && CellDetailOps.ShowsButton(d, 1, col));

        var before = Snapshot.CloneLite(d);
        d.RemoveColumn(col);
        Ok("deleting a column drops its detail", CellDetailOps.Get(d, movedRow, colId) == null);
        var undo = new UndoStack();
        undo.Push(new SnapshotAction("delete column", before, Snapshot.CloneLite(d)));
        undo.Undo(d);
        var back = d.ColumnIds.IndexOf(colId);
        Ok("undo column delete restores the detail", back >= 0 && CellDetailOps.Get(d, movedRow, colId)?.Rows[0][0] == "keep");
        Ok("undo column delete restores the button", CellDetailOps.ShowsButton(d, d.RowIds.IndexOf(movedRow), back));

        var rowBefore = Snapshot.CloneLite(d);
        var rowIndex = d.RowIds.IndexOf(movedRow);
        d.RemoveRow(rowIndex);
        Ok("deleting a row drops its detail", CellDetailOps.Get(d, movedRow, colId) == null);
        undo.Push(new SnapshotAction("delete row", rowBefore, Snapshot.CloneLite(d)));
        undo.Undo(d);
        Ok("undo row delete restores the detail", CellDetailOps.Get(d, movedRow, colId)?.Rows[0][1] == "1");
    }

    public static void TestCellDetailExportAndLimits()
    {
        En();
        var plain = Doc();
        var (headers, rows) = Exporter.FromDocument(plain, respectHideDone: false);
        Ok("export without details matches the main columns", headers.SequenceEqual(plain.Columns) && rows[0].Cells.Count == plain.Columns.Count && rows[0].Cells[0] == "Task");

        var d = Doc();
        CellDetailOps.Set(d, d.RowIdAt(0), d.ColumnIdAt(1), Grid("nails", "10"));
        (headers, rows) = Exporter.FromDocument(d, respectHideDone: false);
        Ok("export adds one details column", headers.Count == d.Columns.Count + 1 && headers[^1] == Localizer.T("detail.export_header", d.Columns[1]));
        Ok("export writes the detail grid", rows[0].Cells[^1].Contains("nails", StringComparison.Ordinal) && rows[0].Cells[^1].Contains("10", StringComparison.Ordinal));
        Ok("a row without a detail exports a blank detail cell", rows[1].Cells[^1].Length == 0);
        Ok("the main cell text is unchanged", rows[0].Cells[1] == "body");

        var wide = new CellDetail { Kind = "table", Columns = [], Rows = [Enumerable.Range(0, 20).Select(i => "c" + i).ToList()] };
        var issues = new List<string>();
        var norm = CellDetailOps.Normalize(wide, issues, "cellDetails");
        Ok("over-wide detail is clipped to the limit", norm.Columns.Count == CellDetailLimits.MaxColumns && norm.Rows[0].Count == CellDetailLimits.MaxColumns);
        Ok("the clip is reported", issues.Any(i => i.Contains("columns", StringComparison.Ordinal)));
        var again = CellDetailOps.Normalize(CellDetailOps.Clone(norm));
        Ok("normalize is stable", CellDetailOps.ToJson(new CellDetailStore { Tables = { ["a/b"] = norm } }) == CellDetailOps.ToJson(new CellDetailStore { Tables = { ["a/b"] = again } }));

        var jagged = CellDetailOps.Normalize(new CellDetail { Kind = "table", Columns = ["A"], Rows = [["only", "extra"]] });
        Ok("a short header list grows instead of dropping a cell", jagged.Columns.Count == 2 && jagged.Rows[0][1] == "extra");
    }
}
