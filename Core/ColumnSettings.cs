using System.Globalization;

namespace TableNote;

/// <summary>v2.4.0: per-column settings stored in note.json "columnSettings", keyed by the stable column id
/// (<see cref="NoteDocument.ColumnIdAt"/>). Name stays in Columns, width in ColumnWidths. A column without an
/// entry is a plain text column, so old files need no migration. Keyed by id → column moves don't touch it.</summary>
public sealed class ColumnSetting
{
    /// <summary>One of <see cref="ColumnTypes"/>; unknown values read as text.</summary>
    public string Type { get; set; } = ColumnTypes.Text;
    /// <summary>Choices of a "choice" column (drop-down), in display order.</summary>
    public List<string> Options { get; set; } = [];
}

public static class ColumnTypes
{
    public const string Text = "text";
    public const string Number = "number";
    public const string Choice = "choice";
    public const string Check = "check";

    public static readonly string[] All = [Text, Number, Choice, Check];

    public static string Normalize(string? type)
    {
        var t = (type ?? "").Trim().ToLowerInvariant();
        return All.Contains(t) ? t : Text;
    }

    /// <summary>Shown name in the current UI language (v2.4.1: was Thai only).</summary>
    public static string Label(string type) => Localizer.T("coltype." + Normalize(type));
}

public static class ColumnSettingsOps
{
    public const int MaxOptions = 50;
    public const int MaxOptionChars = 100;
    /// <summary>Value written by a ticked check cell (readable in exports / Excel copy).</summary>
    public const string CheckedValue = "✓";

    private static readonly HashSet<string> TrueWords = new(StringComparer.OrdinalIgnoreCase)
        { "✓", "✔", "☑", "x", "true", "yes", "y", "1", "ใช่", "เสร็จ" };

    public static bool IsChecked(string? value) => TrueWords.Contains((value ?? "").Trim());

    public static bool IsNumber(string? value)
    {
        var raw = (value ?? "").Trim();
        if (raw.Length == 0) return true;
        // v2.4.1: "NaN", "Infinity", "1e400" (→ ∞) and "," alone were accepted as numbers.
        var v = raw.Replace(",", "");
        return v.Any(char.IsAsciiDigit) &&
               double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && double.IsFinite(d);
    }

    /// <summary>Status / due columns have their own editors; their type cannot be changed.</summary>
    public static bool IsSystemColumn(NoteDocument doc, int column) =>
        column == doc.StatusColumnIndex() || column == doc.DueColumnIndex();

    /// <summary>Settings of a column (a fresh default when none is stored). Never null; orphan entries are ignored.</summary>
    public static ColumnSetting Get(NoteDocument doc, int column)
    {
        if (column < 0 || column >= doc.Columns.Count) return new ColumnSetting();
        if (IsSystemColumn(doc, column)) return new ColumnSetting();
        return doc.ColumnSettings.TryGetValue(doc.ColumnIdAt(column), out var s) && s != null
            ? new ColumnSetting { Type = ColumnTypes.Normalize(s.Type), Options = CleanOptions(s.Options) }
            : new ColumnSetting();
    }

    public static string TypeOf(NoteDocument doc, int column) => Get(doc, column).Type;

    public static List<string> CleanOptions(IEnumerable<string?>? options)
    {
        var list = new List<string>();
        if (options == null) return list;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var o in options)
        {
            var t = (o ?? "").Trim();
            if (t.Length == 0) continue;
            if (t.Length > MaxOptionChars) t = TableLimits.Clip(t, MaxOptionChars);
            if (seen.Add(t)) list.Add(t);
            if (list.Count >= MaxOptions) break;
        }
        return list;
    }

    /// <summary>Apply the settings dialog result to one column. Returns true when anything changed.
    /// Cell data is never rewritten by a type change.</summary>
    public static bool Apply(NoteDocument doc, int column, string? name, string? type, int? width, IEnumerable<string?>? options)
    {
        if (column < 0 || column >= doc.Columns.Count) return false;
        var changed = false;
        if (name != null)
        {
            var n = TableLimits.Clip(name, TableLimits.MaxCellChars);
            if (doc.Columns[column] != n) { doc.Columns[column] = n; changed = true; }
        }
        if (width is { } w && doc.ColumnWidth(column) != Math.Clamp(w, TableLimits.MinColumnWidth, TableLimits.MaxColumnWidth))
        {
            doc.SetColumnWidth(column, w);
            changed = true;
        }
        if (IsSystemColumn(doc, column)) return changed; // status/due: only name + width
        var id = doc.ColumnIdAt(column);
        var t = ColumnTypes.Normalize(type);
        var opts = t == ColumnTypes.Choice ? CleanOptions(options) : [];
        var before = Get(doc, column);
        if (before.Type == t && before.Options.SequenceEqual(opts)) return changed;
        if (t == ColumnTypes.Text) doc.ColumnSettings.Remove(id); // default → no entry (keeps note.json small)
        else doc.ColumnSettings[id] = new ColumnSetting { Type = t, Options = opts };
        return true;
    }

    /// <summary>Call after a column was removed (or let <see cref="PruneOrphans"/> clean up).</summary>
    public static void OnColumnRemoved(this NoteDocument doc, string columnId) => doc.ColumnSettings.Remove(columnId);

    /// <summary>Drop settings / styles whose column or row no longer exists. Returns the number removed.</summary>
    public static int PruneOrphans(NoteDocument doc)
    {
        doc.EnsureColumnIds();
        doc.EnsureRowIds();
        var cols = new HashSet<string>(doc.ColumnIds, StringComparer.Ordinal);
        var rows = new HashSet<string>(doc.RowIds, StringComparer.Ordinal);
        var n = 0;
        foreach (var k in doc.ColumnSettings.Keys.Where(k => !cols.Contains(k)).ToList()) { doc.ColumnSettings.Remove(k); n++; }
        foreach (var k in doc.RowStyles.Keys.Where(k => !rows.Contains(k)).ToList()) { doc.RowStyles.Remove(k); n++; }
        return n;
    }

    /// <summary>Deep copy (undo snapshots must not share instances with the live document).</summary>
    public static Dictionary<string, ColumnSetting> Clone(Dictionary<string, ColumnSetting>? map) =>
        (map ?? new()).Where(kv => kv.Value != null)
            .ToDictionary(kv => kv.Key, kv => new ColumnSetting { Type = ColumnTypes.Normalize(kv.Value.Type), Options = CleanOptions(kv.Value.Options) },
                StringComparer.Ordinal);
}
