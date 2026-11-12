namespace TableNote;

/// <summary>In-memory note table. v5 adds stable column ids. v4 adds reminders. v3 adds column roles.</summary>
public sealed class NoteDocument
{
    /// <summary>5 = 2.4.0 (stable columnIds). 4 = 2.2.0 (adds "reminders"). 3 = 2.1.0 (column roles).</summary>
    public const int CurrentVersion = 5;
    public const int DefaultRowCount = 4;
    /// <summary>User-editable column cap; system roles may push AbsoluteMaxColumns.</summary>
    public const int MaxUserColumns = 30;
    public const int AbsoluteMaxColumns = 32;

    // Thai default headers of notes made by ≤ 2.4.1. Kept ONLY to recognise columns in existing files
    // (role detection by name); new notes take their headers from DefaultColumnNames() in the UI language.
    public const string ColTitle = "หัวข้อ";
    public const string ColDetail = "รายละเอียด";
    public const string ColStatus = "สถานะ";
    public const string ColDue = "กำหนดส่ง";

    public static readonly string[] DefaultColumns = [ColTitle, ColDetail, ColStatus, ColDue];

    static readonly string[] DefaultNameKeys = ["defcol.title", "defcol.detail", "defcol.status", "defcol.due"];

    /// <summary>v2.4.2: headers for a NEW note in the current UI language (title, detail, status, due).
    /// Existing files are never renamed.</summary>
    public static string[] DefaultColumnNames() => DefaultNameKeys.Select(k => Localizer.T(k)).ToArray();

    /// <summary>v2.4.2: header for a newly added column of <paramref name="role"/> in the current UI language.</summary>
    public static string DefaultColumnName(string role) =>
        DefaultColumnNames()[Math.Max(0, Array.FindIndex(DefaultRoles, r => r == role))];

    /// <summary>v2.4.2: role whose default header (2.4.1 Thai, or any embedded language) is <paramref name="name"/>.</summary>
    public static string? RoleOfDefaultName(string name)
    {
        var n = (name ?? "").Trim();
        if (n.Length == 0) return null;
        for (var i = 0; i < DefaultRoles.Length; i++)
        {
            if (n == DefaultColumns[i]) return DefaultRoles[i];
            foreach (var code in Localizer.Supported)
                if (Localizer.Table(code).TryGetValue(DefaultNameKeys[i], out var v) && string.Equals(n, v.Trim(), StringComparison.OrdinalIgnoreCase))
                    return DefaultRoles[i];
        }
        return null;
    }

    /// <summary>Index of a column whose header is a default name for <paramref name="role"/> (any language), or -1.</summary>
    public int IndexOfDefaultName(string role) => Columns.FindIndex(c => RoleOfDefaultName(c ?? "") == role);
    public static readonly string[] DefaultRoles =
        [ColumnRoles.Title, ColumnRoles.Detail, ColumnRoles.Status, ColumnRoles.Due];

    public int Version { get; set; } = CurrentVersion;
    public List<string> Columns { get; set; } = new(DefaultColumns);
    /// <summary>Parallel to Columns: title/detail/status/due or empty.</summary>
    public List<string> ColumnRolesList { get; set; } = new(DefaultRoles);
    /// <summary>Stable ids parallel to Columns (survive header renames and column moves). Missing ids are generated on load.</summary>
    public List<string> ColumnIds { get; set; } = [];
    /// <summary>v2.4.0: column type/options by column id ("columnSettings"). No entry = text column.</summary>
    public Dictionary<string, ColumnSetting> ColumnSettings { get; set; } = new(StringComparer.Ordinal);
    /// <summary>v2.4.0: row colour/highlight by row id ("rowStyles"). No entry = normal row.</summary>
    public Dictionary<string, RowStyle> RowStyles { get; set; } = new(StringComparer.Ordinal);
    public List<List<string>> Rows { get; set; } = [];
    public WindowMemory Window { get; set; } = new();
    public bool LaunchAtLogin { get; set; }
    /// <summary>Top-level JSON fields this version doesn't know (e.g. from a newer TableNote); written back unchanged.</summary>
    public Dictionary<string, System.Text.Json.JsonElement> ExtraFields { get; set; } = new(StringComparer.Ordinal);
    /// <summary>One-time "closing hides to tray" notice was shown.</summary>
    public bool TrayNoticeShown { get; set; }
    public List<int> ColumnWidths { get; set; } = [];
    public ViewMode View { get; set; }
    public bool HideDone { get; set; }
    public bool MuteSound { get; set; }
    public Dictionary<string, string> Snoozes { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> Recurrence { get; set; } = new(StringComparer.Ordinal);
    /// <summary>Advance reminders per row id (offsets in minutes + acknowledged fires).</summary>
    public Dictionary<string, ReminderInfo> Reminders { get; set; } = new(StringComparer.Ordinal);
    /// <summary>Legacy (2.1.0) rowId → acknowledged due. Read for compatibility; new acks go to Reminders.</summary>
    public Dictionary<string, string> Acknowledged { get; set; } = new(StringComparer.Ordinal);
    public List<string> RowIds { get; set; } = [];
    /// <summary>Set when due column could not be ensured (e.g. hard failure).</summary>
    public string? DueColumnWarning { get; set; }

    public static NoteDocument CreateDefault()
    {
        var doc = new NoteDocument
        {
            Columns = DefaultColumnNames().ToList(),
            ColumnRolesList = DefaultRoles.ToList(),
            Window = new WindowMemory { Topmost = false, Layer = DesktopLayerKind.Desktop, Width = 780, Height = 520 },
            // v2.3.1: explicit default widths (title, detail, status 120, due 200) — see the L8 note in Normalize().
            ColumnWidths = [TableLimits.DefaultColumnWidth, TableLimits.DefaultColumnWidth, 120, 200]
        };
        for (var i = 0; i < DefaultRowCount; i++)
            doc.Rows.Add(BlankRow(doc.Columns.Count));
        doc.EnsureRowIds();
        doc.EnsureColumnIds();
        return doc;
    }

    public static bool HasText(string? value) => !string.IsNullOrWhiteSpace(value);

    public bool RowHasContent(int row) =>
        row >= 0 && row < Rows.Count && Rows[row].Any(HasText);

    public bool ColumnHasContent(int column)
    {
        if (column < 0 || column >= Columns.Count) return false;
        if (HasText(Columns[column])) return true;
        return Rows.Any(r => column < r.Count && HasText(r[column]));
    }

    public int IndexOfColumn(string name) =>
        Columns.FindIndex(c => string.Equals(c, name, StringComparison.Ordinal));

    public int IndexOfRole(string role)
    {
        EnsureColumnRoles();
        return ColumnRolesList.FindIndex(r => string.Equals(r, role, StringComparison.OrdinalIgnoreCase));
    }

    public int StatusColumnIndex()
    {
        var byRole = IndexOfRole(ColumnRoles.Status);
        if (byRole >= 0) return byRole;
        return IndexOfDefaultName(ColumnRoles.Status);
    }

    public int DueColumnIndex()
    {
        var byRole = IndexOfRole(ColumnRoles.Due);
        if (byRole >= 0) return byRole;
        return IndexOfDefaultName(ColumnRoles.Due);
    }

    public string GetCell(int row, int col)
    {
        if (row < 0 || row >= Rows.Count || col < 0) return "";
        var r = Rows[row];
        return col < r.Count ? r[col] ?? "" : "";
    }

    public void SetCellRaw(int row, int col, string value)
    {
        if (row < 0 || row >= Rows.Count || col < 0 || col >= Columns.Count) return;
        while (Rows[row].Count < Columns.Count) Rows[row].Add("");
        Rows[row][col] = value;
    }

    public string RowIdAt(int row)
    {
        EnsureRowIds();
        if (row < 0 || row >= RowIds.Count) return Guid.NewGuid().ToString("N");
        return RowIds[row];
    }

    public void EnsureRowIds()
    {
        while (RowIds.Count < Rows.Count)
            RowIds.Add(Guid.NewGuid().ToString("N"));
        if (RowIds.Count > Rows.Count)
            RowIds.RemoveRange(Rows.Count, RowIds.Count - Rows.Count);
    }

    /// <summary>Keep ColumnIds the same length as Columns; new slots get a fresh id.</summary>
    public void EnsureColumnIds()
    {
        while (ColumnIds.Count < Columns.Count)
            ColumnIds.Add(Guid.NewGuid().ToString("N"));
        if (ColumnIds.Count > Columns.Count)
            ColumnIds.RemoveRange(Columns.Count, ColumnIds.Count - Columns.Count);
    }

    public string ColumnIdAt(int column)
    {
        EnsureColumnIds();
        if (column < 0 || column >= ColumnIds.Count) return Guid.NewGuid().ToString("N");
        return ColumnIds[column];
    }

    public void EnsureColumnRoles()
    {
        while (ColumnRolesList.Count < Columns.Count)
        {
            var idx = ColumnRolesList.Count;
            var inferred = ColumnRoles.InferFromName(Columns[idx]);
            // Avoid duplicate roles when inferring
            if (inferred != null && ColumnRolesList.Any(r => string.Equals(r, inferred, StringComparison.OrdinalIgnoreCase)))
                inferred = null;
            ColumnRolesList.Add(inferred ?? "");
        }
        if (ColumnRolesList.Count > Columns.Count)
            ColumnRolesList.RemoveRange(Columns.Count, ColumnRolesList.Count - Columns.Count);
    }

    public bool InsertRowAfter(int rowIndex)
    {
        if (!AppendRow()) return false;
        var last = Rows.Count - 1;
        var at = rowIndex < 0 || rowIndex >= last ? last : rowIndex + 1;
        if (at != last)
        {
            var row = Rows[last];
            var id = RowIds[last];
            Rows.RemoveAt(last);
            RowIds.RemoveAt(last);
            Rows.Insert(at, row);
            RowIds.Insert(at, id);
        }
        return true;
    }

    public bool AppendRow()
    {
        if (Rows.Count >= TableLimits.MaxRows) return false;
        Rows.Add(BlankRow(Columns.Count));
        RowIds.Add(Guid.NewGuid().ToString("N"));
        return true;
    }

    public bool RemoveRow(int index)
    {
        if (index < 0 || index >= Rows.Count) return false;
        var id = RowIdAt(index);
        Rows.RemoveAt(index);
        if (index < RowIds.Count) RowIds.RemoveAt(index);
        ReminderPlan.ClearSnoozes(this, id);
        Recurrence.Remove(id);
        Acknowledged.Remove(id);
        Reminders.Remove(id);
        return true;
    }

    public bool AddColumn(string? name = null, string? role = null)
    {
        var isSystem = role is ColumnRoles.Status or ColumnRoles.Due;
        var cap = isSystem ? AbsoluteMaxColumns : MaxUserColumns;
        // When adding a user column, still respect AbsoluteMax if system already filled extras
        if (!isSystem && Columns.Count >= MaxUserColumns) return false;
        if (Columns.Count >= AbsoluteMaxColumns) return false;
        if (isSystem && Columns.Count >= AbsoluteMaxColumns) return false;

        EnsureColumnRoles();
        EnsureColumnIds();
        Columns.Add(string.IsNullOrEmpty(name) ? NextColumnName() : name!);
        ColumnRolesList.Add(role ?? "");
        ColumnIds.Add(Guid.NewGuid().ToString("N"));
        foreach (var row in Rows) row.Add("");
        return true;
    }

    public bool RemoveColumn(int index)
    {
        if (Columns.Count <= 1 || index < 0 || index >= Columns.Count) return false;
        EnsureColumnRoles();
        EnsureColumnIds();
        // The due column is protected: reminders depend on it (DeletePlanner shows the Thai message).
        if (index == DueColumnIndex()) return false;
        if (index < ColumnIds.Count) this.OnColumnRemoved(ColumnIds[index]); // drop its settings (undo restores via snapshot)
        Columns.RemoveAt(index);
        if (index < ColumnRolesList.Count) ColumnRolesList.RemoveAt(index);
        if (index < ColumnWidths.Count) ColumnWidths.RemoveAt(index);
        if (index < ColumnIds.Count) ColumnIds.RemoveAt(index);
        foreach (var row in Rows)
            if (index < row.Count) row.RemoveAt(index);
        return true;
    }

    public string NextColumnName()
    {
        var used = new HashSet<string>(Columns);
        for (var i = 1; i < 10000; i++)
        {
            var name = Localizer.T("defcol.numbered", i);
            if (!used.Contains(name)) return name;
        }
        return Localizer.T("defcol.plain");
    }

    /// <summary>Ensure status + due exist by stable role; may exceed 30 up to AbsoluteMaxColumns.</summary>
    public void EnsureReminderColumns()
    {
        DueColumnWarning = null;
        EnsureColumnRoles();

        if (IndexOfRole(ColumnRoles.Status) < 0 && IndexOfDefaultName(ColumnRoles.Status) < 0)
        {
            if (!AddColumn(DefaultColumnName(ColumnRoles.Status), ColumnRoles.Status))
                DueColumnWarning = Localizer.T("load.no_status_column");
            else
                EnsureColumnRoles();
        }
        else if (IndexOfRole(ColumnRoles.Status) < 0)
        {
            var i = IndexOfDefaultName(ColumnRoles.Status);
            if (i >= 0) { EnsureColumnRoles(); ColumnRolesList[i] = ColumnRoles.Status; }
        }

        if (IndexOfRole(ColumnRoles.Due) < 0 && IndexOfDefaultName(ColumnRoles.Due) < 0)
        {
            if (!AddColumn(DefaultColumnName(ColumnRoles.Due), ColumnRoles.Due))
                DueColumnWarning = Localizer.T("load.no_due_column_full");
            else
                EnsureColumnRoles();
        }
        else if (IndexOfRole(ColumnRoles.Due) < 0)
        {
            var i = IndexOfDefaultName(ColumnRoles.Due);
            if (i >= 0) { EnsureColumnRoles(); ColumnRolesList[i] = ColumnRoles.Due; }
        }

        // Seed title/detail roles if still blank and names match defaults
        for (var i = 0; i < Columns.Count; i++)
        {
            if (!string.IsNullOrEmpty(ColumnRolesList[i])) continue;
            var inferred = ColumnRoles.InferFromName(Columns[i]);
            if (inferred == null) continue;
            if (ColumnRolesList.Any(r => string.Equals(r, inferred, StringComparison.OrdinalIgnoreCase))) continue;
            ColumnRolesList[i] = inferred;
        }

        if (DueColumnIndex() < 0 && string.IsNullOrEmpty(DueColumnWarning))
            DueColumnWarning = Localizer.T("load.no_due_column");
    }

    public bool Normalize()
    {
        var clipped = false;
        if (Columns.Count == 0)
        {
            Columns.AddRange(DefaultColumnNames());
            ColumnRolesList = DefaultRoles.ToList();
            ColumnIds = [];
        }

        EnsureReminderColumns();

        if (Columns.Count > AbsoluteMaxColumns)
        {
            Columns.RemoveRange(AbsoluteMaxColumns, Columns.Count - AbsoluteMaxColumns);
            clipped = true;
        }
        EnsureColumnRoles();

        for (var i = 0; i < Columns.Count; i++)
        {
            Columns[i] ??= "";
            if (Columns[i].Length > TableLimits.MaxCellChars)
            {
                Columns[i] = TableLimits.Clip(Columns[i], TableLimits.MaxCellChars);
                clipped = true;
            }
        }

        if (Rows.Count > TableLimits.MaxRows)
        {
            Rows.RemoveRange(TableLimits.MaxRows, Rows.Count - TableLimits.MaxRows);
            clipped = true;
        }

        for (var j = 0; j < Rows.Count; j++)
        {
            Rows[j] ??= [];
            while (Rows[j].Count < Columns.Count) Rows[j].Add("");
            if (Rows[j].Count > Columns.Count)
                Rows[j].RemoveRange(Columns.Count, Rows[j].Count - Columns.Count);
            for (var k = 0; k < Rows[j].Count; k++)
            {
                Rows[j][k] ??= "";
                if (Rows[j][k].Length > TableLimits.MaxCellChars)
                {
                    Rows[j][k] = TableLimits.Clip(Rows[j][k], TableLimits.MaxCellChars);
                    clipped = true;
                }
            }
        }

        EnsureRowIds();
        EnsureColumnIds();
        Window ??= new WindowMemory();
        Window.Width = Math.Clamp(Window.Width <= 0 ? 780 : Window.Width, 340, 5000);
        Window.Height = Math.Clamp(Window.Height <= 0 ? 520 : Window.Height, 300, 5000);
        Window.Topmost = DesktopPlacement.EffectiveTopmost(Window.Layer);

        // v2.3.1 (L9): width fixes are display settings, not data → they no longer set "clipped"
        // (which showed the "data too big, trimmed" warning at start-up although nothing was cut).
        var widthsBefore = ColumnWidths.Count;
        while (ColumnWidths.Count < Columns.Count) ColumnWidths.Add(TableLimits.DefaultColumnWidth);
        if (ColumnWidths.Count > Columns.Count)
            ColumnWidths.RemoveRange(Columns.Count, ColumnWidths.Count - Columns.Count);
        for (var l = 0; l < ColumnWidths.Count; l++)
            ColumnWidths[l] = Math.Clamp(ColumnWidths[l], TableLimits.MinColumnWidth, TableLimits.MaxColumnWidth);

        // v2.3.1 (L8): old-default migration (140 → 120 status / 200 due) only for files older than v3 or for a width
        // filled in just now — before, a user who set the status column to exactly 140 px got 120 back on every save.
        var migrate = Version < 3;
        var statusIdx = StatusColumnIndex();
        if (statusIdx >= 0 && (migrate || statusIdx >= widthsBefore) && ColumnWidths[statusIdx] == 140) ColumnWidths[statusIdx] = 120;
        var dueIdx = DueColumnIndex();
        if (dueIdx >= 0 && (migrate || dueIdx >= widthsBefore) && ColumnWidths[dueIdx] == 140) ColumnWidths[dueIdx] = 200;

        Version = Math.Max(Version, CurrentVersion); // never downgrade a newer file
        return clipped;
    }

    public int ColumnWidth(int index)
    {
        if (index < 0 || index >= Columns.Count) return TableLimits.DefaultColumnWidth;
        while (ColumnWidths.Count < Columns.Count) ColumnWidths.Add(TableLimits.DefaultColumnWidth);
        return ColumnWidths[index];
    }

    public void SetColumnWidth(int index, int width)
    {
        if (index < 0 || index >= Columns.Count) return;
        while (ColumnWidths.Count < Columns.Count) ColumnWidths.Add(TableLimits.DefaultColumnWidth);
        ColumnWidths[index] = Math.Clamp(width, TableLimits.MinColumnWidth, TableLimits.MaxColumnWidth);
    }

    private static List<string> BlankRow(int columns) =>
        Enumerable.Repeat("", Math.Max(columns, 0)).ToList();
}
