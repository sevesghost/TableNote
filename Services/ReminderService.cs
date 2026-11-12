using Avalonia.Threading;

namespace TableNote;

/// <summary>
/// Polls every ~30 s and hands all currently-due fires (at most one per task, see
/// <see cref="ReminderPlan.Next"/>) to one batch callback. Tasks shown in an open popup are
/// excluded until the popup releases them.
/// </summary>
public sealed class ReminderService : IDisposable
{
    private readonly Func<NoteDocument> _doc;
    private readonly Action<IReadOnlyList<DueTask>> _onDueBatch;
    private readonly DispatcherTimer _timer;
    private readonly HashSet<string> _inPopup = new(StringComparer.Ordinal);
    private bool _disposed;

    public ReminderService(Func<NoteDocument> doc, Action<IReadOnlyList<DueTask>> onDueBatch)
    {
        _doc = doc;
        _onDueBatch = onDueBatch;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _timer.Tick += (_, _) =>
        {
            _timer.Stop(); // no overlapping ticks
            try { Check(); }
            finally { if (!_disposed && _running) _timer.Start(); }
        };
    }

    private bool _running;
    private bool _checking;
    public void Start() { _running = true; _timer.Start(); }
    public void Stop() { _running = false; _timer.Stop(); }
    public void CheckNow() => Check();

    public void ReleaseFromPopup(IEnumerable<string> rowIds)
    {
        foreach (var id in rowIds) _inPopup.Remove(id);
    }

    private void Check()
    {
        if (_disposed || _checking) return; // CheckNow() from a handler while a check runs → skip, not recurse
        _checking = true;
        IReadOnlyList<DueTask>? batch = null;
        try
        {
            using var _ = UiActivity.Scope("reminders: check");
            batch = ReminderPlan.Collect(_doc(), DateTime.Now, _inPopup);
            if (batch.Count == 0) return;
            foreach (var t in batch) _inPopup.Add(t.RowId);
            _onDueBatch(batch);
        }
        catch (Exception ex)
        {
            AppLog.Write("reminders", "check failed: " + ex.Message);
            // v2.3.1: the popup could not be shown → don't keep these tasks blocked as "in popup" forever.
            if (batch != null) foreach (var t in batch) _inPopup.Remove(t.RowId);
        }
        finally { _checking = false; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
    }
}

/// <summary>
/// One reminder fire. Offset 0 = on-time; &gt;0 = advance (minutes before due).
/// <paramref name="Late"/> is decided ONCE when the reminder is created (fired &gt; 2 min after its time,
/// e.g. app was closed); a popup left open never turns into "missed" by itself.
/// </summary>
public readonly record struct DueTask(int RowIndex, string RowId, string Title, string Detail, DateTime Due, int Offset = 0, DateTime FireAt = default, bool Late = false)
{
    public static readonly TimeSpan LateThreshold = TimeSpan.FromMinutes(2);

    public DateTime FireTime => FireAt == default ? Due.AddMinutes(-Offset) : FireAt;

    public string WhenLabel(DateTime now)
    {
        var missed = Late ? Localizer.T("when.missed", DueDates.Format(FireTime)) : "";
        if (Offset > 0)
        {
            if (now >= Due) return Localizer.T("when.advance_past", DueDates.Format(Due)) + missed;
            // Live countdown: real time left (equals the configured offset at fire time).
            return Localizer.T("when.advance", Remaining(Due - now), DueDates.Format(Due)) + missed;
        }
        return Late ? Localizer.T("when.due_late") + missed : Localizer.T("when.due", DueDates.Format(Due));
    }

    /// <summary>Real time left, rounded up to the minute, in the UI language (e.g. "1 hour 5 minutes").</summary>
    public static string Remaining(TimeSpan left) =>
        Localizer.Duration(Math.Max(1, (int)Math.Ceiling(left.TotalMinutes)));
}
