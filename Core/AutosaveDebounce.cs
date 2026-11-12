namespace TableNote;
public static class AutosaveDebounce
{
    // v2.3.1: 1.5 s (was 400 ms). Typing a sentence rewrote the whole note.json tens of times (disk + OneDrive sync).
    // Data is still flushed immediately on structure changes, hide-to-tray, quit and Background mode (FlushSave).
    public static TimeSpan Delay { get; } = TimeSpan.FromMilliseconds(1500);
}
