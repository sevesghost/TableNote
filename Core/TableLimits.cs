namespace TableNote;

public static class TableLimits
{
    public const int MaxRows = 1000;
    public const int MaxColumns = 30;
    public const int MaxCellChars = 2000;
    public const int MaxPasteChars = 262144;
    public const int MaxSaveFileBytes = 8 * 1024 * 1024; // 1000 rows fit comfortably
    public const int DefaultColumnWidth = 140;
    public const int MinColumnWidth = 72;
    public const int MaxColumnWidth = 420;

    // v2.4.2: messages are properties resolved when shown (were Thai consts).
    public static string PasteTooBigMessage => Localizer.T("limit.paste_too_big");
    public static string TooManyRowsMessage => Localizer.T("limit.paste_too_many_rows", MaxRows);
    public static string TooManyColumnsMessage => Localizer.T("limit.paste_too_many_columns");
    public static string CellTooLongMessage => Localizer.T("limit.cell_too_long");
    public static string BadQuoteMessage => Localizer.T("limit.paste_bad_quote");
    public static string FileTooBigMessage => Localizer.T("limit.file_too_big");
    public static string FileTooBigKeptMessage => Localizer.T("limit.file_too_big_kept");

    /// <summary>v2.4.1: first <paramref name="max"/> UTF-16 units without cutting an emoji / surrogate pair in half
    /// (a plain [..max] left a lone high surrogate that was saved as U+FFFD "�").</summary>
    public static string Clip(string value, int max)
    {
        if (value.Length <= max) return value;
        var n = max > 0 && char.IsHighSurrogate(value[max - 1]) ? max - 1 : max;
        return value[..n];
    }

    public static string OverflowNotice(int had, string backupName) =>
        Localizer.T("limit.rows_overflow", had, MaxRows, backupName);

    public static string SpawnRowLimitMessage => Localizer.T("recur.spawn_limit", MaxRows);

    public static string RowLimitMessage => Localizer.T("limit.row_limit", MaxRows);
    public static string ColumnLimitMessage => Localizer.T("limit.column_limit", MaxColumns);
}
