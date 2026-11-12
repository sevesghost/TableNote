using System.Text.RegularExpressions;
using TableNote;

// v2.4.2: exit code 0 = all passed. Each Test* runs in "en" and/or "th" and restores the language after.
AppLog.PathOverride = Path.Combine(Path.GetTempPath(), "tn-i18n-tests.log");
foreach (var t in typeof(T).GetMethods().Where(m => m.Name.StartsWith("Test")).OrderBy(m => m.MetadataToken))
{
    var lang = Localizer.Current;
    try { t.Invoke(null, null); }
    catch (Exception ex) { T.Ok(t.Name, false, (ex.InnerException ?? ex).ToString()); }
    finally { Localizer.SetLanguage(lang); }
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

    static readonly Regex ThaiChar = new("[\u0E00-\u0E7F]");
    public static bool HasThai(string? s) => s != null && ThaiChar.IsMatch(s);
    public static void NoThai(string name, string? s) => Ok(name, !HasThai(s), s ?? "(null)");

    /// <summary>Run <paramref name="body"/> with the UI language set to <paramref name="lang"/>, then restore it.</summary>
    public static void In(string lang, Action body)
    {
        var old = Localizer.Current;
        Localizer.SetLanguage(lang);
        try { body(); } finally { Localizer.SetLanguage(old); }
    }
    public static TR In<TR>(string lang, Func<TR> body) { TR r = default!; In(lang, () => { r = body(); }); return r; }

    /// <summary>Note with the default columns, one filled row and one empty row, an extra filled column "Notes" and an empty one.</summary>
    public static NoteDocument DeleteSample()
    {
        var d = NoteDocument.CreateDefault();
        d.Rows.Clear(); d.RowIds.Clear();
        d.Rows.Add(Enumerable.Repeat("", d.Columns.Count).ToList());
        d.Rows.Add(Enumerable.Repeat("", d.Columns.Count).ToList());
        d.Rows[0][0] = "x";
        d.EnsureRowIds();
        d.AddColumn("Notes"); d.Rows[0][^1] = "n";
        d.AddColumn("   "); // blank header, empty cells
        return d;
    }
}
