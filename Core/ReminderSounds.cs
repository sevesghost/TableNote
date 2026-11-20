using System.Buffers.Binary;

namespace TableNote;

/// <summary>2.6.0: which sound a reminder plays. The choice is kept in settings.json ("reminderSound": "" = the Windows
/// warning sound, a file name in C:\Windows\Media, or the full path of the user's own .wav) with "reminderVolume" (0–100).</summary>
public static class ReminderSounds
{
    public const string Default = "";
    public const int DefaultVolume = 50;
    /// <summary>Above this the sound dialog asks "are you sure?".</summary>
    public const int LoudFrom = 51;

    /// <summary>Windows' own sounds offered in the list (only those that exist on this PC are shown).</summary>
    public static readonly (string File, string LabelKey, int Number)[] WindowsSounds =
    [
        ("Windows Notify Calendar.wav", "sound.calendar", 0),
        ("Windows Notify System Generic.wav", "sound.notify", 0),
        ("Windows Notify Messaging.wav", "sound.message", 0),
        ("Windows Notify Email.wav", "sound.email", 0),
        ("chimes.wav", "sound.chimes", 0),
        ("chord.wav", "sound.chord", 0),
        ("ding.wav", "sound.ding", 0),
        ("tada.wav", "sound.tada", 0),
        ("Alarm01.wav", "sound.alarm", 1), ("Alarm02.wav", "sound.alarm", 2), ("Alarm03.wav", "sound.alarm", 3),
        ("Alarm04.wav", "sound.alarm", 4), ("Alarm05.wav", "sound.alarm", 5), ("Alarm06.wav", "sound.alarm", 6),
        ("Alarm07.wav", "sound.alarm", 7), ("Alarm08.wav", "sound.alarm", 8), ("Alarm09.wav", "sound.alarm", 9),
        ("Alarm10.wav", "sound.alarm", 10),
        ("Ring01.wav", "sound.ring", 1), ("Ring02.wav", "sound.ring", 2), ("Ring03.wav", "sound.ring", 3),
        ("Ring04.wav", "sound.ring", 4), ("Ring05.wav", "sound.ring", 5), ("Ring06.wav", "sound.ring", 6)
    ];

    /// <summary>Test seam: the folder of Windows' sounds.</summary>
    public static string? MediaFolderOverride { get; set; }

    public static string MediaFolder => MediaFolderOverride ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows) is { Length: > 0 } w ? w : @"C:\Windows", "Media");

    public sealed record Choice(string Id, string Label);

    /// <summary>The list for the dialog: the Windows warning sound first, then the Windows sounds that exist here.</summary>
    public static List<Choice> Available()
    {
        var list = new List<Choice> { new(Default, Localizer.T("sound.default")) };
        foreach (var (file, key, n) in WindowsSounds)
            if (File.Exists(Path.Combine(MediaFolder, file)))
                list.Add(new Choice(file, n > 0 ? Localizer.T(key, n) : Localizer.T(key)));
        return list;
    }

    public static bool IsOwnFile(string id) => Path.IsPathRooted(id);

    public static string LabelFor(string id) =>
        id == Default ? Localizer.T("sound.default")
        : IsOwnFile(id) ? Localizer.T("sound.own", Path.GetFileName(id))
        : Available().FirstOrDefault(c => c.Id == id)?.Label ?? id;

    /// <summary>The .wav to play for a choice, or null (then the system warning sound plays at its own volume).</summary>
    public static string? Resolve(string? id, Func<string?>? systemWarning = null)
    {
        id ??= Default;
        if (id == Default)
        {
            var warning = (systemWarning ?? SystemWarningSound)();
            if (!string.IsNullOrEmpty(warning) && File.Exists(warning)) return warning;
            var fallback = Path.Combine(MediaFolder, "Windows Exclamation.wav");
            return File.Exists(fallback) ? fallback : null;
        }
        var path = IsOwnFile(id) ? id : Path.Combine(MediaFolder, id);
        return File.Exists(path) ? path : null;
    }

    /// <summary>The user's sound for Windows' "Exclamation" event (what the reminders played up to 2.5), or null.</summary>
    public static string? SystemWarningSound()
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"AppEvents\Schemes\Apps\.Default\SystemExclamation\.Current");
            return key?.GetValue("") is string s && s.Trim().Length > 0 ? Environment.ExpandEnvironmentVariables(s.Trim()) : null;
        }
        catch { return null; }
    }

    public static int ClampVolume(int v) => Math.Clamp(v, 0, 100);

    /// <summary>
    /// A copy of a PCM / float .wav with every sample scaled to <paramref name="volume"/> % (linear, 100 = as recorded).
    /// Null when the file is not a plain PCM (8/16/24/32-bit) or 32-bit float wave (then it plays unscaled).
    /// </summary>
    public static byte[]? Scaled(byte[] wav, int volume)
    {
        if (wav.Length < 44 || !wav.AsSpan(0, 4).SequenceEqual("RIFF"u8) || !wav.AsSpan(8, 4).SequenceEqual("WAVE"u8)) return null;
        int format = -1, bits = 0, dataAt = -1, dataLen = 0;
        for (var at = 12; at + 8 <= wav.Length;)
        {
            var id = wav.AsSpan(at, 4);
            var len = BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(at + 4, 4));
            if (len < 0 || at + 8 + (long)len > wav.Length) len = wav.Length - at - 8; // a short last chunk: take what is there
            if (id.SequenceEqual("fmt "u8) && len >= 16)
            {
                format = BinaryPrimitives.ReadUInt16LittleEndian(wav.AsSpan(at + 8, 2));
                bits = BinaryPrimitives.ReadUInt16LittleEndian(wav.AsSpan(at + 22, 2));
                if (format == 0xFFFE && len >= 40) format = BinaryPrimitives.ReadUInt16LittleEndian(wav.AsSpan(at + 32, 2)); // extensible: sub-format
            }
            else if (id.SequenceEqual("data"u8)) { dataAt = at + 8; dataLen = len; break; }
            at += 8 + len + (len & 1);
        }
        if (dataAt < 0 || format is not (1 or 3)) return null;
        var f = ClampVolume(volume) / 100.0;
        var copy = (byte[])wav.Clone();
        var data = copy.AsSpan(dataAt, dataLen);
        switch (format, bits)
        {
            case (1, 8):
                for (var i = 0; i < data.Length; i++) data[i] = (byte)Math.Clamp((int)Math.Round((data[i] - 128) * f) + 128, 0, 255);
                break;
            case (1, 16):
                for (var i = 0; i + 1 < data.Length; i += 2)
                    BinaryPrimitives.WriteInt16LittleEndian(data[i..], (short)Math.Clamp(Math.Round(BinaryPrimitives.ReadInt16LittleEndian(data[i..]) * f), short.MinValue, short.MaxValue));
                break;
            case (1, 24):
                for (var i = 0; i + 2 < data.Length; i += 3)
                {
                    var s = data[i] | (data[i + 1] << 8) | ((sbyte)data[i + 2] << 16);
                    var v = (int)Math.Clamp(Math.Round(s * f), -8388608, 8388607);
                    data[i] = (byte)v; data[i + 1] = (byte)(v >> 8); data[i + 2] = (byte)(v >> 16);
                }
                break;
            case (1, 32):
                for (var i = 0; i + 3 < data.Length; i += 4)
                    BinaryPrimitives.WriteInt32LittleEndian(data[i..], (int)Math.Clamp(Math.Round(BinaryPrimitives.ReadInt32LittleEndian(data[i..]) * f), int.MinValue, int.MaxValue));
                break;
            case (3, 32):
                for (var i = 0; i + 3 < data.Length; i += 4)
                    BinaryPrimitives.WriteSingleLittleEndian(data[i..], (float)(BinaryPrimitives.ReadSingleLittleEndian(data[i..]) * f));
                break;
            default:
                return null;
        }
        return copy;
    }
}
