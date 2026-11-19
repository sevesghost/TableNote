namespace TableNote;

public sealed class LoadResult
{
    public NoteDocument Document { get; set; } = NoteDocument.CreateDefault();
    public string? Warning { get; set; }
    public bool PreserveExistingFile { get; set; }
    /// <summary>The document is an empty stand-in: note.json itself could not be read (e.g. locked by another program)
    /// and is left untouched. Its settings (launch at sign-in, window, view) are defaults, not the user's, so they must
    /// not be applied to Windows; MainWindow keeps retrying and opens note.json once it can be read.</summary>
    public bool Placeholder { get; set; }
    /// <summary>Non-blocking info: some values were dropped/reset while loading (details in tablenote.log).</summary>
    public string? Notice { get; set; }
    /// <summary>The individual load issues (also written to tablenote.log).</summary>
    public List<string> Issues { get; set; } = [];
}
