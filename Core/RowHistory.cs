using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TableNote;

/// <summary>2.6.0: one entry in a row's history (note.json "rowHistory", by row id). Written for tables that have a
/// days counter column (<see cref="ColumnTypes.Days"/>): what changed in the row and when, plus progress notes.</summary>
public sealed class RowEvent
{
    /// <summary>Local time, "yyyy-MM-dd HH:mm".</summary>
    public string Time { get; set; } = "";
    /// <summary>One of <see cref="RowEventKinds"/>; unknown kinds are kept and shown as a plain change.</summary>
    public string Kind { get; set; } = RowEventKinds.Edit;
    /// <summary>Column id of an edit or status change.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Column { get; set; }
    /// <summary>Column name at the time, shown when the column was deleted since.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? From { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? To { get; set; }
    /// <summary>Text of a progress note.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; set; }
    /// <summary>2.6.0: when a progress note's text was last changed ("yyyy-MM-dd HH:mm"); null if never.
    /// <see cref="Time"/> stays the time the note was written.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Edited { get; set; }

    public RowEvent Clone() => new() { Time = Time, Kind = Kind, Column = Column, Name = Name, From = From, To = To, Text = Text, Edited = Edited };
}

public static class RowEventKinds
{
    public const string Added = "added";
    public const string Edit = "edit";
    public const string Status = "status";
    public const string Note = "note";
}

public static class RowHistoryOps
{
    public const int MaxPerRow = 100;
    public const int MaxTotal = 10_000;
    public const int MaxValueChars = 120;
    public const int MaxNoteChars = 1000;
    /// <summary>Changes to the same cell this close together are one entry ("typed in pieces" → first value → last).</summary>
    public static readonly TimeSpan MergeWindow = TimeSpan.FromMinutes(10);
    /// <summary>A new row's first minutes are data entry: only its status changes are listed.</summary>
    public static readonly TimeSpan EntryWindow = TimeSpan.FromMinutes(10);
    public const string TimeFormat = "yyyy-MM-dd HH:mm";

    public static string FormatTime(DateTime local) => local.ToString(TimeFormat, CultureInfo.InvariantCulture);

    public static bool TryTime(string? text, out DateTime local) =>
        DateTime.TryParseExact(text, TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out local);

    public static string Clip(string? value, int max = MaxValueChars)
    {
        var v = (value ?? "").Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');
        return v.Length <= max ? v : v[..(max - 1)] + "…";
    }

    /// <summary>Entries of a row, oldest first. Empty (not stored) when there are none.</summary>
    public static IReadOnlyList<RowEvent> Of(NoteDocument doc, string rowId) =>
        !string.IsNullOrEmpty(rowId) && doc.RowHistory.TryGetValue(rowId, out var list) ? list : [];

    public static int Count(NoteDocument doc) => doc.RowHistory.Values.Sum(l => l.Count);

    /// <summary>Adds an entry and keeps the limits: per row the oldest change goes first (notes and "added" last),
    /// over <see cref="MaxTotal"/> the oldest change of the whole table.</summary>
    public static void Append(NoteDocument doc, string rowId, RowEvent e)
    {
        if (string.IsNullOrEmpty(rowId)) return;
        if (!doc.RowHistory.TryGetValue(rowId, out var list)) doc.RowHistory[rowId] = list = [];
        list.Add(e);
        while (list.Count > MaxPerRow) list.RemoveAt(OldestDroppable(list));
        if (Count(doc) <= MaxTotal) return;
        var excess = Count(doc) - MaxTotal;
        var all = doc.RowHistory
            .SelectMany(kv => kv.Value.Select(ev => (row: kv.Key, ev)))
            .Where(x => x.ev.Kind is not (RowEventKinds.Note or RowEventKinds.Added))
            .OrderBy(x => x.ev.Time, StringComparer.Ordinal)
            .Take(excess).ToList();
        foreach (var (row, ev) in all) doc.RowHistory[row].Remove(ev);
    }

    private static int OldestDroppable(List<RowEvent> list)
    {
        var i = list.FindIndex(e => e.Kind is not (RowEventKinds.Note or RowEventKinds.Added));
        if (i >= 0) return i;
        i = list.FindIndex(e => e.Kind != RowEventKinds.Added);
        return i >= 0 ? i : 0;
    }

    /// <summary>A progress note typed in the days pop-up. Blank text → nothing.</summary>
    public static RowEvent? AddNote(NoteDocument doc, string rowId, string? text, DateTime now)
    {
        var t = (text ?? "").Trim();
        if (t.Length == 0 || string.IsNullOrEmpty(rowId)) return null;
        if (t.Length > MaxNoteChars) t = t[..MaxNoteChars];
        var e = new RowEvent { Time = FormatTime(now), Kind = RowEventKinds.Note, Text = t };
        Append(doc, rowId, e);
        return e;
    }

    /// <summary>A progress note gets new text. Its time stays (when it was written); <see cref="RowEvent.Edited"/> says
    /// when it was changed. Blank text, the same text, or a note that is no longer in the row's history → false.</summary>
    public static bool EditNote(NoteDocument doc, string rowId, RowEvent e, string? text, DateTime now)
    {
        if (e.Kind != RowEventKinds.Note || string.IsNullOrEmpty(rowId)) return false;
        if (!doc.RowHistory.TryGetValue(rowId, out var list) || !list.Contains(e)) return false;
        var t = (text ?? "").Trim();
        if (t.Length == 0) return false;
        if (t.Length > MaxNoteChars) t = t[..MaxNoteChars];
        if (t == e.Text) return false;
        e.Text = t;
        e.Edited = FormatTime(now);
        return true;
    }

    public static bool Remove(NoteDocument doc, string rowId, RowEvent e)
    {
        if (!doc.RowHistory.TryGetValue(rowId, out var list) || !list.Remove(e)) return false;
        if (list.Count == 0) doc.RowHistory.Remove(rowId);
        return true;
    }

    /// <summary>When the row was last set to done (null: it is not done, or that was not recorded).</summary>
    public static DateTime? DoneAt(NoteDocument doc, string rowId)
    {
        var list = Of(doc, rowId);
        for (var i = list.Count - 1; i >= 0; i--)
            if (list[i].Kind == RowEventKinds.Status)
                return TaskStatuses.IsDone(list[i].To) && TryTime(list[i].Time, out var t) ? t : null;
        return null;
    }

    /// <summary>Time of the last status change (null: none recorded).</summary>
    public static DateTime? StatusSince(NoteDocument doc, string rowId)
    {
        var list = Of(doc, rowId);
        for (var i = list.Count - 1; i >= 0; i--)
            if (list[i].Kind == RowEventKinds.Status && TryTime(list[i].Time, out var t)) return t;
        return null;
    }

    /// <summary>Time of the newest change or note ("added" does not count as an update).</summary>
    public static DateTime? LastUpdate(NoteDocument doc, string rowId)
    {
        DateTime? best = null;
        foreach (var e in Of(doc, rowId))
            if (e.Kind != RowEventKinds.Added && TryTime(e.Time, out var t) && (best == null || t > best)) best = t;
        return best;
    }

    /// <summary>Changes the pop-up needs to notice in a kept (side-copy) session: count, newest entry, and the notes'
    /// text (a hash, so an edited note counts too).</summary>
    public static string Signature(NoteDocument doc)
    {
        var n = 0; var newest = ""; var notes = 14695981039346656037UL; // FNV-1a, the same in every run
        foreach (var list in doc.RowHistory.Values)
            foreach (var e in list)
            {
                n++;
                if (string.CompareOrdinal(e.Time, newest) > 0) newest = e.Time;
                if (e.Kind != RowEventKinds.Note) continue;
                foreach (var ch in (e.Text ?? "") + "\u0001" + (e.Edited ?? "") + "\u0002")
                    notes = (notes ^ ch) * 1099511628211UL;
            }
        return n + "/" + notes.ToString("x16", CultureInfo.InvariantCulture) + "/" + newest;
    }

    /// <summary>Entries of rows that no longer exist are dropped (on load and when saving).</summary>
    public static int Prune(NoteDocument doc)
    {
        if (doc.RowHistory.Count == 0) return 0;
        var ids = new HashSet<string>(doc.RowIds, StringComparer.Ordinal);
        var gone = doc.RowHistory.Keys.Where(k => !ids.Contains(k) || doc.RowHistory[k].Count == 0).ToList();
        foreach (var k in gone) doc.RowHistory.Remove(k);
        return gone.Count;
    }

    /// <summary>note.json value: rows that exist, in table order. Null when there is nothing to write (old files stay unchanged).</summary>
    public static Dictionary<string, List<RowEvent>>? ForSave(NoteDocument doc)
    {
        if (doc.RowHistory.Count == 0) return null;
        var outMap = new Dictionary<string, List<RowEvent>>(StringComparer.Ordinal);
        foreach (var id in doc.RowIds)
            if (doc.RowHistory.TryGetValue(id, out var list) && list.Count > 0)
                outMap[id] = list;
        return outMap.Count == 0 ? null : outMap;
    }

    /// <summary>Lenient: a bad row or entry is skipped (and reported), never the whole note.</summary>
    public static Dictionary<string, List<RowEvent>> Parse(JsonElement? el, ICollection<string> issues)
    {
        var map = new Dictionary<string, List<RowEvent>>(StringComparer.Ordinal);
        if (el is not { } root || root.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return map;
        if (root.ValueKind != JsonValueKind.Object) { issues.Add("rowHistory: not an object, skipped"); return map; }
        foreach (var row in root.EnumerateObject())
        {
            if (row.Value.ValueKind != JsonValueKind.Array) { issues.Add($"rowHistory.{row.Name}: not a list, skipped"); continue; }
            var list = new List<RowEvent>();
            foreach (var item in row.Value.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) { issues.Add($"rowHistory.{row.Name}: an entry is not an object, skipped"); continue; }
                var e = new RowEvent
                {
                    Time = Str(item, "time") ?? "",
                    Kind = Str(item, "kind") ?? RowEventKinds.Edit,
                    Column = Str(item, "column"),
                    Name = Str(item, "name"),
                    From = Str(item, "from"),
                    To = Str(item, "to"),
                    Text = Str(item, "text"),
                    Edited = Str(item, "edited")
                };
                if (!TryTime(e.Time, out _)) { issues.Add($"rowHistory.{row.Name}: entry without a valid time, skipped"); continue; }
                if (e.Edited != null && !TryTime(e.Edited, out _)) e.Edited = null; // only shown, so a bad one is just dropped
                list.Add(e);
            }
            if (list.Count > MaxPerRow) list.RemoveRange(0, list.Count - MaxPerRow);
            if (list.Count > 0) map[row.Name] = list;
        }
        return map;
    }

    private static string? Str(JsonElement obj, string name)
    {
        foreach (var p in obj.EnumerateObject())
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                return p.Value.ValueKind switch
                {
                    JsonValueKind.String => p.Value.GetString(),
                    JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => p.Value.GetRawText(),
                    _ => null
                };
        return null;
    }
}

/// <summary>2.6.0: finds what changed in the table since the last look and writes it to the rows' history.
/// Called on every autosave (MainWindow.ScheduleSave / DoSave), so every way a cell changes — typing, paste,
/// the status list, the reminder pop-up, undo — is seen the same way. Only works while the table has a days
/// counter column; when one appears, history starts from that moment.</summary>
public sealed class RowHistoryTracker
{
    private List<string>? _columns; // column ids of the baseline, in its order
    private readonly Dictionary<string, string[]> _rows = new(StringComparer.Ordinal); // row id → cells (incl. deleted rows, so undoing a delete is no "added")

    public bool Active => _columns != null;

    /// <summary>New baseline (after loading a note). No entries are written.</summary>
    public void Reset(NoteDocument doc)
    {
        _columns = null;
        _rows.Clear();
        if (DaysCounter.HasColumn(doc)) Take(doc);
    }

    /// <summary>Compares the table with the last look and records the changes. True when the history changed.</summary>
    public bool Capture(NoteDocument doc, DateTime now)
    {
        if (!DaysCounter.HasColumn(doc)) { _columns = null; _rows.Clear(); return false; }
        doc.EnsureRowIds();
        doc.EnsureColumnIds();
        if (_columns == null) { Take(doc); return false; }

        var sameColumns = doc.ColumnIds.Count == _columns.Count && doc.ColumnIds.SequenceEqual(_columns, StringComparer.Ordinal);
        var map = new int[doc.Columns.Count];
        for (var c = 0; c < map.Length; c++)
            map[c] = sameColumns ? c : c < doc.ColumnIds.Count ? _columns.IndexOf(doc.ColumnIds[c]) : -1;
        var statusCol = doc.StatusColumnIndex();
        var changed = false;
        for (var r = 0; r < doc.Rows.Count; r++)
        {
            var id = doc.RowIdAt(r);
            if (!_rows.TryGetValue(id, out var old))
            {
                RowHistoryOps.Append(doc, id, new RowEvent { Time = RowHistoryOps.FormatTime(now), Kind = RowEventKinds.Added });
                changed = true;
                continue; // its cells join the baseline below
            }
            var row = doc.Rows[r];
            for (var c = 0; c < map.Length; c++)
            {
                var b = map[c];
                if (b < 0 || b >= old.Length) continue;
                var cur = c < row.Count ? row[c] ?? "" : "";
                var prev = old[b];
                if (ReferenceEquals(cur, prev) || cur == prev) continue;
                changed |= Record(doc, id, doc.ColumnIds[c], doc.Columns[c], prev, cur, c == statusCol, now);
                old[b] = cur;
            }
        }
        if (!sameColumns) Take(doc, map);
        else
            for (var r = 0; r < doc.Rows.Count; r++)
            {
                var id = doc.RowIdAt(r);
                if (!_rows.ContainsKey(id)) _rows[id] = Cells(doc.Rows[r], doc.Columns.Count);
            }
        return changed;
    }

    private static bool Record(NoteDocument doc, string rowId, string columnId, string columnName, string from, string to, bool status, DateTime now)
    {
        var list = RowHistoryOps.Of(doc, rowId);
        if (!status && list.Count > 0 && list[0].Kind == RowEventKinds.Added &&
            RowHistoryOps.TryTime(list[0].Time, out var added) && now - added < RowHistoryOps.EntryWindow)
            return false; // typing a new row's first values is not "progress"
        var kind = status ? RowEventKinds.Status : RowEventKinds.Edit;
        var last = list.Count > 0 ? list[^1] : null;
        if (last != null && last.Kind == kind && last.Column == columnId &&
            RowHistoryOps.TryTime(last.Time, out var lastTime) && now - lastTime < RowHistoryOps.MergeWindow && now >= lastTime)
        {
            last.To = RowHistoryOps.Clip(to);
            last.Name = columnName;
            last.Time = RowHistoryOps.FormatTime(now);
            if (last.From == last.To) RowHistoryOps.Remove(doc, rowId, last); // typed and put back: nothing happened
            return true;
        }
        RowHistoryOps.Append(doc, rowId, new RowEvent
        {
            Time = RowHistoryOps.FormatTime(now), Kind = kind, Column = columnId, Name = columnName,
            From = RowHistoryOps.Clip(from), To = RowHistoryOps.Clip(to)
        });
        return true;
    }

    private void Take(NoteDocument doc, int[]? remap = null)
    {
        // Rows that are gone (deleted, maybe undone later) keep their cells, moved to the new column order.
        var kept = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (remap != null)
        {
            var current = new HashSet<string>(doc.RowIds, StringComparer.Ordinal);
            foreach (var (id, cells) in _rows)
                if (!current.Contains(id))
                    kept[id] = remap.Select(b => b >= 0 && b < cells.Length ? cells[b] : "").ToArray();
        }
        _columns = doc.ColumnIds.ToList();
        _rows.Clear();
        foreach (var (id, cells) in kept) _rows[id] = cells;
        for (var r = 0; r < doc.Rows.Count; r++) _rows[doc.RowIdAt(r)] = Cells(doc.Rows[r], doc.Columns.Count);
    }

    private static string[] Cells(List<string> row, int count)
    {
        var a = new string[count];
        for (var c = 0; c < count; c++) a[c] = c < row.Count ? row[c] ?? "" : "";
        return a;
    }
}
