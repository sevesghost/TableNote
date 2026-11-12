namespace TableNote;

public static class TaskStatuses
{
    public const string NotStarted = "ยังไม่เริ่ม";
    public const string InProgress = "กำลังทำ";
    public const string Done = "เสร็จแล้ว";

    public static readonly string[] All = [NotStarted, InProgress, Done];

    /// <summary>Exact (trimmed, lower-cased) values that mean "done". No substring matching.</summary>
    private static readonly HashSet<string> DoneWords = new(StringComparer.Ordinal)
    {
        "เสร็จ", "เสร็จแล้ว", "done", "completed", "complete", "✓", "✔"
    };

    /// <summary>
    /// Single shared done-check used by reminders, colors, and dropdown.
    /// Exact whitelist match only — "ยังไม่เสร็จ" / "ไม่เสร็จ" are NOT done.
    /// </summary>
    public static bool IsDone(string? value)
    {
        var v = (value ?? "").Trim().ToLowerInvariant();
        if (v.Length == 0) return false;
        if (v.StartsWith("ยังไม่", StringComparison.Ordinal) || v.StartsWith("ไม่", StringComparison.Ordinal) ||
            v.StartsWith("not ", StringComparison.Ordinal)) return false;
        return DoneWords.Contains(v);
    }

    // Language-neutral codes stored in note.json; the dropdown shows Localizer labels.
    public const string TodoCode = "todo";
    public const string DoingCode = "doing";
    public const string DoneCode = "done";
    public static readonly string[] Codes = [TodoCode, DoingCode, DoneCode];

    public static string Label(string code) => Localizer.T("task_status." + code);
    public static IReadOnlyList<string> Labels => Codes.Select(Label).ToList();

    /// <summary>
    /// Load-time mapping of the standard labels (any supported language) to codes. Blank stays blank.
    /// Other done words (completed, ✓, ✔ …) stay exactly as typed and still count as done via IsDone.
    /// </summary>
    public static string CanonicalOnLoad(string? value)
    {
        var raw = value ?? "";
        var k = raw.Trim().ToLowerInvariant();
        return k switch
        {
            "ยังไม่เริ่ม" or "ยังไม่เสร็จ" or "todo" or "not started" => TodoCode,
            "กำลังทำ" or "doing" or "in progress" => DoingCode,
            "เสร็จ" or "เสร็จแล้ว" or "done" or "complete" => DoneCode,
            _ => raw
        };
    }

    /// <summary>Code to SHOW for a cell: blank/unknown → todo (unless IsDone), standard words → their code.</summary>
    public static string DisplayCode(string? value)
    {
        var c = CanonicalOnLoad(value);
        if (Codes.Contains(c)) return c;
        if (IsDone(value)) return DoneCode;
        var v = (value ?? "").Trim().ToLowerInvariant();
        if (v.Contains("กำลัง") || v.Contains("doing") || v.Contains("progress")) return DoingCode;
        return TodoCode;
    }

    public static string Normalize(string? value)
    {
        var v = value?.Trim() ?? "";
        if (All.Any(a => string.Equals(a, v, StringComparison.Ordinal))) return v;
        if (string.IsNullOrEmpty(v)) return NotStarted;
        if (IsDone(v)) return Done;
        if (v.Contains("กำลัง", StringComparison.Ordinal) ||
            v.Contains("doing", StringComparison.OrdinalIgnoreCase) ||
            v.Contains("progress", StringComparison.OrdinalIgnoreCase))
            return InProgress;
        if (v.Contains("ยังไม่", StringComparison.Ordinal) ||
            string.Equals(v, "todo", StringComparison.OrdinalIgnoreCase))
            return NotStarted;
        return v;
    }
}
