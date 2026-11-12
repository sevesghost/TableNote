namespace TableNote;

public sealed class GridPasteResult
{
    public GridPasteKind Kind { get; init; }
    public string? Message { get; init; }
    public int Rows { get; init; }
    public int Columns { get; init; }

    public static GridPasteResult Applied(int rows, int cols) =>
        new() { Kind = GridPasteKind.Applied, Rows = rows, Columns = cols, Message = Localizer.T("limit.pasted", rows, cols) };

    public static GridPasteResult Rejected(string message) =>
        new() { Kind = GridPasteKind.Rejected, Message = message };

    public static GridPasteResult NotAGrid() =>
        new() { Kind = GridPasteKind.NotAGrid };
}
