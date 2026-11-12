using TableNote;

// v2.4.2: load issues (tablenote.log) are English only in every UI language; user-facing load warnings,
// table limits, paste errors and save errors follow the UI language (Thai = 2.4.1 text).
public static partial class T
{
    const string BadValuesFile = """
    { "version": 5, "columns": ["Title","Details","Status","Due date"], "columnRoles": ["title","detail","status","due"],
      "columnIds": ["a","a","c"], "rowIds": ["r1","r1"],
      "rows": [["x", 5, "", ""], 7], "columnWidths": ["wide", 100],
      "window": 3, "reminders": { "r1": { "offsets": ["no"], "acked": 1 } }, "rowStyles": { "r1": { "color": "plaid" } },
      "columnSettings": 4, "hideDone": "maybe" }
    """;

    static LoadResult LoadText(string lang, string text)
    {
        var dir = Path.Combine(Path.GetTempPath(), "tn-i18n-load-" + Guid.NewGuid().ToString("N")[..8]); Directory.CreateDirectory(dir);
        var p = Path.Combine(dir, "note.json"); File.WriteAllText(p, text);
        NoteStore.ResetBackupCheck();
        try { return In(lang, () => NoteStore.Load(p)); }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    public static void TestLoadIssuesEnglishOnly()
    {
        foreach (var lang in Localizer.Supported)
        {
            var r = LoadText(lang, BadValuesFile);
            var issues = NoteStore.LastLoadIssues.ToList();
            Ok($"[{lang}] bad values reported", issues.Count >= 8 && r.Warning == null, string.Join(" | ", issues));
            Ok($"[{lang}] load issues have no Thai (log is English only)", !issues.Any(HasThai), string.Join(" | ", issues.Where(HasThai)));
            Ok($"[{lang}] columnIds issue still says columnIds", issues.Any(i => i.StartsWith("columnIds", StringComparison.Ordinal)));
            Ok($"[{lang}] reminder issue still starts with reminders", issues.Any(i => i.StartsWith("reminders", StringComparison.Ordinal)));
            Ok($"[{lang}] banner follows the UI language", r.Notice == Localizer.T("load.bad_values", issues.Count, "") || In(lang, () => r.Notice == Localizer.T("load.bad_values", issues.Count, Localizer.T("load.bad_values_reminders"))), r.Notice ?? "");
            if (lang == "en") NoThai("[en] load banner", r.Notice);
            if (lang == "th") Ok("[th] load banner = 2.4.1", r.Notice == $"บางค่าในไฟล์บันทึกอ่านไม่ได้ จึงใช้ค่าเริ่มต้นแทน ({issues.Count} รายการ รวมค่าเตือนล่วงหน้าบางงาน) — ตารางยังอยู่ครบ ดูรายละเอียดใน tablenote.log", r.Notice ?? "");
        }
        var broken = LoadText("en", "{ not json");
        NoThai("[en] corrupt file warning", broken.Warning);
        Ok("[en] corrupt file warning is load.corrupt", broken.Warning == In("en", () => NoteStore.WarningCorrupt));
        Ok("[th] corrupt file warning = 2.4.1", LoadText("th", "{ not json").Warning == "ไฟล์บันทึกอ่านไม่ได้ จึงเริ่มโน้ตใหม่ และเก็บไฟล์เดิมไว้เป็นสำเนา .bak");
    }

    static Dictionary<string, string?> LimitMessages()
    {
        var full = NoteDocument.CreateDefault();
        while (full.Columns.Count < NoteDocument.AbsoluteMaxColumns && full.AddColumn(null)) { }
        var s = new TableSession(full);
        var d = NoteDocument.CreateDefault(); d.EnsureRowIds();
        ExcelPaste.TryRead("\"open\tquote\n\"x", out _, out var quoteErr);
        var noDue = new NoteDocument { Columns = ["a"], ColumnRolesList = [""] };
        while (noDue.Columns.Count < NoteDocument.AbsoluteMaxColumns) { noDue.Columns.Add("c" + noDue.Columns.Count); noDue.ColumnRolesList.Add(""); }
        noDue.EnsureReminderColumns();
        return new()
        {
            ["paste_too_big"] = ExcelPaste.Apply(d, 0, 0, new string('a', TableLimits.MaxPasteChars + 1)).Message,
            ["pasted"] = ExcelPaste.Apply(d, 0, 0, "a\tb\nc\td").Message,
            ["quote"] = quoteErr ?? TableLimits.BadQuoteMessage,
            ["rows"] = TableLimits.TooManyRowsMessage, ["cols"] = TableLimits.TooManyColumnsMessage, ["cell"] = TableLimits.CellTooLongMessage,
            ["big"] = TableLimits.FileTooBigMessage, ["big_kept"] = TableLimits.FileTooBigKeptMessage,
            ["overflow"] = TableLimits.OverflowNotice(1200, "note.overlimit.bak"),
            ["row_limit"] = TableLimits.RowLimitMessage,
            ["column_limit"] = s.AddColumn(),
            ["save"] = SaveFeedback.Failure(new IOException("disk full")),
            ["recur_row"] = Recurrence.CompleteAndSpawnNext(d, 99),
            ["due_warning"] = noDue.DueColumnWarning,
        };
    }

    public static void TestLimitsAndPasteMessages()
    {
        var en = In("en", LimitMessages);
        foreach (var (k, v) in en) { Ok("[en] limit." + k + " not empty", !string.IsNullOrEmpty(v)); NoThai("[en] limit." + k, v); }
        var th = In("th", LimitMessages);
        var expected = new Dictionary<string, string>
        {
            ["paste_too_big"] = "ไม่วางข้อความนี้ เพราะยาวเกิน 256 KB",
            ["pasted"] = "วางแล้ว 2 แถว 2 คอลัมน์",
            ["rows"] = "ไม่วางตารางนี้ เพราะเกิน 1000 แถว", ["cols"] = "ไม่วางตารางนี้ เพราะเกิน 30 คอลัมน์",
            ["cell"] = "ไม่วางข้อความนี้ เพราะช่องหนึ่งยาวเกิน 2000 ตัวอักษร",
            ["overflow"] = "ไฟล์มี 1200 แถว เกินที่รองรับ 1000 แถว จึงเปิดเฉพาะ 1000 แถวแรก — ไฟล์เดิมครบทุกแถวสำรองไว้ที่ note.overlimit.bak (ไม่ถูกเขียนทับ)",
            ["row_limit"] = "เพิ่มแถวไม่ได้ เพราะมีได้ไม่เกิน 1000 แถว",
            ["column_limit"] = "เพิ่มคอลัมน์ไม่ได้ เพราะมีได้ไม่เกิน 30 คอลัมน์",
            ["save"] = "บันทึกไม่สำเร็จ: disk full",
            ["recur_row"] = "ไม่พบแถว",
            ["due_warning"] = "ไม่พบคอลัมน์กำหนดส่ง และเพิ่มไม่ได้เพราะครบจำนวนคอลัมน์แล้ว — การแจ้งเตือนจะไม่ทำงาน",
        };
        foreach (var (k, v) in expected) Ok("[th] limit." + k + " = 2.4.1 text", th[k] == v, th[k] ?? "(null)");
        Ok("[th] quote error = 2.4.1 text", In("th", () => TableLimits.BadQuoteMessage) == "รูปแบบที่วางจาก Excel อ่านไม่จบ");
    }
}
