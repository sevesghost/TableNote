namespace TableNote;

/// <summary>2.6.0: inserting and deleting several rows or columns at a position (Excel's Insert / Delete on selected
/// row numbers or column headers), and "Insert copied" — rows or columns copied in TableNote come back with their
/// row colour, repeat and advance reminders, or column type, choices and width; text from elsewhere (Excel) is
/// inserted as new rows. Every function changes the document only; the window makes one undo step of each call.</summary>
public static class TableEdits
{
    /// <summary>Inserts <paramref name="count"/> blank rows; the first new row ends up at <paramref name="index"/>
    /// (0 … Rows.Count). False, and nothing changes, when the table would pass <see cref="TableLimits.MaxRows"/>.</summary>
    public static bool InsertRows(NoteDocument doc, int index, int count)
    {
        if (count <= 0 || doc.Rows.Count + count > TableLimits.MaxRows) return false;
        doc.EnsureRowIds();
        index = Math.Clamp(index, 0, doc.Rows.Count);
        for (var i = 0; i < count; i++)
        {
            if (!doc.AppendRow()) return false;
            MoveLastRowTo(doc, index + i);
        }
        return true;
    }

    private static void MoveLastRowTo(NoteDocument doc, int at)
    {
        var last = doc.Rows.Count - 1;
        if (at >= last) return;
        var row = doc.Rows[last];
        var id = doc.RowIds[last];
        doc.Rows.RemoveAt(last);
        doc.RowIds.RemoveAt(last);
        doc.Rows.Insert(at, row);
        doc.RowIds.Insert(at, id);
    }

    /// <summary>Removes the given rows (document indexes, any order). Returns how many went.</summary>
    public static int RemoveRows(NoteDocument doc, IEnumerable<int> rows)
    {
        var n = 0;
        foreach (var r in rows.Distinct().OrderByDescending(r => r))
            if (doc.RemoveRow(r)) n++;
        return n;
    }

    /// <summary>Inserts <paramref name="count"/> new text columns (default names, or <paramref name="names"/>); the first
    /// ends up at <paramref name="index"/>. Returns the new columns' ids, or null (nothing changed) past the limit.</summary>
    public static List<string>? InsertColumns(NoteDocument doc, int index, int count, IReadOnlyList<string>? names = null)
    {
        if (count <= 0 || doc.Columns.Count + count > NoteDocument.MaxUserColumns) return null;
        doc.EnsureColumnIds();
        doc.EnsureColumnRoles();
        index = Math.Clamp(index, 0, doc.Columns.Count);
        var ids = new List<string>();
        for (var i = 0; i < count; i++)
        {
            var name = names?.ElementAtOrDefault(i);
            if (!doc.AddColumn(string.IsNullOrWhiteSpace(name) ? null : UniqueName(doc, name.Trim()))) return ids.Count == 0 ? null : ids;
            var last = doc.Columns.Count - 1;
            ids.Add(doc.ColumnIds[last]);
            if (index + i < last) doc.MoveColumn(last, index + i);
        }
        return ids;
    }

    /// <summary>"Name", or "Name (2)", "Name (3)" … when the table already has a column called that.</summary>
    public static string UniqueName(NoteDocument doc, string name)
    {
        if (!doc.Columns.Contains(name)) return name;
        for (var i = 2; i < 1000; i++)
        {
            var candidate = $"{name} ({i})";
            if (!doc.Columns.Contains(candidate)) return candidate;
        }
        return name;
    }

    /// <summary>Removes the given columns (indexes, any order), keeping at least one. Returns how many went.</summary>
    public static int RemoveColumns(NoteDocument doc, IEnumerable<int> columns)
    {
        var n = 0;
        foreach (var c in columns.Distinct().OrderByDescending(c => c))
        {
            if (doc.Columns.Count <= 1) break;
            if (doc.RemoveColumn(c)) n++;
        }
        return n;
    }

    // ───────────────────────── copied rows / columns ─────────────────────────

    /// <summary>A copied row: its cells by column id, and what belongs to the row (not its id, history, or acknowledgements).</summary>
    public sealed class RowCopy
    {
        public Dictionary<string, string> Cells { get; init; } = new(StringComparer.Ordinal);
        public string? Recurrence { get; init; }
        public List<int>? Offsets { get; init; }
        public RowStyle? Style { get; init; }
        public int? Height { get; init; }
    }

    /// <summary>A copied column: name, type and width, and its cells by row id.</summary>
    public sealed class ColumnCopy
    {
        public string Name { get; init; } = "";
        public ColumnSetting? Setting { get; init; }
        public int Width { get; init; }
        public Dictionary<string, string> Cells { get; init; } = new(StringComparer.Ordinal);
        /// <summary>Row ids in the order they were copied (the text on the clipboard has these lines).</summary>
        public List<string> RowOrder { get; init; } = [];
    }

    /// <summary>What Ctrl+C / Ctrl+X last copied from this table, next to the text it put on the clipboard. "Insert
    /// copied" uses it while the clipboard still holds that text; otherwise the clipboard text is inserted.</summary>
    public sealed class Clip
    {
        public required string Text { get; init; }
        public List<RowCopy>? Rows { get; init; }
        public List<ColumnCopy>? Columns { get; init; }
        public bool Matches(string? clipboard) => clipboard != null && Normalize(clipboard) == Normalize(Text);
        private static string Normalize(string s) => s.Replace("\r\n", "\n").TrimEnd('\n');
    }

    public static Clip CopyRows(NoteDocument doc, IEnumerable<int> rows, string text)
    {
        doc.EnsureRowIds();
        doc.EnsureColumnIds();
        var list = new List<RowCopy>();
        foreach (var r in rows)
        {
            if (r < 0 || r >= doc.Rows.Count) continue;
            var id = doc.RowIdAt(r);
            var cells = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var c = 0; c < doc.Columns.Count; c++) cells[doc.ColumnIds[c]] = doc.GetCell(r, c);
            doc.Recurrence.TryGetValue(id, out var rec);
            doc.Reminders.TryGetValue(id, out var rem);
            doc.RowStyles.TryGetValue(id, out var style);
            int? height = doc.RowHeights != null && doc.RowHeights.TryGetValue(id, out var h) ? h : null;
            list.Add(new RowCopy
            {
                Cells = cells,
                Recurrence = rec,
                Offsets = rem?.Offsets is { Count: > 0 } o ? o.ToList() : null,
                Style = style == null ? null : new RowStyle { Color = style.Color, Highlight = style.Highlight },
                Height = height
            });
        }
        return new Clip { Text = text, Rows = list };
    }

    public static Clip CopyColumns(NoteDocument doc, IEnumerable<int> columns, IReadOnlyList<int> rows, string text)
    {
        doc.EnsureRowIds();
        doc.EnsureColumnIds();
        var status = doc.StatusColumnIndex();
        var list = new List<ColumnCopy>();
        foreach (var c in columns)
        {
            if (c < 0 || c >= doc.Columns.Count) continue;
            doc.ColumnSettings.TryGetValue(doc.ColumnIds[c], out var setting);
            var cells = new Dictionary<string, string>(StringComparer.Ordinal);
            var order = new List<string>();
            foreach (var r in rows)
            {
                if (r < 0 || r >= doc.Rows.Count) continue;
                var v = doc.GetCell(r, c);
                // The copy is a plain column: a status shows its label there, not the stored code.
                if (c == status && v.Length > 0) v = TaskStatuses.Label(TaskStatuses.DisplayCode(v));
                var id = doc.RowIdAt(r);
                cells[id] = v;
                order.Add(id);
            }
            list.Add(new ColumnCopy
            {
                Name = doc.Columns[c],
                Setting = setting == null ? null : new ColumnSetting { Type = setting.Type, Options = setting.Options.ToList() },
                Width = doc.ColumnWidth(c),
                Cells = cells,
                RowOrder = order
            });
        }
        return new Clip { Text = text, Columns = list };
    }

    /// <summary>Inserts copied rows at <paramref name="index"/>: new ids, the cells of columns that still exist, the row
    /// colour, repeat, advance reminders and height. False (nothing changed) past the row limit.</summary>
    public static bool InsertRowCopies(NoteDocument doc, int index, IReadOnlyList<RowCopy> rows)
    {
        if (rows.Count == 0 || !InsertRows(doc, index, rows.Count)) return false;
        index = Math.Clamp(index, 0, doc.Rows.Count - rows.Count);
        for (var i = 0; i < rows.Count; i++)
        {
            var r = index + i;
            var id = doc.RowIdAt(r);
            var copy = rows[i];
            for (var c = 0; c < doc.Columns.Count; c++)
                if (copy.Cells.TryGetValue(doc.ColumnIds[c], out var v)) doc.SetCellRaw(r, c, v);
            if (copy.Recurrence != null) doc.Recurrence[id] = copy.Recurrence;
            if (copy.Offsets is { Count: > 0 } offsets) ReminderPlan.Info(doc, id).Offsets = offsets.ToList();
            if (copy.Style != null) doc.RowStyles[id] = new RowStyle { Color = copy.Style.Color, Highlight = copy.Style.Highlight };
            if (copy.Height is int h) (doc.RowHeights ??= new(StringComparer.Ordinal))[id] = h;
        }
        return true;
    }

    /// <summary>Inserts copied columns at <paramref name="index"/> as plain user columns (a copied status or reminder
    /// column becomes text; the table keeps one of each), with the copied type, choices, width and cells of rows that
    /// still exist. Returns how many were inserted (0: past the column limit, nothing changed).</summary>
    public static int InsertColumnCopies(NoteDocument doc, int index, IReadOnlyList<ColumnCopy> columns)
    {
        if (columns.Count == 0) return 0;
        var ids = InsertColumns(doc, index, columns.Count, columns.Select(c => c.Name).ToList());
        if (ids == null) return 0;
        doc.EnsureRowIds();
        for (var i = 0; i < ids.Count; i++)
        {
            var c = doc.ColumnIds.IndexOf(ids[i]);
            if (c < 0) continue;
            var copy = columns[i];
            if (copy.Setting != null && ColumnTypes.Normalize(copy.Setting.Type) != ColumnTypes.Text)
                doc.ColumnSettings[ids[i]] = new ColumnSetting { Type = ColumnTypes.Normalize(copy.Setting.Type), Options = copy.Setting.Options.ToList() };
            if (copy.Width > 0) doc.SetColumnWidth(c, copy.Width);
            foreach (var (rowId, value) in copy.Cells)
            {
                var r = doc.RowIds.IndexOf(rowId);
                if (r >= 0) doc.SetCellRaw(r, c, value);
            }
        }
        return ids.Count;
    }

    /// <summary>Text from the clipboard (Excel, another program, or a copy whose rows are gone) inserted as new rows at
    /// <paramref name="index"/>, from column <paramref name="column"/>: one row per line, tabs between cells (like a paste
    /// into empty rows). Null when it went in; otherwise the message to show (nothing changed).</summary>
    public static string? InsertTextRows(NoteDocument doc, int index, int column, string text)
    {
        var trimmed = text.EndsWith("\r\n", StringComparison.Ordinal) ? text[..^2] : text.EndsWith('\n') ? text[..^1] : text;
        if (trimmed.Length == 0) return Localizer.T("insert.nothing_copied");
        if (text.Length > TableLimits.MaxPasteChars) return TableLimits.PasteTooBigMessage;
        if (!ExcelPaste.LooksLikeGrid(trimmed))
        {
            if (!InsertRows(doc, index, 1)) return TableLimits.RowLimitMessage;
            var at = Math.Clamp(index, 0, doc.Rows.Count - 1);
            doc.SetCellRaw(at, Math.Clamp(column, 0, doc.Columns.Count - 1), TableLimits.Clip(trimmed, TableLimits.MaxCellChars));
            return null;
        }
        if (!ExcelPaste.TryRead(trimmed, out var grid, out var error) || grid.Count == 0)
            return error ?? Localizer.T("ui.paste_failed");
        var before = Snapshot.Serialize(doc);
        if (!InsertRows(doc, index, grid.Count)) return TableLimits.RowLimitMessage;
        var result = ExcelPaste.Apply(doc, Math.Clamp(index, 0, doc.Rows.Count - 1), Math.Max(0, column), trimmed);
        if (result.Kind == GridPasteKind.Applied) return null;
        Snapshot.Restore(doc, before); // too wide for the table: the new rows go again
        return result.Message ?? Localizer.T("ui.paste_failed");
    }

    /// <summary>Text from the clipboard inserted as new columns at <paramref name="index"/>: one column per tab-separated
    /// cell, lines go down <paramref name="visibleRows"/> (the rows on screen, top first). Null when it went in; otherwise the message.</summary>
    public static string? InsertTextColumns(NoteDocument doc, int index, IReadOnlyList<int> visibleRows, string text)
    {
        var trimmed = text.EndsWith("\r\n", StringComparison.Ordinal) ? text[..^2] : text.EndsWith('\n') ? text[..^1] : text;
        if (trimmed.Length == 0) return Localizer.T("insert.nothing_copied");
        if (text.Length > TableLimits.MaxPasteChars) return TableLimits.PasteTooBigMessage;
        List<List<string>> grid;
        if (ExcelPaste.LooksLikeGrid(trimmed))
        {
            if (!ExcelPaste.TryRead(trimmed, out grid, out var error) || grid.Count == 0) return error ?? Localizer.T("ui.paste_failed");
        }
        else grid = [[trimmed]];
        var width = grid.Max(r => r.Count);
        if (width == 0) return Localizer.T("insert.nothing_copied");
        var ids = InsertColumns(doc, index, width);
        if (ids == null) return TableLimits.ColumnLimitMessage;
        for (var i = 0; i < grid.Count && i < visibleRows.Count; i++)
            for (var k = 0; k < grid[i].Count && k < ids.Count; k++)
            {
                var c = doc.ColumnIds.IndexOf(ids[k]);
                if (c >= 0 && visibleRows[i] < doc.Rows.Count) doc.SetCellRaw(visibleRows[i], c, TableLimits.Clip(grid[i][k], TableLimits.MaxCellChars));
            }
        return null;
    }
}
