using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TableNote;

/// <summary>Kind of nested detail. "table" is the editor shipped today; any other kind is kept and written back.</summary>
public static class CellDetailKinds
{
    public const string Table = "table";
}

/// <summary>Limits for one nested detail table. The main note keeps its own limits.</summary>
public static class CellDetailLimits
{
    public const int MaxRows = 100;
    public const int MaxColumns = 12;
    public const int MaxCellChars = TableLimits.MaxCellChars;
}

/// <summary>
/// One cell's nested detail. <see cref="Kind"/> selects the editor. Properties this version does not
/// know live in <see cref="Extra"/> and are written back unchanged, so a later kind (chart, form, …)
/// survives a round-trip through this build.
/// </summary>
public sealed class CellDetail
{
    public string Kind { get; set; } = CellDetailKinds.Table;
    public List<string> Columns { get; set; } = [];
    public List<List<string>> Rows { get; set; } = [];
    /// <summary>JSON properties this version does not model. For a non-table kind this is the whole object.</summary>
    public Dictionary<string, JsonElement> Extra { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>
/// Which columns and cells show a detail button, and the payload for each cell.
/// Stored as note.json "cellDetails". The document version is intentionally unchanged;
/// a build that does not know the field keeps it via ExtraFields.
/// Unknown properties on this object live in <see cref="Extra"/> (for example a future "rows" anchor).
/// </summary>
public sealed class CellDetailStore
{
    public const int CurrentSchema = 1;

    public int Schema { get; set; } = CurrentSchema;
    /// <summary>Column ids that show a detail button on every cell of that column.</summary>
    public List<string> Columns { get; set; } = [];
    /// <summary>Cell keys (rowId/columnId) that show a button on their own.</summary>
    public List<string> Cells { get; set; } = [];
    /// <summary>Cell keys hidden even when their column shows a button.</summary>
    public List<string> Hidden { get; set; } = [];
    /// <summary>Payload keyed by rowId/columnId. Kept when the button is hidden, until the row or column is deleted.</summary>
    public Dictionary<string, CellDetail> Tables { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, JsonElement> Extra { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>
/// Detail buttons and nested tables. Keys are <c>rowId/columnId</c> (both ids are Guid "N", so they never
/// contain '/'). Column moves and row moves do not rewrite keys: everything is keyed by id.
/// </summary>
public static class CellDetailOps
{
    public const string ButtonName = "CellDetailButton";
    private const char Sep = '/';

    private static readonly JsonSerializerOptions Loose = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly HashSet<string> StoreProps = new(StringComparer.OrdinalIgnoreCase)
        { "schema", "columns", "cells", "hidden", "tables" };

    private static readonly HashSet<string> TableProps = new(StringComparer.OrdinalIgnoreCase)
        { "kind", "columns", "rows" };

    public static bool IsTable(CellDetail? detail) =>
        detail != null && (detail.Kind.Length == 0 ||
                           string.Equals(detail.Kind, CellDetailKinds.Table, StringComparison.OrdinalIgnoreCase));

    public static bool IsEmpty(CellDetailStore? store) =>
        store == null || (store.Columns.Count == 0 && store.Cells.Count == 0 && store.Hidden.Count == 0 &&
                          store.Tables.Count == 0 && store.Extra.Count == 0);

    public static bool TryKey(string? rowId, string? columnId, out string key)
    {
        key = "";
        if (string.IsNullOrEmpty(rowId) || string.IsNullOrEmpty(columnId)) return false;
        if (rowId.Contains(Sep) || columnId.Contains(Sep)) return false;
        key = rowId + Sep + columnId;
        return true;
    }

    public static string Key(string rowId, string columnId) =>
        TryKey(rowId, columnId, out var key) ? key : "";

    public static bool TrySplit(string? key, out string rowId, out string columnId)
    {
        rowId = columnId = "";
        if (string.IsNullOrEmpty(key)) return false;
        var i = key.IndexOf(Sep);
        if (i <= 0 || i != key.LastIndexOf(Sep) || i >= key.Length - 1) return false;
        rowId = key[..i];
        columnId = key[(i + 1)..];
        return rowId.Length > 0 && columnId.Length > 0;
    }

    public static CellDetail CreateDefault() => new()
    {
        Kind = CellDetailKinds.Table,
        Columns = [Localizer.T("detail.default_col1"), Localizer.T("detail.default_col2")],
        Rows = [["", ""], ["", ""], ["", ""]]
    };

    public static CellDetail Clone(CellDetail detail) => new()
    {
        Kind = detail.Kind,
        Columns = detail.Columns.ToList(),
        Rows = detail.Rows.Select(r => r.ToList()).ToList(),
        Extra = detail.Extra.ToDictionary(kv => kv.Key, kv => kv.Value.Clone(), StringComparer.Ordinal)
    };

    public static CellDetailStore Clone(CellDetailStore store) => new()
    {
        Schema = store.Schema,
        Columns = store.Columns.ToList(),
        Cells = store.Cells.ToList(),
        Hidden = store.Hidden.ToList(),
        Tables = store.Tables.ToDictionary(kv => kv.Key, kv => Clone(kv.Value), StringComparer.Ordinal),
        Extra = store.Extra.ToDictionary(kv => kv.Key, kv => kv.Value.Clone(), StringComparer.Ordinal)
    };

    public static bool ColumnButtonOn(NoteDocument doc, int column) =>
        column >= 0 && column < doc.Columns.Count && Contains(doc.CellDetails.Columns, doc.ColumnIdAt(column));

    /// <summary>True when this cell should draw the detail button.</summary>
    public static bool ShowsButton(NoteDocument doc, int row, int column)
    {
        if (row < 0 || column < 0 || row >= doc.Rows.Count || column >= doc.Columns.Count) return false;
        if (!TryKey(doc.RowIdAt(row), doc.ColumnIdAt(column), out var key)) return false;
        var store = doc.CellDetails;
        if (Contains(store.Hidden, key)) return false;
        if (Contains(store.Columns, doc.ColumnIdAt(column))) return true;
        return Contains(store.Cells, key);
    }

    /// <summary>Turn the per-column button on or off. Returns false when nothing changed.
    /// Per-cell buttons are left in place, so turning the column off again restores them.
    /// Turning the column on clears per-cell hides (every cell shows the button).</summary>
    public static bool SetColumnButton(NoteDocument doc, int column, bool on)
    {
        if (column < 0 || column >= doc.Columns.Count) return false;
        var id = doc.ColumnIdAt(column);
        var changed = false;
        if (on)
        {
            if (!Contains(doc.CellDetails.Columns, id)) { doc.CellDetails.Columns.Add(id); changed = true; }
            changed |= doc.CellDetails.Hidden.RemoveAll(k => TrySplit(k, out _, out var c) && c == id) > 0;
        }
        else changed |= doc.CellDetails.Columns.RemoveAll(x => x == id) > 0;
        return changed;
    }

    /// <summary>Show or hide the button on one cell. A column-wide button is hidden per cell via <see cref="CellDetailStore.Hidden"/>.</summary>
    public static bool SetCellButton(NoteDocument doc, int row, int column, bool on)
    {
        if (row < 0 || column < 0 || row >= doc.Rows.Count || column >= doc.Columns.Count) return false;
        var colId = doc.ColumnIdAt(column);
        if (!TryKey(doc.RowIdAt(row), colId, out var key)) return false;
        var store = doc.CellDetails;
        var columnOn = Contains(store.Columns, colId);
        var changed = false;
        if (on)
        {
            changed |= store.Hidden.RemoveAll(x => x == key) > 0;
            if (!columnOn && !Contains(store.Cells, key)) { store.Cells.Add(key); changed = true; }
        }
        else
        {
            changed |= store.Cells.RemoveAll(x => x == key) > 0;
            if (columnOn && !Contains(store.Hidden, key)) { store.Hidden.Add(key); changed = true; }
            else if (!columnOn) changed |= store.Hidden.RemoveAll(x => x == key) > 0;
        }
        return changed;
    }

    public static CellDetail? Get(NoteDocument doc, string rowId, string columnId) =>
        TryKey(rowId, columnId, out var key) && doc.CellDetails.Tables.TryGetValue(key, out var table) ? table : null;

    public static void Set(NoteDocument doc, string rowId, string columnId, CellDetail detail)
    {
        if (!TryKey(rowId, columnId, out var key)) return;
        doc.CellDetails.Tables[key] = IsTable(detail) ? Normalize(Clone(detail)) : Clone(detail);
    }

    public static void OnRowRemoved(NoteDocument doc, string rowId)
    {
        if (string.IsNullOrEmpty(rowId)) return;
        bool Gone(string key) => TrySplit(key, out var r, out _) && r == rowId;
        doc.CellDetails.Cells.RemoveAll(Gone);
        doc.CellDetails.Hidden.RemoveAll(Gone);
        foreach (var k in doc.CellDetails.Tables.Keys.Where(Gone).ToList()) doc.CellDetails.Tables.Remove(k);
    }

    public static void OnColumnRemoved(NoteDocument doc, string columnId)
    {
        if (string.IsNullOrEmpty(columnId)) return;
        doc.CellDetails.Columns.RemoveAll(x => x == columnId);
        bool Gone(string key) => TrySplit(key, out _, out var c) && c == columnId;
        doc.CellDetails.Cells.RemoveAll(Gone);
        doc.CellDetails.Hidden.RemoveAll(Gone);
        foreach (var k in doc.CellDetails.Tables.Keys.Where(Gone).ToList()) doc.CellDetails.Tables.Remove(k);
    }

    /// <summary>Drop buttons and tables whose row or column id is no longer in the note. Returns how many entries were removed.</summary>
    public static int Prune(NoteDocument doc)
    {
        doc.EnsureRowIds();
        doc.EnsureColumnIds();
        var rows = new HashSet<string>(doc.RowIds, StringComparer.Ordinal);
        var cols = new HashSet<string>(doc.ColumnIds, StringComparer.Ordinal);
        var n = doc.CellDetails.Columns.RemoveAll(id => !cols.Contains(id));
        bool Gone(string key)
        {
            if (!TrySplit(key, out var r, out var c)) return true;
            return !rows.Contains(r) || !cols.Contains(c);
        }
        n += doc.CellDetails.Cells.RemoveAll(Gone);
        n += doc.CellDetails.Hidden.RemoveAll(Gone);
        foreach (var k in doc.CellDetails.Tables.Keys.Where(Gone).ToList())
        {
            doc.CellDetails.Tables.Remove(k);
            n++;
        }
        return n;
    }

    /// <summary>Make a table kind rectangular and inside the limits. Non-table kinds are returned unchanged.</summary>
    public static CellDetail Normalize(CellDetail detail, IList<string>? issues = null, string prefix = "cellDetails")
    {
        if (!IsTable(detail)) return detail;
        detail.Kind = CellDetailKinds.Table;
        if (detail.Columns.Count == 0 && detail.Rows.Count == 0) return detail;

        if (detail.Rows.Count > CellDetailLimits.MaxRows)
        {
            detail.Rows.RemoveRange(CellDetailLimits.MaxRows, detail.Rows.Count - CellDetailLimits.MaxRows);
            issues?.Add($"{prefix} trimmed to {CellDetailLimits.MaxRows} rows");
        }

        var width = detail.Columns.Count;
        foreach (var r in detail.Rows) width = Math.Max(width, r.Count);
        while (detail.Columns.Count < width)
            detail.Columns.Add(Localizer.T("detail.col_default", detail.Columns.Count + 1));
        if (detail.Columns.Count > CellDetailLimits.MaxColumns)
        {
            detail.Columns.RemoveRange(CellDetailLimits.MaxColumns, detail.Columns.Count - CellDetailLimits.MaxColumns);
            issues?.Add($"{prefix} trimmed to {CellDetailLimits.MaxColumns} columns");
        }

        var clipped = false;
        for (var c = 0; c < detail.Columns.Count; c++)
        {
            detail.Columns[c] = Clip(detail.Columns[c] ?? "", ref clipped);
        }
        foreach (var row in detail.Rows)
        {
            if (row.Count > detail.Columns.Count) row.RemoveRange(detail.Columns.Count, row.Count - detail.Columns.Count);
            while (row.Count < detail.Columns.Count) row.Add("");
            for (var c = 0; c < row.Count; c++) row[c] = Clip(row[c] ?? "", ref clipped);
        }
        if (clipped) issues?.Add($"{prefix} a cell was longer than {CellDetailLimits.MaxCellChars} characters, trimmed");
        return detail;
    }

    public static string ToJson(CellDetailStore store)
    {
        var o = new JsonObject { ["schema"] = store.Schema < 1 ? CellDetailStore.CurrentSchema : store.Schema };
        AddList(o, "columns", store.Columns);
        AddList(o, "cells", store.Cells);
        AddList(o, "hidden", store.Hidden);
        if (store.Tables.Count > 0)
        {
            var tables = new JsonObject();
            foreach (var kv in store.Tables.OrderBy(k => k.Key, StringComparer.Ordinal))
                tables[kv.Key] = TableNode(kv.Value);
            o["tables"] = tables;
        }
        AddExtra(o, store.Extra);
        return o.ToJsonString(Loose);
    }

    /// <summary>Add "cellDetails" when the store holds something. Empty stores are omitted so older files stay unchanged.</summary>
    public static void WriteProperty(JsonObject node, CellDetailStore store)
    {
        if (IsEmpty(store) || node.ContainsKey("cellDetails")) return;
        node["cellDetails"] = JsonNode.Parse(ToJson(store));
    }

    public static CellDetailStore Parse(JsonElement el, IList<string> issues)
    {
        var store = new CellDetailStore();
        if (el.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) return store;
        if (el.ValueKind != JsonValueKind.Object)
        {
            issues.Add($"cellDetails: not an object ({el.ValueKind}), skipped");
            return store;
        }
        store.Schema = ReadInt(el, "schema", CellDetailStore.CurrentSchema, issues);
        if (store.Schema > CellDetailStore.CurrentSchema)
            AppLog.Write("load", $"cellDetails schema {store.Schema} is newer; known fields were loaded and unknown fields kept");
        store.Columns = ReadIdList(el, "columns", issues);
        store.Cells = ReadKeyList(el, "cells", issues);
        store.Hidden = ReadKeyList(el, "hidden", issues);
        if (Find(el, "tables") is { } tables)
        {
            if (tables.ValueKind != JsonValueKind.Object)
                issues.Add("cellDetails.tables: not an object, skipped");
            else
            {
                foreach (var p in tables.EnumerateObject())
                {
                    if (!TrySplit(p.Name, out _, out _))
                    {
                        issues.Add($"cellDetails.tables.{p.Name}: key is not rowId/columnId, skipped");
                        continue;
                    }
                    if (p.Value.ValueKind != JsonValueKind.Object)
                    {
                        issues.Add($"cellDetails.tables.{p.Name}: not an object, skipped");
                        continue;
                    }
                    store.Tables[p.Name] = ParseTable(p.Value, issues, $"cellDetails.tables.{p.Name}");
                }
            }
        }
        foreach (var p in el.EnumerateObject())
            if (!StoreProps.Contains(p.Name)) store.Extra[p.Name] = p.Value.Clone();
        return store;
    }

    public static CellDetailStore ParseJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return Parse(doc.RootElement, []);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            return new CellDetailStore();
        }
    }

    /// <summary>
    /// Append one export column per main column that has detail text, for the rows already chosen.
    /// No-op when nothing would be written, so a note without details exports exactly as before.
    /// </summary>
    public static void AppendExport(NoteDocument doc, List<string> headers, List<TaskRow> rows, IReadOnlyList<int> docRows)
    {
        var cols = new List<int>();
        for (var c = 0; c < doc.Columns.Count; c++)
        {
            for (var i = 0; i < rows.Count && i < docRows.Count; i++)
            {
                if (ExportText(doc, docRows[i], c).Length > 0) { cols.Add(c); break; }
            }
        }
        if (cols.Count == 0) return;
        foreach (var c in cols)
            headers.Add(Localizer.T("detail.export_header", c < doc.Columns.Count ? doc.Columns[c] : ""));
        for (var i = 0; i < rows.Count; i++)
        {
            var docRow = i < docRows.Count ? docRows[i] : -1;
            var cells = rows[i].Cells.ToList();
            foreach (var c in cols)
                cells.Add(docRow >= 0 ? ExportText(doc, docRow, c) : "");
            rows[i] = new TaskRow(cells, rows[i].Recurrence);
        }
    }

    /// <summary>Readable grid (headers, then one line per row, cells joined by " | "). Empty when the table has no text.
    /// A non-table kind is exported as its JSON so the values are not dropped.</summary>
    public static string ExportText(NoteDocument doc, int row, int column)
    {
        if (row < 0 || column < 0 || row >= doc.Rows.Count || column >= doc.Columns.Count) return "";
        var table = Get(doc, doc.RowIdAt(row), doc.ColumnIdAt(column));
        if (table == null) return "";
        if (!IsTable(table)) return TableNode(table).ToJsonString(Loose);
        if (IsBlank(table)) return "";
        var sb = new StringBuilder();
        sb.Append(string.Join(" | ", table.Columns.Select(Sanitize)));
        foreach (var r in table.Rows)
        {
            if (sb.Length > 0) sb.Append('\n');
            var width = Math.Max(table.Columns.Count, r.Count);
            var parts = new string[width];
            for (var i = 0; i < width; i++) parts[i] = Sanitize(i < r.Count ? r[i] : "");
            sb.Append(string.Join(" | ", parts));
        }
        return sb.ToString();
    }

    private static bool IsBlank(CellDetail table) =>
        table.Columns.All(string.IsNullOrWhiteSpace) && table.Rows.All(r => r.All(string.IsNullOrWhiteSpace));

    private static string Sanitize(string? value)
    {
        var v = value ?? "";
        return v.Replace("\r\n", " ", StringComparison.Ordinal).Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
    }

    private static string Clip(string value, ref bool clipped)
    {
        if (value.Length <= CellDetailLimits.MaxCellChars) return value;
        clipped = true;
        return TableLimits.Clip(value, CellDetailLimits.MaxCellChars);
    }

    private static CellDetail ParseTable(JsonElement el, IList<string> issues, string prefix)
    {
        var kind = ReadString(el, "kind");
        if (!string.IsNullOrWhiteSpace(kind) &&
            !string.Equals(kind.Trim(), CellDetailKinds.Table, StringComparison.OrdinalIgnoreCase))
        {
            var kept = new CellDetail { Kind = kind.Trim() };
            foreach (var p in el.EnumerateObject()) kept.Extra[p.Name] = p.Value.Clone();
            return kept;
        }
        var detail = new CellDetail { Kind = CellDetailKinds.Table };
        detail.Columns = ReadStrings(el, "columns", issues, prefix + ".columns");
        detail.Rows = ReadRows(el, issues, prefix + ".rows");
        foreach (var p in el.EnumerateObject())
            if (!TableProps.Contains(p.Name)) detail.Extra[p.Name] = p.Value.Clone();
        return Normalize(detail, issues, prefix);
    }

    private static JsonObject TableNode(CellDetail detail)
    {
        if (!IsTable(detail))
        {
            var raw = new JsonObject();
            foreach (var kv in detail.Extra) raw[kv.Key] = JsonNode.Parse(kv.Value.GetRawText());
            if (!raw.ContainsKey("kind")) raw["kind"] = detail.Kind;
            return raw;
        }
        var o = new JsonObject
        {
            ["kind"] = CellDetailKinds.Table,
            ["columns"] = StringArray(detail.Columns),
            ["rows"] = RowArray(detail.Rows)
        };
        AddExtra(o, detail.Extra);
        return o;
    }

    private static void AddList(JsonObject o, string name, List<string> values)
    {
        if (values.Count == 0) return;
        var arr = new JsonArray();
        foreach (var v in values.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal))
            arr.Add(JsonValue.Create(v));
        o[name] = arr;
    }

    private static void AddExtra(JsonObject o, Dictionary<string, JsonElement> extra)
    {
        foreach (var kv in extra.OrderBy(k => k.Key, StringComparer.Ordinal))
            if (!o.ContainsKey(kv.Key)) o[kv.Key] = JsonNode.Parse(kv.Value.GetRawText());
    }

    private static JsonArray StringArray(IEnumerable<string> values)
    {
        var arr = new JsonArray();
        foreach (var v in values) arr.Add(JsonValue.Create(v ?? ""));
        return arr;
    }

    private static JsonArray RowArray(List<List<string>> rows)
    {
        var arr = new JsonArray();
        foreach (var r in rows) arr.Add(StringArray(r));
        return arr;
    }

    private static bool Contains(List<string> list, string value) =>
        list.Any(x => string.Equals(x, value, StringComparison.Ordinal));

    private static JsonElement? Find(JsonElement obj, string name)
    {
        foreach (var p in obj.EnumerateObject())
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return p.Value;
        return null;
    }

    private static int ReadInt(JsonElement obj, string name, int def, IList<string> issues)
    {
        if (Find(obj, name) is not { } v || v.ValueKind == JsonValueKind.Null) return def;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n)) return n;
        if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out var s)) return s;
        issues.Add($"cellDetails.{name}: value {v.GetRawText()} is not an integer, using {def}");
        return def;
    }

    private static string? ReadString(JsonElement obj, string name)
    {
        if (Find(obj, name) is not { } v || v.ValueKind != JsonValueKind.String) return null;
        return v.GetString();
    }

    private static List<string> ReadIdList(JsonElement obj, string name, IList<string> issues)
    {
        var list = new List<string>();
        if (Find(obj, name) is not { } v || v.ValueKind == JsonValueKind.Null) return list;
        if (v.ValueKind != JsonValueKind.Array) { issues.Add($"cellDetails.{name}: not an array, skipped"); return list; }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in v.EnumerateArray())
        {
            if (e.ValueKind != JsonValueKind.String) { issues.Add($"cellDetails.{name}: value {e.GetRawText()} is not a string, skipped"); continue; }
            var id = (e.GetString() ?? "").Trim();
            if (id.Length == 0 || id.Contains(Sep) || !seen.Add(id)) continue;
            list.Add(id);
        }
        return list;
    }

    private static List<string> ReadKeyList(JsonElement obj, string name, IList<string> issues)
    {
        var list = new List<string>();
        if (Find(obj, name) is not { } v || v.ValueKind == JsonValueKind.Null) return list;
        if (v.ValueKind != JsonValueKind.Array) { issues.Add($"cellDetails.{name}: not an array, skipped"); return list; }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in v.EnumerateArray())
        {
            if (e.ValueKind != JsonValueKind.String) { issues.Add($"cellDetails.{name}: value {e.GetRawText()} is not a string, skipped"); continue; }
            var key = (e.GetString() ?? "").Trim();
            if (!TrySplit(key, out _, out _)) { issues.Add($"cellDetails.{name}: \"{key}\" is not rowId/columnId, skipped"); continue; }
            if (seen.Add(key)) list.Add(key);
        }
        return list;
    }

    private static List<string> ReadStrings(JsonElement obj, string name, IList<string> issues, string prefix)
    {
        var list = new List<string>();
        if (Find(obj, name) is not { } v || v.ValueKind == JsonValueKind.Null) return list;
        if (v.ValueKind != JsonValueKind.Array) { issues.Add($"{prefix}: not an array, skipped"); return list; }
        foreach (var e in v.EnumerateArray()) list.Add(CellString(e, issues, prefix));
        return list;
    }

    private static List<List<string>> ReadRows(JsonElement obj, IList<string> issues, string prefix)
    {
        var rows = new List<List<string>>();
        if (Find(obj, "rows") is not { } v || v.ValueKind == JsonValueKind.Null) return rows;
        if (v.ValueKind != JsonValueKind.Array) { issues.Add($"{prefix}: not an array, skipped"); return rows; }
        foreach (var r in v.EnumerateArray())
        {
            if (r.ValueKind != JsonValueKind.Array)
            {
                issues.Add($"{prefix}: a row is not an array, using an empty row");
                rows.Add([]);
                continue;
            }
            rows.Add(r.EnumerateArray().Select(c => CellString(c, issues, prefix)).ToList());
        }
        return rows;
    }

    private static string CellString(JsonElement c, IList<string> issues, string prefix) => c.ValueKind switch
    {
        JsonValueKind.String => c.GetString() ?? "",
        JsonValueKind.Null => "",
        JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => c.GetRawText(),
        _ => Issue(issues, $"{prefix}: {c.ValueKind} value in a cell, using an empty cell")
    };

    private static string Issue(IList<string> issues, string text)
    {
        issues.Add(text);
        return "";
    }
}
