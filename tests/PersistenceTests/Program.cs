using System.Diagnostics;
using System.Text.Json;
using TableNote;

// v2.4.1: exit code 0 = all passed. "writer <path>" = child process for the kill test.
if (args.Length > 0 && args[0] == "writer") { T.Writer(args[1]); return 0; }
AppLog.PathOverride = Path.Combine(Path.GetTempPath(), "tn-persist-tests.log");
foreach (var t in typeof(T).GetMethods().Where(m => m.Name.StartsWith("Test")))
{
    try { t.Invoke(null, null); }
    catch (Exception ex) { T.Ok(t.Name, false, (ex.InnerException ?? ex).Message); }
}
Console.WriteLine($"\n{T.Pass} passed, {T.Fail} failed");
return T.Fail == 0 ? 0 : 1;

public static partial class T
{
    public static int Pass, Fail;
    public static void Ok(string name, bool ok, string detail = "")
    {
        if (ok) Pass++; else Fail++;
        Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {name}{(detail.Length > 0 ? " — " + detail : "")}");
    }
    public static string Dir(string n)
    {
        var d = Path.Combine(Path.GetTempPath(), "tn-persist-" + n + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(d);
        NoteStore.ResetBackupCheck();
        return d;
    }
    public static NoteDocument Sample(int rows = 6)
    {
        var d = NoteDocument.CreateDefault();
        d.Rows.Clear(); d.RowIds.Clear();
        for (var i = 0; i < rows; i++) d.Rows.Add(["งาน " + i, "detail,1,234 " + i, i == 2 ? "done" : "", "2026-12-0" + (i % 9 + 1) + " 09:00"]);
        d.EnsureRowIds();
        return d;
    }

    public static void TestBrokenFileOpensPrev()
    {
        foreach (var (label, mutate) in new (string, Func<byte[], byte[]>)[] {
            ("truncated", b => b[..(b.Length / 2)]), ("empty", _ => []), ("nul-filled", b => new byte[b.Length]) })
        {
            var p = Path.Combine(Dir("broken"), "note.json");
            var d = Sample(); NoteStore.Save(p, d); d.Rows[0][0] = "second"; NoteStore.Save(p, d);
            File.WriteAllBytes(p, mutate(File.ReadAllBytes(p)));
            var r = NoteStore.Load(p);
            var bak = Directory.GetFiles(Path.GetDirectoryName(p)!, "note.json*.bak").Length;
            Ok($"broken note.json ({label}) → kept as .bak, last good .prev opened", bak == 1 && r.Document.Rows.Count == 6 && r.Document.Rows[0][0] == "งาน 0");
        }
    }

    public static void TestKillDuringSave()
    {
        var p = Path.Combine(Dir("kill"), "note.json");
        var self = Environment.ProcessPath!;
        var dll = typeof(T).Assembly.Location;
        var rnd = new Random(7); var bad = 0; const int runs = 20;
        for (var i = 0; i < runs; i++)
        {
            var psi = self.EndsWith("dotnet") || self.EndsWith("dotnet.exe")
                ? new ProcessStartInfo(self, [dll, "writer", p]) : new ProcessStartInfo(self, ["writer", p]);
            psi.RedirectStandardOutput = true;
            using var proc = Process.Start(psi)!;
            proc.StandardOutput.ReadLine();
            Thread.Sleep(rnd.Next(5, 120));
            proc.Kill(); proc.WaitForExit();
            NoteStore.ResetBackupCheck();
            var r = NoteStore.Load(p);
            if (r.Warning != null || r.Document.Rows.Count != 200) bad++;
        }
        Ok($"kill during save loop x{runs} → note.json always complete", bad == 0, $"bad={bad}");
    }

    public static void Writer(string p)
    {
        var d = NoteDocument.CreateDefault();
        d.Rows.Clear(); d.RowIds.Clear();
        for (var i = 0; i < 200; i++) d.Rows.Add(["row " + i + " " + new string('ก', 300), "x", "", ""]);
        d.EnsureRowIds();
        NoteStore.Save(p, d);
        Console.WriteLine("ready"); Console.Out.Flush();
        for (var n = 0; ; n++) { d.Rows[n % 200][1] = "edit " + n; RowOps.MoveRow(d, n % 200, (n * 7) % 200); NoteStore.Save(p, d); }
    }

    public static void TestUpgradeBackup()
    {
        var dir = Dir("upg"); var p = Path.Combine(dir, "note.json");
        const string v3 = """{"version":3,"columns":["หัวข้อ","รายละเอียด","สถานะ","กำหนดส่ง"],"rows":[["a","b","",""]],"rowIds":["r1"],"columnRoles":["title","detail","status","due"]}""";
        File.WriteAllText(p, v3);
        var r = NoteStore.Load(p); NoteStore.Save(p, r.Document);
        Ok("v3 file → note.v3.bak byte-identical before first save", File.ReadAllText(Path.Combine(dir, "note.v3.bak")) == v3);
    }

    public static void TestRowMovePersists()
    {
        var p = Path.Combine(Dir("move"), "note.json");
        var d = Sample(6); var ids = d.RowIds.ToList();
        d.RowStyles[ids[1]] = new RowStyle { Color = "red", Highlight = true };
        d.Reminders[ids[1]] = new ReminderInfo { Offsets = [30, 60] };
        d.Recurrence[ids[1]] = "weekly";
        d.Snoozes[ids[1]] = "2026-12-02 09:10";
        NoteStore.Save(p, d);
        var undo = new UndoStack();
        RowOps.MoveRow(d, 1, 4); undo.Push(new RowMoveAction(ids[1], 1, 4));
        NoteStore.Save(p, d);
        var r = NoteStore.Load(p).Document;
        string[] moved = [ids[0], ids[2], ids[3], ids[4], ids[1], ids[5]];
        Ok("move → save → reopen: order", r.RowIds.SequenceEqual(moved));
        Ok("move → save → reopen: cells follow row", r.Rows[4][0] == "งาน 1" && r.Rows[4][1] == "detail,1,234 1" && r.Rows[1][0] == "งาน 2");
        Ok("move → save → reopen: per-row metadata follows row id",
            r.RowStyles.TryGetValue(ids[1], out var st) && st.Color == "red" && st.Highlight &&
            r.Reminders.TryGetValue(ids[1], out var rem) && rem.Offsets.OrderBy(x => x).SequenceEqual([30, 60]) &&
            r.Recurrence.GetValueOrDefault(ids[1]) == "weekly" && r.Snoozes.ContainsKey(ids[1]));
        using (var j = JsonDocument.Parse(File.ReadAllText(p)))
            Ok("saved rows/rowIds same length", j.RootElement.GetProperty("rows").GetArrayLength() == j.RootElement.GetProperty("rowIds").GetArrayLength());

        var at = d.RowIds.IndexOf(ids[1]);
        d.SetCellRaw(at, 0, "แก้แล้ว"); undo.Push(CellEditAction.For(d, ids[1], 0, "งาน 1", "แก้แล้ว"));
        RowOps.MoveRow(d, d.RowIds.IndexOf(ids[3]), 0); undo.Push(new RowMoveAction(ids[3], 2, 0));
        undo.Undo(d); undo.Undo(d);
        NoteStore.Save(p, d); r = NoteStore.Load(p).Document;
        Ok("move → edit → move → undo×2 → reopen", r.RowIds.SequenceEqual(moved) && r.Rows[4][0] == "งาน 1");
        undo.Undo(d); NoteStore.Save(p, d); r = NoteStore.Load(p).Document;
        Ok("undo first move → reopen in original order", r.RowIds.SequenceEqual(ids));
        undo.Redo(d); NoteStore.Save(p, d); r = NoteStore.Load(p).Document;
        Ok("redo move → reopen in moved order", r.RowIds.SequenceEqual(moved));

        var before = Snapshot.CloneLite(d);
        var s = new TableSession(d); s.ChooseCell(0, 0); s.AddRow();
        undo.Push(new SnapshotAction("เพิ่มแถว", before, Snapshot.CloneLite(d)));
        RowOps.MoveRow(d, 1, 6); undo.Push(new RowMoveAction(d.RowIds[6], 1, 6));
        undo.Undo(d); undo.Undo(d);
        NoteStore.Save(p, d); r = NoteStore.Load(p).Document;
        Ok("snapshot step + move + undo×2 → reopen intact", r.RowIds.SequenceEqual(moved) && r.Rows.Count == 6);

        var vis = Enumerable.Range(0, d.Rows.Count).Where(i => !TaskStatuses.IsDone(d.GetCell(i, 2))).ToList();
        var mover = d.RowIds[vis[0]]; var target = RowOps.TargetFor(vis, vis[0], RowMoveKind.Down);
        RowOps.MoveRow(d, vis[0], target); NoteStore.Save(p, d); r = NoteStore.Load(p).Document;
        Ok("Hide done move → reopen", r.RowIds.IndexOf(mover) == target && r.Rows.Count == 6);
    }
}

public static partial class T
{
    // v2.4.1 fix: transient lock at start-up no longer opens an empty note in keep-file mode.
    public static void TestTransientLockAtLoad()
    {
        var p = Path.Combine(Dir("lock"), "note.json");
        NoteStore.Save(p, Sample());
        var hold = new FileStream(p, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var release = Task.Run(async () => { await Task.Delay(200); hold.Dispose(); });
        var r = NoteStore.Load(p);
        release.Wait();
        Ok("note.json locked ~200 ms at start-up → loads normally (retry)", !r.PreserveExistingFile && r.Document.Rows[0][0] == "งาน 0", $"preserve={r.PreserveExistingFile}");
    }

    public static void TestLongLockKeepsFileAndSideCopyWorks()
    {
        var dir = Dir("lock2"); var p = Path.Combine(dir, "note.json");
        NoteStore.Save(p, Sample());
        var original = File.ReadAllBytes(p);
        LoadResult r;
        using (new FileStream(p, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) r = NoteStore.Load(p);
        Ok("note.json locked for the whole load → PreserveExistingFile", r.PreserveExistingFile);
        r.Document.Rows[0][0] = "typed while note.json was kept";
        var side = NoteStore.SaveSideCopy(p, r.Document);
        Ok("session edits saved to note.unsaved.json; note.json untouched",
            Path.GetFileName(side) == "note.unsaved.json" && File.ReadAllBytes(p).SequenceEqual(original) &&
            NoteStore.Load(side).Document.Rows[0][0] == "typed while note.json was kept");
    }
}

public static partial class T
{
    // v2.4.1 fix: a failed version backup is no longer swallowed and forgotten.
    public static void TestUpgradeBackupFallback()
    {
        var dir = Dir("upgfail"); var p = Path.Combine(dir, "note.json");
        const string v3 = """{"version":3,"columns":["หัวข้อ","รายละเอียด","สถานะ","กำหนดส่ง"],"rows":[["a","b","",""]],"rowIds":["r1"],"columnRoles":["title","detail","status","due"]}""";
        File.WriteAllText(p, v3);
        Directory.CreateDirectory(Path.Combine(dir, "note.v3.bak")); // name unusable for File.Copy
        var r = NoteStore.Load(p);
        NoteStore.Save(p, r.Document); r.Document.Rows[0][0] = "edit"; NoteStore.Save(p, r.Document);
        var copies = Directory.GetFiles(dir, "note.v3*.bak").Where(f => File.ReadAllText(f) == v3).ToList();
        Ok("v3 backup name unusable → exact v3 copy still on disk after 2 saves", copies.Count == 1, string.Join(",", copies.Select(Path.GetFileName)));
    }
}

public static partial class T
{
    // v2.4.1 finding 4: 2.4.0 → 2.3.1 (columns changed, columnIds kept verbatim) → 2.4.x.
    const string Cols5 = "\"columnIds\":[\"c-title\",\"c-owner\",\"c-status\",\"c-due\",\"c-prio\"],\"columnSettings\":{\"c-owner\":{\"type\":\"choice\",\"options\":[\"สมชาย\",\"สมหญิง\"]},\"c-prio\":{\"type\":\"choice\",\"options\":[\"สูง\",\"ต่ำ\"]}}";

    static (LoadResult r, string dir) LoadJson(string name, string json)
    {
        var dir = Dir(name); var p = Path.Combine(dir, "note.json");
        File.WriteAllText(p, json);
        return (NoteStore.Load(p), dir);
    }

    public static void TestColumnIdsMatchSettingsApplied()
    {
        var (r, _) = LoadJson("colok", "{\"version\":5,\"columns\":[\"หัวข้อ\",\"ผู้รับผิดชอบ\",\"สถานะ\",\"กำหนดส่ง\",\"ลำดับ\"],\"rows\":[[\"a\",\"สมชาย\",\"todo\",\"\",\"สูง\"]],\"rowIds\":[\"r1\"],\"columnRoles\":[\"title\",\"\",\"status\",\"due\",\"\"]," + Cols5 + "}");
        var d = r.Document;
        Ok("columnIds match → ids kept, settings applied to their columns",
            d.ColumnIds.SequenceEqual(["c-title", "c-owner", "c-status", "c-due", "c-prio"]) &&
            d.ColumnSettings.TryGetValue("c-owner", out var o) && o.Options.SequenceEqual(["สมชาย", "สมหญิง"]) &&
            d.ColumnSettings.ContainsKey("c-prio") && r.Issues.Count == 0);
    }

    public static void TestColumnDeletedIn231()
    {
        // 2.3.1 deleted "ผู้รับผิดชอบ": 4 columns, 5 ids
        var (r, dir) = LoadJson("coldel", "{\"version\":5,\"columns\":[\"หัวข้อ\",\"สถานะ\",\"กำหนดส่ง\",\"ลำดับ\"],\"rows\":[[\"a\",\"todo\",\"2026-12-01 09:00\",\"สูง\"],[\"b\",\"\",\"\",\"ต่ำ\"]],\"rowIds\":[\"r1\",\"r2\"],\"columnRoles\":[\"title\",\"status\",\"due\",\"\"]," + Cols5 + "}");
        var d = r.Document;
        Ok("2.3.1 column delete (5 ids / 4 cols) → no column gets a stale setting", d.ColumnSettings.Count == 0 && d.ColumnIds.Count == 4 && d.ColumnIds.Distinct().Count() == 4 && !d.ColumnIds.Contains("c-owner"));
        Ok("2.3.1 column delete → cell data intact", d.Rows.Count == 2 && d.Rows[0].SequenceEqual(["a", "todo", "2026-12-01 09:00", "สูง"]) && d.Rows[1][3] == "ต่ำ");
        var log = File.Exists(Path.Combine(dir, "tablenote.log")) ? File.ReadAllText(Path.Combine(dir, "tablenote.log")) : "";
        Ok("2.3.1 column delete → tablenote.log has the mismatch + old settings", log.Contains("columnIds: 5") && log.Contains("สมชาย"));
        // save → reload is consistent (ids now match)
        var p = Path.Combine(dir, "note.json"); NoteStore.Save(p, d); var again = NoteStore.Load(p);
        Ok("after save, file loads cleanly with matching ids", again.Issues.Count == 0 && again.Document.ColumnIds.SequenceEqual(d.ColumnIds));
    }

    public static void TestColumnMovedAndInsertedIn231()
    {
        // 2.3.1 moved "ลำดับ" to 2nd place and inserted "หมายเหตุ": 6 columns, 5 ids
        var (r, _) = LoadJson("colmove", "{\"version\":5,\"columns\":[\"หัวข้อ\",\"ลำดับ\",\"ผู้รับผิดชอบ\",\"สถานะ\",\"กำหนดส่ง\",\"หมายเหตุ\"],\"rows\":[[\"a\",\"สูง\",\"สมชาย\",\"todo\",\"\",\"x\"]],\"rowIds\":[\"r1\"],\"columnRoles\":[\"title\",\"\",\"\",\"status\",\"due\",\"\"]," + Cols5 + "}");
        var d = r.Document;
        Ok("2.3.1 column move + insert (5 ids / 6 cols) → settings not applied, data intact",
            d.ColumnSettings.Count == 0 && d.ColumnIds.Count == 6 && d.Rows[0].SequenceEqual(["a", "สูง", "สมชาย", "todo", "", "x"]) &&
            r.Issues.Any(i => i.StartsWith("columnIds: 5")));
    }

    public static void TestOldFileWithoutColumnIds()
    {
        var (r, _) = LoadJson("colnone", "{\"version\":4,\"columns\":[\"หัวข้อ\",\"รายละเอียด\",\"สถานะ\",\"กำหนดส่ง\"],\"rows\":[[\"a\",\"b\",\"\",\"\"]],\"rowIds\":[\"r1\"],\"columnRoles\":[\"title\",\"detail\",\"status\",\"due\"]}");
        Ok("v4 file without columnIds → fresh ids, no issue logged", r.Document.ColumnIds.Count == 4 && r.Issues.Count == 0);
    }
}
