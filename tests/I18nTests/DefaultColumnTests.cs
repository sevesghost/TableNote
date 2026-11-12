using TableNote;

// v2.4.2: default headers of a NEW note follow the UI language; existing files keep their headers.
public static partial class T
{
    static readonly string[] Th241 = ["หัวข้อ", "รายละเอียด", "สถานะ", "กำหนดส่ง"];

    public static void TestDefaultColumnsFollowLanguage()
    {
        var en = In("en", NoteDocument.CreateDefault);
        Ok("[en] new note headers", en.Columns.SequenceEqual(["Title", "Details", "Status", "Due date"]), string.Join("|", en.Columns));
        NoThai("[en] new note headers", string.Join("|", en.Columns));
        Ok("[en] roles", en.DueColumnIndex() == 3 && en.StatusColumnIndex() == 2);
        var th = In("th", NoteDocument.CreateDefault);
        Ok("[th] new note headers = 2.4.1", th.Columns.SequenceEqual(Th241), string.Join("|", th.Columns));
        Ok("[th] Thai consts unchanged", NoteDocument.DefaultColumns.SequenceEqual(Th241));
        var add = In("en", () => { var d = NoteDocument.CreateDefault(); d.AddColumn(null); d.AddColumn(null); return d; });
        Ok("[en] added columns", add.Columns[^2] == "Column 1" && add.Columns[^1] == "Column 2", string.Join("|", add.Columns));
        var addTh = In("th", () => { var d = NoteDocument.CreateDefault(); d.AddColumn(null); return d; });
        Ok("[th] added column = 2.4.1", addTh.Columns[^1] == "คอลัมน์ 1");
    }

    public static void TestRoleDetectionAnyLanguage()
    {
        foreach (var lang in Localizer.Supported) In(lang, () =>
        {
            foreach (var (name, role) in new[] { ("หัวข้อ", "title"), ("รายละเอียด", "detail"), ("สถานะ", "status"), ("กำหนดส่ง", "due"),
                                                  ("Title", "title"), ("Details", "detail"), ("Status", "status"), ("Due date", "due"), ("due", "due") })
                Ok($"[{lang}] role of \"{name}\"", ColumnRoles.InferFromName(name) == role);
            // old file without roles: due found by its Thai header whatever the UI language
            var d = new NoteDocument { Columns = [.. Th241], ColumnRolesList = ["", "", "", ""] };
            d.EnsureReminderColumns();
            Ok($"[{lang}] Thai headers keep roles, no column added", d.Columns.Count == 4 && d.DueColumnIndex() == 3 && d.StatusColumnIndex() == 2, string.Join("|", d.Columns));
        });
    }

    public static void TestExistingFileNotRenamed()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tn-i18n-" + Guid.NewGuid().ToString("N")[..8]); Directory.CreateDirectory(dir);
        var p = Path.Combine(dir, "note.json");
        try
        {
            NoteStore.ResetBackupCheck();
            In("th", () => NoteStore.Save(p, NoteDocument.CreateDefault()));
            var r = In("en", () => NoteStore.Load(p));
            Ok("[en] Thai note opens with Thai headers (not renamed)", r.Document.Columns.SequenceEqual(Th241), string.Join("|", r.Document.Columns));
            Ok("[en] roles intact after reopen", r.Document.DueColumnIndex() == 3 && r.Document.StatusColumnIndex() == 2);
            // a file missing the due column gets one in the UI language (added, nothing renamed)
            var noDue = In("th", NoteDocument.CreateDefault); noDue.Columns.RemoveAt(3); noDue.ColumnRolesList.RemoveAt(3);
            In("en", () => noDue.EnsureReminderColumns());
            Ok("[en] missing due column added as \"Due date\"", noDue.Columns[^1] == "Due date" && noDue.DueColumnIndex() == noDue.Columns.Count - 1
               && noDue.Columns.Take(3).SequenceEqual(Th241.Take(3)), string.Join("|", noDue.Columns));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
