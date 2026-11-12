namespace TableNote;

/// <summary>v2.4.2: where the app icon lives. Edit = taskbar + tray; Background = tray only.
/// Pure logic so tests can cover every switch; MainWindow applies the result (ShowInTaskbar + WindowsTaskbar).
/// (Named TaskbarPresence because Platform/TaskbarPolicy already holds the Win32 ex-style helpers.)</summary>
public static class TaskbarPresence
{
    /// <summary>Should the main window have a taskbar button in this mode?</summary>
    public static bool ShowInTaskbar(ViewMode view) => view == ViewMode.Edit;

    /// <summary>Mode to restore at startup from the saved file: same rules as RecoveryGate, plus the
    /// off-screen saved position check that OnOpened already does (Background → Edit).</summary>
    public static ViewMode StartupView(ViewMode persisted, bool forceEdit, RecoveryRoutes routes, bool savedPositionNeedsCenter)
    {
        var mode = RecoveryGate.ResolveStartup(persisted, forceEdit, routes);
        return mode == ViewMode.Background && savedPositionNeedsCenter ? ViewMode.Edit : mode;
    }

    /// <summary>Mode after asking for Background: anything short of a full success leaves the editor (icon on taskbar).</summary>
    public static ViewMode AfterBackgroundAttempt(bool entered) => entered ? ViewMode.Background : ViewMode.Edit;
}
