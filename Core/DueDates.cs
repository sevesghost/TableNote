using System.Globalization;
using System.Text.RegularExpressions;

namespace TableNote;

public static class DueDates
{
    /// <summary>
    /// Exact formats, parsed with the invariant culture (Gregorian calendar) BEFORE any culture-based
    /// fallback, so "5/1/2026 9:00" is 5 Jan 2026 09:00 (day/month, Thai order) even when the PC uses
    /// th-TH with the Buddhist calendar. "d"/"M" also accept two digits; "H" accepts 9 or 09.
    /// B.E. years (≥ 2400) are converted afterwards by <see cref="NormalizeBuddhistYear"/>.
    /// </summary>
    public static readonly string[] Formats = BuildFormats();

    private static string[] BuildFormats()
    {
        string[] dates = ["yyyy-MM-dd", "yyyy-M-d", "d/M/yyyy", "d-M-yyyy", "d.M.yyyy"];
        string[] times = [" H:mm", " H:mm:ss", ""];
        var list = new List<string>();
        foreach (var d in dates)
            foreach (var tm in times)
                list.Add(d + tm);
        list.AddRange(["yyyy-MM-ddTH:mm:ss", "yyyy-MM-ddTH:mm", "o"]);
        return list.ToArray();
    }

    private static readonly CultureInfo Gregorian = CultureInfo.InvariantCulture; // invariant = Gregorian calendar
    private static readonly CultureInfo ThaiBuddhist = CultureInfo.GetCultureInfo("th-TH");
    private static readonly CultureInfo ThaiGregorian = ThaiWithGregorianCalendar();

    /// <summary>th-TH (Thai month names) on the Gregorian calendar, so "2026" stays 2026.</summary>
    private static CultureInfo ThaiWithGregorianCalendar()
    {
        var c = (CultureInfo)CultureInfo.GetCultureInfo("th-TH").Clone();
        c.DateTimeFormat.Calendar = c.OptionalCalendars.OfType<GregorianCalendar>().FirstOrDefault() ?? new GregorianCalendar();
        return CultureInfo.ReadOnly(c);
    }

    public static string Format(DateTime local) =>
        local.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    public static string FormatDate(DateTime local) =>
        local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>True when the stored due text includes a time-of-day (e.g. "09:00").</summary>
    public static bool HasTime(string? text) => !string.IsNullOrWhiteSpace(text) && text.Contains(':');

    public static string FormatTime(DateTime local) =>
        local.ToString("HH:mm", CultureInfo.InvariantCulture);

    public static bool TryParse(string? text, out DateTime local)
    {
        local = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = text.Trim();

        // Reject obvious non-dates early
        if (t.Contains("พรุ่งนี้", StringComparison.Ordinal) ||
            t.Contains("มะรืน", StringComparison.Ordinal))
            return false;

        if (DateTime.TryParseExact(t, Formats, Gregorian,
                DateTimeStyles.AssumeLocal | DateTimeStyles.AllowWhiteSpaces, out local))
        {
            local = NormalizeBuddhistYear(local, t);
            return true;
        }

        // Thai month names ("5 ม.ค. 2569") need th-TH, but its default calendar is Buddhist: a C.E. year was read as
        // B.E. ("5 Jan 2026" → 1483, "5-Jan-26" → 2526 B.E. = 1983). Read it with both calendars and keep the date
        // nearest to today; B.E. years ≥ 2400 still become C.E. through NormalizeBuddhistYear.
        const DateTimeStyles styles = DateTimeStyles.AssumeLocal | DateTimeStyles.AllowWhiteSpaces;
        var okBuddhist = DateTime.TryParse(t, ThaiBuddhist, styles, out var buddhist);
        var okGregorian = DateTime.TryParse(t, ThaiGregorian, styles, out var gregorian);
        if (okBuddhist || okGregorian)
        {
            if (okBuddhist) buddhist = NormalizeBuddhistYear(buddhist, t);
            if (okGregorian) gregorian = NormalizeBuddhistYear(gregorian, t);
            var now = DateTime.Now;
            local = !okGregorian ? buddhist
                : !okBuddhist ? gregorian
                : (buddhist - now).Duration() <= (gregorian - now).Duration() ? buddhist : gregorian;
            return true;
        }

        if (DateTime.TryParse(t, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal | DateTimeStyles.AllowWhiteSpaces, out local))
        {
            local = NormalizeBuddhistYear(local, t);
            return true;
        }

        return false;
    }

    /// <summary>Years &gt;= 2400 in the parsed value or source text are treated as Buddhist Era (พ.ศ.).</summary>
    public static DateTime NormalizeBuddhistYear(DateTime dt, string? sourceText = null)
    {
        if (dt.Year >= 2400)
            return dt.AddYears(-543);

        if (!string.IsNullOrEmpty(sourceText))
        {
            // Catch cases where parser used a wrong calendar but year token was พ.ศ.
            var m = Regex.Match(sourceText, @"\b(24\d{2}|25\d{2}|26\d{2})\b");
            if (m.Success && int.TryParse(m.Value, out var be) && be >= 2400 && dt.Year == be)
                return dt.AddYears(-543);
            if (m.Success && int.TryParse(m.Value, out be) && be >= 2400 && dt.Year >= 2400)
                return new DateTime(be - 543, dt.Month, dt.Day, dt.Hour, dt.Minute, dt.Second, dt.Kind);
        }
        return dt;
    }

    public static bool IsOverdue(DateTime due, DateTime now) => due < now;
    public static bool IsDueToday(DateTime due, DateTime now) => due.Date == now.Date;

    /// <summary>
    /// D5: CE dates everywhere. A Thai (Buddhist-calendar) Windows culture would make the date picker show and
    /// parse 2569-style years; keep the user's culture but switch its calendar to Gregorian.
    /// </summary>
    public static void UseGregorianCalendar()
    {
        try
        {
            var c = (System.Globalization.CultureInfo)System.Globalization.CultureInfo.CurrentCulture.Clone();
            if (c.DateTimeFormat.Calendar is System.Globalization.GregorianCalendar) return;
            var greg = c.OptionalCalendars.OfType<System.Globalization.GregorianCalendar>().FirstOrDefault() ?? new System.Globalization.GregorianCalendar();
            c.DateTimeFormat.Calendar = greg;
            System.Globalization.CultureInfo.CurrentCulture = c;
            System.Globalization.CultureInfo.DefaultThreadCurrentCulture = c;
        }
        catch { /* keep the system culture */ }
    }

}
