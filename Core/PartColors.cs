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
/// <item><c>alert:{id}</c> — one alert box, also stored on that box as color / textColor</item>
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

    /// <summary>One user alert box (note.json alertBoxes, the id on the box). Also written as the box's
    /// <c>color</c> / <c>textColor</c> properties.</summary>
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

/// <summary>
/// Alert boxes live in note.json "alertBoxes" (format version 6). This build does not own that list:
/// it is kept as an unknown field and written back. Per-box colours are the <c>color</c> and
/// <c>textColor</c> properties on each box (a missing or unreadable value is skipped) and are mirrored
/// in <see cref="NoteDocument.PartColors"/> under <c>alert:{id}</c>.
/// </summary>
public static class AlertColorSync
{
    public const string BackgroundName = "color";
    public const string ForegroundName = "textColor";

    public static void Import(NoteDocument doc, Action<string>? issue = null)
    {
        if (!TryGetBoxes(doc, out _, out var raw) || raw.ValueKind != JsonValueKind.Array) return;
        foreach (var item in raw.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var id = ReadId(item);
            if (id == null) continue;
            var bg = ReadChannel(item, BackgroundName, id, issue);
            var fg = ReadChannel(item, ForegroundName, id, issue);
            if (bg.Present == false && fg.Present == false) continue;
            var key = PartColorKeys.Alert(id);
            doc.PartColors.TryGetValue(key, out var existing);
            var merged = existing ?? new PartColor();
            if (bg.Present && bg.Hex != null) merged.Background = bg.Hex;
            if (fg.Present && fg.Hex != null) merged.Foreground = fg.Hex;
            if (merged.Background == null && merged.Foreground == null) doc.PartColors.Remove(key);
            else doc.PartColors[key] = merged;
        }
    }

    /// <summary>Write partColors back onto each alert box. Other properties (id, title, text, anything else) stay.</summary>
    public static void Export(NoteDocument doc)
    {
        if (!TryGetBoxes(doc, out var key, out var raw) || key == null || raw.ValueKind != JsonValueKind.Array) return;
        var arr = new JsonArray();
        foreach (var item in raw.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                arr.Add(JsonNode.Parse(item.GetRawText()));
                continue;
            }
            var o = (JsonObject)JsonNode.Parse(item.GetRawText())!;
            var id = ReadId(item);
            if (id != null)
            {
                doc.PartColors.TryGetValue(PartColorKeys.Alert(id), out var color);
                SetChannel(o, BackgroundName, PartColorOps.NormalizeHex(color?.Background));
                SetChannel(o, ForegroundName, PartColorOps.NormalizeHex(color?.Foreground));
            }
            arr.Add(o);
        }
        using var parsed = JsonDocument.Parse(arr.ToJsonString(new JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        }));
        doc.ExtraFields[key] = parsed.RootElement.Clone();
    }

    private readonly struct Channel
    {
        public bool Present { get; init; }
        public string? Hex { get; init; }
    }

    private static Channel ReadChannel(JsonElement box, string name, string id, Action<string>? issue)
    {
        foreach (var p in box.EnumerateObject())
        {
            if (!string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
            if (p.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                return new Channel { Present = true, Hex = null };
            if (p.Value.ValueKind != JsonValueKind.String)
            {
                issue?.Invoke($"alertBoxes.{id}.{name}: not a string, ignored");
                return new Channel { Present = true, Hex = null };
            }
            var hex = PartColorOps.NormalizeHex(p.Value.GetString());
            if (hex == null && !string.IsNullOrWhiteSpace(p.Value.GetString()))
                issue?.Invoke($"alertBoxes.{id}.{name}: \"{p.Value.GetString()}\" is not a color, ignored");
            return new Channel { Present = true, Hex = hex };
        }
        return new Channel { Present = false };
    }

    private static void SetChannel(JsonObject box, string name, string? hex)
    {
        foreach (var k in box.Select(p => p.Key).ToList())
            if (string.Equals(k, name, StringComparison.OrdinalIgnoreCase) && k != name)
                box.Remove(k);
        if (hex == null) box.Remove(name);
        else box[name] = hex;
    }

    private static string? ReadId(JsonElement box)
    {
        foreach (var p in box.EnumerateObject())
        {
            if (!string.Equals(p.Name, "id", StringComparison.OrdinalIgnoreCase)) continue;
            if (p.Value.ValueKind != JsonValueKind.String) return null;
            var id = (p.Value.GetString() ?? "").Trim();
            return id.Length == 0 || id.Length > 80 ? null : id;
        }
        return null;
    }

    private static bool TryGetBoxes(NoteDocument doc, out string? key, out JsonElement raw)
    {
        foreach (var kv in doc.ExtraFields)
        {
            if (!string.Equals(kv.Key, "alertBoxes", StringComparison.OrdinalIgnoreCase)) continue;
            key = kv.Key;
            raw = kv.Value;
            return true;
        }
        key = null;
        raw = default;
        return false;
    }
}
