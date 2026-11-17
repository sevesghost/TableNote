using System.Text.Json;
using TableNote;

// Status column and reminder date column can be deleted and added again, at most once each.
// A 2.4.2 / 2.5.0 file that has no removal marker still opens with both columns.
public static partial class T
{
    static NoteDocument Fresh()
    {
        En();
        var d = NoteDocument.CreateDefault();
        d.Rows.Clear();
        d.RowIds.Clear();
        d.Rows.Add(["Task", "body", "done", "2026-12-01 09:00"]);
        d.Rows.Add(["Next", "more", "", ""]);
        d.EnsureRowIds();
        d.EnsureColumnIds();
        return d;
    }

    public static void TestDeleteAndReaddStatusAndDue()
    {
        var d = Fresh();
        var session = new TableSession(d);
        var status = d.StatusColumnIndex();
        var due = d.DueColumnIndex();
        d.Recurrence[d.RowIdAt(0)] = "daily";
        d.Reminders[d.RowIdAt(0)] = new ReminderInfo { Offsets = [15] };
        d.Snoozes[d.RowIdAt(0)] = "2026-12-01 09:10";
        Ok("both built-in columns start present", status == 2 && due == 3 && d.Columns.Count == 4);

        string? asked = null;
        var canceled = session.DeleteColumnAtAsync(due, m => { asked = m; return Task.FromResult(false); }).GetAwaiter().GetResult();
        Ok("deleting the reminder column asks first",
            !canceled.Changed && asked == Localizer.T("delete.column_has_text", DeletePlanner.ColumnLabel(d, due)) && d.DueColumnIndex() == due,
            asked ?? "");

        var removedDue = session.DeleteColumnAtAsync(due, _ => Task.FromResult(true)).GetAwaiter().GetResult();
        Ok("reminder column delete removes its cells",
            removedDue.Changed && d.DueColumnIndex() < 0 && d.DueColumnOmitted && d.Columns.Count == 3 && d.Rows[0].Count == 3 && d.Rows[0][2] == "done");
        Ok("reminder column delete clears repeat and pending reminders",
            d.Recurrence.Count == 0 && d.Reminders.Count == 0 && d.Snoozes.Count == 0 && d.Acknowledged.Count == 0);

        status = d.StatusColumnIndex();
        var removedStatus = session.DeleteColumnAtAsync(status, _ => Task.FromResult(true)).GetAwaiter().GetResult();
        Ok("status column delete removes its cells",
            removedStatus.Changed && d.StatusColumnIndex() < 0 && d.StatusColumnOmitted && d.Rows[0].SequenceEqual(["Task", "body"]));

        var choices = SystemColumnToggle.AddChoices(d);
        Ok("add menu enables both missing columns and keeps a plain column",
            choices.Count == 3 && choices[0].Enabled && choices[0].Role == "" &&
            choices[1].Role == ColumnRoles.Status && choices[1].Enabled && choices[1].DisabledReasonKey == null &&
            choices[2].Role == ColumnRoles.Due && choices[2].Enabled);

        Ok("re-added status column is empty", session.AddSystemColumn(ColumnRoles.Status) == null && d.StatusColumnIndex() == d.Columns.Count - 1);
        Ok("re-added status cells are blank and the flag is clear",
            !d.StatusColumnOmitted && d.Rows.All(r => r[d.StatusColumnIndex()] == "") && d.Rows[0][0] == "Task");
        var statusAgain = session.AddSystemColumn(ColumnRoles.Status);
        Ok("only one status column", statusAgain == Localizer.T("col.only_one_status") && d.Columns.Count(c => true) == 3 &&
            d.ColumnRolesList.Count(r => r == ColumnRoles.Status) == 1, statusAgain ?? "");

        Ok("re-added reminder column is empty", session.AddSystemColumn(ColumnRoles.Due) == null && d.DueColumnIndex() == d.Columns.Count - 1);
        Ok("re-added reminder cells are blank",
            !d.DueColumnOmitted && d.Rows.All(r => r[d.DueColumnIndex()] == "") && d.Recurrence.Count == 0);
        var dueAgain = session.AddSystemColumn(ColumnRoles.Due);
        Ok("only one reminder column", dueAgain == Localizer.T("col.only_one_due") && d.ColumnRolesList.Count(r => r == ColumnRoles.Due) == 1, dueAgain ?? "");

        var present = SystemColumnToggle.AddChoices(d);
        Ok("add menu disables a column that already exists",
            !present[1].Enabled && present[1].DisabledReasonKey == "col.only_one_status" &&
            !present[2].Enabled && present[2].DisabledReasonKey == "col.only_one_due");
    }

    public static void TestRemovedColumnsStayRemovedAcrossSave()
    {
        var d = Fresh();
        d.AddColumn("Status"); // a normal column that happens to use the default name
        d.AddColumn("Due date");
        d.Rows[0][d.Columns.Count - 2] = "not-the-status";
        d.Rows[0][d.Columns.Count - 1] = "not-the-due";
        d.Recurrence[d.RowIdAt(1)] = "weekly";
        d.RemoveColumn(d.DueColumnIndex());
        d.RemoveColumn(d.StatusColumnIndex());
        d.Normalize();
        Ok("normalize does not restore a removed status or reminder column",
            d.StatusColumnIndex() < 0 && d.DueColumnIndex() < 0 && d.StatusColumnOmitted && d.DueColumnOmitted);
        Ok("a column named Status is not adopted after status was removed",
            d.Columns.Contains("Status") && d.ColumnRolesList[d.Columns.IndexOf("Status")] != ColumnRoles.Status &&
            d.GetCell(0, d.Columns.IndexOf("Status")) == "not-the-status");
        Ok("a column named Due date is not adopted after the reminder column was removed",
            d.Columns.Contains("Due date") && d.DueColumnIndex() < 0);
        d.HideDone = true;
        var (headers, rows) = Exporter.FromDocument(d, respectHideDone: true);
        Ok("hide-done does not treat the lookalike Status column as the status column",
            rows.Count == 2 && headers.Contains("Status") && !headers.Contains("done"));

        var path = Path.Combine(Dir("toggle-save"), "note.json");
        NoteStore.Save(path, d);
        using (var json = JsonDocument.Parse(File.ReadAllText(path)))
        {
            Ok("file version stays 6", json.RootElement.GetProperty("version").GetInt32() == 6 && NoteDocument.CurrentVersion == 6);
            Ok("removal markers are stored",
                json.RootElement.GetProperty("statusColumnOmitted").GetBoolean() &&
                json.RootElement.GetProperty("dueColumnOmitted").GetBoolean());
        }
        var loaded = NoteStore.Load(path).Document;
        Ok("reopen keeps both columns removed",
            loaded.StatusColumnOmitted && loaded.DueColumnOmitted &&
            loaded.StatusColumnIndex() < 0 && loaded.DueColumnIndex() < 0 &&
            loaded.Columns.Contains("Status") && loaded.Columns.Contains("Due date") &&
            loaded.ColumnRolesList[loaded.Columns.IndexOf("Status")] != ColumnRoles.Status &&
            loaded.GetCell(0, 0) == "Task" && loaded.Recurrence.Count == 0);
        Ok("version was not bumped", loaded.Version == NoteDocument.CurrentVersion);
    }

    public static void TestOldFilesStillHaveBothColumns()
    {
        const string v242 = """
        {"version":5,"columns":["หัวข้อ","รายละเอียด","สถานะ","กำหนดส่ง"],
         "columnRoles":["title","detail","status","due"],"columnIds":["t","d","s","u"],
         "rows":[["งาน","รายละเอียด","todo","2026-12-01 09:00"]],"rowIds":["r1"],
         "recurrence":{"r1":"daily"},"reminders":{"r1":{"offsets":[60],"acked":[]}}}
        """;
        var old = LoadJson("toggle-old", v242).r.Document;
        Ok("v2.4.2 file opens with status and the reminder column",
            old.StatusColumnIndex() == 2 && old.DueColumnIndex() == 3 &&
            !old.StatusColumnOmitted && !old.DueColumnOmitted &&
            old.Columns[2] == "สถานะ" && old.Columns[3] == "กำหนดส่ง" &&
            old.Rows[0][2] == "todo" && old.Recurrence["r1"] == "daily" &&
            old.Version == NoteDocument.CurrentVersion);

        const string v250 = """
        {"version":6,"columns":["Title","Details","Status","Due date"],
         "columnRoles":["title","detail","status","due"],
         "rows":[["a","b","doing","2026-01-02"]],"rowIds":["r1"]}
        """;
        var current = LoadJson("toggle-v6", v250).r.Document;
        Ok("current v2.5.0 file without the new fields keeps both columns",
            current.StatusColumnIndex() == 2 && current.DueColumnIndex() == 3 &&
            !current.StatusColumnOmitted && !current.DueColumnOmitted && current.Rows[0][0] == "a");

        var path = Path.Combine(Dir("toggle-default-save"), "note.json");
        En();
        NoteStore.Save(path, NoteDocument.CreateDefault());
        var text = File.ReadAllText(path);
        Ok("a note that still has both columns does not write the removal markers",
            !text.Contains("statusColumnOmitted") && !text.Contains("dueColumnOmitted"));
        var round = NoteStore.Load(path).Document;
        Ok("that note still loads both columns", round.StatusColumnIndex() >= 0 && round.DueColumnIndex() >= 0 && round.Columns.Count == 4);

        // Missing column and no marker: the old repair still adds it.
        var noDue = NoteDocument.CreateDefault();
        noDue.Columns.RemoveAt(3);
        noDue.ColumnRolesList.RemoveAt(3);
        noDue.EnsureReminderColumns();
        Ok("a file that never removed the reminder column still gets one",
            !noDue.DueColumnOmitted && noDue.DueColumnIndex() == noDue.Columns.Count - 1);
    }

    public static void TestPre210FileWithoutColumnRolesResolvesByName()
    {
        // Version 2 is before 2.1.0 (version 3), which is when columnRoles was added. The key is absent on purpose.
        const string file = """
        {"version":2,"columns":["หัวข้อ","รายละเอียด","สถานะ","กำหนดส่ง"],
         "rows":[["งานประชุม","รายละเอียดงาน","todo","2026-12-01 09:00"]],"rowIds":["r1"]}
        """;
        var loaded = LoadJson("pre210", file).r;
        var doc = loaded.Document;
        Ok("pre-2.1.0 file without columnRoles opens", loaded.Warning == null && loaded.Issues.Count == 0,
            (loaded.Warning ?? "") + " | " + string.Join(" | ", loaded.Issues));
        Ok("pre-2.1.0 name fallback resolves status and due, and does not add columns",
            doc.Columns.Count == 4 &&
            doc.StatusColumnIndex() == 2 && doc.DueColumnIndex() == 3 &&
            doc.ColumnRolesList[2] == ColumnRoles.Status && doc.ColumnRolesList[3] == ColumnRoles.Due &&
            doc.ColumnRolesList[0] == ColumnRoles.Title && doc.ColumnRolesList[1] == ColumnRoles.Detail &&
            !doc.StatusColumnOmitted && !doc.DueColumnOmitted &&
            doc.GetCell(0, 0) == "งานประชุม" && doc.GetCell(0, 2) == "todo" && doc.GetCell(0, 3) == "2026-12-01 09:00",
            string.Join("|", doc.Columns) + " / " + string.Join("|", doc.ColumnRolesList));

        const string omitted = """
        {"version":2,"columns":["หัวข้อ","รายละเอียด","สถานะ","กำหนดส่ง"],
         "rows":[["งานประชุม","รายละเอียดงาน","todo","2026-12-01 09:00"]],"rowIds":["r1"],
         "statusColumnOmitted":true,"dueColumnOmitted":true}
        """;
        var gone = LoadJson("pre210-omitted", omitted).r.Document;
        Ok("the same names are not status or due when the removal flags are set",
            gone.Columns.Count == 4 &&
            gone.StatusColumnOmitted && gone.DueColumnOmitted &&
            gone.StatusColumnIndex() < 0 && gone.DueColumnIndex() < 0 &&
            gone.ColumnRolesList[2] != ColumnRoles.Status && gone.ColumnRolesList[3] != ColumnRoles.Due &&
            gone.GetCell(0, 2) == "todo" && gone.GetCell(0, 3) == "2026-12-01 09:00");
    }

    public static void TestNameLookalikeIsNotTheSpecialColumn()
    {
        foreach (var (lang, statusName, dueName) in new[] { ("en", "Status", "Due date"), ("th", "สถานะ", "กำหนดส่ง") })
        {
            Localizer.SetLanguage(lang);
            var d = NoteDocument.CreateDefault();
            d.Rows[0][d.StatusColumnIndex()] = "done";
            d.AddColumn(statusName);
            d.AddColumn(dueName);
            var extraStatus = d.Columns.Count - 2;
            var extraDue = d.Columns.Count - 1;
            d.Rows[0][0] = "keep";
            d.Rows[1][0] = "also";
            d.Rows[0][extraStatus] = "done";
            d.Rows[0][extraDue] = "2020-01-01 09:00";
            d.RemoveColumn(d.DueColumnIndex());
            d.RemoveColumn(d.StatusColumnIndex());
            d.Normalize();
            Ok($"[{lang}] removed roles are not reassigned by name",
                d.StatusColumnIndex() < 0 && d.DueColumnIndex() < 0 &&
                d.ColumnRolesList[d.Columns.IndexOf(statusName)] == "" &&
                d.ColumnRolesList[d.Columns.IndexOf(dueName)] == "");
            var fired = ReminderPlan.Collect(d, new DateTime(2026, 6, 1));
            Ok($"[{lang}] the lookalike date is not a reminder", fired.Count == 0);
            d.HideDone = true;
            var hidden = Exporter.FromDocument(d, respectHideDone: true).Rows.Count;
            var all = Exporter.FromDocument(d, respectHideDone: false).Rows.Count;
            Ok($"[{lang}] hide-done ignores the lookalike status text", hidden == all && hidden == 2, $"hidden={hidden} all={all}");
            Ok($"[{lang}] the real columns can be added beside the lookalikes",
                SystemColumnToggle.Add(d, ColumnRoles.Status) == null &&
                SystemColumnToggle.Add(d, ColumnRoles.Due) == null &&
                d.ColumnRolesList.Count(r => r == ColumnRoles.Status) == 1 &&
                d.ColumnRolesList.Count(r => r == ColumnRoles.Due) == 1 &&
                d.GetCell(0, d.StatusColumnIndex()) == "" &&
                d.GetCell(0, d.DueColumnIndex()) == "");
        }
        En();
    }

    public static void TestRemindersStopWhenDueColumnIsGone()
    {
        En();
        var d = Fresh();
        d.SetCellRaw(0, d.StatusColumnIndex(), "todo");
        var before = ReminderPlan.Collect(d, new DateTime(2026, 12, 2, 10, 0, 0));
        Ok("a past due date produces a reminder", before.Count == 1, before.Count.ToString());
        d.RemoveColumn(d.DueColumnIndex());
        List<DueTask>? after = null;
        Exception? error = null;
        try { after = ReminderPlan.Collect(d, DateTime.Now); }
        catch (Exception ex) { error = ex; }
        Ok("collect skips cleanly when the reminder column is gone",
            error == null && after is { Count: 0 } && ReminderPlan.Next(d, 0, DateTime.Now) == null, error?.Message ?? "");

        // Stale reminder settings must not throw if they are still in the file.
        d.Reminders[d.RowIdAt(0)] = new ReminderInfo { Offsets = [15], Acked = ["0@2026-12-01 09:00"] };
        try { after = ReminderPlan.Collect(d, DateTime.Now); ReminderPlan.SkipPastAdvance(d, d.RowIdAt(0), DateTime.Now); }
        catch (Exception ex) { error = ex; }
        Ok("stale reminder rows do not throw without a due column", error == null && after!.Count == 0, error?.Message ?? "");
    }

    public static void TestExportsWhenSpecialColumnsAreAbsent()
    {
        En();
        var d = Fresh();
        d.Recurrence[d.RowIdAt(0)] = "daily";
        d.Recurrence[d.RowIdAt(1)] = "weekly";
        var dueId = d.ColumnIdAt(d.DueColumnIndex());
        var statusId = d.ColumnIdAt(d.StatusColumnIndex());
        var rowId = d.RowIdAt(0);
        PartColorOps.Assign(d.PartColors, PartColorKeys.HeaderColumn(dueId), "#112233", "#ABCDEF");
        PartColorOps.Assign(d.PartColors, PartColorKeys.HeaderColumn(statusId), "#222222", null);
        PartColorOps.Assign(d.PartColors, PartColorKeys.Cell(rowId, dueId), "#010203", "#445566");
        PartColorOps.Assign(d.PartColors, PartColorKeys.Notify(rowId), "#998877", "#776655");
        PartColorOps.Assign(d.PartColors, PartColorKeys.Header, "#333333", "#111111");
        d.RemoveColumn(d.DueColumnIndex());
        d.RemoveColumn(d.StatusColumnIndex());
        Ok("deleting the columns drops their header, cell, and card colors",
            !d.PartColors.ContainsKey(PartColorKeys.HeaderColumn(dueId)) &&
            !d.PartColors.ContainsKey(PartColorKeys.HeaderColumn(statusId)) &&
            !d.PartColors.ContainsKey(PartColorKeys.Cell(rowId, dueId)) &&
            !d.PartColors.ContainsKey(PartColorKeys.Notify(rowId)) &&
            d.PartColors.ContainsKey(PartColorKeys.Header));

        // Keys that outlive the column must not break export.
        d.PartColors[PartColorKeys.HeaderColumn(dueId)] = new PartColor { Background = "#112233" };
        d.PartColors[PartColorKeys.Notify(rowId)] = new PartColor { Background = "#998877" };
        d.PartColors[PartColorKeys.Cell(rowId, dueId)] = new PartColor { Background = "#010203" };
        Exception? error = null;
        string csv = "";
        try
        {
            var paints = ImagePartColors.From(d);
            Ok("stale color keys are ignored by the image map",
                paints.HeaderColors.Values.All(h => h?.Background != "#112233") &&
                paints.CellColors.Values.All(c => c != "#010203"));
            var (headers, rows) = Exporter.FromDocument(d, respectHideDone: true);
            var dir = Dir("toggle-export");
            var csvPath = Path.Combine(dir, "table.csv");
            var xlsxPath = Path.Combine(dir, "table.xlsx");
            Exporter.Export(rows, headers, csvPath, ExportFormat.Csv, new ExportColumns(d.StatusColumnIndex(), d.DueColumnIndex()));
            Exporter.Export(rows, headers, xlsxPath, ExportFormat.Xlsx, new ExportColumns(-1, -1));
            csv = File.ReadAllText(csvPath);
            Ok("csv and xlsx export with both columns absent",
                File.Exists(xlsxPath) && new FileInfo(xlsxPath).Length > 32 && csv.Contains("Task") && csv.Contains("body"));
        }
        catch (Exception ex) { error = ex; }
        Ok("export does not throw when status and the reminder column are absent", error == null, error?.ToString() ?? "");
        Ok("cleared repeats are not added as an export column", !csv.Contains("Daily") && !csv.Contains("Repeat") && !csv.Contains("Weekly"));

        var added = SystemColumnToggle.Add(d, ColumnRoles.Due);
        var newId = d.ColumnIdAt(d.DueColumnIndex());
        Ok("re-added reminder column does not inherit the old colors",
            added == null && newId != dueId &&
            !d.PartColors.ContainsKey(PartColorKeys.HeaderColumn(newId)) &&
            !d.PartColors.ContainsKey(PartColorKeys.Notify(rowId)) &&
            !d.PartColors.ContainsKey(PartColorKeys.Cell(rowId, newId)));
    }

    public static void TestExportWhenOnlyOneSpecialColumnIsRemoved()
    {
        En();
        static (string Csv, string Sheet) Export(NoteDocument doc, string name)
        {
            var (headers, rows) = Exporter.FromDocument(doc, respectHideDone: false);
            var dir = Dir(name);
            var csvPath = Path.Combine(dir, "table.csv");
            var xlsxPath = Path.Combine(dir, "table.xlsx");
            var cols = new ExportColumns(doc.StatusColumnIndex(), doc.DueColumnIndex());
            Exporter.Export(rows, headers, csvPath, ExportFormat.Csv, cols);
            Exporter.Export(rows, headers, xlsxPath, ExportFormat.Xlsx, cols);
            using var zip = System.IO.Compression.ZipFile.OpenRead(xlsxPath);
            using var reader = new StreamReader(zip.GetEntry("xl/worksheets/sheet1.xml")!.Open());
            return (File.ReadAllText(csvPath), reader.ReadToEnd());
        }

        var statusOnly = Fresh();
        statusOnly.Recurrence[statusOnly.RowIdAt(0)] = "daily";
        statusOnly.RemoveColumn(statusOnly.StatusColumnIndex());
        Exception? error = null;
        var statusCsv = "";
        var statusSheet = "";
        try { (statusCsv, statusSheet) = Export(statusOnly, "export-status-only"); }
        catch (Exception ex) { error = ex; }
        Ok("csv and xlsx export when only the status column is removed",
            error == null && statusOnly.StatusColumnIndex() < 0 && statusOnly.DueColumnIndex() >= 0 &&
            statusCsv.Contains("Due date") && statusCsv.Contains("2026-12-01") && statusCsv.Contains("Task") &&
            !statusCsv.Contains("Status") && statusCsv.Contains("Daily") &&
            statusSheet.Contains("Due date") && statusSheet.Contains("2026-12-01") && statusSheet.Contains("Task") &&
            !statusSheet.Contains("Status") && statusSheet.Contains("Daily"),
            error?.ToString() ?? "");

        var dueOnly = Fresh();
        dueOnly.SetCellRaw(0, dueOnly.StatusColumnIndex(), "todo");
        dueOnly.Recurrence[dueOnly.RowIdAt(0)] = "weekly";
        dueOnly.RemoveColumn(dueOnly.DueColumnIndex());
        error = null;
        var dueCsv = "";
        var dueSheet = "";
        try { (dueCsv, dueSheet) = Export(dueOnly, "export-due-only"); }
        catch (Exception ex) { error = ex; }
        Ok("csv and xlsx export when only the reminder column is removed",
            error == null && dueOnly.DueColumnIndex() < 0 && dueOnly.StatusColumnIndex() >= 0 &&
            dueOnly.Recurrence.Count == 0 &&
            dueCsv.Contains("Status") && dueCsv.Contains("Not started") && dueCsv.Contains("Task") &&
            !dueCsv.Contains("Due date") && !dueCsv.Contains("2026-12-01") && !dueCsv.Contains("Weekly") && !dueCsv.Contains("Repeat") &&
            dueSheet.Contains("Status") && dueSheet.Contains("Not started") && dueSheet.Contains("Task") &&
            !dueSheet.Contains("Due date") && !dueSheet.Contains("2026-12-01") && !dueSheet.Contains("Weekly"),
            error?.ToString() ?? "");
    }

    public static void TestUndoRestoresRemovalMarker()
    {
        En();
        var d = Fresh();
        var id = d.RowIdAt(0);
        d.Recurrence[id] = "weekly";
        var before = Snapshot.Serialize(d);
        d.RemoveColumn(d.DueColumnIndex());
        Ok("delete sets the marker", d.DueColumnOmitted && d.DueColumnIndex() < 0 && d.Recurrence.Count == 0);
        Snapshot.Restore(d, before);
        Ok("undo brings the reminder column and its repeat back and clears the marker",
            !d.DueColumnOmitted && d.DueColumnIndex() >= 0 && d.Recurrence.TryGetValue(id, out var kind) && kind == "weekly" &&
            d.GetCell(0, d.DueColumnIndex()) == "2026-12-01 09:00");
    }

    public static void TestHeaderDeleteAllowsDueWhilePlannerMessageStays()
    {
        En();
        var d = Fresh();
        var blocked = DeletePlanner.ForColumn(d, d.DueColumnIndex());
        var allowed = SystemColumnToggle.PlanHeaderDelete(d, d.DueColumnIndex());
        Ok("the old due-column message is unchanged",
            !blocked.CanDelete && blocked.StatusIfBlocked == Localizer.T("delete.due_protected"));
        Ok("the column delete flow can remove the reminder column",
            allowed.CanDelete && allowed.NeedsConfirm && allowed.ConfirmMessage.Contains("Due date"));
        var statusPlan = SystemColumnToggle.PlanHeaderDelete(d, d.StatusColumnIndex());
        Ok("the column delete flow can remove the status column", statusPlan.CanDelete && statusPlan.NeedsConfirm);
    }
}
