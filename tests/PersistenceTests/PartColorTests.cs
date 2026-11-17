using System.Text.Json;
using TableNote;

public static partial class T
{
    public static void TestPartColorsRoundTrip()
    {
        Ok("PartColors", PartColorOps.NormalizeHex("#abc") == "#AABBCC", PartColorOps.NormalizeHex("#abc") ?? "");
        Ok("PartColors", PartColorOps.NormalizeHex("rgb(1, 2, 3)") == "#010203", "");
        Ok("PartColors", PartColorOps.NormalizeHex("nope") == null, "");
        Ok("PartColors", PartColorKeys.Alert("box-1") == "alert:box-1", "");
        Ok("PartColors", PartColorOps.CleanId(" box-1 ") == "box-1" && PartColorOps.CleanId("a b") == null, "");

        // Version 5 still opens, and the first save becomes version 7 without dropping the table.
        var (v5load, dir5) = LoadJson("p5", "{\"version\":5,\"columns\":[\"Title\",\"Details\",\"Status\",\"Due\"],\"rows\":[[\"a\",\"b\",\"\",\"\"]],\"rowIds\":[\"r1\"],\"columnRoles\":[\"title\",\"detail\",\"status\",\"due\"]}");
        var v5 = v5load.Document;
        try
        {
            Ok("PartColors", v5load.Warning == null && v5.GetCell(0, 0) == "a", "v5 opens");
            NoteStore.Save(Path.Combine(dir5, "note.json"), v5);
            var again5 = NoteStore.Load(Path.Combine(dir5, "note.json")).Document;
            Ok("PartColors", again5.Version == 5 && again5.GetCell(0, 0) == "a", "v5 stays v5");
        }
        finally { try { Directory.Delete(dir5, true); } catch { /* temp */ } }

        // Version 6 (alert boxes) opens, keeps unknown box fields, and takes color / textColor by id.
        const string v6 = """
            {"version":6,"columns":["Title","Details","Status","Due"],"rows":[["a","b","",""]],"rowIds":["r1"],
            "columnRoles":["title","detail","status","due"],
            "alertBoxes":[
              {"id":"box-1","title":"Hello","text":"One","pinned":true,"color":"#abc","textColor":"nope"},
              {"id":"box-2","title":"Second","text":"Two","color":"#112233"}
            ]}
            """;
        var (load6, dir) = LoadJson("p6", v6);
        var loaded = load6.Document;
        try
        {
            Ok("PartColors", load6.Warning == null && loaded.GetCell(0, 0) == "a", "v6 opens and keeps the table");
            Ok("PartColors", loaded.ExtraFields.Keys.Any(k => string.Equals(k, "alertBoxes", StringComparison.OrdinalIgnoreCase)), "alertBoxes kept");
            Ok("PartColors", loaded.PartColors.TryGetValue("alert:box-1", out var b1) && b1.Background == "#AABBCC" && b1.Foreground == null, "bad textColor dropped");
            Ok("PartColors", loaded.PartColors["alert:box-2"].Background == "#112233", "box color imported");
            Ok("PartColors", load6.Issues.Any(i => i.Contains("textColor")), "bad textColor logged");

            PartColorOps.Assign(loaded.PartColors, PartColorKeys.Cell("r1", "c1"), "#ff00aa", "");
            PartColorOps.Assign(loaded.PartColors, PartColorKeys.Alert("box-2"), "#00ff00", "#010203");
            PartColorOps.Assign(loaded.PartColors, PartColorKeys.Alert("box-1"), null, null); // reset: drop the box color
            NoteStore.Save(Path.Combine(dir, "note.json"), loaded);

            using var saved = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "note.json")));
            var root = saved.RootElement;
            Ok("PartColors", root.GetProperty("version").GetInt32() == 6, "v6 file stays v6");
            Ok("PartColors", root.GetProperty("partColors").GetProperty("cell:r1:c1").GetProperty("background").GetString() == "#FF00AA", "cell color in note.json");
            var boxes = root.GetProperty("alertBoxes").EnumerateArray().ToList();
            var box1 = boxes.Single(b => b.GetProperty("id").GetString() == "box-1");
            var box2 = boxes.Single(b => b.GetProperty("id").GetString() == "box-2");
            Ok("PartColors", box1.GetProperty("title").GetString() == "Hello" && box1.GetProperty("text").GetString() == "One" && box1.GetProperty("pinned").GetBoolean(), "box fields other than color survive");
            Ok("PartColors", !box1.TryGetProperty("color", out _) && !box1.TryGetProperty("textColor", out _), "reset removes color from the box");
            Ok("PartColors", box2.GetProperty("color").GetString() == "#00FF00" && box2.GetProperty("textColor").GetString() == "#010203" && box2.GetProperty("title").GetString() == "Second", "color written on the box");

            var reloaded = NoteStore.Load(Path.Combine(dir, "note.json")).Document;
            Ok("PartColors", reloaded.Version == 6 && reloaded.PartColors["alert:box-2"].Foreground == "#010203" && reloaded.GetCell(0, 0) == "a", "v6 reloads colors and the table");
        }
        finally { try { Directory.Delete(dir, true); } catch { /* temp */ } }

        // A newer file is not forced down to 7, and a missing color field is fine.
        var (load8, dir8) = LoadJson("p8", "{\"version\":8,\"columns\":[\"Title\"],\"rows\":[[\"z\"]],\"rowIds\":[\"r1\"],\"future\":1,\"alertBoxes\":[{\"id\":\"box-9\",\"title\":\"T\",\"text\":\"\"}]}");
        var v8 = load8.Document;
        try
        {
            Ok("PartColors", v8.Version == 8 && !v8.PartColors.ContainsKey("alert:box-9"), "v8 opens, missing color is the default");
            NoteStore.Save(Path.Combine(dir8, "note.json"), v8);
            using var saved8 = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir8, "note.json")));
            Ok("PartColors", saved8.RootElement.GetProperty("version").GetInt32() == 8 && saved8.RootElement.GetProperty("future").GetInt32() == 1, "newer version and unknown field kept");
            Ok("PartColors", saved8.RootElement.GetProperty("alertBoxes")[0].GetProperty("title").GetString() == "T", "box with no color still saved");
        }
        finally { try { Directory.Delete(dir8, true); } catch { /* temp */ } }
    }
}
