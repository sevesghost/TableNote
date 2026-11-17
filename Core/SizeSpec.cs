using System.Globalization;
using System.Reflection;

namespace TableNote;

/// <summary>
/// v2.6: typed column width and row height. Width is the stored pixel width at 100% zoom
/// (<see cref="TableLimits.MinColumnWidth"/>–<see cref="TableLimits.MaxColumnWidth"/>, default
/// <see cref="TableLimits.DefaultColumnWidth"/>). Row height is auto unless <see cref="NoteDocument.RowHeights"/>
/// has an entry (32–400). Invalid text is refused — nothing is clamped to the nearest limit.
/// </summary>
public static class SizeSpec
{
    public const int MinRowHeight = 32;
    public const int MaxRowHeight = 400;

    /// <summary>
    /// Stand-in for feature 2.1 <c>ColumnGroupOps.IsHidden(NoteDocument, int)</c>.
    /// Production leaves this null and calls that method by reflection when the type exists.
    /// </summary>
    public static Func<NoteDocument, int, bool>? IsHiddenOverride { get; set; }

    /// <summary>True when feature 2.1 has collapsed this column (on-screen width 0). False when 2.1 is not loaded.</summary>
    public static bool IsHiddenColumn(NoteDocument doc, int column)
    {
        if (IsHiddenOverride != null) return IsHiddenOverride(doc, column);
        var type = typeof(SizeSpec).Assembly.GetType("TableNote.ColumnGroupOps");
        var method = type?.GetMethod("IsHidden", BindingFlags.Public | BindingFlags.Static, null, [typeof(NoteDocument), typeof(int)], null);
        if (method == null || method.ReturnType != typeof(bool)) return false;
        try { return method.Invoke(null, [doc, column]) is true; }
        catch (TargetInvocationException) { return false; }
    }

    /// <summary>Stored width. Never the collapsed on-screen 0, and never a value outside 72–800.</summary>
    public static int StoredColumnWidth(NoteDocument doc, int column)
    {
        var stored = doc.ColumnWidth(column);
        if (stored < TableLimits.MinColumnWidth || stored > TableLimits.MaxColumnWidth)
            return TableLimits.DefaultColumnWidth;
        return stored;
    }

    /// <summary>Stored height, or null when the row is auto (no entry, or the entry is not 32–400).</summary>
    public static int? StoredRowHeight(NoteDocument doc, int row)
    {
        if (row < 0 || row >= doc.Rows.Count || doc.RowHeights == null) return null;
        return doc.RowHeights.TryGetValue(doc.RowIdAt(row), out var h) && h >= MinRowHeight && h <= MaxRowHeight ? h : null;
    }

    public readonly record struct Prefill(bool Mixed, int? Value);

    /// <summary>One shared value, or <see cref="Prefill.Mixed"/> when the selection disagrees (the box stays blank).</summary>
    public static Prefill Describe(IReadOnlyList<int?> values)
    {
        if (values.Count == 0) return new Prefill(false, null);
        var first = values[0];
        for (var i = 1; i < values.Count; i++)
            if (values[i] != first) return new Prefill(true, null);
        return new Prefill(false, first);
    }

    /// <summary>Indexes the dialog edits. A click outside the shift-selection edits only that index.</summary>
    public static List<int> TargetIndexes(int count, int clicked, int anchor, int active, bool hasSelection)
    {
        if (clicked < 0 || clicked >= count) return [];
        if (!hasSelection) return [clicked];
        var a = Math.Clamp(Math.Min(anchor, active), 0, count - 1);
        var b = Math.Clamp(Math.Max(anchor, active), 0, count - 1);
        if (clicked < a || clicked > b || a == b) return [clicked];
        return Enumerable.Range(a, b - a + 1).ToList();
    }

    /// <summary>Columns to edit. A hidden (collapsed) column is never included, so its stored width is left alone.</summary>
    public static List<int> TargetColumns(NoteDocument doc, int clicked, int anchor, int active, bool hasSelection)
    {
        if (clicked < 0 || clicked >= doc.Columns.Count || IsHiddenColumn(doc, clicked)) return [];
        return TargetIndexes(doc.Columns.Count, clicked, anchor, active, hasSelection)
            .Where(c => !IsHiddenColumn(doc, c)).ToList();
    }

    public static List<int> TargetRows(NoteDocument doc, int clicked, int anchor, int active, bool hasSelection) =>
        TargetIndexes(doc.Rows.Count, clicked, anchor, active, hasSelection);

    /// <summary>Whole number in range after trim. Decimals, letters, empty text and digits outside ASCII are rejected.</summary>
    public static bool IsAccepted(string? text, int min, int max, out int value)
    {
        value = 0;
        var raw = (text ?? "").Trim();
        if (raw.Length == 0) return false;
        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)) return false;
        if (value < min || value > max) return false;
        return true;
    }

    /// <summary>OK is enabled for a number in range, or for Auto (row height only).</summary>
    public static bool CanApply(string? text, int min, int max, bool auto = false) =>
        auto || IsAccepted(text, min, max, out _);

    /// <summary>Localized reason OK is disabled, or null when <paramref name="text"/> is a number in range.</summary>
    public static string? ValidationMessage(string? text, int min, int max)
    {
        var raw = (text ?? "").Trim();
        if (raw.Length == 0) return Localizer.T("size.empty");
        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
            return Localizer.T("size.not_number");
        if (n < min || n > max) return Localizer.T("size.out_of_range", min, max);
        return null;
    }

    /// <summary>Write <paramref name="width"/> onto visible columns only. Out of range or a hidden column: no change, no clamp.</summary>
    public static bool ApplyColumnWidths(NoteDocument doc, IReadOnlyList<int> columns, int width)
    {
        if (width < TableLimits.MinColumnWidth || width > TableLimits.MaxColumnWidth) return false;
        var changed = false;
        foreach (var c in columns.Distinct())
        {
            if (c < 0 || c >= doc.Columns.Count || IsHiddenColumn(doc, c)) continue;
            if (doc.ColumnWidth(c) == width) continue;
            doc.SetColumnWidth(c, width);
            if (doc.ColumnWidth(c) == width) changed = true;
        }
        return changed;
    }

    public static bool ResetColumnWidths(NoteDocument doc, IReadOnlyList<int> columns) =>
        ApplyColumnWidths(doc, columns, TableLimits.DefaultColumnWidth);

    /// <summary>Set a pixel height, or null for auto (the entry is removed). Out of range: no change, no clamp.</summary>
    public static bool ApplyRowHeights(NoteDocument doc, IReadOnlyList<int> rows, int? height)
    {
        if (height is int v && (v < MinRowHeight || v > MaxRowHeight)) return false;
        doc.RowHeights ??= new Dictionary<string, int>(StringComparer.Ordinal);
        var changed = false;
        foreach (var r in rows.Distinct())
        {
            if (r < 0 || r >= doc.Rows.Count) continue;
            var id = doc.RowIdAt(r);
            if (height is int h)
            {
                if (doc.RowHeights.TryGetValue(id, out var cur) && cur == h) continue;
                doc.RowHeights[id] = h;
                changed = true;
            }
            else if (doc.RowHeights.Remove(id)) changed = true;
        }
        return changed;
    }

    /// <summary>Forget heights whose row is gone, and heights outside 32–400. Does not clamp.</summary>
    public static void PruneRowHeights(NoteDocument doc)
    {
        if (doc.RowHeights == null || doc.RowHeights.Count == 0) return;
        var ids = new HashSet<string>(doc.RowIds, StringComparer.Ordinal);
        foreach (var k in doc.RowHeights.Keys.ToList())
            if (!ids.Contains(k) || doc.RowHeights[k] < MinRowHeight || doc.RowHeights[k] > MaxRowHeight)
                doc.RowHeights.Remove(k);
    }
}
