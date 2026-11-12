namespace TableNote;

/// <summary>Per-task advance-reminder settings stored in note.json "reminders".</summary>
public sealed class ReminderInfo
{
    /// <summary>Minutes before due (1..43200). On-time (0) is always implied.</summary>
    public List<int> Offsets { get; set; } = [];
    /// <summary>Acknowledged fires as "offset@yyyy-MM-dd HH:mm(due)". Tied to the due value, so editing due invalidates them.</summary>
    public List<string> Acked { get; set; } = [];

    public ReminderInfo Clone() => new() { Offsets = Offsets.ToList(), Acked = Acked.ToList() };
}

/// <summary>
/// Pure scheduling logic for reminders (A1–A8). No UI, no timers — unit-testable.
/// Fire key = "offset@dueKey". Snooze key in doc.Snoozes = "rowId|offset@dueKey"
/// (legacy plain "rowId" keys are treated as an on-time snooze).
/// </summary>
public static class ReminderPlan
{
    public const int MaxOffsets = 5;
    public const int MaxOffsetMinutes = 30 * 24 * 60;
    public const int SnoozeMinutes = 10;

    public static readonly int[] PresetMinutes = [15, 60, 1440];
    public static IEnumerable<(int Minutes, string Label)> Presets => PresetMinutes.Select(m => (m, OffsetLabel(m)));

    public static string FireKey(int offset, DateTime due) => $"{offset}@{DueDates.Format(due)}";
    public static string SnoozeKey(string rowId, string fireKey) => rowId + "|" + fireKey;

    /// <summary>Clean offsets: drop invalid, dedupe, sort desc, cap at 5.</summary>
    public static List<int> CleanOffsets(IEnumerable<int>? offsets) =>
        (offsets ?? [])
            .Where(o => o > 0 && o <= MaxOffsetMinutes)
            .Distinct()
            .OrderByDescending(o => o)
            .Take(MaxOffsets)
            .ToList();

    public static ReminderInfo Info(NoteDocument doc, string rowId)
    {
        if (!doc.Reminders.TryGetValue(rowId, out var info))
        {
            info = new ReminderInfo();
            doc.Reminders[rowId] = info;
        }
        return info;
    }

    public static List<int> OffsetsFor(NoteDocument doc, string rowId) =>
        doc.Reminders.TryGetValue(rowId, out var i) ? i.Offsets : [];

    public static string OffsetLabel(int minutes)
    {
        return Localizer.Duration(minutes);
    }

    /// <summary>Find the one fire per task that should show now (nearest to due), or null.</summary>
    public static DueTask? Next(NoteDocument doc, int row, DateTime now)
    {
        var dueCol = doc.DueColumnIndex();
        if (dueCol < 0) return null;
        var statusCol = doc.StatusColumnIndex();
        if (statusCol >= 0 && TaskStatuses.IsDone(doc.GetCell(row, statusCol))) return null;
        if (!DueDates.TryParse(doc.GetCell(row, dueCol), out var due)) return null;

        var id = doc.RowIdAt(row);
        doc.Reminders.TryGetValue(id, out var info);
        var acked = info?.Acked ?? [];

        // Offsets ascending: 0 (on time) first = nearest to due.
        var offsets = new List<int> { 0 };
        // Missed advance reminders of an already-overdue task are skipped (only on-time remains).
        if (due > now && info != null) offsets.AddRange(info.Offsets.Where(o => o > 0).OrderBy(o => o));

        foreach (var off in offsets)
        {
            var fireAt = due.AddMinutes(-off);
            if (fireAt > now) continue;
            var key = FireKey(off, due);
            if (acked.Contains(key)) continue;
            // Legacy v2.1 acknowledgement (rowId -> due) counts as on-time ack.
            if (off == 0 && doc.Acknowledged.TryGetValue(id, out var legacy) && legacy == DueDates.Format(due)) continue;
            if (IsSnoozed(doc, id, key, off, now)) return null; // nearest pending fire is snoozed → wait
            return Build(doc, row, id, due, off, fireAt, now - fireAt > DueTask.LateThreshold);
        }
        return null;
    }

    private static bool IsSnoozed(NoteDocument doc, string id, string key, int off, DateTime now)
    {
        if (doc.Snoozes.TryGetValue(SnoozeKey(id, key), out var s) && DueDates.TryParse(s, out var until) && until > now)
            return true;
        if (off == 0 && doc.Snoozes.TryGetValue(id, out var legacy) && DueDates.TryParse(legacy, out var lu) && lu > now)
            return true;
        return false;
    }

    private static DueTask Build(NoteDocument doc, int row, string id, DateTime due, int off, DateTime fireAt, bool late)
    {
        var titleIdx = doc.IndexOfRole(ColumnRoles.Title);
        if (titleIdx < 0) titleIdx = doc.IndexOfColumn(NoteDocument.ColTitle);
        var title = titleIdx >= 0 ? doc.GetCell(row, titleIdx).Trim() : "";
        if (title.Length == 0) title = Localizer.T("ui.row_task", row + 1);
        var detailIdx = doc.IndexOfRole(ColumnRoles.Detail);
        if (detailIdx < 0) detailIdx = doc.IndexOfColumn(NoteDocument.ColDetail);
        var detail = detailIdx >= 0 ? doc.GetCell(row, detailIdx).Trim() : "";
        return new DueTask(row, id, title, detail, due, off, fireAt, late);
    }

    public static List<DueTask> Collect(NoteDocument doc, DateTime now, ISet<string>? exclude = null)
    {
        var list = new List<DueTask>();
        for (var i = 0; i < doc.Rows.Count; i++)
        {
            if (exclude != null && exclude.Contains(doc.RowIdAt(i))) continue;
            if (Next(doc, i, now) is { } t) list.Add(t);
        }
        return list;
    }

    /// <summary>Close: acknowledge this fire and every earlier (larger-offset) fire of the same due.</summary>
    public static void Acknowledge(NoteDocument doc, DueTask t)
    {
        var info = Info(doc, t.RowId);
        foreach (var off in info.Offsets.Append(0).Where(o => o >= t.Offset))
        {
            var key = FireKey(off, t.Due);
            if (!info.Acked.Contains(key)) info.Acked.Add(key);
            doc.Snoozes.Remove(SnoozeKey(t.RowId, key));
        }
        if (t.Offset == 0) doc.Snoozes.Remove(t.RowId);
        Prune(doc, t.RowId);
    }

    /// <summary>
    /// Snooze only this fire for 10 min. If an advance fire would be snoozed past due,
    /// merge into the on-time reminder (ack the advance instead).
    /// </summary>
    public static void Snooze(NoteDocument doc, DueTask t, DateTime now)
    {
        var until = now.AddMinutes(SnoozeMinutes);
        if (t.Offset > 0 && until >= t.Due)
        {
            Acknowledge(doc, t);
            return;
        }
        var info = Info(doc, t.RowId);
        // Earlier (larger-offset) fires are superseded by this one.
        foreach (var off in info.Offsets.Where(o => o > t.Offset))
        {
            var k = FireKey(off, t.Due);
            if (!info.Acked.Contains(k)) info.Acked.Add(k);
        }
        doc.Snoozes[SnoozeKey(t.RowId, FireKey(t.Offset, t.Due))] = DueDates.Format(until);
        if (t.Offset == 0) doc.Snoozes.Remove(t.RowId); // replace legacy key
        Prune(doc, t.RowId);
    }

    /// <summary>Due edited / offsets changed / status reopened: clear snoozes+acks, skip fires already past.</summary>
    public static void Reset(NoteDocument doc, string rowId, DateTime now)
    {
        ClearSnoozes(doc, rowId);
        doc.Acknowledged.Remove(rowId);
        if (!doc.Reminders.TryGetValue(rowId, out var info)) return;
        info.Acked.Clear();
        // Advance fires that are already in the past at setting time are skipped (not popped now).
        SkipPastAdvance(doc, rowId, now);
        Prune(doc, rowId);
    }

    public static void ClearSnoozes(NoteDocument doc, string rowId)
    {
        doc.Snoozes.Remove(rowId);
        foreach (var k in doc.Snoozes.Keys.Where(k => k.StartsWith(rowId + "|", StringComparison.Ordinal)).ToList())
            doc.Snoozes.Remove(k);
    }

    /// <summary>Done: clear pending snoozes/acks for this row.</summary>
    public static void OnDone(NoteDocument doc, string rowId)
    {
        ClearSnoozes(doc, rowId);
        if (doc.Reminders.TryGetValue(rowId, out var info)) info.Acked.Clear();
        Prune(doc, rowId);
    }

    /// <summary>
    /// Change offsets only. Existing acknowledgements and snoozes are KEPT (only a due-date edit clears them);
    /// newly added advance fires that are already in the past are skipped (marked acked), not popped.
    /// </summary>
    public static void SetOffsets(NoteDocument doc, string rowId, IEnumerable<int> offsets, DateTime now)
    {
        var info = Info(doc, rowId);
        info.Offsets = CleanOffsets(offsets);
        SkipPastAdvance(doc, rowId, now);
        Prune(doc, rowId);
    }

    /// <summary>Mark advance fires whose time has already passed as acked (they never pop retroactively).</summary>
    public static void SkipPastAdvance(NoteDocument doc, string rowId, DateTime now)
    {
        if (!doc.Reminders.TryGetValue(rowId, out var info)) return;
        var row = doc.RowIds.IndexOf(rowId);
        var dueCol = doc.DueColumnIndex();
        if (row < 0 || dueCol < 0 || !DueDates.TryParse(doc.GetCell(row, dueCol), out var due)) return;
        foreach (var off in info.Offsets)
        {
            var key = FireKey(off, due);
            if (due.AddMinutes(-off) <= now && !info.Acked.Contains(key)) info.Acked.Add(key);
        }
    }

    /// <summary>Drop empty entries so note.json stays tidy.</summary>
    public static void Prune(NoteDocument doc, string rowId)
    {
        if (doc.Reminders.TryGetValue(rowId, out var i) && i.Offsets.Count == 0 && i.Acked.Count == 0)
            doc.Reminders.Remove(rowId);
    }

    /// <summary>Load-time validation: bad values dropped individually (no .bak).</summary>
    public static void Sanitize(NoteDocument doc)
    {
        var ids = new HashSet<string>(doc.RowIds, StringComparer.Ordinal);
        foreach (var key in doc.Reminders.Keys.ToList())
        {
            var info = doc.Reminders[key];
            if (!ids.Contains(key) || info == null) { doc.Reminders.Remove(key); continue; }
            info.Offsets = CleanOffsets(info.Offsets);
            info.Acked = (info.Acked ?? []).Where(a => !string.IsNullOrWhiteSpace(a)).Distinct().Take(200).ToList();
            Prune(doc, key);
        }
    }
}
