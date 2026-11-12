namespace TableNote;

public readonly record struct StartupSyncResult(bool Ok, bool Enabled, string? Error)
{
    public static StartupSyncResult Failed(string error) => new(false, false, error);
}
