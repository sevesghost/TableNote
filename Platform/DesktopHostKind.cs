namespace TableNote;
/// <summary>
/// Where the table lives in background mode. BottomFallback = not parented; a tool window kept at
/// HWND_BOTTOM that restores itself after Win+D (used when no desktop host accepts us).
/// </summary>
public enum DesktopHostKind { None, WorkerWBehindIcons, Progman, IconHost, BottomFallback }
