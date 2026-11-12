using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TableNote;

/// <summary>
/// UI strings. v2.4.2: the texts live in <c>lang/&lt;code&gt;.json</c> (embedded in the exe as
/// <c>TableNote.lang.&lt;code&gt;.json</c>), one flat JSON object of key → text per language. To add a language, add
/// <c>lang/xx.json</c> with the same keys as <c>lang/en.json</c> and rebuild; it then appears in the language menu.
/// Data in note.json is language-neutral (status/recurrence codes); only what is SHOWN goes through here.
/// Lookup order: current language → English → the key itself (each missing key is logged once).
/// </summary>
public static class Localizer
{
    public const string Fallback = "en";
    private const string ResourcePrefix = "TableNote.lang.", ResourceSuffix = ".json";
    private static readonly Regex CodePattern = new("^[a-z]{2,3}(-[a-z0-9]{2,8})?$", RegexOptions.CultureInvariant);

    private static readonly Dictionary<string, Dictionary<string, string>> Tables = LoadEmbedded(typeof(Localizer).Assembly);

    // Kept as fields with these names: tests and older tooling read them by reflection.
    private static readonly Dictionary<string, string> En = TableOrEmpty("en");
    private static readonly Dictionary<string, string> Th = TableOrEmpty("th");

    public static string Current { get; private set; } = Fallback;
    public static event Action? LanguageChanged;

    /// <summary>Every embedded language, English first, then by code.</summary>
    public static readonly string[] Supported =
        Tables.Keys.OrderBy(c => c == Fallback ? 0 : 1).ThenBy(c => c, StringComparer.Ordinal).ToArray();

    public static bool IsSupported(string? code) => code != null && Tables.ContainsKey(code.Trim().ToLowerInvariant());

    /// <summary>The language's own name from its <c>_language.name</c> key ("English", "ไทย"); the code if missing.</summary>
    public static string LanguageName(string code) =>
        Tables.TryGetValue(code, out var t) && t.TryGetValue("_language.name", out var n) && n.Trim().Length > 0 ? n : code;

    /// <summary>The language after <paramref name="code"/> in <see cref="Supported"/> (wraps round) — for the EN/TH button.</summary>
    public static string Next(string code)
    {
        var i = Array.IndexOf(Supported, code);
        return Supported.Length == 0 ? Fallback : Supported[(i + 1) % Supported.Length];
    }

    /// <summary>Read-only view of one language's table (for tests and tools); empty when the language is unknown.</summary>
    public static IReadOnlyDictionary<string, string> Table(string code) => TableOrEmpty(code);

    /// <summary>Switch UI language (any code in <see cref="Supported"/>). Never touches note.json.</summary>
    /// <returns>false (and no change, no exception) for an unknown code.</returns>
    public static bool SetLanguage(string code)
    {
        var c = (code ?? "").Trim().ToLowerInvariant();
        if (!Tables.ContainsKey(c)) return false;
        if (c == Current) return true;
        Current = c;
        LanguageChanged?.Invoke();
        return true;
    }

    private static readonly HashSet<string> MissingLogged = new(StringComparer.Ordinal);

    /// <summary>Current language → English → the key itself (logged once per key).</summary>
    public static string T(string key)
    {
        if (Current != Fallback)
        {
            if (TableOrEmpty(Current).TryGetValue(key, out var s)) return s;
            lock (MissingLogged) if (MissingLogged.Add(Current + ":" + key)) AppLog.Write("i18n", $"missing {Current} key '{key}' → English");
        }
        if (En.TryGetValue(key, out var e)) return e;
        lock (MissingLogged) if (MissingLogged.Add("en:" + key)) AppLog.Write("i18n", $"missing key '{key}'");
        return key;
    }

    public static string T(string key, params object[] args)
    {
        var fmt = T(key);
        try { return string.Format(CultureInfo.InvariantCulture, fmt, args); }
        catch (FormatException)
        {
            // A translation with a broken {n} placeholder must not crash the app: fall back to English, then the raw text.
            lock (MissingLogged) if (MissingLogged.Add("fmt:" + Current + ":" + key)) AppLog.Write("i18n", $"bad format in {Current} key '{key}'");
            try { return En.TryGetValue(key, out var e) ? string.Format(CultureInfo.InvariantCulture, e, args) : fmt; }
            catch (FormatException) { return fmt; }
        }
    }

    /// <summary>Whole-sentence duration ("5 minutes" / "1 ชั่วโมง 5 นาที") with en singular/plural.</summary>
    public static string Duration(int minutes)
    {
        minutes = Math.Max(1, minutes);
        int d = minutes / 1440, h = minutes % 1440 / 60, m = minutes % 60;
        var parts = new List<string>();
        if (Current == "th")
        {
            if (d > 0) parts.Add($"{d} วัน");
            if (h > 0) parts.Add($"{h} ชั่วโมง");
            if (m > 0) parts.Add($"{m} นาที");
        }
        else
        {
            if (d > 0) parts.Add(d == 1 ? "1 day" : $"{d} days");
            if (h > 0) parts.Add(h == 1 ? "1 hour" : $"{h} hours");
            if (m > 0) parts.Add(m == 1 ? "1 minute" : $"{m} minutes");
        }
        return string.Join(" ", parts);
    }

    private static Dictionary<string, string> TableOrEmpty(string code) =>
        Tables != null && Tables.TryGetValue(code, out var t) ? t : new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Parse one language file: a flat JSON object of string values. Non-string values are skipped.</summary>
    public static Dictionary<string, string> Parse(string json, List<string>? issues = null)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        if (doc.RootElement.ValueKind != JsonValueKind.Object) throw new FormatException("language file is not a JSON object");
        foreach (var p in doc.RootElement.EnumerateObject())
        {
            if (p.Value.ValueKind == JsonValueKind.String) map[p.Name] = p.Value.GetString() ?? "";
            else issues?.Add($"key '{p.Name}' is not a string");
        }
        return map;
    }

    /// <summary>All <c>TableNote.lang.*.json</c> resources of <paramref name="asm"/>. A broken file is logged and skipped.</summary>
    internal static Dictionary<string, Dictionary<string, string>> LoadEmbedded(Assembly asm)
    {
        var tables = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (var name in asm.GetManifestResourceNames())
        {
            if (!name.StartsWith(ResourcePrefix, StringComparison.Ordinal) || !name.EndsWith(ResourceSuffix, StringComparison.Ordinal)) continue;
            var code = name[ResourcePrefix.Length..^ResourceSuffix.Length].ToLowerInvariant();
            if (!CodePattern.IsMatch(code)) { SafeLog($"skipped language resource '{name}' (bad code)"); continue; }
            try
            {
                using var s = asm.GetManifestResourceStream(name)!;
                using var r = new StreamReader(s, System.Text.Encoding.UTF8);
                var issues = new List<string>();
                tables[code] = Parse(r.ReadToEnd(), issues);
                foreach (var i in issues) SafeLog($"{code}.json: {i}");
            }
            catch (Exception ex) { SafeLog($"{code}.json unreadable ({ex.Message}) → skipped"); }
        }
        if (!tables.ContainsKey(Fallback)) SafeLog("lang/en.json missing → keys are shown as-is");
        return tables;
    }

    private static void SafeLog(string msg) { try { AppLog.Write("i18n", msg); } catch { /* logging must never break startup */ } }
}

/// <summary>Per-user UI settings in settings.json next to note.json (never in note.json).</summary>
public sealed class AppSettings
{
    public string Language { get; set; } = "en";
    public int Scale { get; set; } = 100;
    public const int MinScale = 80, MaxScale = 200, ScaleStep = 10;
    public static int ClampScale(int s) => s < MinScale || s > MaxScale ? 100 : (int)Math.Round(s / (double)ScaleStep) * ScaleStep;

    public static string PathFor(string notePath) => Path.Combine(Path.GetDirectoryName(notePath) ?? ".", "settings.json");

    /// <summary>Missing/bad file → en, 100 %. A bad file is backed up (settings.json.bad-&lt;ts&gt;.bak). Never throws.</summary>
    public static AppSettings Load(string notePath)
    {
        var s = new AppSettings();
        var p = PathFor(notePath);
        try
        {
            if (!File.Exists(p)) return s;
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(p));
            var root = doc.RootElement;
            if (root.ValueKind != System.Text.Json.JsonValueKind.Object) throw new FormatException("not an object");
            foreach (var prop in root.EnumerateObject())
            {
                if (prop.NameEquals("language") && prop.Value.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    var l = prop.Value.GetString()?.Trim().ToLowerInvariant();
                    s.Language = l != null && Localizer.IsSupported(l) ? l : Localizer.Fallback; // v2.4.2: any embedded language
                }
                else if ((prop.NameEquals("uiScale") || prop.NameEquals("scale")) && prop.Value.ValueKind == System.Text.Json.JsonValueKind.Number && prop.Value.TryGetInt32(out var sc))
                    s.Scale = ClampScale(sc); // out of range / not a number → 100 (Z7)
            }
        }
        catch (Exception ex)
        {
            try { File.Copy(p, p + $".bad-{DateTime.Now:yyyyMMddHHmmss}.bak", overwrite: false); } catch { /* ignore */ }
            AppLog.Write("settings", $"settings.json unreadable ({ex.Message}) → en, 100 % (backed up)");
            s = new AppSettings();
        }
        return s;
    }

    public void Save(string notePath)
    {
        try
        {
            var p = PathFor(notePath);
            var dir = Path.GetDirectoryName(p);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var tmp = p + ".tmp";
            File.WriteAllText(tmp, $"{{\n  \"language\": \"{Language}\",\n  \"uiScale\": {Scale}\n}}\n");
            File.Move(tmp, p, overwrite: true);
        }
        catch (Exception ex) { AppLog.Write("settings", "save failed: " + ex.Message); }
    }
}
