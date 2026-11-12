namespace TableNote;

public sealed class UndoStack
{
    public const int MaxSteps = 100;
    /// <summary>Total undo+redo memory cap; oldest steps are dropped first (the newest step is always kept).</summary>
    public const long MaxBytes = 32L * 1024 * 1024;
    private readonly List<IUndoAction> _undo = [];
    private readonly List<IUndoAction> _redo = [];

    public bool CanUndo => _undo.Count > 0;
    public int Count => _undo.Count;
    public long Bytes => _undo.Sum(a => a.Bytes) + _redo.Sum(a => a.Bytes);

    private void Trim()
    {
        while (_undo.Count > MaxSteps) _undo.RemoveAt(0);
        while (_undo.Count > 1 && Bytes > MaxBytes) _undo.RemoveAt(0);
    }
    public bool CanRedo => _redo.Count > 0;
    public string? LastUndoLabel => _undo.Count > 0 ? _undo[^1].Label : null;

    public void Push(IUndoAction action)
    {
        _undo.Add(action);
        _redo.Clear();
        Trim();
    }

    public string? Undo(NoteDocument doc)
    {
        if (_undo.Count == 0) return null;
        var a = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        a.Undo(doc);
        _redo.Add(a);
        return a.Label;
    }

    public string? Redo(NoteDocument doc)
    {
        if (_redo.Count == 0) return null;
        var a = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        a.Redo(doc);
        _undo.Add(a);
        Trim();
        return a.Label;
    }
}

public interface IUndoAction
{
    string Label { get; }
    /// <summary>Approximate memory held by this step.</summary>
    long Bytes => 64;
    void Undo(NoteDocument doc);
    void Redo(NoteDocument doc);
}

/// <summary>Cell edit keyed by row id (not index) so sorting / inserting rows can't break undo.</summary>
public sealed class CellEditAction(string rowId, int col, string before, string after, string? label = null,
    string? colName = null, string? colRole = null) : IUndoAction
{
    public string Label => label ?? Localizer.T("undo.edit_cell"); // v2.4.2: default resolved when shown
    public string RowId => rowId;
    public int Column => col;

    /// <summary>v2.3.1 (L3): also remember WHICH column (header + role) so a step whose index went stale
    /// (column added/removed outside the undo order) can't write into a different column.</summary>
    public static CellEditAction For(NoteDocument doc, string rowId, int col, string before, string after, string? label = null)
    {
        doc.EnsureColumnRoles();
        return new CellEditAction(rowId, col, before, after, label,
            doc.Columns.ElementAtOrDefault(col) ?? "", doc.ColumnRolesList.ElementAtOrDefault(col) ?? "");
    }

    private int ResolveColumn(NoteDocument doc)
    {
        if (colName == null) return col; // created without identity (old call sites / tests)
        doc.EnsureColumnRoles();
        if (col >= 0 && col < doc.Columns.Count && doc.Columns[col] == colName &&
            (doc.ColumnRolesList.ElementAtOrDefault(col) ?? "") == (colRole ?? "")) return col;
        if (!string.IsNullOrEmpty(colRole)) return doc.IndexOfRole(colRole);
        var hits = doc.Columns.Select((n, i) => (n, i)).Where(x => x.n == colName).ToList();
        return hits.Count == 1 ? hits[0].i : -1; // ambiguous / gone → skip rather than overwrite another column
    }

    public void Undo(NoteDocument doc) { var r = doc.RowIds.IndexOf(rowId); var c = ResolveColumn(doc); if (r >= 0 && c >= 0) doc.SetCellRaw(r, c, before); }
    public void Redo(NoteDocument doc) { var r = doc.RowIds.IndexOf(rowId); var c = ResolveColumn(doc); if (r >= 0 && c >= 0) doc.SetCellRaw(r, c, after); }
}

public sealed class HeaderEditAction(int col, string before, string after) : IUndoAction
{
    public string Label => Localizer.T("undo.edit_header");
    // v2.3.1 (L3/L13): the header must still read the value this step left there; otherwise find it by that value.
    private static int Find(NoteDocument doc, int col, string expected)
    {
        if (col >= 0 && col < doc.Columns.Count && doc.Columns[col] == expected) return col;
        var hits = doc.Columns.Select((n, i) => (n, i)).Where(x => x.n == expected).ToList();
        return hits.Count == 1 ? hits[0].i : -1;
    }
    public void Undo(NoteDocument doc) { var c = Find(doc, col, after); if (c >= 0) doc.Columns[c] = before; }
    public void Redo(NoteDocument doc) { var c = Find(doc, col, before); if (c >= 0) doc.Columns[c] = after; }
}

public sealed class RowDeleteAction(
    int index,
    List<string> row,
    string rowId,
    Dictionary<string, string> snoozes,
    string? recurrence,
    ReminderInfo? reminders) : IUndoAction
{
    public string Label => Localizer.T("delete.undo_row");

    /// <summary>Capture everything tied to a row before deleting it.</summary>
    public static RowDeleteAction Capture(NoteDocument doc, int index)
    {
        var id = doc.RowIdAt(index);
        var sn = doc.Snoozes.Where(k => k.Key == id || k.Key.StartsWith(id + "|", StringComparison.Ordinal))
            .ToDictionary(k => k.Key, k => k.Value);
        doc.Recurrence.TryGetValue(id, out var rec);
        doc.Reminders.TryGetValue(id, out var rem);
        if (doc.Acknowledged.TryGetValue(id, out var ack)) sn["\u0001ack"] = ack;
        return new RowDeleteAction(index, doc.Rows[index].ToList(), id, sn, rec, rem?.Clone());
    }

    public void Undo(NoteDocument doc)
    {
        // v2.3.1 (L10): ids parallel to rows BEFORE inserting, so the id lands next to its row.
        doc.EnsureRowIds();
        if (doc.RowIds.Contains(rowId)) return; // already back (e.g. restored by a snapshot step)
        var at = Math.Clamp(index, 0, doc.Rows.Count);
        doc.Rows.Insert(at, row.ToList());
        doc.RowIds.Insert(at, rowId);
        foreach (var kv in snoozes)
        {
            if (kv.Key == "\u0001ack") doc.Acknowledged[rowId] = kv.Value;
            else doc.Snoozes[kv.Key] = kv.Value;
        }
        if (recurrence != null) doc.Recurrence[rowId] = recurrence;
        if (reminders != null) doc.Reminders[rowId] = reminders.Clone();
    }

    public void Redo(NoteDocument doc)
    {
        var i = doc.RowIds.FindIndex(id => id == rowId);
        if (i >= 0) doc.RemoveRow(i);
    }
}

public sealed class SnapshotAction(string label, NoteDocument before, NoteDocument after) : IUndoAction
{
    // Safe for paste / recurrence spawn / structure changes. Stored gzip-compressed UTF-8
    // (1000 rows ≈ 300 KB JSON → tens of KB), so 100 steps stay far below UndoStack.MaxBytes.
    private readonly byte[] _before = Snapshot.Pack(Snapshot.Serialize(before));
    private readonly byte[] _after = Snapshot.Pack(Snapshot.Serialize(after));
    public string Label => label;
    public long Bytes => _before.Length + _after.Length + 64;
    public void Undo(NoteDocument doc) => Snapshot.Restore(doc, Snapshot.Unpack(_before));
    public void Redo(NoteDocument doc) => Snapshot.Restore(doc, Snapshot.Unpack(_after));
}

public static class Snapshot
{
    public static byte[] Pack(string json)
    {
        using var ms = new MemoryStream();
        using (var z = new System.IO.Compression.GZipStream(ms, System.IO.Compression.CompressionLevel.Fastest, leaveOpen: true))
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(json);
            z.Write(bytes, 0, bytes.Length);
        }
        return ms.ToArray();
    }

    public static string Unpack(byte[] packed)
    {
        using var z = new System.IO.Compression.GZipStream(new MemoryStream(packed), System.IO.Compression.CompressionMode.Decompress);
        using var r = new StreamReader(z, System.Text.Encoding.UTF8);
        return r.ReadToEnd();
    }

    private static readonly System.Text.Json.JsonSerializerOptions Opt = new()
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    // Content only. Snoozes, acknowledgements, hide-done and mute are NOT part of undo (spec U-list).
    private sealed class Dto
    {
        public List<string> Columns { get; set; } = [];
        public List<string> ColumnRoles { get; set; } = [];
        public List<string> ColumnIds { get; set; } = [];
        public Dictionary<string, ColumnSetting> ColumnSettings { get; set; } = new(); // v2.4.0
        public Dictionary<string, RowStyle> RowStyles { get; set; } = new();
        public List<List<string>> Rows { get; set; } = [];
        public List<string> RowIds { get; set; } = [];
        public List<int> ColumnWidths { get; set; } = [];
        public Dictionary<string, string> Recurrence { get; set; } = new();
        public Dictionary<string, List<int>> ReminderOffsets { get; set; } = new();
    }

    public static string Serialize(NoteDocument doc)
    {
        doc.EnsureRowIds(); // v2.3.1 (L4): RowIds parallel to Rows, or Restore could look up a missing row
        doc.EnsureColumnIds();
        return SerializeCore(doc);
    }

    private static string SerializeCore(NoteDocument doc) =>
        System.Text.Json.JsonSerializer.Serialize(new Dto
        {
            Columns = doc.Columns.ToList(),
            ColumnRoles = doc.ColumnRolesList.ToList(),
            ColumnIds = doc.ColumnIds.ToList(),
            ColumnSettings = ColumnSettingsOps.Clone(doc.ColumnSettings), // v2.4.0
            RowStyles = RowStyleOps.Clone(doc.RowStyles),
            Rows = doc.Rows.Select(r => r.ToList()).ToList(),
            RowIds = doc.RowIds.ToList(),
            ColumnWidths = doc.ColumnWidths.ToList(),
            Recurrence = new Dictionary<string, string>(doc.Recurrence),
            ReminderOffsets = doc.Reminders.Where(k => k.Value.Offsets.Count > 0).ToDictionary(k => k.Key, k => k.Value.Offsets.ToList())
        }, Opt);

    /// <summary>
    /// Restore content by row id. Rows that exist both now and in the snapshot keep their CURRENT
    /// order (so a later sort isn't reverted); rows only in the snapshot are re-inserted next to their
    /// snapshot neighbour; rows only in the current doc are removed.
    /// </summary>
    public static void Restore(NoteDocument doc, string json)
    {
        var d = System.Text.Json.JsonSerializer.Deserialize<Dto>(json, Opt) ?? new Dto();
        var snapRows = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        for (var i = 0; i < d.Rows.Count && i < d.RowIds.Count; i++) snapRows[d.RowIds[i]] = d.Rows[i];
        var snapSet = new HashSet<string>(snapRows.Keys, StringComparer.Ordinal);
        var order = doc.RowIds.Where(snapSet.Contains).Distinct().ToList();
        var placed = new HashSet<string>(order, StringComparer.Ordinal);
        for (var i = 0; i < d.RowIds.Count; i++)
        {
            var id = d.RowIds[i];
            if (placed.Contains(id) || !snapRows.ContainsKey(id)) continue; // v2.3.1 (L4): id without a row → skip (was KeyNotFound)
            var at = 0;
            for (var k = i - 1; k >= 0; k--)
            {
                var p = order.IndexOf(d.RowIds[k]);
                if (p >= 0) { at = p + 1; break; }
            }
            order.Insert(at, id);
            placed.Add(id);
        }
        doc.Columns = d.Columns;
        doc.ColumnRolesList = d.ColumnRoles;
        doc.ColumnIds = d.ColumnIds;
        doc.ColumnWidths = d.ColumnWidths;
        doc.ColumnSettings = ColumnSettingsOps.Clone(d.ColumnSettings); // v2.4.0
        doc.RowStyles = RowStyleOps.Clone(d.RowStyles);
        doc.RowIds = order;
        doc.Rows = order.Select(id => snapRows[id].ToList()).ToList();
        doc.Recurrence = new Dictionary<string, string>(d.Recurrence, StringComparer.Ordinal);
        // Reminder offsets are content; acknowledged fires stay as they are now.
        foreach (var id in doc.Reminders.Keys.ToList())
            if (!d.ReminderOffsets.ContainsKey(id)) { doc.Reminders[id].Offsets = []; ReminderPlan.Prune(doc, id); }
        foreach (var (id, offs) in d.ReminderOffsets)
            ReminderPlan.Info(doc, id).Offsets = offs.ToList();
        // Drop state of rows that no longer exist
        var ids = new HashSet<string>(order, StringComparer.Ordinal);
        foreach (var k in doc.Reminders.Keys.Where(k => !ids.Contains(k)).ToList()) doc.Reminders.Remove(k);
        foreach (var k in doc.Snoozes.Keys.Where(k => !ids.Contains(k.Split('|')[0])).ToList()) doc.Snoozes.Remove(k);
        doc.EnsureColumnRoles();
        doc.EnsureRowIds();
        doc.EnsureColumnIds();
    }

    public static NoteDocument CloneLite(NoteDocument doc)
    {
        var n = NoteDocument.CreateDefault();
        n.RowIds = doc.RowIds.ToList();
        Restore(n, Serialize(doc));
        n.Window = doc.Window;
        n.LaunchAtLogin = doc.LaunchAtLogin;
        n.TrayNoticeShown = doc.TrayNoticeShown;
        n.ExtraFields = doc.ExtraFields;
        n.View = doc.View;
        n.Version = doc.Version;
        return n;
    }
}
