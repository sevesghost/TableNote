using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TableNote;

/// <summary>
/// One user-managed notice shown above the table (feature 2.4). <see cref="Id"/> stays the same when the
/// title, text, or order changes, and across restarts, so another feature can attach data (for example a
/// colour) to this box later. This feature does not read or write a colour. Unknown JSON properties are
/// kept in <see cref="Extra"/> and written back unchanged.
/// </summary>
public sealed class AlertBox
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";
    /// <summary>JSON properties this version does not use (for example a future "color"), keyed by the name in the file.</summary>
    public Dictionary<string, JsonElement> Extra { get; } = new(StringComparer.Ordinal);
}

/// <summary>Add, delete, reorder, and lenient load/save of <see cref="AlertBox"/> lists. Order is list order.</summary>
public static class AlertBoxOps
{
    public const int MaxBoxes = 30;
    public const int MaxTitleChars = 200;
    public const int MaxTextChars = TableLimits.MaxCellChars;
    public const int MaxIdChars = 80;

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false
    };

    public static AlertBox Create(string? title = null, string? text = null) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Title = TableLimits.Clip(title ?? "", MaxTitleChars),
        Text = TableLimits.Clip(text ?? "", MaxTextChars)
    };

    public static int IndexOf(IReadOnlyList<AlertBox>? boxes, string? id)
    {
        if (boxes == null || string.IsNullOrEmpty(id)) return -1;
        for (var i = 0; i < boxes.Count; i++)
            if (boxes[i] != null && string.Equals(boxes[i].Id, id, StringComparison.Ordinal)) return i;
        return -1;
    }

    /// <summary>Move the box at <paramref name="from"/> so it ends up at index <paramref name="to"/>.</summary>
    public static bool Move(IList<AlertBox> boxes, int from, int to)
    {
        if (boxes == null) return false;
        var n = boxes.Count;
        if (from < 0 || from >= n || n == 0) return false;
        to = Math.Clamp(to, 0, n - 1);
        if (to == from) return false;
        var item = boxes[from];
        boxes.RemoveAt(from);
        boxes.Insert(to, item);
        return true;
    }

    /// <summary>
    /// Drag-and-drop: <paramref name="slot"/> is the gap (0 = before the first box, <paramref name="count"/> = after the last).
    /// Returns the final index for the box currently at <paramref name="from"/>, or -1 when the drop is a no-op.
    /// </summary>
    public static int TargetForSlot(int from, int slot, int count)
    {
        if (from < 0 || from >= count || slot < 0 || slot > count) return -1;
        if (slot == from || slot == from + 1) return -1;
        return slot > from ? slot - 1 : slot;
    }

    /// <summary>
    /// Read "alertBoxes". Missing or null → empty list and no issue. A wrong type, a bad entry, a duplicate id,
    /// or more than <see cref="MaxBoxes"/> entries is skipped or repaired; <paramref name="issue"/> is told, and
    /// the rest of the note is kept. Unknown properties on a box are kept.
    /// </summary>
    public static List<AlertBox> Parse(JsonElement? el, Action<string>? issue = null)
    {
        void Log(string message) => issue?.Invoke(message);
        var list = new List<AlertBox>();
        if (el is not { } e || e.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return list;
        if (e.ValueKind != JsonValueKind.Array)
        {
            Log("alertBoxes: not an array, all skipped");
            return list;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var skippedExtra = false;
        foreach (var item in e.EnumerateArray())
        {
            if (list.Count >= MaxBoxes)
            {
                if (!skippedExtra)
                {
                    Log($"alertBoxes: more than {MaxBoxes}, extra boxes skipped");
                    skippedExtra = true;
                }
                continue;
            }
            if (item.ValueKind != JsonValueKind.Object)
            {
                Log($"alertBoxes: entry is {item.ValueKind}, skipped");
                continue;
            }

            var box = new AlertBox();
            string? id = null;
            var idBad = false;
            foreach (var p in item.EnumerateObject())
            {
                if (NameIs(p.Name, "id"))
                {
                    if (p.Value.ValueKind == JsonValueKind.String) id = p.Value.GetString();
                    else if (p.Value.ValueKind != JsonValueKind.Null) idBad = true;
                }
                else if (NameIs(p.Name, "title"))
                {
                    if (p.Value.ValueKind == JsonValueKind.String) box.Title = p.Value.GetString() ?? "";
                    else if (p.Value.ValueKind != JsonValueKind.Null)
                    {
                        Log("alertBoxes.title: not a string, using empty");
                        box.Title = "";
                    }
                }
                else if (NameIs(p.Name, "text"))
                {
                    if (p.Value.ValueKind == JsonValueKind.String) box.Text = p.Value.GetString() ?? "";
                    else if (p.Value.ValueKind != JsonValueKind.Null)
                    {
                        Log("alertBoxes.text: not a string, using empty");
                        box.Text = "";
                    }
                }
                else box.Extra[p.Name] = p.Value.Clone();
            }

            if (idBad) Log("alertBoxes.id: not a string, new id created");
            id = (id ?? "").Trim();
            if (id.Length > MaxIdChars)
            {
                Log($"alertBoxes: id longer than {MaxIdChars}, new id created");
                id = "";
            }
            if (id.Length > 0 && !seen.Add(id))
            {
                Log($"alertBoxes: duplicate id \"{LogId(id)}\", new id created");
                id = "";
            }
            if (id.Length == 0)
            {
                id = Guid.NewGuid().ToString("N");
                seen.Add(id);
            }
            box.Id = id;
            if (box.Title.Length > MaxTitleChars)
            {
                Log($"alertBoxes.title: longer than {MaxTitleChars}, cut");
                box.Title = TableLimits.Clip(box.Title, MaxTitleChars);
            }
            if (box.Text.Length > MaxTextChars)
            {
                Log($"alertBoxes.text: longer than {MaxTextChars}, cut");
                box.Text = TableLimits.Clip(box.Text, MaxTextChars);
            }
            list.Add(box);
        }
        return list;
    }

    /// <summary>JSON array in storage order. Unknown properties are written back. Null list → [].</summary>
    public static JsonElement ToElement(IReadOnlyList<AlertBox>? boxes)
    {
        var arr = new JsonArray();
        if (boxes != null)
        {
            foreach (var b in boxes)
            {
                if (b == null) continue;
                var o = new JsonObject
                {
                    ["id"] = b.Id ?? "",
                    ["title"] = b.Title ?? "",
                    ["text"] = b.Text ?? ""
                };
                foreach (var (k, v) in b.Extra)
                {
                    if (string.IsNullOrEmpty(k) || NameIs(k, "id") || NameIs(k, "title") || NameIs(k, "text")) continue;
                    if (o.ContainsKey(k)) continue;
                    var node = JsonNode.Parse(v.GetRawText());
                    if (node != null) o[k] = node;
                }
                arr.Add(o);
            }
        }
        using var doc = JsonDocument.Parse(arr.ToJsonString(WriteOptions));
        return doc.RootElement.Clone();
    }

    /// <summary>
    /// Make the list safe to save: drop nulls, clip text, repair blank/duplicate/over-long ids, cap the count.
    /// Returns true when text was cut or boxes past the cap were removed (ids repaired does not count).
    /// </summary>
    public static bool Normalize(List<AlertBox>? boxes)
    {
        if (boxes == null) return false;
        var clipped = false;
        for (var i = boxes.Count - 1; i >= 0; i--)
            if (boxes[i] == null) boxes.RemoveAt(i);
        if (boxes.Count > MaxBoxes)
        {
            boxes.RemoveRange(MaxBoxes, boxes.Count - MaxBoxes);
            clipped = true;
        }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var b in boxes)
        {
            var title = b.Title ?? "";
            var text = b.Text ?? "";
            if (title.Length > MaxTitleChars || text.Length > MaxTextChars) clipped = true;
            b.Title = TableLimits.Clip(title, MaxTitleChars);
            b.Text = TableLimits.Clip(text, MaxTextChars);
            var id = (b.Id ?? "").Trim();
            if (id.Length == 0 || id.Length > MaxIdChars || !seen.Add(id))
            {
                id = Guid.NewGuid().ToString("N");
                seen.Add(id);
            }
            b.Id = id;
        }
        return clipped;
    }

    private static bool NameIs(string name, string expected) =>
        string.Equals(name, expected, StringComparison.OrdinalIgnoreCase);

    private static string LogId(string id) => id.Length <= 40 ? id : id[..40] + "...";
}
