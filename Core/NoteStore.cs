using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TableNote;

public static class NoteStore
{
    private sealed class FileDto
    {
        public int Version { get; set; } = 1;
        public List<string?>? Columns { get; set; }
        public List<List<string?>?>? Rows { get; set; }
        public bool LaunchAtLogin { get; set; }
        public bool TrayNoticeShown { get; set; }
        public List<int>? ColumnWidths { get; set; }
        public string? ViewMode { get; set; }
        public WindowDto? Window { get; set; }
        public bool HideDone { get; set; }
        public JsonElement? Snoozes { get; set; }
        public JsonElement? Recurrence { get; set; }
        public List<string?>? RowIds { get; set; }
        public List<string?>? ColumnIds { get; set; }
        public List<string?>? ColumnRoles { get; set; }
        public bool MuteSound { get; set; }
        public JsonElement? Acknowledged { get; set; }
        /// <summary>Parsed leniently per task (A8): a bad value never makes the whole file "corrupt".</summary>
        public JsonElement? Reminders { get; set; }
        /// <summary>v2.4.0: parsed leniently per entry, like reminders.</summary>
        public JsonElement? ColumnSettings { get; set; }
        public JsonElement? RowStyles { get; set; }
    }

    /// <summary>Problems found while reading optional sections (logged, not shown as "file corrupt").</summary>
    public static List<string> LastLoadIssues { get; } = [];

    private static Dictionary<string, string> StringMap(JsonElement? el, string section)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (el is not { } e || e.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return map;
        if (e.ValueKind != JsonValueKind.Object) { LastLoadIssues.Add($"{section}: not an object, skipped"); return map; }
        foreach (var p in e.EnumerateObject())
        {
            if (p.Value.ValueKind == JsonValueKind.String) map[p.Name] = p.Value.GetString() ?? "";
            else LastLoadIssues.Add($"{section}.{p.Name}: value is not a string, skipped");
        }
        return map;
    }

    /// <summary>v2.4.1: note.json as text. BOM (UTF-8/16/32) is honoured; without a BOM the bytes must be
    /// valid UTF-8, otherwise they are read as Thai ANSI (Windows-874) and <paramref name="legacyEncoding"/> is set.</summary>
    internal static string ReadNoteText(string path, out bool legacyEncoding)
    {
        var bytes = File.ReadAllBytes(path);
        legacyEncoding = false;
        try
        {
            using var r = new StreamReader(new MemoryStream(bytes), new UTF8Encoding(false, throwOnInvalidBytes: true), detectEncodingFromByteOrderMarks: true);
            return r.ReadToEnd();
        }
        catch (DecoderFallbackException)
        {
            legacyEncoding = true;
            return CodePagesEncodingProvider.Instance.GetEncoding(874)?.GetString(bytes)
                   ?? new UTF8Encoding(false).GetString(bytes);
        }
    }

    /// <summary>Copy of a non-UTF-8 note.json before it is rewritten as UTF-8: note.encoding-&lt;yyyyMMdd-HHmmss&gt;.bak.</summary>
    private static string? EncodingBackup(string path)
    {
        try
        {
            var dir = Path.GetDirectoryName(path) ?? ".";
            var name = Path.Combine(dir, $"note.encoding-{DateTime.Now:yyyyMMdd-HHmmss}.bak");
            for (var i = 1; File.Exists(name); i++)
                name = Path.Combine(dir, $"note.encoding-{DateTime.Now:yyyyMMdd-HHmmss}-{i}.bak");
            File.Copy(path, name, overwrite: false);
            return name;
        }
        catch { return null; }
    }

    /// <summary>Copy of the untouched file before trimming to the limits: note.overlimit-&lt;yyyyMMdd-HHmmss&gt;.bak (never overwritten).</summary>
    private static string? OverflowBackup(string path)
    {
        try
        {
            var dir = Path.GetDirectoryName(path) ?? ".";
            var name = Path.Combine(dir, $"note.overlimit-{DateTime.Now:yyyyMMdd-HHmmss}.bak");
            for (var i = 1; File.Exists(name); i++)
                name = Path.Combine(dir, $"note.overlimit-{DateTime.Now:yyyyMMdd-HHmmss}-{i}.bak");
            File.Copy(path, name, overwrite: false);
            return name;
        }
        catch { return null; }
    }

    private static readonly HashSet<string> KnownFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "version", "columns", "rows", "launchAtLogin", "trayNoticeShown", "columnWidths", "viewMode", "window",
        "hideDone", "snoozes", "recurrence", "rowIds", "columnIds", "columnRoles", "muteSound", "acknowledged", "reminders",
        "columnSettings", "rowStyles" // v2.4.0
    };

    private static JsonElement? Prop(JsonElement obj, string name)
    {
        foreach (var p in obj.EnumerateObject())
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return p.Value;
        return null;
    }

    private static string CellText(JsonElement c, string section) => c.ValueKind switch
    {
        JsonValueKind.String => c.GetString() ?? "",
        JsonValueKind.Null => "",
        JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => c.GetRawText(),
        _ => Issue($"{section}: {c.ValueKind} value in a cell, using an empty cell", "")
    };

    private static T Issue<T>(string text, T value) { LastLoadIssues.Add(text); return value; }

    private static int GetInt(JsonElement obj, string name, int def, bool allowString = true, string prefix = "")
    {
        if (Prop(obj, name) is not { } v || v.ValueKind == JsonValueKind.Null) return def;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n)) return n;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var dbl) && dbl is > int.MinValue and < int.MaxValue) return (int)Math.Round(dbl);
        if (allowString && v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var s)) return s;
        return Issue($"{prefix}{name}: value {v.GetRawText()} is not an integer, using default {def}", def);
    }

    private static int? GetNullableInt(JsonElement obj, string name)
    {
        if (Prop(obj, name) is not { } v || v.ValueKind == JsonValueKind.Null) return null;
        var n = GetInt(obj, name, int.MinValue, true, "window.");
        return n == int.MinValue ? null : n;
    }

    private static bool GetBool(JsonElement obj, string name, bool def, string prefix = "")
    {
        if (Prop(obj, name) is not { } v || v.ValueKind == JsonValueKind.Null) return def;
        if (v.ValueKind == JsonValueKind.True) return true;
        if (v.ValueKind == JsonValueKind.False) return false;
        if (v.ValueKind == JsonValueKind.String && bool.TryParse(v.GetString()?.Trim(), out var b)) return b;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) && n is 0 or 1) return n == 1;
        return Issue($"{prefix}{name}: value {v.GetRawText()} is not true/false, using default", def);
    }

    private static string? GetString(JsonElement obj, string name, string prefix = "")
    {
        if (Prop(obj, name) is not { } v || v.ValueKind == JsonValueKind.Null) return null;
        if (v.ValueKind == JsonValueKind.String) return v.GetString();
        return Issue<string?>($"{prefix}{name}: not a string, using default", null);
    }

    private static List<int> GetIntList(JsonElement obj, string name)
    {
        var list = new List<int>();
        if (Prop(obj, name) is not { } v || v.ValueKind == JsonValueKind.Null) return list;
        if (v.ValueKind != JsonValueKind.Array) return Issue($"{name}: not an array, using default", list);
        foreach (var e in v.EnumerateArray())
        {
            if (e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out var n)) list.Add(n);
            else if (e.ValueKind == JsonValueKind.String && int.TryParse(e.GetString(), out var s)) list.Add(s);
            else { LastLoadIssues.Add($"{name}: value {e.GetRawText()} is not usable, using the default width"); list.Add(TableLimits.DefaultColumnWidth); }
        }
        return list;
    }

    private static List<string> GetStringList(JsonElement obj, string name)
    {
        var list = new List<string>();
        if (Prop(obj, name) is not { } v || v.ValueKind == JsonValueKind.Null) return list;
        if (v.ValueKind != JsonValueKind.Array) return Issue($"{name}: not an array, using default", list);
        foreach (var e in v.EnumerateArray())
            list.Add(e.ValueKind == JsonValueKind.String ? e.GetString() ?? "" : Issue($"{name}: value {e.GetRawText()} is not a string, skipped", ""));
        return list;
    }

    /// <summary>Row ids keep their position; a bad/missing/duplicate id gets a fresh one (reminders for it are dropped by Sanitize).</summary>
    private static List<string> GetRowIds(JsonElement obj, int rowCount)
    {
        var raw = GetStringList(obj, "rowIds");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var ids = new List<string>();
        for (var i = 0; i < raw.Count && i < rowCount; i++)
        {
            var id = raw[i].Trim();
            if (id.Length == 0 || !seen.Add(id))
            {
                if (id.Length > 0) LastLoadIssues.Add($"rowIds: duplicate row id \"{id}\", new id created");
                id = Guid.NewGuid().ToString("N");
                seen.Add(id);
            }
            ids.Add(id);
        }
        return ids;
    }

    /// <summary>Column ids keep their position; a bad/missing/duplicate id gets a fresh one (same style as rowIds).</summary>
    private static List<string> GetColumnIds(JsonElement obj, int columnCount)
    {
        var raw = GetStringList(obj, "columnIds");
        // v2.4.1: columnIds are positional. TableNote 2.3.1 keeps them verbatim (unknown field) while the user
        // deletes / inserts / moves columns there, so after 2.4.0 → 2.3.1 → 2.4.x a count mismatch means the ids no longer
        // belong to their columns: per-column settings would land on the WRONG column. Use fresh ids for every column
        // (settings keyed by the old ids are then dropped by PruneOrphans; cell data is untouched) and log the old ones.
        if (raw.Count > 0 && raw.Count != columnCount)
        {
            var settings = Prop(obj, "columnSettings") is { ValueKind: JsonValueKind.Object } cs ? cs.GetRawText() : "{}";
            LastLoadIssues.Add($"columnIds: {raw.Count} ids but {columnCount} columns (file edited by an older version), new ids created and old column settings dropped: {settings}");
            return Enumerable.Range(0, columnCount).Select(_ => Guid.NewGuid().ToString("N")).ToList();
        }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var ids = new List<string>();
        for (var i = 0; i < raw.Count && i < columnCount; i++)
        {
            var id = raw[i].Trim();
            if (id.Length == 0 || !seen.Add(id))
            {
                if (id.Length > 0) LastLoadIssues.Add($"columnIds: duplicate column id \"{id}\", new id created");
                id = Guid.NewGuid().ToString("N");
                seen.Add(id);
            }
            ids.Add(id);
        }
        return ids;
    }

    /// <summary>Lenient per-task parse of "reminders": wrong types are dropped one value / one task at a time.</summary>
    internal static Dictionary<string, ReminderInfo> ParseReminders(JsonElement? el)
    {
        var map = new Dictionary<string, ReminderInfo>(StringComparer.Ordinal);
        if (el is not { } e || e.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return map;
        if (e.ValueKind != JsonValueKind.Object) { LastLoadIssues.Add("reminders: not an object, all skipped"); return map; }
        foreach (var task in e.EnumerateObject())
        {
            if (task.Value.ValueKind != JsonValueKind.Object) { LastLoadIssues.Add($"reminders.{task.Name}: not an object, this task skipped"); continue; }
            var info = new ReminderInfo();
            foreach (var f in task.Value.EnumerateObject())
            {
                if (string.Equals(f.Name, "offsets", StringComparison.OrdinalIgnoreCase))
                {
                    if (f.Value.ValueKind != JsonValueKind.Array) { LastLoadIssues.Add($"reminders.{task.Name}.offsets: not an array, skipped"); continue; }
                    foreach (var o in f.Value.EnumerateArray())
                    {
                        if (o.ValueKind == JsonValueKind.Number && o.TryGetInt32(out var n)) info.Offsets.Add(n);
                        else LastLoadIssues.Add($"reminders.{task.Name}.offsets: value {o.GetRawText()} is not an integer, skipped");
                    }
                }
                else if (string.Equals(f.Name, "acked", StringComparison.OrdinalIgnoreCase))
                {
                    if (f.Value.ValueKind != JsonValueKind.Array) { LastLoadIssues.Add($"reminders.{task.Name}.acked: not an array, skipped"); continue; }
                    foreach (var a in f.Value.EnumerateArray())
                    {
                        if (a.ValueKind == JsonValueKind.String) info.Acked.Add(a.GetString() ?? "");
                        else LastLoadIssues.Add($"reminders.{task.Name}.acked: value {a.GetRawText()} is not a string, skipped");
                    }
                }
            }
            map[task.Name] = info;
        }
        return map;
    }

    /// <summary>v2.4.0: lenient parse of "columnSettings" {columnId: {type, options[]}}; bad entries are skipped.</summary>
    internal static Dictionary<string, ColumnSetting> ParseColumnSettings(JsonElement? el)
    {
        var map = new Dictionary<string, ColumnSetting>(StringComparer.Ordinal);
        if (el is not { } e || e.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return map;
        if (e.ValueKind != JsonValueKind.Object) { LastLoadIssues.Add("columnSettings: not an object, all skipped"); return map; }
        foreach (var col in e.EnumerateObject())
        {
            if (col.Value.ValueKind != JsonValueKind.Object) { LastLoadIssues.Add($"columnSettings.{col.Name}: not an object, skipped"); continue; }
            var type = GetString(col.Value, "type", $"columnSettings.{col.Name}.");
            var opts = GetStringList(col.Value, "options");
            map[col.Name] = new ColumnSetting { Type = ColumnTypes.Normalize(type), Options = ColumnSettingsOps.CleanOptions(opts) };
        }
        return map;
    }

    /// <summary>v2.4.0: lenient parse of "rowStyles" {rowId: {color, highlight}}; bad entries are skipped.</summary>
    internal static Dictionary<string, RowStyle> ParseRowStyles(JsonElement? el)
    {
        var map = new Dictionary<string, RowStyle>(StringComparer.Ordinal);
        if (el is not { } e || e.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return map;
        if (e.ValueKind != JsonValueKind.Object) { LastLoadIssues.Add("rowStyles: not an object, all skipped"); return map; }
        foreach (var row in e.EnumerateObject())
        {
            if (row.Value.ValueKind != JsonValueKind.Object) { LastLoadIssues.Add($"rowStyles.{row.Name}: not an object, skipped"); continue; }
            var color = GetString(row.Value, "color", $"rowStyles.{row.Name}.");
            if (color != null && RowStyleOps.HexFor(color) == null) { LastLoadIssues.Add($"rowStyles.{row.Name}.color: \"{color}\" is not usable, no color"); color = null; }
            var hl = GetBool(row.Value, "highlight", false, $"rowStyles.{row.Name}.");
            if (color != null || hl) map[row.Name] = new RowStyle { Color = color, Highlight = hl };
        }
        return map;
    }

    private static void LogIssues(string path)
    {
        if (LastLoadIssues.Count == 0) return;
        AppLog.WriteFor(path, "load", LastLoadIssues); // same 256 KB rotation as every other log line
    }

    private sealed class WindowDto
    {
        public int Width { get; set; } = 720;
        public int Height { get; set; } = 480;
        public int? X { get; set; }
        public int? Y { get; set; }
        public string? Layer { get; set; }
        public bool Locked { get; set; }
        public bool? Topmost { get; set; }
    }

    // v2.4.2: user-facing load warnings are resolved in the UI language when Load runs (was Thai consts).
    // LastLoadIssues / tablenote.log stay English only so logs attached to GitHub issues are readable.
    public static string WarningCorrupt => Localizer.T("load.corrupt");

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static string DefaultPath()
    {
        var env = Environment.GetEnvironmentVariable("TABLENOTE_DATA");
        if (!string.IsNullOrWhiteSpace(env)) return env;

        if (OperatingSystem.IsWindows())
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrEmpty(appData))
                appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "Roaming");
            return Path.Combine(appData, "TableNote", "note.json");
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(home)) home = Path.GetTempPath();
        var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        return Path.Combine(string.IsNullOrEmpty(xdg) ? Path.Combine(home, ".local", "share") : xdg, "TableNote", "note.json");
    }

    public static LoadResult Load(string path) => Load(path, TableLimits.MaxSaveFileBytes);

    public static LoadResult Load(string path, int maxBytes)
    {
        var result = LoadCore(path, maxBytes);
        // v2.4.1: OneDrive / antivirus may hold note.json for a moment at log-in (same as L15 for Save).
        // Before, ONE IOException at start-up opened an empty note in "keep existing file" mode for the whole session.
        for (var attempt = 1; attempt < 4 && result.PreserveExistingFile && result.Warning == WarningReadFailed && File.Exists(path); attempt++)
        {
            Thread.Sleep(150);
            result = LoadCore(path, maxBytes);
        }
        // v2.3.1 (L1): note.json unreadable and already moved to .bak → open the last good copy (note.json.prev)
        // instead of starting an empty note. The .prev file itself is never moved or changed here.
        if (result.Warning == WarningCorrupt && File.Exists(PrevPath(path)) && !File.Exists(path))
        {
            try
            {
                var tmpCopy = path + ".prev-open.tmp";
                File.Copy(PrevPath(path), tmpCopy, overwrite: true);
                var prev = LoadCore(tmpCopy, maxBytes);
                try { File.Delete(tmpCopy); } catch { /* ignore */ }
                if (prev.Warning != WarningCorrupt && !prev.PreserveExistingFile)
                {
                    AppLog.Write("load", "note.json unreadable → opened note.json.prev (last good save)");
                    prev.Warning = Localizer.T("load.opened_prev");
                    return prev;
                }
            }
            catch (Exception ex) { AppLog.Write("load", "note.json.prev fallback failed: " + ex.Message); }
        }
        return result;
    }

    private static LoadResult LoadCore(string path, int maxBytes)
    {
        if (!File.Exists(path))
            return new LoadResult { Document = NoteDocument.CreateDefault() };

        try
        {
            // D2/P5: over the 8 MB cap → full copy to note.overlimit-*.bak, then open what fits. Never start a new note.
            var oversize = new FileInfo(path).Length > maxBytes;
            var preserveForEncoding = false;

            LastLoadIssues.Clear();
            // v2.4.1: File.ReadAllText silently turned non-UTF-8 bytes (e.g. a note.json re-saved as Thai
            // ANSI / Windows-874 by an old Notepad) into U+FFFD, and the next autosave overwrote the original.
            var text = ReadNoteText(path, out var legacyEncoding);
            string? encodingNotice = null;
            if (legacyEncoding)
            {
                var encBak = EncodingBackup(path);
                AppLog.Write("load", $"note.json is not UTF-8 → read as Windows-874, original copied to {encBak ?? "FAILED"}");
                // No copy → never overwrite the only original (PreserveExistingFile).
                if (encBak == null) preserveForEncoding = true;
                else encodingNotice = Localizer.T("load.legacy_encoding", Path.GetFileName(encBak));
            }
            using var json = JsonDocument.Parse(text,
                new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new JsonException("root is not an object");

            // Core table data: the only part whose unreadability makes the file "corrupt".
            var columnsEl = Prop(root, "columns");
            if (columnsEl == null || columnsEl.Value.ValueKind == JsonValueKind.Null)
            {
                if (BackupBrokenFile(path))
                    return new LoadResult { Document = NoteDocument.CreateDefault(), Warning = WarningCorrupt };
                return new LoadResult
                {
                    Document = NoteDocument.CreateDefault(), PreserveExistingFile = true,
                    Warning = Localizer.T("load.unreadable_kept")
                };
            }
            if (columnsEl.Value.ValueKind != JsonValueKind.Array) throw new JsonException("columns is not an array");
            var rowsEl = Prop(root, "rows");
            if (rowsEl is { ValueKind: not (JsonValueKind.Array or JsonValueKind.Null) }) throw new JsonException("rows is not an array");

            var columns = columnsEl.Value.EnumerateArray().Select(c => CellText(c, "columns")).ToList();
            var rows = new List<List<string>>();
            if (rowsEl is { ValueKind: JsonValueKind.Array })
                foreach (var r in rowsEl.Value.EnumerateArray())
                {
                    if (r.ValueKind == JsonValueKind.Array) rows.Add(r.EnumerateArray().Select(c => CellText(c, "rows")).ToList());
                    else { LastLoadIssues.Add($"rows: a row is not an array ({r.ValueKind}), using an empty row"); rows.Add([]); }
                }

            // Everything else: tolerant, field by field, default on a bad type.
            var version = GetInt(root, "version", 1, allowString: true);
            if (version <= 0) version = 1;
            var win = Prop(root, "window") is { ValueKind: JsonValueKind.Object } w ? w : (JsonElement?)null;
            if (Prop(root, "window") is { } wBad && wBad.ValueKind is not (JsonValueKind.Object or JsonValueKind.Null))
                LastLoadIssues.Add("window: not an object, using defaults");
            var layer = DesktopPlacement.ParseLayer(win is { } w1 ? GetString(w1, "layer", "window.") : null);

            var doc = new NoteDocument
            {
                Version = version,
                Columns = columns,
                Rows = rows,
                LaunchAtLogin = GetBool(root, "launchAtLogin", false),
                TrayNoticeShown = GetBool(root, "trayNoticeShown", false),
                ColumnWidths = GetIntList(root, "columnWidths"),
                View = RecoveryGate.Parse(GetString(root, "viewMode")),
                HideDone = GetBool(root, "hideDone", false),
                Snoozes = StringMap(Prop(root, "snoozes"), "snoozes"),
                Recurrence = StringMap(Prop(root, "recurrence"), "recurrence"),
                RowIds = GetRowIds(root, rows.Count),
                ColumnIds = GetColumnIds(root, columns.Count),
                ColumnRolesList = GetStringList(root, "columnRoles"),
                MuteSound = GetBool(root, "muteSound", false),
                Acknowledged = StringMap(Prop(root, "acknowledged"), "acknowledged"),
                Reminders = ParseReminders(Prop(root, "reminders")),
                ColumnSettings = ParseColumnSettings(Prop(root, "columnSettings")), // v2.4.0
                RowStyles = ParseRowStyles(Prop(root, "rowStyles")),
                Window = new WindowMemory
                {
                    Width = win is { } a ? GetInt(a, "width", 720, true, "window.") : 720,
                    Height = win is { } b ? GetInt(b, "height", 480, true, "window.") : 480,
                    X = win is { } c ? GetNullableInt(c, "x") : null,
                    Y = win is { } d ? GetNullableInt(d, "y") : null,
                    Layer = layer,
                    Locked = win is { } e && GetBool(e, "locked", false, "window."),
                    Topmost = DesktopPlacement.EffectiveTopmost(layer)
                }
            };
            // Keep fields this version doesn't know (e.g. from a newer TableNote) so saving doesn't drop them.
            foreach (var prop in root.EnumerateObject())
                if (!KnownFields.Contains(prop.Name))
                    doc.ExtraFields[prop.Name] = prop.Value.Clone();
            if (version > NoteDocument.CurrentVersion)
                LastLoadIssues.Add($"version {version}: file comes from a newer TableNote, will back up to note.v{version}.bak before saving and keep unknown fields ({doc.ExtraFields.Count} fields)");

            // Status: map standard labels to the dropdown values (others kept as typed).
            var stCol = doc.StatusColumnIndex();
            if (stCol >= 0)
                foreach (var r in doc.Rows)
                    if (stCol < r.Count) r[stCol] = TaskStatuses.CanonicalOnLoad(r[stCol]);

            // Recurrence: store language-neutral codes (old Thai / English labels → none/daily/weekly/monthly:N).
            foreach (var k in doc.Recurrence.Keys.ToList())
            {
                var n = Recurrence.NormalizeKind(doc.Recurrence[k]);
                if (n == Recurrence.MonthlyPrefix + "0")
                {
                    var ri = doc.RowIds.IndexOf(k); var dc = doc.DueColumnIndex();
                    n = ri >= 0 && dc >= 0 && DueDates.TryParse(doc.GetCell(ri, dc), out var dd) ? Recurrence.MonthlyPrefix + dd.Day : n;
                }
                if (n != Recurrence.None) doc.Recurrence[k] = n;
                else if (Recurrence.IsExplicitNone(doc.Recurrence[k])) doc.Recurrence.Remove(k);
                // else: unknown text (e.g. "monthly:45", "ทุก 2 วัน") is kept as written and counts as no repeat (L33)
            }

            // Never drop data silently: back up the FULL file before trimming anything to the limits.
            string? overflowNotice = null;
            if (oversize || doc.Rows.Count > TableLimits.MaxRows || doc.Columns.Count > NoteDocument.AbsoluteMaxColumns ||
                doc.Rows.Any(r => r.Count > NoteDocument.AbsoluteMaxColumns || r.Any(c => c.Length > TableLimits.MaxCellChars)))
            {
                var bak = OverflowBackup(path);
                AppLog.Write("load", $"over limits ({doc.Rows.Count} rows, {doc.Columns.Count} cols) → backup {bak ?? "FAILED"}");
                if (bak == null)
                    return new LoadResult
                    {
                        Document = doc, PreserveExistingFile = true,
                        Warning = Localizer.T("load.over_limits_kept")
                    };
                overflowNotice = oversize && doc.Rows.Count <= TableLimits.MaxRows
                    ? Localizer.T("load.over_cap", Path.GetFileName(bak))
                    : doc.Rows.Count > TableLimits.MaxRows
                    ? TableLimits.OverflowNotice(doc.Rows.Count, Path.GetFileName(bak))
                    : Localizer.T("load.over_limits_trimmed", Path.GetFileName(bak));
            }
            var clipped = doc.Normalize();
            if (overflowNotice != null) { clipped = false; }
            ReminderPlan.Sanitize(doc); // bad reminder values dropped individually, never .bak
            ColumnSettingsOps.PruneOrphans(doc); // v2.4.0: settings/styles of gone columns/rows
            LogIssues(path);
            var issues = LastLoadIssues.ToList();
            var reminderIssue = issues.Any(i => i.StartsWith("reminders", StringComparison.Ordinal));
            return new LoadResult
            {
                Document = doc,
                Issues = issues,
                PreserveExistingFile = preserveForEncoding,
                Notice = issues.Count == 0 ? null :
                    Localizer.T("load.bad_values", issues.Count, reminderIssue ? Localizer.T("load.bad_values_reminders") : ""),
                Warning = overflowNotice ?? encodingNotice ?? (preserveForEncoding
                    ? Localizer.T("load.legacy_encoding_kept")
                    : null) ?? (clipped
                    ? Localizer.T("load.clipped", TableLimits.MaxRows, TableLimits.MaxColumns, TableLimits.MaxCellChars)
                    : null)
            };
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException or InvalidOperationException)
        {
            if (BackupBrokenFile(path))
                return new LoadResult { Document = NoteDocument.CreateDefault(), Warning = WarningCorrupt };
            return new LoadResult
            {
                Document = NoteDocument.CreateDefault(), PreserveExistingFile = true,
                Warning = Localizer.T("load.unreadable_kept")
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new LoadResult
            {
                Document = NoteDocument.CreateDefault(), PreserveExistingFile = true,
                Warning = WarningReadFailed
            };
        }
        catch (Exception ex)
        {
            // v2.3.1 (L2): any other error (e.g. KeyNotFound / IndexOutOfRange / Format from Normalize, Sanitize,
            // Recurrence) used to escape into the MainWindow constructor → the app closed at start-up silently.
            // Keep the original file untouched (COPY, not move) and start a new note that saves normally.
            AppLog.Write("load", $"unexpected {ex.GetType().Name}: {ex.Message}");
            string? copy = null;
            try
            {
                copy = path + "." + DateTime.Now.ToString("yyyyMMddHHmmss") + ".bak";
                for (var i = 1; File.Exists(copy); i++) copy = path + "." + DateTime.Now.ToString("yyyyMMddHHmmss") + "-" + i + ".bak";
                File.Copy(path, copy, overwrite: false);
            }
            catch { copy = null; }
            if (copy != null)
                return new LoadResult { Document = NoteDocument.CreateDefault(), Warning = WarningCorrupt };
            return new LoadResult
            {
                Document = NoteDocument.CreateDefault(), PreserveExistingFile = true,
                Warning = Localizer.T("load.unreadable_kept")
            };
        }
    }

    /// <summary>Warning of LoadCore's IOException/UnauthorizedAccess branch (must match the text there).</summary>
    private static string WarningReadFailed => Localizer.T("load.read_failed");

    /// <summary>Side file used while note.json must not be overwritten (<see cref="LoadResult.PreserveExistingFile"/>).</summary>
    public static string UnsavedPath(string path) => Path.Combine(Path.GetDirectoryName(path) ?? ".", "note.unsaved.json");

    /// <summary>
    /// v2.4.1: save the session to note.unsaved.json (same atomic write as note.json, never touches note.json).
    /// Used when note.json is kept as-is because it could not be read/backed up: before, every edit of that session
    /// was dropped silently (DoSave returned "ok" without writing anything, and quitting did not warn).
    /// </summary>
    public static string SaveSideCopy(string path, NoteDocument doc)
    {
        var side = UnsavedPath(path);
        Save(side, doc); // full document (same Normalize as a normal save)
        return side;
    }

    /// <summary>Thrown by <see cref="Save"/> when the file would exceed the 8 MB cap; note.json is not touched.</summary>
    public sealed class TooLargeException(long bytes, string? copy) : IOException(Localizer.T("save.too_large", copy ?? "-"))
    {
        public long Bytes { get; } = bytes;
        public string? CopyPath { get; } = copy;
    }

    /// <summary>Last known-good copy of note.json, kept by every save (see <see cref="WriteDurably"/>).</summary>
    public static string PrevPath(string path) => path + ".prev";

    /// <summary>
    /// v2.3.1 (L1): write the temp file and flush it to the DISK (not just the OS cache) before the rename, then
    /// atomically replace note.json keeping the previous good file as note.json.prev. Before, WriteAllBytes + Move
    /// without a flush could leave an empty/garbled note.json after a power loss or a crash of Windows.
    /// </summary>
    private static void WriteDurably(string tmp, string path, byte[] bytes)
    {
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            fs.Write(bytes, 0, bytes.Length);
            fs.Flush(flushToDisk: true);
        }
        if (File.Exists(path))
        {
            try { File.Replace(tmp, path, PrevPath(path), ignoreMetadataErrors: true); return; }
            catch (Exception ex) when (ex is IOException or PlatformNotSupportedException or UnauthorizedAccessException)
            {
                // e.g. FAT/network drive without ReplaceFile: keep a .prev copy, then the plain atomic move.
                AppLog.Write("save", "File.Replace failed (" + ex.Message + ") → copy .prev + move");
                try { File.Copy(path, PrevPath(path), overwrite: true); } catch { /* best effort */ }
            }
        }
        File.Move(tmp, path, overwrite: true);
    }

    private static DateTime _lastTooLargeCopy = DateTime.MinValue;
    private static string? _lastSavedPath;
    private static byte[]? _lastSavedBytes;
    private static string? _lastTooLargeCopyName;

    /// <summary>Saves note.json atomically and returns its size in bytes. Over <see cref="TableLimits.MaxSaveFileBytes"/>
    /// (8,388,608 bytes) nothing is written to note.json (P5b); the unsaved table is copied to note.overlimit-*.bak
    /// (at most once per 5 minutes) and <see cref="TooLargeException"/> is thrown.</summary>
    public static long Save(string path, NoteDocument doc)
    {
        doc.Normalize();
        TryBackupBeforeV3(path, doc);
        var dto = new FileDto
        {
            // Never downgrade a file written by a newer TableNote (its unknown fields are kept in ExtraFields).
            Version = Math.Max(NoteDocument.CurrentVersion, doc.Version),
            Columns = doc.Columns.Select(c => (string?)c).ToList(),
            Rows = doc.Rows.Select(r => r.Select(c => (string?)c).ToList()!).ToList()!,
            LaunchAtLogin = doc.LaunchAtLogin,
            TrayNoticeShown = doc.TrayNoticeShown,
            ColumnWidths = doc.ColumnWidths,
            ViewMode = RecoveryGate.Format(doc.View),
            HideDone = doc.HideDone,
            Snoozes = JsonSerializer.SerializeToElement(doc.Snoozes, Options),
            Recurrence = JsonSerializer.SerializeToElement(doc.Recurrence, Options),
            RowIds = doc.RowIds.Select(id => (string?)id).ToList(),
            ColumnIds = doc.ColumnIds.Select(id => (string?)id).ToList(),
            ColumnRoles = doc.ColumnRolesList.Select(r => (string?)r).ToList(),
            MuteSound = doc.MuteSound,
            Acknowledged = JsonSerializer.SerializeToElement(doc.Acknowledged, Options),
            Reminders = JsonSerializer.SerializeToElement(doc.Reminders, Options),
            ColumnSettings = JsonSerializer.SerializeToElement(doc.ColumnSettings, Options), // v2.4.0
            RowStyles = JsonSerializer.SerializeToElement(doc.RowStyles, Options),
            Window = new WindowDto
            {
                Width = doc.Window.Width,
                Height = doc.Window.Height,
                X = doc.Window.X,
                Y = doc.Window.Y,
                Layer = DesktopPlacement.FormatLayer(doc.Window.Layer),
                Locked = doc.Window.Locked,
                Topmost = DesktopPlacement.EffectiveTopmost(doc.Window.Layer)
            }
        };

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        string json;
        if (doc.ExtraFields.Count == 0) json = JsonSerializer.Serialize(dto, Options);
        else
        {
            // Preserve unknown top-level fields (from a newer version) instead of dropping them silently.
            var node = (System.Text.Json.Nodes.JsonObject)System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(dto, Options))!;
            foreach (var (k, v) in doc.ExtraFields)
                if (!node.ContainsKey(k)) node[k] = System.Text.Json.Nodes.JsonNode.Parse(v.GetRawText());
            json = node.ToJsonString(Options);
        }
        var bytes = new UTF8Encoding(false).GetBytes(json);
        if (bytes.Length > TableLimits.MaxSaveFileBytes)
        {
            string? copy = _lastTooLargeCopyName;
            if (DateTime.UtcNow - _lastTooLargeCopy > TimeSpan.FromMinutes(5) || copy == null)
            {
                try
                {
                    var d = string.IsNullOrEmpty(dir) ? "." : dir;
                    var name = Path.Combine(d, $"note.overlimit-{DateTime.Now:yyyyMMdd-HHmmss}.bak");
                    for (var i = 1; File.Exists(name); i++) name = Path.Combine(d, $"note.overlimit-{DateTime.Now:yyyyMMdd-HHmmss}-{i}.bak");
                    File.WriteAllBytes(name, bytes);
                    copy = Path.GetFileName(name); _lastTooLargeCopyName = copy; _lastTooLargeCopy = DateTime.UtcNow;
                }
                catch (Exception ex) { AppLog.Write("save", "over-cap copy failed: " + ex.Message); copy = null; }
            }
            AppLog.Write("save", $"not saved: {bytes.Length} bytes > {TableLimits.MaxSaveFileBytes}; copy {copy ?? "FAILED"}");
            throw new TooLargeException(bytes.Length, copy);
        }
        // v2.3.1: skip the write when the bytes equal the last successful save of this file and the file
        // is still there with that size (window move / tab-through / no-op edits used to rewrite note.json).
        var full = Path.GetFullPath(path);
        if (_lastSavedPath == full && _lastSavedBytes != null && bytes.AsSpan().SequenceEqual(_lastSavedBytes))
        {
            try { if (new FileInfo(path) is { Exists: true } fi && fi.Length == bytes.Length) return bytes.Length; }
            catch { /* fall through and write */ }
        }
        var tmp = path + ".tmp";
        try
        {
            // v2.3.1 (L15): OneDrive / antivirus may hold the file for a moment → retry twice before failing.
            for (var attempt = 0; ; attempt++)
            {
                try { WriteDurably(tmp, path, bytes); break; }
                catch (IOException) when (attempt < 2) { Thread.Sleep(120); }
            }
            _lastSavedPath = full;
            _lastSavedBytes = bytes;
        }
        catch
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* ignore */ }
            throw;
        }
        _lastTooLargeCopyName = null;
        return bytes.Length;
    }


    /// <summary>
    /// Before the first save in the new format, keep a copy of the older file as note.v2.bak
    /// (older builds would drop fields they don't know, e.g. "reminders").
    /// </summary>
    private static void TryBackupBeforeV3(string path, NoteDocument doc)
    {
        var full = "";
        try
        {
            if (!File.Exists(path)) return;
            full = Path.GetFullPath(path);
            // Check each file once per run (not on every autosave) — but only once the backup really exists.
            if (BackupChecked.Contains(full)) return;
            var fileVersion = ReadFileVersion(path);
            // Older AND newer files are backed up before we write our version over them.
            if (fileVersion == NoteDocument.CurrentVersion) { BackupChecked.Add(full); return; }
            var bak = BackupPathForVersion(path, fileVersion);
            if (bak != null)
            {
                try { File.Copy(path, bak, overwrite: false); }
                catch (Exception first) when (first is IOException or UnauthorizedAccessException)
                {
                    // v2.4.1: the name was unusable → timestamped name. Before, the failure was swallowed,
                    // the file was marked "checked" and the old-version file was overwritten (gone after 2 saves).
                    var dir = Path.GetDirectoryName(path) ?? ".";
                    var alt = Path.Combine(dir, $"note.v{Math.Max(2, fileVersion)}.{DateTime.Now:yyyyMMddHHmmss}.bak");
                    for (var i = 1; File.Exists(alt) || Directory.Exists(alt); i++)
                        alt = Path.Combine(dir, $"note.v{Math.Max(2, fileVersion)}.{DateTime.Now:yyyyMMddHHmmss}-{i}.bak");
                    AppLog.Write("save", $"version backup {Path.GetFileName(bak)} failed ({first.Message}) → {Path.GetFileName(alt)}");
                    File.Copy(path, alt, overwrite: false);
                }
            }
            BackupChecked.Add(full);
        }
        catch (Exception ex)
        {
            // Non-fatal (saving is still better than not saving), but logged and retried on the next save.
            AppLog.Write("save", "version backup before save failed (retry next save): " + ex.Message);
        }
    }

    /// <summary>
    /// Backup name by the OLD file's version: v1/v2 → note.v2.bak (historic name), v3 → note.v3.bak, …
    /// Never overwrites: if the name is taken by a different file, a timestamped name is used;
    /// if it already holds identical bytes, null (nothing to do).
    /// </summary>
    public static string? BackupPathForVersion(string sourcePath, int fileVersion)
    {
        var dir = Path.GetDirectoryName(sourcePath) ?? ".";
        var v = Math.Max(2, fileVersion);
        var bak = Path.Combine(dir, $"note.v{v}.bak");
        if (!File.Exists(bak)) return bak;
        // A newer-version file is kept as version N after our save, so back it up only once (no copy per launch).
        if (fileVersion > NoteDocument.CurrentVersion) return null;
        var src = sourcePath;
        try
        {
            if (File.Exists(src) && File.ReadAllBytes(src).AsSpan().SequenceEqual(File.ReadAllBytes(bak))) return null;
        }
        catch { /* fall through to timestamped */ }
        var stamped = Path.Combine(dir, $"note.v{v}.{DateTime.Now:yyyyMMddHHmmss}.bak");
        for (var i = 1; File.Exists(stamped); i++)
            stamped = Path.Combine(dir, $"note.v{v}.{DateTime.Now:yyyyMMddHHmmss}-{i}.bak");
        return stamped;
    }

    private static readonly HashSet<string> BackupChecked = new(StringComparer.Ordinal);

    /// <summary>Test hook: forget which files were already checked for a pre-upgrade backup.</summary>
    public static void ResetBackupCheck() => BackupChecked.Clear();

    private static int ReadFileVersion(string path)
    {
        var fileVersion = 1;
        using var json = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        if (json.RootElement.ValueKind != JsonValueKind.Object) return fileVersion;
        foreach (var prop in json.RootElement.EnumerateObject())
            if (string.Equals(prop.Name, "version", StringComparison.OrdinalIgnoreCase))
            {
                if (prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetInt32(out var v)) fileVersion = v;
                else if (prop.Value.ValueKind == JsonValueKind.String && int.TryParse(prop.Value.GetString(), out var sv)) fileVersion = sv;
            }
        return fileVersion;
    }

    private static bool BackupBrokenFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return false;
            var bak = path + ".bak";
            if (File.Exists(bak))
                bak = path + "." + DateTime.Now.ToString("yyyyMMddHHmmss") + ".bak";
            for (var i = 1; File.Exists(bak); i++) // several broken files within one second must not collide
                bak = path + "." + DateTime.Now.ToString("yyyyMMddHHmmss") + "-" + i + ".bak";
            File.Move(path, bak, overwrite: false);
            BackupRetention.Prune(Path.GetDirectoryName(path) ?? ".");
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
}
