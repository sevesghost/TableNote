namespace TableNote;

/// <summary>
/// State that undo snapshots leave out on purpose — colours (<c>partColors</c>) and reminder acknowledgements and
/// snoozes — so that undoing an edit never wipes them. Some steps remove that state as a side effect, though:
/// deleting a column drops its header and cell colours, and deleting the reminder date column drops every
/// acknowledgement, snooze and reminder-card colour. Before, undoing such a step brought the column back without
/// its colours, and every overdue task the user had already acknowledged popped up again.
/// <see cref="RemovedBy"/> keeps exactly what one step removed, so undo can put it back and redo can take it out.
/// </summary>
public sealed class UndoSideState
{
    private readonly Dictionary<string, PartColor> _colors;
    private readonly Dictionary<string, string> _snoozes;
    private readonly Dictionary<string, string> _acknowledged;
    private readonly Dictionary<string, List<string>> _acked; // row id → acknowledged fire keys

    private UndoSideState(Dictionary<string, PartColor> colors, Dictionary<string, string> snoozes,
        Dictionary<string, string> acknowledged, Dictionary<string, List<string>> acked)
    {
        _colors = colors;
        _snoozes = snoozes;
        _acknowledged = acknowledged;
        _acked = acked;
    }

    public bool IsEmpty => _colors.Count == 0 && _snoozes.Count == 0 && _acknowledged.Count == 0 && _acked.Count == 0;

    /// <summary>Copy of the colours, snoozes and acknowledgements as they are now (before a step runs).</summary>
    public static UndoSideState Capture(NoteDocument doc) => new(
        PartColorOps.Clone(doc.PartColors),
        new Dictionary<string, string>(doc.Snoozes, StringComparer.Ordinal),
        new Dictionary<string, string>(doc.Acknowledged, StringComparer.Ordinal),
        doc.Reminders.Where(kv => kv.Value?.Acked is { Count: > 0 })
            .ToDictionary(kv => kv.Key, kv => kv.Value.Acked.ToList(), StringComparer.Ordinal));

    /// <summary>The entries of this (earlier) capture that are gone from <paramref name="doc"/> now, i.e. what the
    /// step removed. Null when it removed nothing.</summary>
    public UndoSideState? RemovedBy(NoteDocument doc)
    {
        var colors = _colors.Where(kv => doc.PartColors == null || !doc.PartColors.ContainsKey(kv.Key))
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
        var snoozes = _snoozes.Where(kv => !doc.Snoozes.ContainsKey(kv.Key))
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
        var acknowledged = _acknowledged.Where(kv => !doc.Acknowledged.ContainsKey(kv.Key))
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
        var acked = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (id, keys) in _acked)
        {
            var now = doc.Reminders.TryGetValue(id, out var info) ? info.Acked : null;
            var gone = keys.Where(k => now == null || !now.Contains(k)).ToList();
            if (gone.Count > 0) acked[id] = gone;
        }
        var removed = new UndoSideState(colors, snoozes, acknowledged, acked);
        return removed.IsEmpty ? null : removed;
    }

    /// <summary>Undo: put the removed entries back, for rows that exist again. Anything set since is kept.</summary>
    public void PutBack(NoteDocument doc)
    {
        doc.PartColors ??= new Dictionary<string, PartColor>(StringComparer.Ordinal);
        foreach (var (key, color) in _colors)
            doc.PartColors.TryAdd(key, new PartColor { Background = color.Background, Foreground = color.Foreground });
        var rows = new HashSet<string>(doc.RowIds, StringComparer.Ordinal);
        foreach (var (key, until) in _snoozes)
            if (rows.Contains(key.Split('|')[0])) doc.Snoozes.TryAdd(key, until);
        foreach (var (id, due) in _acknowledged)
            if (rows.Contains(id)) doc.Acknowledged.TryAdd(id, due);
        foreach (var (id, keys) in _acked)
        {
            if (!rows.Contains(id)) continue;
            var info = ReminderPlan.Info(doc, id);
            foreach (var k in keys)
                if (!info.Acked.Contains(k)) info.Acked.Add(k);
        }
    }

    /// <summary>Redo: take the same entries out again, as the step itself did.</summary>
    public void TakeOut(NoteDocument doc)
    {
        foreach (var key in _colors.Keys) doc.PartColors?.Remove(key);
        foreach (var key in _snoozes.Keys) doc.Snoozes.Remove(key);
        foreach (var id in _acknowledged.Keys) doc.Acknowledged.Remove(id);
        foreach (var (id, keys) in _acked)
        {
            if (!doc.Reminders.TryGetValue(id, out var info)) continue;
            info.Acked.RemoveAll(keys.Contains);
            ReminderPlan.Prune(doc, id);
        }
    }
}
