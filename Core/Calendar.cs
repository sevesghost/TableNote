using Avalonia;

namespace TableNote;

/// <summary>2.7.0: what the wallpaper shows of the calendar (settings.json <c>calendarWallpaper</c>).</summary>
public enum CalendarWallpaper
{
    /// <summary>No calendar on the wallpaper (the default; written as nothing).</summary>
    Off,
    /// <summary>The calendar at the bottom right of the screen, with the table card where the window was.</summary>
    Corner,
    /// <summary>Only the calendar at the bottom right; no table card.</summary>
    Only
}

/// <summary>
/// 2.7.0: one row of the table on a day of the calendar. The calendar has no data of its own: every item is a row
/// whose due date (the reminder column) falls on that day, so a change in the calendar is a change in the table and
/// the other way round. <see cref="Ghost"/> items are later rounds of a repeating task, shown before they exist
/// (the row of the next round is added when this round is set to done).
/// </summary>
public sealed record CalendarItem
{
    public required string RowId { get; init; }
    /// <summary>Index of the row in the table when the list was made.</summary>
    public required int Row { get; init; }
    public required DateTime Due { get; init; }
    /// <summary>False for a date-only due ("2026-11-24"): the item is shown as all day.</summary>
    public bool HasTime { get; init; }
    public string Title { get; init; } = "";
    public string Detail { get; init; } = "";
    /// <summary>Status code as shown (todo / doing / done).</summary>
    public string Status { get; init; } = TaskStatuses.TodoCode;
    public bool Done { get; init; }
    public bool Overdue { get; init; }
    public bool Today { get; init; }
    /// <summary>Normalized repeat kind (<see cref="Recurrence"/>), "none" when the task does not repeat.</summary>
    public string Repeat { get; init; } = Recurrence.None;
    /// <summary>Advance reminders in minutes before the due time (the on-time reminder is always there).</summary>
    public IReadOnlyList<int> Advance { get; init; } = [];
    /// <summary>The row colour (#RRGGBB) when one is set, else null.</summary>
    public string? Color { get; init; }
    public bool Ghost { get; init; }
    public DateTime Date => Due.Date;
}

/// <summary>
/// 2.7.0: a row with text that is not on the calendar yet: it has no due date (<see cref="Due"/> = ""), or one that
/// can't be read. <see cref="Found"/> is a date written elsewhere in the row (another column, or inside its text),
/// offered as its day; <see cref="FoundIn"/> names that column.
/// </summary>
public sealed record UnplacedRow(string RowId, int Row, string Title, string Due, bool Done, DateTime? Found, bool FoundTime, string? FoundIn);

/// <summary>Calendar settings kept in settings.json (never in note.json).</summary>
public sealed record CalendarPrefs(CalendarWallpaper Wallpaper, int WeekStart, bool HideDone);

/// <summary>2.7.0: the calendar's logic (no UI), so tests cover it: the month grid, which rows fall on which day,
/// adding a row for a day, moving a row to another day, and where the calendar goes on the wallpaper.</summary>
public static class CalendarOps
{
    /// <summary>Six weeks: every month fits, and the grid never changes height between months.</summary>
    public const int GridDays = 42;
    /// <summary>Later rounds of one repeating task shown in a range (a daily task fills two months).</summary>
    public const int MaxGhostsPerRow = 62;
    public const int MaxGhostsTotal = 2000;
    public const int MaxTitleChars = 120;
    /// <summary>Time of a new item (the same 09:00 as Shift+📅 in the table).</summary>
    public static readonly TimeSpan DefaultTime = new(9, 0, 0);
    /// <summary>Time-of-day typed in the calendar: "9:30", "09:30" (0:00–23:59).</summary>
    public static readonly string[] TimeFormats = [@"h\:mm", @"hh\:mm"];

    public static int NormalizeWeekStart(int day) => day == 1 ? 1 : 0;

    /// <summary>First day shown for a month: the Sunday (or Monday) on or before the 1st.</summary>
    public static DateTime GridStart(int year, int month, int weekStart)
    {
        var first = new DateTime(year, Math.Clamp(month, 1, 12), 1);
        var offset = ((int)first.DayOfWeek - NormalizeWeekStart(weekStart) + 7) % 7;
        return first.AddDays(-offset);
    }

    public static IReadOnlyList<DateTime> MonthGrid(int year, int month, int weekStart)
    {
        var start = GridStart(year, month, weekStart);
        return Enumerable.Range(0, GridDays).Select(i => start.AddDays(i)).ToList();
    }

    /// <summary>The seven week days in the order of the grid's columns.</summary>
    public static IReadOnlyList<DayOfWeek> WeekDays(int weekStart) =>
        Enumerable.Range(0, 7).Select(i => (DayOfWeek)((i + NormalizeWeekStart(weekStart)) % 7)).ToList();

    /// <summary>The calendar needs the due date column (the one the reminders use).</summary>
    public static bool CanShow(NoteDocument doc) => doc.DueColumnIndex() >= 0;

    /// <summary>
    /// Rows due on a day from <paramref name="from"/> up to (not including) <paramref name="to"/>, by day and time
    /// (all-day items first), then in table order. With <paramref name="ghosts"/>, later rounds of repeating tasks
    /// are added as <see cref="CalendarItem.Ghost"/> items (from the next round after now, as the table will add it).
    /// </summary>
    public static List<CalendarItem> Items(NoteDocument doc, DateTime from, DateTime to, DateTime now, bool hideDone = false, bool ghosts = true)
    {
        var list = new List<CalendarItem>();
        var due = doc.DueColumnIndex();
        if (due < 0) return list;
        from = from.Date;
        to = to.Date;
        var status = doc.StatusColumnIndex();
        doc.EnsureRowIds();
        var ghostCount = 0;
        for (var r = 0; r < doc.Rows.Count; r++)
        {
            var raw = doc.GetCell(r, due);
            if (!DueDates.TryParse(raw, out var d)) continue;
            var item = Make(doc, r, d, DueDates.HasTime(raw), status, now);
            if (item.Date >= from && item.Date < to && !(hideDone && item.Done)) list.Add(item);
            if (!ghosts || item.Done || !Recurrence.IsActive(item.Repeat) || ghostCount >= MaxGhostsTotal) continue;
            foreach (var next in Rounds(d, item.Repeat, now, to))
            {
                if (next.Date < from) continue;
                list.Add(item with { Due = next, Ghost = true, Overdue = false, Today = next.Date == now.Date });
                if (++ghostCount >= MaxGhostsTotal) break;
            }
        }
        list.Sort(Compare);
        return list;
    }

    /// <summary>The items of one day.</summary>
    public static List<CalendarItem> Day(NoteDocument doc, DateTime date, DateTime now, bool hideDone = false, bool ghosts = true) =>
        Items(doc, date.Date, date.Date.AddDays(1), now, hideDone, ghosts);

    public static Dictionary<DateTime, List<CalendarItem>> ByDay(IEnumerable<CalendarItem> items)
    {
        var map = new Dictionary<DateTime, List<CalendarItem>>();
        foreach (var i in items)
        {
            if (!map.TryGetValue(i.Date, out var day)) map[i.Date] = day = [];
            day.Add(i);
        }
        return map;
    }

    /// <summary>
    /// Later rounds of a repeating task before <paramref name="until"/>: the next round is the one the table adds
    /// when this one is done (<see cref="Recurrence.NextDue"/>: after now), then one every day / week / month.
    /// </summary>
    public static IEnumerable<DateTime> Rounds(DateTime due, string? kind, DateTime now, DateTime until, int max = MaxGhostsPerRow)
    {
        var k = Recurrence.NormalizeKind(kind);
        if (!Recurrence.IsActive(k)) yield break;
        if (k == Recurrence.MonthlyPrefix + "0") k = Recurrence.MonthlyPrefix + due.Day; // as CompleteAndSpawnNext
        var next = Recurrence.NextDue(due, k, now);
        for (var i = 0; i < max && next.Date < until.Date; i++)
        {
            yield return next;
            var after = Recurrence.NextDue(next, k, next);
            if (after <= next) yield break;
            next = after;
        }
    }

    private static int Compare(CalendarItem a, CalendarItem b)
    {
        var c = a.Date.CompareTo(b.Date);
        if (c != 0) return c;
        c = a.HasTime.CompareTo(b.HasTime); // all-day first
        if (c != 0) return c;
        c = a.Due.TimeOfDay.CompareTo(b.Due.TimeOfDay);
        if (c != 0) return c;
        c = a.Ghost.CompareTo(b.Ghost);
        return c != 0 ? c : a.Row.CompareTo(b.Row);
    }

    private static CalendarItem Make(NoteDocument doc, int row, DateTime due, bool hasTime, int status, DateTime now)
    {
        var id = doc.RowIdAt(row);
        var raw = status >= 0 ? doc.GetCell(row, status) : "";
        var done = TaskStatuses.IsDone(raw);
        doc.Recurrence.TryGetValue(id, out var repeat);
        return new CalendarItem
        {
            RowId = id,
            Row = row,
            Due = due,
            HasTime = hasTime,
            Title = TitleOf(doc, row),
            Detail = DetailOf(doc, row),
            Status = TaskStatuses.DisplayCode(raw),
            Done = done,
            Overdue = !done && DueDates.IsOverdue(due, now),
            Today = DueDates.IsDueToday(due, now),
            Repeat = Recurrence.NormalizeKind(repeat),
            Advance = ReminderPlan.OffsetsFor(doc, id).OrderBy(o => o).ToList(),
            Color = doc.RowStyles.Count == 0 ? null : RowStyleOps.HexFor(RowStyleOps.Get(doc, id).Color)
        };
    }

    /// <summary>Columns that are not the task's own text: status, due and days counters.</summary>
    private static HashSet<int> SystemColumns(NoteDocument doc)
    {
        var skip = new HashSet<int> { doc.StatusColumnIndex(), doc.DueColumnIndex() };
        foreach (var c in DaysCounter.ColumnIndexes(doc)) skip.Add(c);
        return skip;
    }

    /// <summary>The row's title on one line: the title column, else the first text of the row; "" when the row has none.</summary>
    public static string TitleOf(NoteDocument doc, int row)
    {
        var title = doc.IndexOfRole(ColumnRoles.Title);
        if (title >= 0 && FirstLine(doc.GetCell(row, title)) is { Length: > 0 } t) return t;
        var skip = SystemColumns(doc);
        for (var c = 0; c < doc.Columns.Count; c++)
        {
            if (c == title || skip.Contains(c)) continue;
            if (FirstLine(doc.GetCell(row, c)) is { Length: > 0 } s) return s;
        }
        return "";
    }

    /// <summary>The detail column's text (all of it), or "".</summary>
    public static string DetailOf(NoteDocument doc, int row)
    {
        var detail = doc.IndexOfRole(ColumnRoles.Detail);
        return detail >= 0 ? doc.GetCell(row, detail).Trim() : "";
    }

    private static string FirstLine(string? text)
    {
        var t = (text ?? "").Trim();
        var nl = t.IndexOfAny(['\r', '\n']);
        if (nl >= 0) t = t[..nl].TrimEnd();
        return TableLimits.Clip(t, MaxTitleChars);
    }

    // ───────────────────────── editing ─────────────────────────

    /// <summary>Time of a new item: 09:00, or on today after 09:00 the next whole hour (23:59 at the latest), so a
    /// new item never starts out overdue.</summary>
    public static TimeSpan DefaultTimeFor(DateTime date, DateTime now)
    {
        if (date.Date != now.Date || now.TimeOfDay < DefaultTime) return DefaultTime;
        var next = now.Hour + 1;
        return next >= 24 ? new TimeSpan(23, 59, 0) : new TimeSpan(next, 0, 0);
    }

    /// <summary>Due text as the table writes it: "yyyy-MM-dd" (all day) or "yyyy-MM-dd HH:mm".</summary>
    public static string DueText(DateTime date, TimeSpan? time) =>
        time is { } t ? DueDates.Format(date.Date + t) : DueDates.FormatDate(date.Date);

    /// <summary>"9:30" / "09:30" → 09:30, "" → null (all day). False when the text is not a time of day.</summary>
    public static bool TryParseTime(string? text, out TimeSpan? time)
    {
        time = null;
        var t = (text ?? "").Trim();
        if (t.Length == 0) return true;
        if (!TimeSpan.TryParseExact(t, TimeFormats, System.Globalization.CultureInfo.InvariantCulture, out var ts)) return false;
        time = ts;
        return true;
    }

    /// <summary>
    /// Adds a row due on <paramref name="date"/>: the first of the empty rows at the end of the table is used, so a
    /// new note's blank rows fill up first; otherwise a new last row. Returns its index, or -1 when the table has no
    /// due column or is full. <paramref name="busy"/> marks rows that hold something the document can't see (pictures).
    /// </summary>
    public static int AddRow(NoteDocument doc, DateTime date, TimeSpan? time, DateTime now, Func<string, bool>? busy = null)
    {
        var due = doc.DueColumnIndex();
        if (due < 0) return -1;
        var row = TrailingBlankRow(doc, busy);
        if (row < 0)
        {
            if (!doc.AppendRow()) return -1; // a new row starts its days counter today
            row = doc.Rows.Count - 1;
        }
        else DaysCounter.FillNewRow(doc, row, now);
        doc.SetCellRaw(row, due, DueText(date, time));
        var id = doc.RowIdAt(row);
        ReminderPlan.Reset(doc, id, now);
        SkipPastOnTime(doc, id, date.Date + (time ?? TimeSpan.Zero), now);
        return row;
    }

    /// <summary>
    /// A due moment that has already passed when it is set from the calendar does not pop a reminder (a row put on a
    /// past day is usually a record of what happened; it still shows as overdue until it is done). The table's own
    /// date editor keeps reminding at once, as before.
    /// </summary>
    public static void SkipPastOnTime(NoteDocument doc, string rowId, DateTime due, DateTime now)
    {
        if (due > now) return;
        var info = ReminderPlan.Info(doc, rowId);
        var key = ReminderPlan.FireKey(0, due);
        if (!info.Acked.Contains(key)) info.Acked.Add(key);
    }

    /// <summary>The first row of the run of empty rows at the end of the table, or -1.</summary>
    public static int TrailingBlankRow(NoteDocument doc, Func<string, bool>? busy = null)
    {
        var first = -1;
        for (var r = doc.Rows.Count - 1; r >= 0; r--)
        {
            if (!IsBlankRow(doc, r, busy)) break;
            first = r;
        }
        return first;
    }

    /// <summary>No text (a days counter's start date does not count), no repeat, reminders, colour, detail table or history.</summary>
    public static bool IsBlankRow(NoteDocument doc, int row, Func<string, bool>? busy = null)
    {
        if (row < 0 || row >= doc.Rows.Count) return false;
        var id = doc.RowIdAt(row);
        var days = DaysCounter.ColumnIndexes(doc);
        for (var c = 0; c < doc.Columns.Count; c++)
            if (!days.Contains(c) && NoteDocument.HasText(doc.GetCell(row, c))) return false;
        if (doc.Recurrence.TryGetValue(id, out var rep) && Recurrence.IsActive(rep)) return false;
        if (ReminderPlan.OffsetsFor(doc, id).Count > 0) return false;
        if (doc.RowStyles.TryGetValue(id, out var style) && (style.Highlight || RowStyleOps.HexFor(style.Color) != null)) return false;
        if (CellDetailOps.CaptureRow(doc, id) != null) return false;
        if (doc.RowHistory.TryGetValue(id, out var history) && history.Count > 0) return false;
        return busy?.Invoke(id) != true;
    }

    /// <summary>
    /// Sets the due date and time of a row, as the table's date editor does: snoozes and acknowledgements of the old
    /// time go, a monthly repeat takes the new day; a moment that has already passed does not pop a reminder
    /// (<see cref="SkipPastOnTime"/>). False when the row would not change.
    /// </summary>
    public static bool SetDue(NoteDocument doc, int row, DateTime date, TimeSpan? time, DateTime now)
    {
        var due = doc.DueColumnIndex();
        if (due < 0 || row < 0 || row >= doc.Rows.Count) return false;
        var before = doc.GetCell(row, due);
        var text = DueText(date, time);
        if (before == text) return false;
        var moment = date.Date + (time ?? TimeSpan.Zero);
        if (DueDates.TryParse(before, out var prev) && prev == moment && DueDates.HasTime(before) == time.HasValue) return false;
        doc.SetCellRaw(row, due, text);
        var id = doc.RowIdAt(row);
        ReminderPlan.Reset(doc, id, now);
        SkipPastOnTime(doc, id, moment, now);
        if (doc.Recurrence.TryGetValue(id, out var kind) &&
            Recurrence.NormalizeKind(kind).StartsWith(Recurrence.MonthlyPrefix, StringComparison.Ordinal))
            doc.Recurrence[id] = Recurrence.MonthlyPrefix + date.Day;
        return true;
    }

    /// <summary>Moves a row to another day. Its time stays; an all-day item stays all day.</summary>
    public static bool Move(NoteDocument doc, int row, DateTime date, DateTime now)
    {
        var due = doc.DueColumnIndex();
        if (due < 0 || row < 0 || row >= doc.Rows.Count) return false;
        var raw = doc.GetCell(row, due);
        TimeSpan? time = DueDates.TryParse(raw, out var d) && DueDates.HasTime(raw) ? d.TimeOfDay : null;
        return SetDue(doc, row, date, time, now);
    }

    /// <summary>Changes the time of a row and keeps its day (null = all day).</summary>
    public static bool SetTime(NoteDocument doc, int row, TimeSpan? time, DateTime now)
    {
        var due = doc.DueColumnIndex();
        if (due < 0 || row < 0 || row >= doc.Rows.Count) return false;
        return DueDates.TryParse(doc.GetCell(row, due), out var d) && SetDue(doc, row, d.Date, time, now);
    }

    /// <summary>Takes the row off the calendar: the due date is emptied, the row stays in the table.</summary>
    public static bool ClearDate(NoteDocument doc, int row, DateTime now)
    {
        var due = doc.DueColumnIndex();
        if (due < 0 || row < 0 || row >= doc.Rows.Count || doc.GetCell(row, due).Length == 0) return false;
        doc.SetCellRaw(row, due, "");
        ReminderPlan.Reset(doc, doc.RowIdAt(row), now);
        return true;
    }

    // ───────────────────────── rows written before the calendar ─────────────────────────

    /// <summary>
    /// Rows with text whose due date is empty or can't be read, in table order: what is not on the calendar. For each,
    /// the first date written in its other cells (title, detail, a date column…; not status, due or days counters).
    /// </summary>
    public static List<UnplacedRow> Unplaced(NoteDocument doc)
    {
        var list = new List<UnplacedRow>();
        var due = doc.DueColumnIndex();
        if (due < 0) return list;
        var skip = SystemColumns(doc);
        var days = DaysCounter.ColumnIndexes(doc);
        var status = doc.StatusColumnIndex();
        for (var r = 0; r < doc.Rows.Count; r++)
        {
            var raw = doc.GetCell(r, due).Trim();
            if (DueDates.TryParse(raw, out _)) continue;
            var hasText = raw.Length > 0;
            for (var c = 0; c < doc.Columns.Count && !hasText; c++)
                if (c != due && !days.Contains(c) && NoteDocument.HasText(doc.GetCell(r, c))) hasText = true;
            if (!hasText) continue; // an empty row: nothing written in it
            DateTime? found = null;
            var foundTime = false;
            string? foundIn = null;
            for (var c = 0; c < doc.Columns.Count; c++)
            {
                if (skip.Contains(c) || !DueDates.FindIn(doc.GetCell(r, c), out var d, out var t)) continue;
                found = d;
                foundTime = t;
                foundIn = string.IsNullOrWhiteSpace(doc.Columns[c]) ? Localizer.T("delete.column_fallback", c + 1) : doc.Columns[c].Trim();
                break;
            }
            list.Add(new UnplacedRow(doc.RowIdAt(r), r, TitleOf(doc, r), raw, status >= 0 && TaskStatuses.IsDone(doc.GetCell(r, status)),
                found, foundTime, foundIn));
        }
        return list;
    }

    /// <summary>How many rows <see cref="Unplaced"/> would list (without looking for dates in them).</summary>
    public static int UnplacedCount(NoteDocument doc)
    {
        var due = doc.DueColumnIndex();
        if (due < 0) return 0;
        var days = DaysCounter.ColumnIndexes(doc);
        var n = 0;
        for (var r = 0; r < doc.Rows.Count; r++)
        {
            var raw = doc.GetCell(r, due).Trim();
            if (DueDates.TryParse(raw, out _)) continue;
            var hasText = raw.Length > 0;
            for (var c = 0; c < doc.Columns.Count && !hasText; c++)
                if (c != due && !days.Contains(c) && NoteDocument.HasText(doc.GetCell(r, c))) hasText = true;
            if (hasText) n++;
        }
        return n;
    }

    /// <summary>Puts rows on the calendar, each on its day and time (null = all day), as <see cref="SetDue"/> does
    /// (a moment already past does not pop a reminder). Returns how many rows changed.</summary>
    public static int Place(NoteDocument doc, IEnumerable<(string RowId, DateTime Date, TimeSpan? Time)> rows, DateTime now)
    {
        var n = 0;
        doc.EnsureRowIds();
        foreach (var (id, date, time) in rows)
        {
            var r = doc.RowIds.IndexOf(id);
            if (r >= 0 && SetDue(doc, r, date, time, now)) n++;
        }
        return n;
    }

    // ───────────────────────── wallpaper ─────────────────────────

    /// <summary>The calendar's box on the wallpaper: the bottom right of the monitor's work area (above the taskbar),
    /// <paramref name="margin"/> pixels in. A card larger than the work area is cut to it.</summary>
    public static PixelRect CornerBox(PixelRect workArea, int width, int height, int margin)
    {
        margin = Math.Max(0, margin);
        var w = Math.Clamp(width, 1, Math.Max(1, workArea.Width - 2 * margin));
        var h = Math.Clamp(height, 1, Math.Max(1, workArea.Height - 2 * margin));
        return new PixelRect(workArea.Right - margin - w, workArea.Bottom - margin - h, w, h);
    }

    /// <summary>
    /// For the wallpaper: open items that are overdue (at most <paramref name="overdue"/>, the oldest first), then the
    /// next ones from today on within <paramref name="days"/> days, <paramref name="max"/> in all. <paramref name="more"/>
    /// counts the open items left out.
    /// </summary>
    public static List<CalendarItem> Upcoming(NoteDocument doc, DateTime now, int max, out int more, int overdue = 2, int days = 31)
    {
        var all = Items(doc, now.Date.AddDays(-3660), now.Date.AddDays(days), now, hideDone: true, ghosts: false);
        var late = all.Where(i => i.Overdue).ToList();
        var next = all.Where(i => !i.Overdue && i.Date >= now.Date).ToList();
        var list = late.Take(Math.Min(overdue, max)).ToList();
        list.AddRange(next.Take(Math.Max(0, max - list.Count)));
        more = late.Count + next.Count - list.Count;
        return list;
    }

    // ───────────────────────── texts ─────────────────────────

    public static string MonthName(int month) => Localizer.T("calendar.month." + Math.Clamp(month, 1, 12));
    public static string MonthShort(int month) => Localizer.T("calendar.mon." + Math.Clamp(month, 1, 12));
    public static string WeekdayName(DayOfWeek day) => Localizer.T("calendar.weekday." + (int)day);
    public static string WeekdayShort(DayOfWeek day) => Localizer.T("calendar.wd." + (int)day);

    /// <summary>The year in the Buddhist Era as well, in languages that use it (Thai: " (พ.ศ. 2569)"); "" otherwise.
    /// Dates stay CE everywhere else in TableNote.</summary>
    public static string EraSuffix(int year) =>
        Localizer.T("calendar.show_be") == "yes" ? " " + Localizer.T("calendar.be_year", year + 543) : "";

    public static string MonthTitle(int year, int month) => Localizer.T("calendar.month_title", MonthName(month), year) + EraSuffix(year);

    public static string DayTitle(DateTime date) =>
        Localizer.T("calendar.day_title", WeekdayName(date.DayOfWeek), date.Day, MonthName(date.Month), date.Year) + EraSuffix(date.Year);

    public static string ShortDate(DateTime date) => Localizer.T("calendar.short_date", WeekdayShort(date.DayOfWeek), date.Day, MonthShort(date.Month));

    /// <summary>"Today", "Tomorrow", "Yesterday" or "Wed 25 Nov".</summary>
    public static string When(DateTime date, DateTime now)
    {
        var days = (date.Date - now.Date).Days;
        return days switch
        {
            0 => Localizer.T("calendar.today"),
            1 => Localizer.T("calendar.tomorrow"),
            -1 => Localizer.T("calendar.yesterday"),
            _ => ShortDate(date)
        };
    }

    /// <summary>Why the calendar is empty when the table has no due date column, and how to add it.</summary>
    public static string NoDueColumnText() => Localizer.T("calendar.no_due_column", Localizer.T("menu.table"), Localizer.T("menu.spot.add_due"));

    public static string TimeText(CalendarItem item) => item.HasTime ? DueDates.FormatTime(item.Due) : Localizer.T("calendar.all_day");

    public static string TitleText(CalendarItem item) => item.Title.Length > 0 ? item.Title : Localizer.T("calendar.untitled", item.Row + 1);

    /// <summary>"3 items · 1 done · 1 overdue" for a day or a month.</summary>
    public static string Summary(IReadOnlyCollection<CalendarItem> items)
    {
        var real = items.Where(i => !i.Ghost).ToList();
        if (real.Count == 0) return Localizer.T("calendar.none");
        var parts = new List<string> { Localizer.T(real.Count == 1 ? "calendar.count_one" : "calendar.count", real.Count) };
        var done = real.Count(i => i.Done);
        var late = real.Count(i => i.Overdue);
        if (done > 0) parts.Add(Localizer.T("calendar.count_done", done));
        if (late > 0) parts.Add(Localizer.T("calendar.count_overdue", late));
        return string.Join(" · ", parts);
    }

    /// <summary>What a repeating item does: "every day" / "every week" / "every month".</summary>
    public static string RepeatText(string? kind) => Localizer.T("calendar.repeat." + Recurrence.IndexOf(kind));

    /// <summary>Several lines about one item for a tool tip: time and title, status, repeat, reminders, detail.</summary>
    public static string Describe(CalendarItem item)
    {
        var lines = new List<string> { TimeText(item) + "  " + TitleText(item) };
        lines.Add(Localizer.T("calendar.tip_status", TaskStatuses.Label(item.Status)) +
                  (item.Overdue ? " · " + Localizer.T("calendar.overdue") : ""));
        if (item.Ghost) lines.Add(Localizer.T("calendar.tip_ghost", RepeatText(item.Repeat), item.Row + 1));
        else
        {
            if (Recurrence.IsActive(item.Repeat)) lines.Add("🔁 " + RepeatText(item.Repeat));
            if (item.Advance.Count > 0)
                lines.Add("⏰ " + string.Join(", ", item.Advance.Select(o => Localizer.T("remind.before", ReminderPlan.OffsetLabel(o)))));
            lines.Add(Localizer.T("calendar.tip_row", item.Row + 1));
        }
        if (item.Detail.Length > 0) lines.Add(TableLimits.Clip(item.Detail.Replace("\r", "").Replace('\n', ' '), 200));
        return string.Join("\n", lines);
    }
}
