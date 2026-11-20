using System.Globalization;

namespace TableNote;

public enum DaysState { Blank, Invalid, Future, Counting, Done, DoneUnknown }

/// <summary>Result of counting one days cell. <see cref="Days"/> is never negative (Future: days until the start).</summary>
public readonly record struct DaysCount(DaysState State, int Days, DateTime Start, DateTime? DoneAt);

/// <summary>One line of the days pop-up's history, newest first.</summary>
public sealed class DaysTimelineItem
{
    public required RowEvent Event { get; init; }
    /// <summary>"20/11/2026 14:51".</summary>
    public required string When { get; init; }
    /// <summary>"142 days after the start" (empty without a start date).</summary>
    public required string Offset { get; init; }
    public required string Title { get; init; }
    public string? Body { get; init; }
    public bool IsNote => Event.Kind == RowEventKinds.Note;
}

/// <summary>Everything the days pop-up shows for one row.</summary>
public sealed class DaysInfo
{
    public required DaysCount Count { get; init; }
    public required string Raw { get; init; }
    public required string Headline { get; init; }
    public required string Range { get; init; }
    public string? StatusLine { get; init; }
    public required string LastUpdateLine { get; init; }
    public required List<DaysTimelineItem> Timeline { get; init; }
}

/// <summary>2.6.0: the days counter column. The cell keeps the start date ("yyyy-MM-dd", any date the table
/// already reads is fine); the table, the pictures and the wallpaper show the days since then. A done row stops
/// counting on the day it was set to done (from the row history).</summary>
public static class DaysCounter
{
    public static bool IsDaysColumn(NoteDocument doc, int column) =>
        column >= 0 && column < doc.Columns.Count && ColumnSettingsOps.TypeOf(doc, column) == ColumnTypes.Days;

    public static List<int> ColumnIndexes(NoteDocument doc)
    {
        var list = new List<int>();
        if (doc.ColumnSettings.Count == 0) return list;
        for (var c = 0; c < doc.Columns.Count; c++)
            if (IsDaysColumn(doc, c)) list.Add(c);
        return list;
    }

    public static bool HasColumn(NoteDocument doc) =>
        doc.ColumnSettings.Count > 0 && doc.ColumnSettings.Values.Any(s => s != null && ColumnTypes.Normalize(s.Type) == ColumnTypes.Days)
        && ColumnIndexes(doc).Count > 0;

    public static string TodayText(DateTime now) => DueDates.FormatDate(now);

    /// <summary>A new row starts counting today (blank days cells only).</summary>
    public static void FillNewRow(NoteDocument doc, int row, DateTime now)
    {
        if (row < 0 || row >= doc.Rows.Count) return;
        foreach (var c in ColumnIndexes(doc))
            if (string.IsNullOrWhiteSpace(doc.GetCell(row, c))) doc.SetCellRaw(row, c, TodayText(now));
    }

    /// <summary>A days column that is still empty starts counting today in every row that is not done.
    /// Returns the number of cells written. Columns with any text are left as they are.</summary>
    public static int StartToday(NoteDocument doc, int column, DateTime now)
    {
        if (!IsDaysColumn(doc, column) || doc.Rows.Any(r => column < r.Count && NoteDocument.HasText(r[column]))) return 0;
        var status = doc.StatusColumnIndex();
        var n = 0;
        for (var r = 0; r < doc.Rows.Count; r++)
        {
            if (status >= 0 && TaskStatuses.IsDone(doc.GetCell(r, status))) continue;
            doc.SetCellRaw(r, column, TodayText(now));
            n++;
        }
        return n;
    }

    public static DaysCount Compute(string? raw, DateTime now, bool done, DateTime? doneAt)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new DaysCount(DaysState.Blank, 0, default, null);
        if (!DueDates.TryParse(raw, out var parsed)) return new DaysCount(DaysState.Invalid, 0, default, null);
        var start = parsed.Date;
        if (done)
            return doneAt is { } d
                ? new DaysCount(DaysState.Done, Math.Max(0, (d.Date - start).Days), start, d)
                : new DaysCount(DaysState.DoneUnknown, 0, start, null);
        var days = (now.Date - start).Days;
        return days < 0
            ? new DaysCount(DaysState.Future, -days, start, null)
            : new DaysCount(DaysState.Counting, days, start, null);
    }

    public static DaysCount ForCell(NoteDocument doc, int row, int column, DateTime now)
    {
        var status = doc.StatusColumnIndex();
        var done = status >= 0 && TaskStatuses.IsDone(doc.GetCell(row, status));
        var id = doc.RowIdAt(row);
        return Compute(doc.GetCell(row, column), now, done, done ? RowHistoryOps.DoneAt(doc, id) : null);
    }

    /// <summary>Days as words: "1 day", "150 days" / "1 วัน", "150 วัน".</summary>
    public static string DaysText(int n) =>
        n == 1 ? Localizer.T("days.n_one") : Localizer.T("days.n", n.ToString("#,0", CultureInfo.InvariantCulture));

    /// <summary>What the cell shows. Blank → ""; a value that is not a date → the value itself.</summary>
    public static string CellText(DaysCount c, string? raw) => c.State switch
    {
        DaysState.Blank => "",
        DaysState.Invalid => (raw ?? "").Trim(),
        DaysState.Future => Localizer.T("days.future", DaysText(c.Days)),
        DaysState.Counting => c.Days == 0 ? Localizer.T("days.today") : DaysText(c.Days),
        DaysState.Done => Localizer.T("days.done_n", DaysText(c.Days)),
        _ => Localizer.T("days.done_unknown")
    };

    public static string CellText(NoteDocument doc, int row, int column, DateTime now) =>
        CellText(ForCell(doc, row, column, now), doc.GetCell(row, column));

    public static string Date(DateTime d) => d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
    public static string DateTimeText(DateTime d) => d.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);

    public static string Ago(DateTime then, DateTime now)
    {
        var n = (now.Date - then.Date).Days;
        return n <= 0 ? Localizer.T("days.ago_today") : n == 1 ? Localizer.T("days.ago_one") : Localizer.T("days.ago_n", n.ToString("#,0", CultureInfo.InvariantCulture));
    }

    /// <summary>The pop-up's content for one row.</summary>
    public static DaysInfo Describe(NoteDocument doc, int row, int column, DateTime now)
    {
        var raw = doc.GetCell(row, column);
        var count = ForCell(doc, row, column, now);
        var id = doc.RowIdAt(row);
        var headline = count.State switch
        {
            DaysState.Blank => Localizer.T("days.head_blank"),
            DaysState.Invalid => Localizer.T("days.head_invalid", raw.Trim()),
            DaysState.Future => Localizer.T("days.head_future", DaysText(count.Days)),
            DaysState.Counting => count.Days == 0 ? Localizer.T("days.head_today") : Localizer.T("days.head_open", DaysText(count.Days)),
            DaysState.Done => Localizer.T("days.head_done", DaysText(count.Days)),
            _ => Localizer.T("days.head_done_unknown")
        };
        var range = count.State switch
        {
            DaysState.Counting or DaysState.Future => Localizer.T("days.range_open", Date(count.Start), Date(now)),
            DaysState.Done => Localizer.T("days.range_done", Date(count.Start), Date(count.DoneAt!.Value)),
            DaysState.DoneUnknown => Localizer.T("days.range_done_unknown", Date(count.Start)),
            _ => Localizer.T("days.range_blank")
        };

        string? statusLine = null;
        var statusCol = doc.StatusColumnIndex();
        if (statusCol >= 0)
        {
            var label = TaskStatuses.Label(TaskStatuses.DisplayCode(doc.GetCell(row, statusCol)));
            statusLine = RowHistoryOps.StatusSince(doc, id) is { } since
                ? Localizer.T("days.status_since", label, DateTimeText(since), Ago(since, now))
                : Localizer.T("days.status_now", label);
        }
        var last = RowHistoryOps.LastUpdate(doc, id);
        var lastLine = last is { } l
            ? Localizer.T("days.last_update", DateTimeText(l), Ago(l, now))
            : Localizer.T("days.no_update");

        var timeline = new List<DaysTimelineItem>();
        var events = RowHistoryOps.Of(doc, id);
        for (var i = events.Count - 1; i >= 0; i--)
        {
            var e = events[i];
            var hasTime = RowHistoryOps.TryTime(e.Time, out var t);
            timeline.Add(new DaysTimelineItem
            {
                Event = e,
                When = hasTime ? DateTimeText(t) : e.Time,
                Offset = hasTime && count.State is DaysState.Counting or DaysState.Future or DaysState.Done or DaysState.DoneUnknown
                    ? Offset(count.Start, t) : "",
                Title = Title(doc, e),
                Body = e.Kind == RowEventKinds.Note ? e.Text : null
            });
        }
        return new DaysInfo
        {
            Count = count, Raw = raw, Headline = headline, Range = range, StatusLine = statusLine,
            LastUpdateLine = lastLine, Timeline = timeline
        };
    }

    public static string Offset(DateTime start, DateTime at)
    {
        var d = (at.Date - start.Date).Days;
        return d == 0 ? Localizer.T("days.offset_0")
            : d > 0 ? Localizer.T("days.offset_n", DaysText(d))
            : Localizer.T("days.offset_before", DaysText(-d));
    }

    /// <summary>One line for an entry: "Status: Not started → In progress", "Step: A → B", "Row added", "Progress note".</summary>
    public static string Title(NoteDocument doc, RowEvent e)
    {
        switch (e.Kind)
        {
            case RowEventKinds.Added: return Localizer.T("days.ev_added");
            case RowEventKinds.Note: return Localizer.T("days.ev_note");
            case RowEventKinds.Status:
                return Localizer.T("days.ev_status", StatusText(e.From), StatusText(e.To));
            default:
            {
                var column = e.Column != null ? doc.ColumnIds.IndexOf(e.Column) : -1;
                var name = column >= 0 ? doc.Columns[column] : e.Name;
                if (string.IsNullOrWhiteSpace(name)) name = Localizer.T("days.column_gone");
                var dates = column >= 0 && (column == doc.DueColumnIndex() || IsDaysColumn(doc, column));
                return Localizer.T("days.ev_edit", name!, Value(e.From, dates), Value(e.To, dates));
            }
        }
    }

    private static string StatusText(string? value) => TaskStatuses.Label(TaskStatuses.DisplayCode(value));

    private static string Value(string? v, bool date)
    {
        if (string.IsNullOrWhiteSpace(v)) return Localizer.T("days.blank_value");
        if (date && DueDates.TryParse(v, out var d)) return DueDates.HasTime(v) ? DateTimeText(d) : Date(d);
        return v;
    }
}
