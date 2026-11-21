namespace TableNote;

public enum ActivityLevel { Info, Done, Warning, Error }

/// <param name="Count">Same kind of entry right after itself (e.g. autosaves): kept as one line, counted.</param>
public sealed record ActivityEntry(DateTime Time, ActivityLevel Kind, string Text, TimeSpan? Duration = null, string? Key = null, int Count = 1);

/// <summary>
/// 2.6.0: what TableNote is doing now and what it did lately — for the activity bar at the bottom of the window
/// (optional, can be hidden) and its History list. Long work is a <see cref="Running"/> (Begin … Done / Fail): the bar
/// shows it with a progress bar while it runs. Short events are <see cref="Add"/>ed. Thread-safe: the UI watchdog
/// reports from its own thread; <see cref="Changed"/> may come on any thread (the window posts to the UI thread).
/// </summary>
public sealed class ActivityLog
{
    public static ActivityLog Shared { get; } = new();

    public const int MaxEntries = 60;
    private readonly object _gate = new();
    private readonly List<ActivityEntry> _entries = [];
    private readonly List<Running> _running = [];

    public event Action? Changed;
    public Func<DateTime> Clock { get; set; } = () => DateTime.Now;

    /// <summary>Oldest first.</summary>
    public IReadOnlyList<ActivityEntry> Entries { get { lock (_gate) return _entries.ToList(); } }

    public ActivityEntry? Last { get { lock (_gate) return _entries.Count > 0 ? _entries[^1] : null; } }

    /// <summary>The newest entry with this key (e.g. the last save), or null.</summary>
    public ActivityEntry? LastOf(string key) { lock (_gate) return _entries.LastOrDefault(e => e.Key == key); }

    /// <summary>What runs now (the newest of the running ones): its text and progress 0–1 (null: unknown).</summary>
    public (string Text, double? Progress)? Current
    {
        get { lock (_gate) return _running.Count == 0 ? null : (_running[^1].Text, _running[^1].Fraction); }
    }

    public bool Busy { get { lock (_gate) return _running.Count > 0; } }

    /// <summary>Work that takes a while: shown as running until <see cref="Running.Done"/>, <see cref="Running.Fail"/> or Dispose.</summary>
    public Running Begin(string text, string? key = null)
    {
        var r = new Running(this, text, key, Clock());
        lock (_gate) _running.Add(r);
        Changed?.Invoke();
        return r;
    }

    /// <summary>A finished event. With a <paramref name="key"/>, an entry right after one with the same key and kind
    /// replaces it (time updated, counted), so a run of autosaves is one line.</summary>
    public void Add(ActivityLevel kind, string text, string? key = null, TimeSpan? duration = null)
    {
        lock (_gate)
        {
            var now = Clock();
            if (key != null && _entries.Count > 0 && _entries[^1] is { } last && last.Key == key && last.Kind == kind)
                _entries[^1] = new ActivityEntry(now, kind, text, duration, key, last.Count + 1);
            else
                _entries.Add(new ActivityEntry(now, kind, text, duration, key));
            while (_entries.Count > MaxEntries) _entries.RemoveAt(0);
        }
        Changed?.Invoke();
    }

    public void Clear()
    {
        lock (_gate) { _entries.Clear(); _running.Clear(); }
        Changed?.Invoke();
    }

    private void End(Running r, ActivityLevel kind, string text)
    {
        bool removed;
        lock (_gate) removed = _running.Remove(r);
        if (removed) Add(kind, text, r.Key, Clock() - r.Started);
    }

    private void SetProgress(Running r, double fraction)
    {
        lock (_gate) r.Fraction = Math.Clamp(fraction, 0, 1);
        Changed?.Invoke();
    }

    public sealed class Running : IDisposable
    {
        private readonly ActivityLog _log;
        internal Running(ActivityLog log, string text, string? key, DateTime started) { _log = log; Text = text; Key = key; Started = started; }
        public string Text { get; }
        public string? Key { get; }
        public DateTime Started { get; }
        internal double? Fraction { get; set; }

        /// <summary>How far it is (0–1), when that is known; the bar then fills instead of moving back and forth.</summary>
        public void Report(double fraction) => _log.SetProgress(this, fraction);

        /// <summary>Finished: the entry says <paramref name="text"/> (or the running text).</summary>
        public void Done(string? text = null) => _log.End(this, ActivityLevel.Done, text ?? Text);

        public void Fail(string text) => _log.End(this, ActivityLevel.Error, text);

        /// <summary>Ended without the result, but not an error (e.g. Windows asks first).</summary>
        public void Warn(string text) => _log.End(this, ActivityLevel.Warning, text);

        /// <summary>Ends it as done unless Done / Fail already did.</summary>
        public void Dispose() => _log.End(this, ActivityLevel.Done, Text);
    }

    /// <summary>Plain words for the UI breadcrumbs (<see cref="UiActivity"/>) the watchdog reports.</summary>
    public static string Describe(string? breadcrumb)
    {
        var b = breadcrumb ?? "";
        string Key(string k) => Localizer.T(k);
        if (b.StartsWith("save", StringComparison.Ordinal)) return Key("activity.what_save");
        if (b.StartsWith("rebuild", StringComparison.Ordinal)) return Key("activity.what_rebuild");
        if (b.StartsWith("refresh row", StringComparison.Ordinal)) return Key("activity.what_row");
        if (b.StartsWith("wallpaper", StringComparison.Ordinal) || b.Contains("restore wallpaper", StringComparison.Ordinal)) return Key("activity.what_wallpaper");
        if (b.StartsWith("reminders", StringComparison.Ordinal)) return Key("activity.what_reminders");
        if (b.StartsWith("reminder popup", StringComparison.Ordinal)) return Key("activity.what_popup");
        if (b.StartsWith("start", StringComparison.Ordinal)) return Key("activity.what_start");
        if (b.Length == 0 || b == "idle") return Key("activity.what_other");
        return b;
    }
}
