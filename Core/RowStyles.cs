namespace TableNote;

/// <summary>v2.4.0: per-row look stored in note.json "rowStyles", keyed by row id (so row moves / sorts keep it).
/// Color = cell background of the row (palette key, or "#RRGGBB"); Highlight = marked row (bold number + orange bar).</summary>
public sealed class RowStyle
{
    public string? Color { get; set; }
    public bool Highlight { get; set; }
}

public static class RowStyleOps
{
    /// <summary>Palette (language-neutral key, Thai label, colour). Pastel so the yellow note stays readable.</summary>
    public static readonly (string Key, string Label, string Hex)[] Palette =
    [
        ("yellow", "เหลือง", "#FFF1A8"),
        ("orange", "ส้ม", "#FFDDB8"),
        ("red", "แดง", "#F9C9C2"),
        ("pink", "ชมพู", "#F8D5E3"),
        ("purple", "ม่วง", "#E3D9F4"),
        ("blue", "ฟ้า", "#D2E6F7"),
        ("green", "เขียว", "#D8F0C6"),
        ("gray", "เทา", "#E4E1DA"),
    ];

    /// <summary>"#RRGGBB" for a palette key or a hex value; null for none / unknown (never throws).</summary>
    public static string? HexFor(string? color)
    {
        var c = (color ?? "").Trim();
        if (c.Length == 0) return null;
        foreach (var p in Palette) if (string.Equals(p.Key, c, StringComparison.OrdinalIgnoreCase)) return p.Hex;
        if (c.Length == 7 && c[0] == '#' && c.Skip(1).All(Uri.IsHexDigit)) return c.ToUpperInvariant();
        return null;
    }

    public static RowStyle Get(NoteDocument doc, string rowId) =>
        doc.RowStyles.TryGetValue(rowId, out var s) && s != null
            ? new RowStyle { Color = HexFor(s.Color) == null ? null : s.Color, Highlight = s.Highlight }
            : new RowStyle();

    /// <summary>Set (or clear with null) the row colour. Returns true when it changed.</summary>
    public static bool SetColor(NoteDocument doc, string rowId, string? color)
    {
        var s = Get(doc, rowId);
        var c = HexFor(color) == null ? null : color!.Trim();
        if (string.Equals(s.Color, c, StringComparison.OrdinalIgnoreCase)) return false;
        s.Color = c;
        Store(doc, rowId, s);
        return true;
    }

    public static bool SetHighlight(NoteDocument doc, string rowId, bool on)
    {
        var s = Get(doc, rowId);
        if (s.Highlight == on) return false;
        s.Highlight = on;
        Store(doc, rowId, s);
        return true;
    }

    public static bool Clear(NoteDocument doc, string rowId) => doc.RowStyles.Remove(rowId);

    private static void Store(NoteDocument doc, string rowId, RowStyle s)
    {
        if (s.Color == null && !s.Highlight) doc.RowStyles.Remove(rowId); // default → no entry
        else doc.RowStyles[rowId] = s;
    }

    public static Dictionary<string, RowStyle> Clone(Dictionary<string, RowStyle>? map) =>
        (map ?? new()).Where(kv => kv.Value != null && (HexFor(kv.Value.Color) != null || kv.Value.Highlight))
            .ToDictionary(kv => kv.Key, kv => new RowStyle { Color = HexFor(kv.Value.Color) == null ? null : kv.Value.Color, Highlight = kv.Value.Highlight },
                StringComparer.Ordinal);
}
