using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TableNote;

/// <summary>
/// One paintable part of the UI. Empty background or text means "keep the built-in color".
/// Stored in note.json under "partColors" (optional; missing means every part uses its built-in color).
/// The file format version stays 5:
/// <list type="bullet">
/// <item><c>cell:{rowId}:{columnId}</c> — one table cell (ids survive moves and renames)</item>
/// <item><c>header:{columnId}</c> — one column header; <c>section:header</c> is the fallback for every header</item>
/// <item><c>rownum:{rowId}</c> — the row-number slot</item>
/// <item><c>alert:{id}</c> — one alert box. This map is the only copy that is loaded and saved.
/// Mirroring onto the box (<c>color</c> / <c>textColor</c>) is optional and not required for the color to stick.</item>
/// <item><c>notify:{id}</c> — one reminder-popup card (the task row id)</item>
/// <item><c>section:window</c>, <c>section:toolbar</c>, <c>section:status</c>, <c>section:notice</c>,
/// <c>section:table</c>, <c>section:popup</c>, <c>section:popup-card</c> — the shared chrome</item>
/// </list>
/// </summary>
public sealed class PartColor
{
    public string? Background { get; set; }
    public string? Foreground { get; set; }
}

public enum PartTarget
{
    Cell, RowNum, Header, Headers, Window, Toolbar, Status, Notice, Table, Popup, PopupCard, Notify
}

public static class PartColorKeys
{
    public const string Window = "section:window";
    public const string Toolbar = "section:toolbar";
    public const string Status = "section:status";
    public const string Notice = "section:notice";
    public const string Header = "section:header";
    public const string Table = "section:table";
    public const string Popup = "section:popup";
    public const string PopupCard = "section:popup-card";

    public static string Cell(string rowId, string columnId) => "cell:" + rowId + ":" + columnId;
    public static string HeaderColumn(string columnId) => "header:" + columnId;
    public static string RowNum(string rowId) => "rownum:" + rowId;

    /// <summary>Reminder-popup card for one task. Stored only in partColors.</summary>
    public static string Notify(string id) => "notify:" + id;

    /// <summary>One user alert box, keyed by the box's stable id. Stored only in partColors
    /// (<c>alert:{id}</c>), so it does not matter whether alertBoxes is an extra field or a known list.</summary>
    public static string Alert(string id) => "alert:" + id;

    public static string? Fixed(PartTarget target) => target switch
    {
        PartTarget.Headers => Header,
        PartTarget.Window => Window,
        PartTarget.Toolbar => Toolbar,
        PartTarget.Status => Status,
        PartTarget.Notice => Notice,
        PartTarget.Table => Table,
        PartTarget.Popup => Popup,
        PartTarget.PopupCard => PopupCard,
        _ => null
    };

    public static bool IsSafeKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 160) return false;
        foreach (var c in key)
            if (!(char.IsAsciiLetterOrDigit(c) || c is ':' or '-' or '_' or '.')) return false;
        return true;
    }
}

/// <summary>In-memory map the open windows read. The main window points <see cref="Lookup"/> at settings.json.</summary>
public static class PartColorStore
{
    public static Func<string, PartColor?> Lookup { get; set; } = _ => null;

    /// <summary>Raised after a color is saved. Listeners (open popups, future notification boxes) should repaint.</summary>
    public static event Action? Changed;

    public static PartColor? Get(string? key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        try { return Lookup(key); }
        catch { return null; }
    }

    public static void RaiseChanged()
    {
        var handler = Changed;
        if (handler == null) return;
        try { handler(); }
        catch { /* a listener must not break the save */ }
    }
}

public static class PartColorOps
{
    /// <summary>#RGB, #RRGGBB, #RRGGBBAA (alpha dropped), rgb(r,g,b), or "r, g, b". Null if it is not a color.</summary>
    public static string? NormalizeHex(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var s = input.Trim();
        if (s.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
        {
            var open = s.IndexOf('(');
            var close = s.LastIndexOf(')');
            if (open < 0 || close <= open) return null;
            s = s[(open + 1)..close];
        }
        var parts = s.Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 3 &&
            byte.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var r) &&
            byte.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var g) &&
            byte.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var b))
            return FromRgb(r, g, b);

        if (s.Length > 0 && s[0] == '#') s = s[1..];
        if (s.Length == 8 && s.All(Uri.IsHexDigit)) s = s[..6];
        if (s.Length == 3 && s.All(Uri.IsHexDigit))
            s = string.Concat(s[0], s[0], s[1], s[1], s[2], s[2]);
        if (s.Length == 6 && s.All(Uri.IsHexDigit)) return "#" + s.ToUpperInvariant();
        return null;
    }

    public static string FromRgb(byte r, byte g, byte b) => $"#{r:X2}{g:X2}{b:X2}";

    public static bool TryRgb(string? input, out byte r, out byte g, out byte b)
    {
        r = g = b = 0;
        var hex = NormalizeHex(input);
        if (hex == null) return false;
        r = Convert.ToByte(hex.Substring(1, 2), 16);
        g = Convert.ToByte(hex.Substring(3, 2), 16);
        b = Convert.ToByte(hex.Substring(5, 2), 16);
        return true;
    }

    /// <summary>Id typed for a notification box: letters, digits, and - _ . Empty or unsafe → null.</summary>
    public static string? CleanId(string? id)
    {
        var s = (id ?? "").Trim();
        if (s.Length is 0 or > 80) return null;
        foreach (var c in s)
            if (!(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.')) return null;
        return s;
    }

    /// <summary>Specific channels win; the other channels fall through to <paramref name="fallback"/>.</summary>
    public static PartColor? Merge(PartColor? specific, PartColor? fallback)
    {
        if (specific == null) return fallback;
        if (fallback == null) return specific;
        var bg = specific.Background ?? fallback.Background;
        var fg = specific.Foreground ?? fallback.Foreground;
        if (bg == null && fg == null) return null;
        return new PartColor { Background = bg, Foreground = fg };
    }

    /// <summary>Set or clear one part. Both channels empty removes the key (back to the default). Returns true when the map changed.</summary>
    public static bool Assign(Dictionary<string, PartColor> map, string key, string? background, string? foreground)
    {
        if (!PartColorKeys.IsSafeKey(key)) return false;
        var bg = NormalizeHex(background);
        var fg = NormalizeHex(foreground);
        if (bg == null && fg == null) return map.Remove(key);
        if (map.TryGetValue(key, out var old) &&
            string.Equals(old.Background, bg, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(old.Foreground, fg, StringComparison.OrdinalIgnoreCase))
            return false;
        map[key] = new PartColor { Background = bg, Foreground = fg };
        return true;
    }

    public static Dictionary<string, PartColor> ParseMap(JsonElement element)
    {
        var map = new Dictionary<string, PartColor>(StringComparer.Ordinal);
        if (element.ValueKind != JsonValueKind.Object) return map;
        foreach (var prop in element.EnumerateObject())
        {
            if (!PartColorKeys.IsSafeKey(prop.Name)) continue;
            var color = ParseOne(prop.Value);
            if (color == null) continue;
            map[prop.Name] = color;
        }
        return map;
    }

    private static PartColor? ParseOne(JsonElement value)
    {
        string? bg = null, fg = null;
        if (value.ValueKind == JsonValueKind.String)
            bg = NormalizeHex(value.GetString());
        else if (value.ValueKind == JsonValueKind.Object)
        {
            if (value.TryGetProperty("background", out var b) && b.ValueKind == JsonValueKind.String)
                bg = NormalizeHex(b.GetString());
            if (value.TryGetProperty("foreground", out var f) && f.ValueKind == JsonValueKind.String)
                fg = NormalizeHex(f.GetString());
        }
        if (bg == null && fg == null) return null;
        return new PartColor { Background = bg, Foreground = fg };
    }

    /// <summary>Null when there is nothing to write (note.json then omits partColors).</summary>
    public static JsonObject? ToJson(Dictionary<string, PartColor>? map)
    {
        if (map == null || map.Count == 0) return null;
        var obj = new JsonObject();
        foreach (var kv in map.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            if (!PartColorKeys.IsSafeKey(kv.Key) || kv.Value == null) continue;
            var bg = NormalizeHex(kv.Value.Background);
            var fg = NormalizeHex(kv.Value.Foreground);
            if (bg == null && fg == null) continue;
            var entry = new JsonObject();
            if (bg != null) entry["background"] = bg;
            if (fg != null) entry["foreground"] = fg;
            obj[kv.Key] = entry;
        }
        return obj.Count == 0 ? null : obj;
    }

    public static Dictionary<string, PartColor> Clone(Dictionary<string, PartColor>? map) =>
        (map ?? new()).Where(kv => kv.Value != null && PartColorKeys.IsSafeKey(kv.Key))
            .ToDictionary(kv => kv.Key, kv => new PartColor
            {
                Background = NormalizeHex(kv.Value.Background),
                Foreground = NormalizeHex(kv.Value.Foreground)
            }, StringComparer.Ordinal);
}
