using System.Text;
using System.Text.RegularExpressions;
using TableNote;

// v2.4.2: source scan. Thai text (or \u0E.. escapes) outside comments is only allowed in the files/lines below;
// everything shown to the user goes through Localizer + lang/*.json. Run: dotnet run (project tests/I18nTests).
public static partial class T
{
    // Whole files that may hold Thai on purpose.
    static readonly string[] AllowedFiles =
    [
        "Core/Localizer.cs",      // language plumbing
        "Core/AppInfo.cs",        // TH help text (HelpTextTh)
        "Core/TaskStatuses.cs",   // Thai words typed by users / stored by ≤ 2.3 ("เสร็จแล้ว" …) → status codes
        "Core/Recurrence.cs",     // Thai recurrence words typed by users → codes (messages are localized)
        "Core/DueDates.cs",       // Thai date words ("พรุ่งนี้", "มะรืน") and Buddhist-era years typed by users
        "Core/ColumnRoles.cs",    // detects Thai due headers in existing files ("กำหนด…")
        "Core/ColumnSettings.cs", // truthy words for checkbox columns ("ใช่", "เสร็จ")
        "Core/RowStyles.cs",      // legacy Thai colour labels in the palette tuple (UI uses colour.* keys)
    ];

    // Single lines that may hold Thai on purpose (file, text that must appear on the line).
    static readonly (string File, string Contains)[] AllowedLines =
    [
        ("Core/NoteDocument.cs", "public const string Col"), // 2.4.1 Thai default headers, detection only
        // Date lines that 2.4.2 moved to date.* keys:
        ("MainWindow.axaml.cs", "จะเตือนอีกครั้งตอนครบกำหนด"),
        ("MainWindow.axaml.cs", "PlaceholderText = \"วัน\""),
        ("MainWindow.axaml.cs", "คลิก: เปิดเลือกวัน/เวลา"),
        ("MainWindow.axaml.cs", "\"ตั้งกำหนดส่ง\""),
        ("MainWindow.axaml.cs", "ไม่ถูกต้อง — ใช้รูปแบบ HH:mm"),
        ("MainWindow.axaml.cs", "ตั้งกำหนดส่งเป็น"),
        ("MainWindow.axaml.cs", "งานนี้จะไม่เตือน"),
    ];

    static readonly Regex ThaiOrEscape = new(@"[\u0E00-\u0E7F]|\\u0[eE][0-7][0-9a-fA-F]");

    static string SrcRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "TableNote.csproj"))) d = d.Parent;
        return d?.FullName ?? throw new Exception("TableNote.csproj not found");
    }

    /// <summary>C# source with comments blanked (line breaks kept). Strings, verbatim, raw and char literals are respected.</summary>
    public static string StripComments(string s)
    {
        var o = new StringBuilder(s.Length);
        int i = 0;
        while (i < s.Length)
        {
            char c = s[i], n = i + 1 < s.Length ? s[i + 1] : '\0';
            if (c == '/' && n == '/') { while (i < s.Length && s[i] != '\n') i++; continue; }
            if (c == '/' && n == '*') { i += 2; while (i + 1 < s.Length && !(s[i] == '*' && s[i + 1] == '/')) { if (s[i] == '\n') o.Append('\n'); i++; } i += 2; continue; }
            if (c == '\'') { o.Append(c); i++; while (i < s.Length && s[i] != '\'' && s[i] != '\n') { if (s[i] == '\\') { o.Append(s[i]); i++; } if (i < s.Length) o.Append(s[i]); i++; } if (i < s.Length) { o.Append(s[i]); i++; } continue; }
            if (c == '"')
            {
                int q = 0; while (i + q < s.Length && s[i + q] == '"') q++;
                if (q >= 3) // raw string literal
                {
                    var end = s.IndexOf(new string('"', q), i + q, StringComparison.Ordinal);
                    end = end < 0 ? s.Length : end + q;
                    o.Append(s, i, end - i); i = end; continue;
                }
                bool verbatim = i > 0 && (s[i - 1] == '@' || (s[i - 1] == '$' && i > 1 && s[i - 2] == '@'));
                o.Append(c); i++;
                while (i < s.Length)
                {
                    if (verbatim && s[i] == '"' && i + 1 < s.Length && s[i + 1] == '"') { o.Append("\"\""); i += 2; continue; }
                    if (!verbatim && s[i] == '\\') { o.Append(s[i]); if (i + 1 < s.Length) o.Append(s[i + 1]); i += 2; continue; }
                    if (s[i] == '"' || (!verbatim && s[i] == '\n')) break;
                    o.Append(s[i]); i++;
                }
                if (i < s.Length) { o.Append(s[i]); i++; }
                continue;
            }
            o.Append(c); i++;
        }
        return o.ToString();
    }

    public static List<string> ThaiLeftovers(string root, out List<string> allowedHits)
    {
        var bad = new List<string>(); allowedHits = [];
        foreach (var f in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal))
        {
            var rel = Path.GetRelativePath(root, f).Replace('\\', '/');
            if (rel.StartsWith("bin/") || rel.StartsWith("obj/") || rel.StartsWith("tests/") || rel.Contains("/bin/") || rel.Contains("/obj/")) continue;
            var lines = StripComments(File.ReadAllText(f)).Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                if (!ThaiOrEscape.IsMatch(lines[i])) continue;
                var hit = $"{rel}:{i + 1}: {lines[i].Trim()}";
                if (AllowedFiles.Contains(rel) || AllowedLines.Any(a => a.File == rel && lines[i].Contains(a.Contains, StringComparison.Ordinal))) allowedHits.Add(hit);
                else bad.Add(hit);
            }
        }
        return bad;
    }

    public static void TestStripCommentsSelfCheck()
    {
        var src = "var a = \"// not a comment ไทย\"; // ไทย comment\n/* ไทย */ var b = @\"x\"\"y\"; var c = '\"'; var d = \"\"\"\n raw ไทย \n\"\"\";";
        var s = StripComments(src);
        Ok("scanner keeps Thai inside strings", s.Contains("not a comment ไทย") && s.Contains("raw ไทย"));
        Ok("scanner drops Thai in comments", !s.Contains("ไทย comment") && !s.Contains("/* ไทย */"));
    }

    public static void TestNoThaiLiteralsOutsideAllowList()
    {
        var bad = ThaiLeftovers(SrcRoot(), out var allowed);
        Ok("no Thai UI text outside Localizer/lang and the allow list", bad.Count == 0, "\n  " + string.Join("\n  ", bad));
        Console.WriteLine($"  (allowed on purpose: {allowed.Count} lines in {allowed.Select(a => a.Split(':')[0]).Distinct().Count()} files; set I18N_SCAN_VERBOSE=1 to list)");
        if (Environment.GetEnvironmentVariable("I18N_SCAN_VERBOSE") == "1") foreach (var a in allowed) Console.WriteLine("  allowed " + a);
    }

    public static void TestEnglishSweep()
    {
        // One pass over every representative path in English: nothing Thai may come out.
        var all = new List<(string, string?)>();
        In("en", () =>
        {
            all.AddRange(DeleteMessages().Select(kv => ("delete." + kv.Key, (string?)kv.Value)));
            all.AddRange(UiSamples().Select(kv => ("ui." + kv.Key, (string?)kv.Value)));
            all.AddRange(LimitMessages().Select(kv => ("limit." + kv.Key, kv.Value)));
            all.AddRange(PlatformTexts().Select(kv => ("platform." + kv.Key, kv.Value)));
            all.Add(("new note headers", string.Join("|", NoteDocument.CreateDefault().Columns)));
        });
        var load = LoadText("en", BadValuesFile);
        all.Add(("load banner", load.Notice));
        all.AddRange(NoteStore.LastLoadIssues.Select(i => ("load issue", (string?)i)));
        var thai = all.Where(x => HasThai(x.Item2)).Select(x => $"{x.Item1}: {x.Item2}").ToList();
        Ok($"[en] sweep of {all.Count} messages has no Thai", thai.Count == 0, string.Join(" | ", thai));
    }
}
