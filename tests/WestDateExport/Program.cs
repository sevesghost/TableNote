using System.IO.Compression;
using System.Text;
using TableNote;

// v2.4.2: exit code 0 = all passed.
// Keys are loaded from embedded lang/en.json and lang/th.json (Localizer).
AppLog.PathOverride = Path.Combine(Path.GetTempPath(), "tn-west-date-export.log");
var t = new WestDateExportTests();
t.Run();
Console.WriteLine($"\n{t.Pass} passed, {t.Fail} failed");
return t.Fail == 0 ? 0 : 1;

sealed class WestDateExportTests
{
    public int Pass, Fail;

    void Ok(string name, bool ok, string detail = "")
    {
        if (ok) Pass++; else Fail++;
        Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {name}{(detail.Length > 0 ? " — " + detail : "")}");
    }

    static bool HasThai(string s) => s.Any(c => c is >= '\u0E00' and <= '\u0E7F');

    static string NewDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "tn-west242-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(d);
        return d;
    }

    /// <summary>Walk up from the test output directory to the project that contains lang/*.json.</summary>
    static string SourceRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "TableNote.csproj")))
            d = d.Parent;
        return d?.FullName ?? throw new Exception("TableNote.csproj not found");
    }

    static readonly (string Key, string En, string Th)[] DateKeys =
    [
        ("date.remind_at_due", "\"{0}\" will be reminded again at the due time {1}", "\"{0}\" จะเตือนอีกครั้งตอนครบกำหนด {1}"),
        ("date.placeholder", "Date", "วัน"),
        ("date.set_due", "Due date set to {0}", "ตั้งกำหนดส่งเป็น {0}"),
        ("date.pick_tip", "Click: open the date/time picker (or set +1 minute) · Shift+click: tomorrow 09:00", "คลิก: เปิดเลือกวัน/เวลา (หรือตั้ง +1 นาที) · Shift+คลิก: พรุ่งนี้ 09:00"),
        ("date.bad_time", "Time \"{0}\" is not valid — use HH:mm, e.g. 09:30 (due date unchanged)", "เวลา \"{0}\" ไม่ถูกต้อง — ใช้รูปแบบ HH:mm เช่น 09:30 (ไม่ได้เปลี่ยนกำหนดส่ง)"),
        ("date.unreadable", "Couldn't read due date \"{0}\" — this task will not remind", "อ่านกำหนดส่ง \"{0}\" ไม่ได้ — งานนี้จะไม่เตือน"),
        ("date.undo_set", "Set due date", "ตั้งกำหนดส่ง"),
    ];

    static readonly string[] ThreeMessages = ["date.remind_at_due", "date.placeholder", "date.set_due"];

    public void Run()
    {
        TestExportDateIso();
        TestPresentAndCsvTxt();
        TestXlsxIsoText();
        TestStorageUnchanged();
        TestLangJsonKeys();
        TestEnMessagesHaveNoThai();
    }

    void TestExportDateIso()
    {
        var dateOnly = new DateTime(2026, 11, 20);
        Ok("ExportDate date-only is yyyy-MM-dd",
            Exporter.ExportDate("2026-11-20", dateOnly) == "2026-11-20"
            && Exporter.ExportDate("20/11/2026", dateOnly) == "2026-11-20");

        var withTime = new DateTime(2026, 11, 20, 14, 30, 0);
        Ok("ExportDate with time is yyyy-MM-dd HH:mm",
            Exporter.ExportDate("2026-11-20 14:30", withTime) == "2026-11-20 14:30"
            && Exporter.ExportDate("20/11/2026 14:30", withTime) == "2026-11-20 14:30");

        var midnight = new DateTime(2026, 1, 5, 0, 0, 0);
        Ok("ExportDate midnight follows HasTime",
            Exporter.ExportDate("2026-01-05 00:00", midnight) == "2026-01-05 00:00"
            && Exporter.ExportDate("2026-01-05", midnight) == "2026-01-05");

        Ok("ExportDate is CE, not Buddhist year",
            DueDates.TryParse("09/11/2569 09:00", out var be) && Exporter.ExportDate("09/11/2569 09:00", be) == "2026-11-09 09:00");

        Ok("ExportDate never uses dd/MM/yyyy",
            Exporter.ExportDate("2026-11-20", dateOnly) != "20/11/2026"
            && Exporter.ExportDate("2026-11-20 14:30", withTime) != "20/11/2026 14:30");
    }

    void TestPresentAndCsvTxt()
    {
        var headers = new[] { "หัวข้อ", "สถานะ", "กำหนดส่ง" };
        var rows = new List<TaskRow>
        {
            new(["A", "todo", "2026-11-20"]),
            new(["B", "doing", "20/11/2026 14:30"]),
            new(["C", "todo", "พรุ่งนี้"]),
        };
        var cols = new ExportColumns(1, 2);
        var (_, presented) = Exporter.Present(rows, headers, cols);
        Ok("Present date-only is ISO", presented[0].Cells[2] == "2026-11-20");
        Ok("Present with-time is ISO with time", presented[1].Cells[2] == "2026-11-20 14:30");
        Ok("Present leaves unreadable due unchanged", presented[2].Cells[2] == "พรุ่งนี้");

        var dir = NewDir();
        Exporter.Export(rows, headers, Path.Combine(dir, "a.csv"), ExportFormat.Csv, cols);
        Exporter.Export(rows, headers, Path.Combine(dir, "a.txt"), ExportFormat.Txt, cols);
        var csv = File.ReadAllText(Path.Combine(dir, "a.csv"), Encoding.UTF8);
        var txt = File.ReadAllText(Path.Combine(dir, "a.txt"), Encoding.UTF8);
        Ok("CSV contains ISO dates, not dd/MM/yyyy",
            csv.Contains("2026-11-20") && csv.Contains("2026-11-20 14:30") && !csv.Contains("20/11/2026"));
        Ok("TXT contains ISO dates, not dd/MM/yyyy",
            txt.Contains("2026-11-20") && txt.Contains("2026-11-20 14:30") && !txt.Contains("20/11/2026"));
    }

    void TestXlsxIsoText()
    {
        var dir = NewDir();
        var x = Path.Combine(dir, "a.xlsx");
        var headers = new[] { "Due", "n" };
        var rows = new List<TaskRow>
        {
            new(["2026-11-20", "42"]),
            new(["20/11/2026 14:30", "7"]),
        };
        Exporter.Export(rows, headers, x, ExportFormat.Xlsx, new ExportColumns(-1, 0));
        using var z = ZipFile.OpenRead(x);
        var sheet = new StreamReader(z.GetEntry("xl/worksheets/sheet1.xml")!.Open()).ReadToEnd();
        Ok("xlsx date-only is ISO text, not a number/serial",
            sheet.Contains("<t xml:space=\"preserve\">2026-11-20</t>") && !sheet.Contains("<v>2026-11-20</v>"));
        Ok("xlsx with-time is ISO text",
            sheet.Contains("<t xml:space=\"preserve\">2026-11-20 14:30</t>"));
        Ok("xlsx does not contain dd/MM/yyyy or an Excel serial for the due date",
            !sheet.Contains("20/11/2026") && !sheet.Contains("46346"));
        Ok("xlsx still writes plain numbers as numbers", sheet.Contains("<v>42</v>"));
    }

    void TestStorageUnchanged()
    {
        Ok("DueDates.Format (storage) still yyyy-MM-dd HH:mm",
            DueDates.Format(new DateTime(2026, 11, 20, 14, 30, 0)) == "2026-11-20 14:30");
        Ok("DueDates.FormatDate (storage) still yyyy-MM-dd",
            DueDates.FormatDate(new DateTime(2026, 11, 20)) == "2026-11-20");
        Ok("DueDates still parses d/M/yyyy input",
            DueDates.TryParse("5/1/2026 9:00", out var d) && d == new DateTime(2026, 1, 5, 9, 0, 0));
    }

    void TestLangJsonKeys()
    {
        var prev = Localizer.Current;
        try
        {
            // Embedded tables (what the app actually uses) and the source lang/*.json files must agree.
            var enEmb = Localizer.Table("en");
            var thEmb = Localizer.Table("th");
            var root = SourceRoot();
            var enFile = Localizer.Parse(File.ReadAllText(Path.Combine(root, "lang", "en.json")));
            var thFile = Localizer.Parse(File.ReadAllText(Path.Combine(root, "lang", "th.json")));

            Ok("date.* keys sit at the end of en.json",
                enFile.Keys.Reverse().Take(7).Reverse().SequenceEqual(DateKeys.Select(k => k.Key)));
            Ok("date.* keys sit at the end of th.json",
                thFile.Keys.Reverse().Take(7).Reverse().SequenceEqual(DateKeys.Select(k => k.Key)));

            foreach (var (key, en, th) in DateKeys)
            {
                Ok($"embedded en.json has {key}", enEmb.TryGetValue(key, out var ev) && ev == en, ev ?? "missing");
                Ok($"embedded th.json has {key}", thEmb.TryGetValue(key, out var tv) && tv == th, tv ?? "missing");
                Ok($"lang/en.json matches embedded {key}", enFile.TryGetValue(key, out var ef) && ef == ev);
                Ok($"lang/th.json matches embedded {key}", thFile.TryGetValue(key, out var tf) && tf == tv);
                Localizer.SetLanguage("en");
                Ok($"T() from lang/en.json: {key}", Localizer.T(key) == en, Localizer.T(key));
                Localizer.SetLanguage("th");
                Ok($"T() from lang/th.json: {key}", Localizer.T(key) == th, Localizer.T(key));
            }
        }
        finally { Localizer.SetLanguage(prev); }
    }

    void TestEnMessagesHaveNoThai()
    {
        var prev = Localizer.Current;
        try
        {
            Localizer.SetLanguage("en");
            var en = Localizer.Table("en");
            foreach (var key in ThreeMessages)
            {
                var text = en[key];
                var formatted = key switch
                {
                    "date.remind_at_due" => Localizer.T(key, "Task", "09:00"),
                    "date.set_due" => Localizer.T(key, "2026-11-20 14:30"),
                    _ => Localizer.T(key),
                };
                Ok($"EN {key} has no Thai characters (U+0E00-U+0E7F)", !HasThai(text) && !HasThai(formatted), formatted);
            }
            foreach (var (key, _, _) in DateKeys)
            {
                if (ThreeMessages.Contains(key)) continue;
                var text = Localizer.T(key, "x");
                Ok($"EN {key} has no Thai characters", !HasThai(text), text);
            }
        }
        finally { Localizer.SetLanguage(prev); }
    }
}
