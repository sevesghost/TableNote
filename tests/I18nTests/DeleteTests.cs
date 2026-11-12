using TableNote;

// v2.4.2: delete confirmations / statuses go through Localizer ("delete.*", "colmove.*").
// Tests compare against Localizer.T(...) or plan flags, never against hard-coded Thai UI text,
// except TestDeleteThaiUnchanged, which pins the Thai wording to 2.4.1 on purpose.
public static partial class T
{
    // 2.4.1 Thai wording (must not change).
    const string ThDue = "ลบคอลัมน์กำหนดส่งไม่ได้ เพราะการแจ้งเตือนใช้คอลัมน์นี้ — เปลี่ยนชื่อหัวคอลัมน์ได้ หรือล้างค่าในช่องแทน";

    static Dictionary<string, string> DeleteMessages()
    {
        var d = DeleteSample();
        int notes = d.IndexOfColumn("Notes"), blank = d.Columns.Count - 1, due = d.DueColumnIndex();
        var one = NoteDocument.CreateDefault(); while (one.Columns.Count > 1) one.Columns.RemoveAt(one.Columns.Count - 1);
        var noRows = NoteDocument.CreateDefault(); noRows.Rows.Clear(); noRows.RowIds.Clear();
        var blankFilled = DeleteSample(); blankFilled.Rows[0][^1] = "b";
        return new()
        {
            ["no_row"] = DeletePlanner.ForRow(noRows, 0).StatusIfBlocked,
            ["row_has_text"] = DeletePlanner.ForRow(d, 0).ConfirmMessage,
            ["min_one"] = DeletePlanner.ForColumn(one, 0).StatusIfBlocked,
            ["no_column"] = DeletePlanner.ForColumn(d, 99).StatusIfBlocked,
            ["due"] = DeletePlanner.ForColumn(d, due).StatusIfBlocked,
            ["column_has_text"] = DeletePlanner.ForColumn(d, notes).ConfirmMessage,
            ["column_fallback"] = DeletePlanner.ForColumn(blankFilled, blank).ConfirmMessage,
            ["due_const"] = DeletePlanner.DueColumnProtected,
            ["confirm_empty"] = Localizer.T("delete.column_confirm", DeletePlanner.ColumnLabel(d, blank)),
            ["title"] = Localizer.T("delete.title"), ["ok"] = Localizer.T("delete.button_ok"), ["cancel"] = Localizer.T("delete.button_cancel"),
            ["canceled"] = Localizer.T("delete.canceled"),
            ["row_changed"] = Localizer.T("delete.canceled_row_changed"),
            ["col_changed"] = Localizer.T("delete.canceled_column_changed"),
            ["undo_row"] = new RowDeleteAction(0, [], "", [], null, null).Label,
            ["undo_col"] = Localizer.T("delete.undo_column"),
            ["move_none"] = Localizer.T("colmove.none"), ["move_failed"] = Localizer.T("colmove.failed"),
        };
    }

    public static void TestDeletePlanFlags()
    {
        foreach (var lang in Localizer.Supported) In(lang, () =>
        {
            var d = DeleteSample();
            var empty = DeletePlanner.ForRow(d, 1);
            Ok($"[{lang}] empty row: delete without asking", empty.CanDelete && !empty.NeedsConfirm && empty.ConfirmMessage == "");
            var full = DeletePlanner.ForRow(d, 0);
            Ok($"[{lang}] filled row asks", full.CanDelete && full.NeedsConfirm && full.ConfirmMessage == Localizer.T("delete.row_has_text"));
            var due = DeletePlanner.ForColumn(d, d.DueColumnIndex());
            Ok($"[{lang}] due column blocked", !due.CanDelete && due.StatusIfBlocked == Localizer.T("delete.due_protected") && due.StatusIfBlocked == DeletePlanner.DueColumnProtected);
            var notes = DeletePlanner.ForColumn(d, d.IndexOfColumn("Notes"));
            Ok($"[{lang}] filled column asks with its header", notes.NeedsConfirm && notes.ConfirmMessage == Localizer.T("delete.column_has_text", "Notes"));
            var longName = DeleteSample(); longName.Columns[^2] = new string('a', 50);
            Ok($"[{lang}] header cut at 40", DeletePlanner.ForColumn(longName, longName.Columns.Count - 2).ConfirmMessage == Localizer.T("delete.column_has_text", new string('a', 40) + "…"));
        });
    }

    public static void TestDeleteSessionStatuses()
    {
        foreach (var lang in Localizer.Supported) In(lang, () =>
        {
            var d = DeleteSample(); var s = new TableSession(d);
            string? asked = null;
            var r = s.DeleteColumnAtAsync(d.Columns.Count - 1, m => { asked = m; return Task.FromResult(false); }).GetAwaiter().GetResult();
            Ok($"[{lang}] menu delete of empty column asks + cancel status",
                !r.Changed && asked == Localizer.T("delete.column_confirm", Localizer.T("delete.column_fallback", d.Columns.Count)) && r.Status == Localizer.T("delete.canceled"), $"{asked} / {r.Status}");
            // column changed while the dialog was open (renamed) -> canceled_column_changed
            var col = d.IndexOfColumn("Notes");
            r = s.DeleteColumnAtAsync(col, m => { d.Columns[col] = "renamed"; return Task.FromResult(true); }).GetAwaiter().GetResult();
            Ok($"[{lang}] column changed during confirm", !r.Changed && r.Status == Localizer.T("delete.canceled_column_changed"), r.Status ?? "");
            var mv = s.MoveColumn(99, 0);
            Ok($"[{lang}] move missing column", !mv.Changed && mv.Status == Localizer.T("colmove.none"), mv.Status ?? "");
            r = s.DeleteColumnAtAsync(99, _ => Task.FromResult(true)).GetAwaiter().GetResult();
            Ok($"[{lang}] delete missing column", !r.Changed && r.Status == Localizer.T("delete.no_column"));
            // row removed while the dialog was open -> canceled_row_changed
            var d2 = DeleteSample(); var s2 = new TableSession(d2); s2.ChooseCell(0, 0);
            r = s2.DeleteRowAsync(m => { d2.RemoveRow(0); return Task.FromResult(true); }).GetAwaiter().GetResult();
            Ok($"[{lang}] row changed during confirm", !r.Changed && r.Status == Localizer.T("delete.canceled_row_changed"), r.Status ?? "");
            var d3 = DeleteSample(); var s3 = new TableSession(d3); s3.ChooseCell(0, 0);
            r = s3.DeleteRowAsync(m => Task.FromResult(false)).GetAwaiter().GetResult();
            Ok($"[{lang}] row delete canceled", !r.Changed && r.Status == Localizer.T("delete.canceled"));
        });
    }

    public static void TestDeleteEnglishHasNoThai()
    {
        foreach (var (k, v) in In("en", DeleteMessages)) NoThai("[en] delete." + k, v);
        var en = In("en", DeleteMessages);
        Ok("[en] due message (sample)", en["due"].StartsWith("The due date column can't be deleted"), en["due"]);
        Ok("[en] column confirm", en["confirm_empty"] == "Delete column \"Column 6\"? (Press Ctrl+Z to undo)", en["confirm_empty"]);
    }

    public static void TestDeleteThaiUnchanged()
    {
        var th = In("th", DeleteMessages);
        var expected = new Dictionary<string, string>
        {
            ["no_row"] = "ไม่มีแถวให้ลบ",
            ["row_has_text"] = "แถวนี้มีข้อความอยู่ การลบจะทำให้ทั้งแถวหายไป ต้องการลบหรือไม่",
            ["min_one"] = "ต้องมีอย่างน้อย 1 คอลัมน์",
            ["no_column"] = "ไม่มีคอลัมน์ให้ลบ",
            ["due"] = ThDue, ["due_const"] = ThDue,
            ["column_has_text"] = "คอลัมน์ \"Notes\" มีข้อความอยู่ การลบจะทำให้ทั้งคอลัมน์หายไป ต้องการลบหรือไม่",
            ["column_fallback"] = "คอลัมน์ \"คอลัมน์ 6\" มีข้อความอยู่ การลบจะทำให้ทั้งคอลัมน์หายไป ต้องการลบหรือไม่",
            ["confirm_empty"] = "ลบคอลัมน์ \"คอลัมน์ 6\" ใช่หรือไม่ (กด Ctrl+Z เพื่อเรียกคืนได้)",
            ["title"] = "ยืนยันการลบ", ["ok"] = "ลบ", ["cancel"] = "ยกเลิก",
            ["canceled"] = "ยกเลิกการลบ",
            ["row_changed"] = "ยกเลิกการลบ: แถวถูกเปลี่ยนไประหว่างยืนยัน",
            ["col_changed"] = "ยกเลิกการลบ: คอลัมน์ถูกเปลี่ยนไประหว่างยืนยัน",
            ["undo_row"] = "ลบแถว", ["undo_col"] = "ลบคอลัมน์",
            ["move_none"] = "ไม่มีคอลัมน์ให้ย้าย", ["move_failed"] = "ย้ายคอลัมน์ไม่ได้",
        };
        foreach (var (k, v) in expected) Ok("[th] delete." + k + " = 2.4.1 text", th.TryGetValue(k, out var got) && got == v, got ?? "(missing)");
    }

    public static void TestDeleteFollowsLanguageSwitch()
    {
        // Resolved when shown: switching language after start-up changes the next message.
        var d = DeleteSample();
        var en = In("en", () => DeletePlanner.ForRow(d, 0).ConfirmMessage);
        var th = In("th", () => DeletePlanner.ForRow(d, 0).ConfirmMessage);
        var en2 = In("en", () => DeletePlanner.DueColumnProtected);
        Ok("language switch changes delete text", en != th && !HasThai(en) && HasThai(th) && !HasThai(en2));
    }
}
