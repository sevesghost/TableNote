namespace TableNote;

/// <summary>
/// The ONE writer of tablenote.log (next to note.json). Rotation: above 256 KB the log is renamed to
/// tablenote.1.log (replacing the previous one) and a new log starts → at most 2 files, ≤ 512 KB total,
/// latest lines always kept. Best-effort; never throws.
/// </summary>
public static class AppLog
{
    public const long RotateBytes = 256 * 1024;
    public const int MaxLinesPerWrite = 20;
    public const int MaxLineChars = 1000;
    private static readonly object Gate = new();
    public static string? PathOverride { get; set; }

    public static string LogPath =>
        PathOverride ?? Path.Combine(Path.GetDirectoryName(NoteStore.DefaultPath()) ?? ".", "tablenote.log");

    public static void Write(string area, string message) => WriteLines(LogPath, area, [message]);

    /// <summary>Write to the log next to a specific note.json (load issues for that file).</summary>
    public static void WriteFor(string notePath, string area, IEnumerable<string> messages) =>
        WriteLines(Path.Combine(Path.GetDirectoryName(notePath) ?? ".", "tablenote.log"), area, messages);

    private static void WriteLines(string p, string area, IEnumerable<string> messages)
    {
        try
        {
            var stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            // At most MaxLinesPerWrite lines per call (+ "and N more"), each line at most MaxLineChars.
            var all = messages.Distinct().ToList();
            var lines = all.Take(MaxLinesPerWrite)
                .Select(m => $"{stamp} {area}: {(m.Length > MaxLineChars ? m[..MaxLineChars] + "…" : m)}").ToList();
            if (all.Count > MaxLinesPerWrite) lines.Add($"{stamp} {area}: … and {all.Count - MaxLinesPerWrite} more");
            foreach (var l in lines) System.Diagnostics.Trace.WriteLine(l);
            lock (Gate)
            {
                var dir = Path.GetDirectoryName(p);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                var incoming = lines.Sum(l => System.Text.Encoding.UTF8.GetByteCount(l) + 2L);
                if (File.Exists(p) && new FileInfo(p).Length + incoming > RotateBytes)
                {
                    var old = Path.Combine(dir ?? ".", Path.GetFileNameWithoutExtension(p) + ".1" + Path.GetExtension(p));
                    File.Move(p, old, overwrite: true);
                }
                File.AppendAllLines(p, lines);
            }
        }
        catch { /* logging must never break the app */ }
    }
}

/// <summary>
/// Spec P7: keeps the newest <see cref="Keep"/> rolling note.json*.bak files (broken-file copies), oldest deleted first.
/// NEVER deleted and not counted: note.v&lt;N&gt;*.bak (version backups), note.overlimit-*.bak (may be the only
/// full copy) and every other file.
/// </summary>
public static class BackupRetention
{
    public const int Keep = 5;

    public static bool IsRolling(string fileName) =>
        fileName.StartsWith("note.json", StringComparison.OrdinalIgnoreCase) &&
        fileName.EndsWith(".bak", StringComparison.OrdinalIgnoreCase);

    public static int Prune(string dir)
    {
        var deleted = 0;
        try
        {
            var rolling = new DirectoryInfo(dir).GetFiles("note.json*.bak")
                .Where(f => IsRolling(f.Name))
                .OrderByDescending(f => f.LastWriteTimeUtc).ThenByDescending(f => f.Name, StringComparer.Ordinal)
                .ToList();
            foreach (var f in rolling.Skip(Keep))
            {
                try { f.Delete(); deleted++; AppLog.Write("backup", $"removed old backup {f.Name} (keeping newest {Keep} note.json*.bak)"); }
                catch { /* ignore */ }
            }
        }
        catch { /* ignore */ }
        return deleted;
    }
}
