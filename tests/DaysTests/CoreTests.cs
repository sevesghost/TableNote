using System.Text.Json;
using TableNote;

/// <summary>Days counter and row history without a window.</summary>
static class CoreTests
{
    static readonly DateTime T0 = new(2026, 6, 1, 9, 0, 0);

    /// <summary>Default four columns plus a days column (index 4) with a start date in every row.</summary>
    public static NoteDocument Doc(int rows = 3, string start = "2026-06-01")
    {
        var d = NoteDocument.CreateDefault();
        d.Rows.Clear(); d.RowIds.Clear();
        for (var i = 0; i < rows; i++) d.Rows.Add(["Task " + i, "detail " + i, "", ""]);
        d.EnsureRowIds();
        d.AddColumn("Days");
        ColumnSettingsOps.Apply(d, 4, null, ColumnTypes.Days, null, null);
        for (var i = 0; i < rows; i++) d.SetCellRaw(i, 4, start);
        return d;
    }

    public static void Run(Action<bool, string, string> ok)
    {
        Localizer.SetLanguage("en");

        // ── counting ──
        var now = new DateTime(2026, 10, 29, 15, 0, 0); // 150 days after 1 June
        ok(DaysCounter.Compute("2026-06-01", now, false, null) is { State: DaysState.Counting, Days: 150 }, "150 days after the start", "");
        ok(DaysCounter.Compute("1/6/2569", now, false, null).Days == 150, "a Thai B.E. date counts the same", "");
        ok(DaysCounter.Compute("2026-10-29", now, false, null) is { State: DaysState.Counting, Days: 0 }, "the start day is day 0", "");
        ok(DaysCounter.Compute("", now, false, null).State == DaysState.Blank, "blank start", "");
        ok(DaysCounter.Compute("next week", now, false, null).State == DaysState.Invalid, "text that is not a date", "");
        ok(DaysCounter.Compute("2026-11-01", now, false, null) is { State: DaysState.Future, Days: 3 }, "a start in the future", "");
        ok(DaysCounter.Compute("2026-06-01", now, true, new DateTime(2026, 7, 13, 10, 0, 0)) is { State: DaysState.Done, Days: 42 },
            "a done row stops on the day it was done", "");
        ok(DaysCounter.Compute("2026-06-01", now, true, null).State == DaysState.DoneUnknown, "done without a recorded day", "");
        var c150 = DaysCounter.Compute("2026-06-01", now, false, null);
        ok(DaysCounter.CellText(c150, "") == "150 days", "cell text in English", DaysCounter.CellText(c150, ""));
        ok(DaysCounter.CellText(DaysCounter.Compute("2026-10-28", now, false, null), "") == "1 day", "one day is singular", "");
        ok(DaysCounter.CellText(DaysCounter.Compute("2026-10-29", now, false, null), "") == "Today", "start day shows Today", "");
        ok(DaysCounter.CellText(DaysCounter.Compute("2020-01-01", now, false, null), "") == "2,493 days", "thousands get a separator",
            DaysCounter.CellText(DaysCounter.Compute("2020-01-01", now, false, null), ""));
        Localizer.SetLanguage("th");
        ok(DaysCounter.CellText(c150, "") == "150 วัน", "cell text in Thai", DaysCounter.CellText(c150, ""));
        ok(DaysCounter.CellText(DaysCounter.Compute("2026-06-01", now, true, new DateTime(2026, 7, 13)), "") == "42 วัน ✓", "done cell in Thai", "");
        ok(DaysCounter.CellText(DaysCounter.Compute("ไม่รู้", now, false, null), "ไม่รู้") == "ไม่รู้", "a non-date keeps its text", "");
        Localizer.SetLanguage("en");

        // ── no days column → no history, old files unchanged ──
        var plain = NoteDocument.CreateDefault();
        var tracker = new RowHistoryTracker();
        tracker.Reset(plain);
        plain.SetCellRaw(0, 0, "typed");
        ok(!tracker.Capture(plain, T0) && plain.RowHistory.Count == 0 && !tracker.Active, "no days column: nothing is recorded", "");
        ok(RowHistoryOps.ForSave(plain) == null, "nothing to write without history", "");

        // ── edits, status, merging ──
        var d = Doc();
        tracker.Reset(d);
        var id0 = d.RowIdAt(0);
        d.SetCellRaw(0, 1, "step A");
        ok(tracker.Capture(d, T0), "an edit is recorded", "");
        var h = RowHistoryOps.Of(d, id0);
        ok(h.Count == 1 && h[0] is { Kind: RowEventKinds.Edit, From: "detail 0", To: "step A" } && h[0].Name == d.Columns[1],
            "edit keeps column, from and to", Dump(h));
        d.SetCellRaw(0, 1, "step AB");
        tracker.Capture(d, T0.AddMinutes(3));
        d.SetCellRaw(0, 1, "step ABC");
        tracker.Capture(d, T0.AddMinutes(6));
        h = RowHistoryOps.Of(d, id0);
        ok(h.Count == 1 && h[0].From == "detail 0" && h[0].To == "step ABC" && h[0].Time == "2026-06-01 09:06",
            "typing in pieces is one entry (first value → last value, last time)", Dump(h));
        d.SetCellRaw(0, 1, "detail 0");
        tracker.Capture(d, T0.AddMinutes(8));
        ok(RowHistoryOps.Of(d, id0).Count == 0, "typed and put back: the entry goes away", Dump(RowHistoryOps.Of(d, id0)));
        d.SetCellRaw(0, 1, "later");
        tracker.Capture(d, T0.AddMinutes(30));
        d.SetCellRaw(0, 1, "much later");
        tracker.Capture(d, T0.AddMinutes(45));
        ok(RowHistoryOps.Of(d, id0).Count == 2, "changes far apart stay separate", Dump(RowHistoryOps.Of(d, id0)));

        var status = d.StatusColumnIndex();
        d.SetCellRaw(0, status, TaskStatuses.DoingCode);
        tracker.Capture(d, T0.AddDays(10));
        var last = RowHistoryOps.Of(d, id0)[^1];
        ok(last is { Kind: RowEventKinds.Status, From: "", To: TaskStatuses.DoingCode }, "status change is its own kind", Dump([last]));
        ok(RowHistoryOps.StatusSince(d, id0) == T0.AddDays(10), "status since", "");
        d.SetCellRaw(0, status, TaskStatuses.DoneCode);
        tracker.Capture(d, T0.AddDays(42));
        ok(RowHistoryOps.DoneAt(d, id0) == T0.AddDays(42), "done time comes from the history", "");
        ok(DaysCounter.ForCell(d, 0, 4, T0.AddDays(200)) is { State: DaysState.Done, Days: 42 }, "the counter stays at 42 days later on", "");
        d.SetCellRaw(0, status, TaskStatuses.DoingCode);
        tracker.Capture(d, T0.AddDays(50));
        ok(RowHistoryOps.DoneAt(d, id0) == null && DaysCounter.ForCell(d, 0, 4, T0.AddDays(60)).State == DaysState.Counting,
            "reopened: counting again", "");

        // ── new rows ──
        ok(d.InsertRowAfter(0), "insert a row", "");
        ok(d.GetCell(1, 4) == DaysCounter.TodayText(DateTime.Now), "a new row starts counting today", d.GetCell(1, 4));
        var newId = d.RowIdAt(1);
        tracker.Capture(d, T0.AddDays(60));
        ok(RowHistoryOps.Of(d, newId) is [{ Kind: RowEventKinds.Added }], "a new row gets an Added entry", Dump(RowHistoryOps.Of(d, newId)));
        d.SetCellRaw(1, 0, "New task");
        tracker.Capture(d, T0.AddDays(60).AddMinutes(2));
        ok(RowHistoryOps.Of(d, newId).Count == 1, "typing a new row's first values is not listed", Dump(RowHistoryOps.Of(d, newId)));
        d.SetCellRaw(1, status, TaskStatuses.DoingCode);
        tracker.Capture(d, T0.AddDays(60).AddMinutes(3));
        ok(RowHistoryOps.Of(d, newId).Count == 2, "but its status change is", Dump(RowHistoryOps.Of(d, newId)));
        d.SetCellRaw(1, 0, "New task, renamed");
        tracker.Capture(d, T0.AddDays(60).AddMinutes(30));
        ok(RowHistoryOps.Of(d, newId).Count == 3, "after ten minutes edits are listed again", Dump(RowHistoryOps.Of(d, newId)));

        // ── delete + undo, column moves, column removal ──
        var before = Snapshot.CloneLite(d);
        var count = RowHistoryOps.Count(d);
        d.RemoveRow(1);
        tracker.Capture(d, T0.AddDays(61));
        Snapshot.Restore(d, Snapshot.Serialize(before));
        tracker.Capture(d, T0.AddDays(61).AddMinutes(1));
        ok(RowHistoryOps.Count(d) == count && RowHistoryOps.Of(d, newId).Count == 3, "undoing a delete brings the row back with its history, no new Added", Dump(RowHistoryOps.Of(d, newId)));
        d.MoveColumn(1, 0);
        tracker.Capture(d, T0.AddDays(62));
        ok(RowHistoryOps.Count(d) == count, "moving a column is not a change", RowHistoryOps.Count(d) + " vs " + count);
        d.SetCellRaw(2, 0, "edited after the move");
        tracker.Capture(d, T0.AddDays(62).AddMinutes(1));
        var e2 = RowHistoryOps.Of(d, d.RowIdAt(2));
        ok(e2.Count == 1 && e2[0].Column == d.ColumnIdAt(0) && e2[0].From == "detail 1", "after a move edits name the right column", Dump(e2));

        // ── notes and limits ──
        var note = RowHistoryOps.AddNote(d, id0, "  called the client  ", T0.AddDays(70));
        ok(note is { Kind: RowEventKinds.Note, Text: "called the client" }, "a note is trimmed", Dump(RowHistoryOps.Of(d, id0)));
        ok(RowHistoryOps.AddNote(d, id0, "   ", T0) == null, "a blank note is ignored", "");
        ok(RowHistoryOps.LastUpdate(d, id0) == T0.AddDays(70), "a note counts as the last update", "");
        var big = Doc(1);
        var bigId = big.RowIdAt(0);
        RowHistoryOps.AddNote(big, bigId, "keep me", T0);
        for (var i = 0; i < 150; i++)
            RowHistoryOps.Append(big, bigId, new RowEvent { Time = RowHistoryOps.FormatTime(T0.AddHours(i + 1)), Kind = RowEventKinds.Edit, Column = "c", From = i.ToString(), To = (i + 1).ToString() });
        var bh = RowHistoryOps.Of(big, bigId);
        ok(bh.Count == RowHistoryOps.MaxPerRow && bh.Any(e => e.Text == "keep me") && bh[^1].To == "150",
            "per row: the oldest changes go first, notes stay", bh.Count.ToString());
        ok(RowHistoryOps.Clip(new string('x', 500)).Length == RowHistoryOps.MaxValueChars, "long values are clipped", "");
        ok(RowHistoryOps.Remove(d, id0, note!) && !RowHistoryOps.Of(d, id0).Contains(note!), "a note can be removed", "");

        // ── the pop-up content ──
        Localizer.SetLanguage("th");
        var p = Doc(1, "2026-06-01");
        var pt = new RowHistoryTracker();
        pt.Reset(p);
        var pid = p.RowIdAt(0);
        p.SetCellRaw(0, p.StatusColumnIndex(), TaskStatuses.DoingCode);
        pt.Capture(p, new DateTime(2026, 7, 1, 10, 2, 0));
        RowHistoryOps.AddNote(p, pid, "โทรตามแล้ว ลูกค้าจะส่งเอกสารวันศุกร์", new DateTime(2026, 10, 20, 14, 30, 0));
        var info = DaysCounter.Describe(p, 0, 4, now);
        ok(info.Headline == "ค้างมา 150 วัน", "headline: open for 150 days", info.Headline);
        ok(info.Range == "ตั้งแต่ 01/06/2026 ถึงวันนี้ 29/10/2026", "range line", info.Range);
        ok(info.StatusLine == "สถานะ: กำลังทำ ตั้งแต่ 01/07/2026 10:02 (120 วันที่แล้ว)", "status line with since", info.StatusLine ?? "");
        ok(info.LastUpdateLine == "อัปเดตล่าสุด: 20/10/2026 14:30 (9 วันที่แล้ว)", "last update line", info.LastUpdateLine);
        ok(info.Timeline.Count == 2 && info.Timeline[0].IsNote && info.Timeline[0].Body == "โทรตามแล้ว ลูกค้าจะส่งเอกสารวันศุกร์"
           && info.Timeline[0].Offset == "หลังรับงาน 141 วัน" && info.Timeline[0].When == "20/10/2026 14:30",
            "newest first: the note with date, time and day", $"{info.Timeline[0].When} | {info.Timeline[0].Offset} | {info.Timeline[0].Title}");
        ok(info.Timeline[1].Title == "สถานะ: ยังไม่เริ่ม → กำลังทำ", "status entry text", info.Timeline[1].Title);
        p.SetCellRaw(0, 4, "2026-06-10");
        pt.Capture(p, new DateTime(2026, 10, 29, 9, 0, 0));
        var startEdit = DaysCounter.Describe(p, 0, 4, now).Timeline[0];
        ok(startEdit.Title == "Days: 01/06/2026 → 10/06/2026", "start date change is shown as dates", startEdit.Title);
        Localizer.SetLanguage("en");
        ok(DaysCounter.Describe(Doc(1, ""), 0, 4, now).Headline == "No start date yet", "blank start headline", "");

        // ── adding the column, conversions, recurrence ──
        var conv = NoteDocument.CreateDefault();
        conv.SetCellRaw(1, conv.StatusColumnIndex(), TaskStatuses.DoneCode);
        conv.AddColumn("Received");
        var rc = conv.Columns.Count - 1;
        ColumnSettingsOps.Apply(conv, rc, null, ColumnTypes.Days, null, null);
        var written = DaysCounter.StartToday(conv, rc, T0);
        ok(written == conv.Rows.Count - 1 && conv.GetCell(0, rc) == "2026-06-01" && conv.GetCell(1, rc) == "",
            "an empty days column starts today in rows that are not done", written.ToString());
        ok(DaysCounter.StartToday(conv, rc, T0.AddDays(5)) == 0 && conv.GetCell(0, rc) == "2026-06-01", "a column with dates is left alone", "");
        var rec = Doc(1);
        rec.SetCellRaw(0, rec.DueColumnIndex(), "2026-06-05 09:00");
        rec.Recurrence[rec.RowIdAt(0)] = Recurrence.Weekly;
        Recurrence.CompleteAndSpawnNext(rec, 0);
        ok(rec.Rows.Count == 2 && rec.GetCell(0, 4) == "2026-06-01" && rec.GetCell(1, 4) == DaysCounter.TodayText(DateTime.Now),
            "the next round of a repeating task counts from today", rec.GetCell(1, 4));

        // ── pictures show days, not the start date ──
        var pic = Doc(2, "2026-06-01");
        pic.SetCellRaw(1, pic.StatusColumnIndex(), TaskStatuses.DoneCode);
        var plan = ViewImage.Plan(pic, new ViewImageOptions { Now = now });
        var texts = plan.Rows.Select(r => r.Cells[4].Text).ToList();
        ok(texts.SequenceEqual(["150 days", "Done"]), "export picture / wallpaper show days", string.Join(" | ", texts));

        // ── note.json ──
        var dir = Directory.CreateTempSubdirectory("tn-days").FullName;
        var path = Path.Combine(dir, "note.json");
        var s = Doc(2);
        var st = new RowHistoryTracker();
        st.Reset(s);
        s.SetCellRaw(0, 1, "changed");
        st.Capture(s, T0.AddDays(3));
        RowHistoryOps.AddNote(s, s.RowIdAt(0), "ตามเอกสาร \"ด่วน\"", T0.AddDays(4));
        NoteStore.Save(path, s);
        var json = File.ReadAllText(path);
        ok(json.Contains("\"rowHistory\"") && json.Contains("ตามเอกสาร") && json.Contains("\"kind\": \"note\"") && !json.Contains("\"text\": null"),
            "history is written readable, without empty fields", json.Length.ToString());
        var loaded = NoteStore.Load(path).Document;
        var lh = RowHistoryOps.Of(loaded, s.RowIdAt(0));
        ok(lh.Count == 2 && lh[0].To == "changed" && lh[1].Text == "ตามเอกสาร \"ด่วน\"", "history survives save and load", Dump(lh));
        ok(DaysCounter.IsDaysColumn(loaded, 4), "the column type survives", "");
        var gone = s.RowIdAt(0);
        s.RemoveRow(0);
        NoteStore.Save(path, s);
        ok(!File.ReadAllText(path).Contains(gone), "a deleted row's history is not written", "");
        var plainPath = Path.Combine(dir, "plain.json");
        NoteStore.Save(plainPath, NoteDocument.CreateDefault());
        ok(!File.ReadAllText(plainPath).Contains("rowHistory"), "a note without history has no rowHistory field", "");

        File.WriteAllText(path, """
            { "version": 6, "columns": ["A", "B"], "rowIds": ["r1"], "rows": [["x", "2026-06-01"]],
              "columnIds": ["a", "b"], "columnSettings": { "b": { "type": "days" } },
              "rowHistory": { "r1": [ { "time": "2026-06-02 10:00", "kind": "note", "text": "ok" },
                                      { "time": "yesterday", "kind": "note", "text": "bad time" },
                                      5,
                                      { "time": "2026-06-03 10:00", "kind": "future-kind", "text": "kept" } ],
                              "zz": [ { "time": "2026-06-02 10:00", "kind": "note", "text": "orphan" } ],
                              "r2": "not a list" } }
            """);
        var lenient = NoteStore.Load(path);
        var lr = RowHistoryOps.Of(lenient.Document, "r1");
        ok(lr.Count == 2 && lr[0].Text == "ok" && lr[1].Kind == "future-kind", "bad entries are skipped one by one, unknown kinds kept", Dump(lr));
        ok(!lenient.Document.RowHistory.ContainsKey("zz") && lenient.Issues.Any(i => i.StartsWith("rowHistory", StringComparison.Ordinal)),
            "history of rows that do not exist is dropped and reported", string.Join(" / ", lenient.Issues));
        ok(lenient.Document.Version == NoteDocument.CurrentVersion, "the file version stays 6 (rowHistory is optional)", lenient.Document.Version.ToString());
        Directory.Delete(dir, true);
    }

    static string Dump(IEnumerable<RowEvent> events) =>
        string.Join(" ; ", events.Select(e => $"{e.Time} {e.Kind} {e.Column} {e.From}→{e.To} {e.Text}"));
}
