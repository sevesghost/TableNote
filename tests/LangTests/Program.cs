// v2.4.2: language files. Every key must exist in every language with the same {n} placeholders,
// every key the code asks for must exist in en.json, and a broken language file must never break the app.
using System.Text.RegularExpressions;
using TableNote;

int fail = 0;
void Ok(bool c, string name, string detail = "") { Console.WriteLine((c ? "PASS " : "FAIL ") + name + (c || detail == "" ? "" : " — " + detail)); if (!c) fail++; }
string Root() { var d = new DirectoryInfo(AppContext.BaseDirectory); while (d != null && !File.Exists(Path.Combine(d.FullName, "TableNote.csproj"))) d = d.Parent; return d?.FullName ?? throw new Exception("TableNote.csproj not found"); }
var root = Root();
var files = Directory.GetFiles(Path.Combine(root, "lang"), "*.json").OrderBy(f => f, StringComparer.Ordinal).ToList();
var tables = files.ToDictionary(f => Path.GetFileNameWithoutExtension(f), f => Localizer.Parse(File.ReadAllText(f)));
var en = tables["en"];

// 1. files on disk == languages embedded in the exe
Ok(tables.ContainsKey("en") && tables.ContainsKey("th"), "lang/en.json and lang/th.json exist");
Ok(Localizer.Supported.SequenceEqual(tables.Keys.OrderBy(c => c == "en" ? 0 : 1).ThenBy(c => c, StringComparer.Ordinal)), "every lang/*.json is embedded", string.Join(",", Localizer.Supported));
Ok(Localizer.Supported[0] == "en", "English is listed first");
foreach (var (code, t) in tables)
    Ok(Localizer.Table(code).Count == t.Count && t.All(kv => Localizer.Table(code).TryGetValue(kv.Key, out var v) && v == kv.Value), $"{code}: embedded copy matches file");

// 2. same keys and placeholders in every language
var ph = new Regex(@"\{(\d+)(?:[,:][^}]*)?\}");
string Sig(string s) => string.Join(",", ph.Matches(s).Select(m => m.Groups[1].Value).Distinct().OrderBy(x => x));
foreach (var (code, t) in tables.Where(kv => kv.Key != "en"))
{
    var missing = en.Keys.Where(k => !t.ContainsKey(k)).ToList();
    var extra = t.Keys.Where(k => !en.ContainsKey(k)).ToList();
    Ok(missing.Count == 0, $"{code}: has every key of en.json", string.Join(", ", missing));
    Ok(extra.Count == 0, $"{code}: no keys that en.json lacks", string.Join(", ", extra));
    var bad = en.Keys.Where(k => t.ContainsKey(k) && Sig(en[k]) != Sig(t[k])).ToList();
    Ok(bad.Count == 0, $"{code}: same {{n}} placeholders as English", string.Join(", ", bad));
    var empty = t.Where(kv => kv.Value.Trim().Length == 0 && en[kv.Key].Trim().Length > 0).Select(kv => kv.Key).ToList();
    Ok(empty.Count == 0, $"{code}: no empty translations", string.Join(", ", empty));
}
foreach (var (code, t) in tables)
    Ok(t.TryGetValue("_language.name", out var n) && n.Trim().Length > 0, $"{code}: has _language.name");

// 3. every key the code asks for exists in en.json
var used = new SortedSet<string>(StringComparer.Ordinal);
foreach (var f in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories).Where(f => !f.Contains("/obj/") && !f.Contains("/bin/") && !f.Contains("/tests/")))
    foreach (Match m in Regex.Matches(File.ReadAllText(f), "(?:Localizer\\.)?\\bT\\(\"([a-z_][a-z_.0-9]*)\""))
        if (!m.Groups[1].Value.EndsWith(".")) used.Add(m.Groups[1].Value);
foreach (var k in new[] { "text", "number", "choice", "check" }) used.Add("coltype." + k);
foreach (var p in RowStyleOps.Palette) used.Add("colour." + p.Key);
var notInEn = used.Where(k => !en.ContainsKey(k)).ToList();
Ok(used.Count > 100, "found the keys used in the code", used.Count.ToString());
Ok(notInEn.Count == 0, "every key used in the code is in en.json", string.Join(", ", notInEn));
var english = tables["en"].Where(kv => kv.Key != "tray.language" && Regex.IsMatch(kv.Value, "[\u0E00-\u0E7F]")).Select(kv => kv.Key).ToList();
Ok(english.Count == 0, "en.json has no Thai text (except the bilingual Language / ภาษา menu)", string.Join(", ", english));

// 4. runtime behaviour
Ok(!Localizer.SetLanguage("xx") && Localizer.Current == "en", "unknown language is refused, no change");
Ok(Localizer.SetLanguage("TH ") && Localizer.Current == "th", "SetLanguage trims and ignores case");
Ok(Localizer.T("task_status.done") == tables["th"]["task_status.done"], "T() reads th.json");
var missingKey = "no.such." + "key"; // built at runtime so source scanners do not treat it as a real key
Ok(Localizer.T(missingKey) == missingKey, "unknown key shows the key itself");
Ok(Localizer.LanguageName("th") == "ไทย" && Localizer.LanguageName("en") == "English", "language names from _language.name");
Ok(Localizer.Next("en") == "th" && Localizer.Next("th") == "en", "EN/TH button cycles through the languages");
Localizer.SetLanguage("en");
Ok(Localizer.T("task_status.done") == "Done", "T() reads en.json");

// 5. bad files: parse errors are reported, not thrown at runtime; non-string values skipped
var issues = new List<string>();
var p1 = Localizer.Parse("{ \"a\": \"x\", \"n\": 5, // comment\n \"b\": \"y\", }", issues);
Ok(p1.Count == 2 && issues.Count == 1, "non-string value skipped, comments and trailing comma allowed");
bool threw = false; try { Localizer.Parse("[1,2]"); } catch (FormatException) { threw = true; }
Ok(threw, "a language file that is not an object is rejected");

// 6. settings.json keeps a saved language; unknown codes fall back to English
var dir = Directory.CreateTempSubdirectory("tn-lang-").FullName; var note = Path.Combine(dir, "note.json");
File.WriteAllText(Path.Combine(dir, "settings.json"), "{ \"language\": \"th\", \"uiScale\": 120 }");
Ok(AppSettings.Load(note).Language == "th", "existing users who chose TH stay TH");
File.WriteAllText(Path.Combine(dir, "settings.json"), "{ \"language\": \"de\" }");
Ok(AppSettings.Load(note).Language == "en", "language that is not embedded → English");
File.Delete(Path.Combine(dir, "settings.json"));
Ok(AppSettings.Load(note).Language == "en", "no settings.json → English (default)");
Directory.Delete(dir, true);

Console.WriteLine(fail == 0 ? "LangTests: all passed" : $"LangTests: {fail} failed");
return fail;
