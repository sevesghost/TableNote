namespace TableNote;

public static class DeletePlanner
{
    /// <summary>v2.4.2: resolved when shown (was a Thai const), so it follows the UI language.</summary>
    public static string DueColumnProtected => Localizer.T("delete.due_protected");

    /// <summary>Header text for messages: trimmed, "Column n" when blank, cut at 40 chars.</summary>
    public static string ColumnLabel(NoteDocument doc, int column)
    {
        var text = column >= 0 && column < doc.Columns.Count ? doc.Columns[column].Trim() : "";
        if (text.Length == 0) text = Localizer.T("delete.column_fallback", column + 1);
        if (text.Length > 40) text = text[..40] + "…";
        return text;
    }

    public static DeletePlan ForRow(NoteDocument doc, int row)
    {
        if (doc.Rows.Count == 0 || row < 0 || row >= doc.Rows.Count)
            return new DeletePlan(false, false, Localizer.T("delete.no_row"), "");
        if (!doc.RowHasContent(row))
            return new DeletePlan(true, false, "", "");
        return new DeletePlan(true, true, "", Localizer.T("delete.row_has_text"));
    }

    public static DeletePlan ForColumn(NoteDocument doc, int column)
    {
        if (doc.Columns.Count <= 1)
            return new DeletePlan(false, false, Localizer.T("delete.min_one_column"), "");
        if (column < 0 || column >= doc.Columns.Count)
            return new DeletePlan(false, false, Localizer.T("delete.no_column"), "");
        if (column == doc.DueColumnIndex())
            return new DeletePlan(false, false, DueColumnProtected, "");
        if (!doc.ColumnHasContent(column))
            return new DeletePlan(true, false, "", "");
        return new DeletePlan(true, true, "", Localizer.T("delete.column_has_text", ColumnLabel(doc, column)));
    }
}
