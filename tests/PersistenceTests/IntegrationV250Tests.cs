using System.IO.Compression;
using System.Text.Json;
using TableNote;

// Integration v2.5.0: a 2.4.2 file (version 5, none of the new fields) still opens,
// and CSV/XLSX/Excel copy keep collapsed columns (only the on-screen view hides them).
public static partial class T
{
    public static void TestV242FileRoundTripsWithoutNewFields()
    {
        const string file = """
        {
          "version": 5,
          "columns": ["หัวข้อ", "รายละเอียด", "สถานะ", "กำหนดส่ง"],
          "columnRoles": ["title", "detail", "status", "due"],
          "columnIds": ["c-title", "c-detail", "c-status", "c-due"],
          "columnWidths": [180, 240, 120, 200],
          "rows": [
            ["งานประชุม", "รายละเอียด, มีจุลภาค", "todo", "2026-12-01 09:00"],
            ["ส่งของ", "ครบ", "done", "2026-12-02 15:30"]
          ],
          "rowIds": ["r1", "r2"],
          "columnSettings": { "c-detail": { "type": "text" } },
          "rowStyles": { "r1": { "color": "yellow", "highlight": true } },
          "reminders": { "r1": { "offsets": [60, 15], "acked": [] } },
          "recurrence": { "r2": "weekly" },
          "hideDone": false,
          "window": { "width": 900, "height": 600, "layer": "window" },
          "futureWidget": { "keep": 1 }
        }
        """;
        var (loaded, dir) = LoadJson("v242", file);
        var doc = loaded.Document;
        Ok("v2.4.2 file opens with no warning", loaded.Warning == null, loaded.Warning ?? "");
        Ok("v2.4.2 file has no load issues", loaded.Issues.Count == 0, string.Join(" | ", loaded.Issues));
        Ok("v2.4.2 version stays 5", doc.Version == 5 && NoteDocument.CurrentVersion == 5);
        Ok("v2.4.2 rows and headers are intact",
            doc.Columns.SequenceEqual(new[] { "หัวข้อ", "รายละเอียด", "สถานะ", "กำหนดส่ง" }) &&
            doc.Rows.Count == 2 &&
            doc.Rows[0][0] == "งานประชุม" && doc.Rows[0][1] == "รายละเอียด, มีจุลภาค" &&
            doc.Rows[1][0] == "ส่งของ" && doc.Rows[1][2] == "done");
        Ok("v2.4.2 ids, widths, settings, style, reminder and repeat are intact",
            doc.RowIds.SequenceEqual(new[] { "r1", "r2" }) &&
            doc.ColumnIds.SequenceEqual(new[] { "c-title", "c-detail", "c-status", "c-due" }) &&
            doc.ColumnWidths.SequenceEqual(new[] { 180, 240, 120, 200 }) &&
            doc.ColumnSettings.TryGetValue("c-detail", out var setting) && setting.Type == ColumnTypes.Text &&
            doc.RowStyles.TryGetValue("r1", out var style) && style.Color == "yellow" && style.Highlight &&
            doc.Reminders.TryGetValue("r1", out var rem) && rem.Offsets.SequenceEqual(new[] { 60, 15 }) &&
            doc.Recurrence.TryGetValue("r2", out var rec) && rec == "weekly");
        Ok("v2.4.2 file has empty defaults for the new fields",
            doc.ColumnGroups.Count == 0 &&
            CellDetailOps.IsEmpty(doc.CellDetails) &&
            doc.AlertBoxes.Count == 0);
        Ok("v2.4.2 unknown field is kept",
            doc.ExtraFields.TryGetValue("futureWidget", out var extra) && extra.GetProperty("keep").GetInt32() == 1);

        var path = Path.Combine(dir, "note.json");
        NoteStore.Save(path, doc);
        var again = NoteStore.Load(path);
        var saved = again.Document;
        Ok("v2.4.2 round-trip still opens cleanly", again.Warning == null && again.Issues.Count == 0, string.Join(" | ", again.Issues));
        Ok("v2.4.2 round-trip keeps version 5 and the table",
            saved.Version == 5 &&
            saved.Rows.Count == 2 && saved.Rows[0][0] == "งานประชุม" && saved.Rows[0][1] == "รายละเอียด, มีจุลภาค" &&
            saved.Rows[1][2] == "done" && saved.RowIds[1] == "r2" && saved.ColumnIds[2] == "c-status");
        Ok("v2.4.2 round-trip keeps settings, style, reminder, repeat and the unknown field",
            saved.ColumnSettings.ContainsKey("c-detail") &&
            saved.RowStyles.TryGetValue("r1", out var style2) && style2.Highlight && style2.Color == "yellow" &&
            saved.Reminders["r1"].Offsets.SequenceEqual(new[] { 60, 15 }) &&
            saved.Recurrence["r2"] == "weekly" &&
            saved.ExtraFields.TryGetValue("futureWidget", out var extra2) && extra2.GetProperty("keep").GetInt32() == 1);
        Ok("v2.4.2 round-trip does not invent detail tables or alert boxes",
            CellDetailOps.IsEmpty(saved.CellDetails) && saved.AlertBoxes.Count == 0 && saved.ColumnGroups.Count == 0);
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        Ok("v2.4.2 save writes version 5 and does not invent cellDetails",
            json.RootElement.GetProperty("version").GetInt32() == 5 &&
            !json.RootElement.TryGetProperty("cellDetails", out _));
    }

    public static void TestExportAndExcelCopyIncludeCollapsedColumns()
    {
        var prev = Localizer.Current;
        try
        {
            Localizer.SetLanguage("en");
            var doc = NoteDocument.CreateDefault();
            doc.Rows.Clear();
            doc.RowIds.Clear();
            doc.Rows.Add(["Parent", "body", "", "2026-12-01 09:00"]);
            doc.EnsureRowIds();
            doc.EnsureColumnIds();
            var created = ColumnGroupOps.Create(doc, 0, 2);
            Ok("group created for export", created.Ok, created.Error ?? "");
            var child = doc.ColumnIds.IndexOf(doc.ColumnGroups[0].ChildIds[0]);
            var childName = doc.Columns[child];
            doc.SetCellRaw(0, child, "HIDDEN-CHILD-XYZ");
            var collapsed = ColumnGroupOps.Toggle(doc, doc.ColumnIds.IndexOf(doc.ColumnGroups[0].ParentId));
            Ok("group is collapsed", collapsed.Ok && ColumnGroupOps.IsHidden(doc, child), collapsed.Error ?? "");

            var (headers, rows) = Exporter.FromDocument(doc, respectHideDone: false);
            Ok("FromDocument keeps the collapsed column",
                headers.Count == doc.Columns.Count &&
                headers[child] == childName &&
                rows.Count == 1 && rows[0].Cells[child] == "HIDDEN-CHILD-XYZ" &&
                rows[0].Cells[0] == "Parent");

            var dir = Dir("collapsed-export");
            var cols = new ExportColumns(doc.StatusColumnIndex(), doc.DueColumnIndex());
            var csvPath = Path.Combine(dir, "table.csv");
            var xlsxPath = Path.Combine(dir, "table.xlsx");
            Exporter.Export(rows, headers, csvPath, ExportFormat.Csv, cols);
            Exporter.Export(rows, headers, xlsxPath, ExportFormat.Xlsx, cols);
            var csv = File.ReadAllText(csvPath);
            Ok("CSV includes the collapsed column header and cell",
                csv.Contains(childName) && csv.Contains("HIDDEN-CHILD-XYZ") && csv.Contains("Parent"));
            using (var zip = ZipFile.OpenRead(xlsxPath))
            {
                using var reader = new StreamReader(zip.GetEntry("xl/worksheets/sheet1.xml")!.Open());
                var sheet = reader.ReadToEnd();
                Ok("XLSX includes the collapsed column header and cell",
                    sheet.Contains(childName) && sheet.Contains("HIDDEN-CHILD-XYZ") && sheet.Contains("Parent"));
            }

            var whole = ExcelCopy.WholeTable(doc, includeHeaders: true);
            var selection = ExcelCopy.Selection(doc, 0, 0, 0, doc.Columns.Count - 1);
            Ok("Excel copy of the whole table includes the collapsed column",
                whole != null && whole.Contains(childName) && whole.Contains("HIDDEN-CHILD-XYZ"));
            Ok("Excel copy of a selection that covers the collapsed column includes its cell",
                selection != null && selection.Contains("HIDDEN-CHILD-XYZ") && selection.Contains("Parent"));
        }
        finally { Localizer.SetLanguage(prev); }
    }
}
