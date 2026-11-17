namespace TableNote;

/// <summary>
/// Temporary print files. They stay after the preview closes so a Windows print spooler can
/// still read them, and files older than an hour are removed on the next start and the next print.
/// </summary>
public static class ViewPrintFiles
{
    public const string FolderName = "TableNote-print";
    public static readonly TimeSpan DefaultMaxAge = TimeSpan.FromHours(1);

    public static string Folder => Path.Combine(Path.GetTempPath(), FolderName);

    /// <summary>Deletes files in <see cref="Folder"/> older than <paramref name="maxAge"/>. Returns how many were removed.</summary>
    public static int CleanupExpired(TimeSpan? maxAge = null)
    {
        try
        {
            var dir = Folder;
            if (!Directory.Exists(dir)) return 0;
            var cutoff = DateTime.UtcNow - (maxAge ?? DefaultMaxAge);
            var removed = 0;
            foreach (var file in Directory.EnumerateFiles(dir))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(file) >= cutoff) continue;
                    File.Delete(file);
                    removed++;
                }
                catch
                {
                    // Still open in a print spooler, or already gone.
                }
            }
            return removed;
        }
        catch
        {
            return 0;
        }
    }

    public static string Write(byte[] bytes, string extension)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        Directory.CreateDirectory(Folder);
        var ext = string.IsNullOrEmpty(extension) ? "" : extension[0] == '.' ? extension : "." + extension;
        var path = Path.Combine(Folder, "tn-" + Guid.NewGuid().ToString("N") + ext);
        File.WriteAllBytes(path, bytes);
        return path;
    }
}
