namespace TableNote;

/// <summary>Stable role tags parallel to Columns (survive header renames).</summary>
public static class ColumnRoles
{
    public const string Title = "title";
    public const string Detail = "detail";
    public const string Status = "status";
    public const string Due = "due";

    public static string? InferFromName(string name)
    {
        var n = name.Trim();
        // v2.4.2: default headers of every embedded language ("Title", "Due date" …) and of 2.4.1 (Thai).
        if (NoteDocument.RoleOfDefaultName(n) is { } byDefault) return byDefault;
        if (n == NoteDocument.ColTitle || n.Equals("title", StringComparison.OrdinalIgnoreCase)) return Title;
        if (n == NoteDocument.ColDetail || n.Equals("detail", StringComparison.OrdinalIgnoreCase)) return Detail;
        if (n == NoteDocument.ColStatus || n.Equals("status", StringComparison.OrdinalIgnoreCase)) return Status;
        if (n == NoteDocument.ColDue || n.Contains("กำหนด", StringComparison.Ordinal) ||
            n.Equals("due", StringComparison.OrdinalIgnoreCase)) return Due;
        return null;
    }
}
