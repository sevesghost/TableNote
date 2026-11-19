using Avalonia.Threading;

namespace TableNote;

/// <summary>
/// note.json could not be read at start-up (another program held it, e.g. antivirus or a backup tool at sign-in).
/// Before, the session ran on an empty stand-in table until TableNote was restarted: started at sign-in it sat hidden in
/// the tray with no reminders all day, and its default "launch at sign-in = off" even removed the sign-in entry.
/// Now the stand-in only waits: TableNote keeps trying and, once note.json can be read, opens it as if it had just
/// started with it. If something was typed into the stand-in, that is kept in the side file and the table is not
/// swapped under it; the user is told to restart instead.
/// </summary>
public partial class MainWindow
{
    /// <summary>True while the table is the empty stand-in for a note.json that could not be read.</summary>
    private bool _placeholder;
    private DispatcherTimer? _reloadTimer;
    private int _reloadTries;
    /// <summary>An earlier run's note.unsaved.json, renamed at start-up (file name, time it was saved).</summary>
    private readonly (string FileName, DateTime Saved)? _archivedUnsaved;
    /// <summary>File name of this session's side copy (note.unsaved.json unless an older one had to be kept).</summary>
    private string? _sideCopyName;

    public const int ReloadFastTries = 12;      // every 5 s for the first minute …
    public static readonly TimeSpan ReloadFast = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan ReloadSlow = TimeSpan.FromSeconds(30); // … then every 30 s

    /// <summary>What a kept note.json session compares to know whether it has edits: the undo content plus the alert
    /// boxes and colours, which the undo snapshot leaves out (before, a new alert box there was never saved).</summary>
    private static string PreserveKey(NoteDocument doc) =>
        Snapshot.Serialize(doc) + "\n" + AlertBoxOps.ToElement(doc.AlertBoxes).GetRawText() + "\n" +
        (PartColorOps.ToJson(doc.PartColors)?.ToJsonString() ?? "");

    private static string UnsavedKeptNotice((string FileName, DateTime Saved) kept) =>
        Localizer.T("load.unsaved_kept", DueDates.Format(kept.Saved), kept.FileName);

    private void StartReloadRetry()
    {
        _reloadTimer ??= new DispatcherTimer();
        _reloadTimer.Tick -= OnReloadTick;
        _reloadTimer.Tick += OnReloadTick;
        _reloadTimer.Interval = ReloadFast;
        _reloadTimer.Start();
    }

    private void OnReloadTick(object? sender, EventArgs e)
    {
        _reloadTimer?.Stop();
        if (!_placeholder) return;
        try
        {
            if (TryReload()) return;
        }
        catch (Exception ex) { AppLog.Write("load", "note.json retry failed: " + ex.GetType().Name + ": " + ex.Message); }
        _reloadTries++;
        if (_reloadTimer == null) return;
        _reloadTimer.Interval = _reloadTries < ReloadFastTries ? ReloadFast : ReloadSlow;
        _reloadTimer.Start();
    }

    /// <returns>true = done (opened, or stopped for good); false = try again later.</returns>
    private bool TryReload()
    {
        // Never swap the table while a dialog or a structure change is working on it.
        if (_busy || _exporting || OwnedWindows.Count > 0) return false;
        if (PreserveKey(_session.Doc) != _preserveBaseline)
        {
            // Typed into the stand-in: it is in the side file, and swapping now would hide it. Say so once
            // note.json can be read, so that a restart opens it.
            if (!NoteStore.CanRead(_path)) return false;
            _placeholder = false;
            var side = _sideCopyName ?? Path.GetFileName(NoteStore.UnsavedPath(_path));
            AppLog.Write("load", $"note.json can be read again; this session has edits (kept in {side}), so it is not swapped in");
            ShowNotice(Localizer.T("load.locked_edited", side));
            return true;
        }
        var loaded = NoteStore.Load(_path, TableLimits.MaxSaveFileBytes, retries: 0); // no sleeping on the UI thread
        if (loaded.Placeholder) return false;
        AppLog.Write("load", $"note.json opened after {_reloadTries + 1} retries (it could not be read at start-up)");
        ApplyLoadedDocument(loaded);
        return true;
    }

    /// <summary>Use the note.json that could finally be read, as the constructor and OnOpened would have.</summary>
    private void ApplyLoadedDocument(LoadResult loaded)
    {
        CommitFocusedEdit();
        var doc = loaded.Document;
        _session = new TableSession(doc);
        _undo.Clear(); // steps of the stand-in must never replay onto the real table
        _placeholder = loaded.Placeholder;
        _preserveExistingFile = loaded.PreserveExistingFile;
        _preserveBaseline = _preserveExistingFile ? PreserveKey(doc) : null;
        _saveFailureMessage = null;
        _loadWarning = loaded.Warning ?? SyncStartup(doc.LaunchAtLogin); // now the user's own setting
        StatusText.Tag = null; // UpdateStatus shows a new load warning once

        _tracking = false; // the toggles below must not write back into the document or save
        try
        {
            TopmostBox.IsChecked = doc.Window.Layer == DesktopLayerKind.Floating;
            LockBox.IsChecked = doc.Window.Locked;
            LaunchBox.IsChecked = doc.LaunchAtLogin;
            HideDoneBox.IsChecked = doc.HideDone;
            MuteSoundBox.IsChecked = doc.MuteSound;
            NotificationSound.Muted = doc.MuteSound;
            Width = Math.Max(720, doc.Window.Width);
            Height = Math.Max(480, doc.Window.Height);
        }
        finally { _tracking = true; }
        EnsureVisible(); // saved position, kept on a screen
        ApplyChromeColors();

        var mode = RecoveryGate.ResolveStartup(doc.View, Program.ForceEdit, CurrentRoutes());
        if (mode == ViewMode.Background && SavedPositionNeedsCenter()) mode = ViewMode.Edit;
        if (mode == ViewMode.Background && !IsVisible)
        {
            // Still hidden in the tray after a sign-in start: go where the user left it, as a normal start would.
            Show();
            if (!EnterBackground()) EnterEdit(Localizer.T("ui.bg_failed"));
        }
        else
        {
            ApplyChrome();        // resize lock, table and alert boxes of the real note
            ApplyRequestedLayer(); // saved layer (normal / floating) of the editor
        }
        UpdateStatus();
        ShowNotice(!string.IsNullOrEmpty(doc.DueColumnWarning) ? doc.DueColumnWarning
            : !string.IsNullOrEmpty(loaded.Notice) ? loaded.Notice
            : Localizer.T("load.locked_opened"));
        _reminders.CheckNow();
    }
}
