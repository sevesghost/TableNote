namespace TableNote;

public sealed class LoadResult
{
    public NoteDocument Document { get; set; } = NoteDocument.CreateDefault();
    public string? Warning { get; set; }
    public bool PreserveExistingFile { get; set; }
    /// <summary>Non-blocking info: some values were dropped/reset while loading (details in tablenote.log).</summary>
    public string? Notice { get; set; }
    /// <summary>The individual load issues (also written to tablenote.log).</summary>
    public List<string> Issues { get; set; } = [];
}
