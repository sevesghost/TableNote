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
            Ok("PartColors", again5.Version == NoteDocument.CurrentVersion && again5.GetCell(0, 0) == "a", "v5 data stays, file becomes current");
        }
        finally { try { Directory.Delete(dir5, true); } catch { /* temp */ } }

        // Version 6 still opens. alertBoxes is left alone (it may move to a known field later).
        // Alert colors come only from partColors, not from color/textColor on the box.
        const string v6 = """
            {"version":6,"columns":["Title","Details","Status","Due"],"rows":[["a","b","",""]],"rowIds":["r1"],
            "columnRoles":["title","detail","status","due"],
            "partColors":{"alert:box-2":{"background":"#112233","foreground":"#010203"}},
            "alertBoxes":[
              {"id":"box-1","title":"Hello","text":"One","pinned":true,"color":"#abc","textColor":"nope"},
              {"id":"box-2","title":"Second","text":"Two","color":"#ffffff"}
            ]}
            """;
        var (load6, dir) = LoadJson("p6", v6);
        var loaded = load6.Document;
        try
        {
            Ok("PartColors", load6.Warning == null && loaded.GetCell(0, 0) == "a", "v6 opens and keeps the table");
            Ok("PartColors", loaded.PartColors["alert:box-2"].Background == "#112233" && loaded.PartColors["alert:box-2"].Foreground == "#010203", "alert color comes from partColors");
            Ok("PartColors", !loaded.PartColors.ContainsKey("alert:box-1"), "a color only on the box is not the stored color");
            PartColorOps.Assign(loaded.PartColors, PartColorKeys.Cell("r1", "c1"), "#ff00aa", "");
            NoteStore.Save(Path.Combine(dir, "note.json"), loaded);

            using var saved = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "note.json")));
            var root = saved.RootElement;
            Ok("PartColors", root.GetProperty("version").GetInt32() == 6, "v6 file stays v6");
            Ok("PartColors", root.GetProperty("partColors").GetProperty("cell:r1:c1").GetProperty("background").GetString() == "#FF00AA", "cell color in note.json");
            Ok("PartColors", root.GetProperty("partColors").GetProperty("alert:box-2").GetProperty("foreground").GetString() == "#010203", "alert color saved in partColors");
            var boxes = root.GetProperty("alertBoxes").EnumerateArray().ToList();
            var box1 = boxes.Single(b => b.GetProperty("id").GetString() == "box-1");
            var box2 = boxes.Single(b => b.GetProperty("id").GetString() == "box-2");
            Ok("PartColors", box1.GetProperty("title").GetString() == "Hello" && box1.GetProperty("text").GetString() == "One" && box1.GetProperty("pinned").GetBoolean() && box1.GetProperty("color").GetString() == "#abc" && box1.GetProperty("textColor").GetString() == "nope", "box left unchanged, including its own color fields");
            Ok("PartColors", box2.GetProperty("title").GetString() == "Second" && box2.GetProperty("color").GetString() == "#ffffff", "box color is not overwritten from partColors");

            var reloaded = NoteStore.Load(Path.Combine(dir, "note.json")).Document;
            Ok("PartColors", reloaded.Version == 6 && reloaded.PartColors["alert:box-2"].Foreground == "#010203" && reloaded.GetCell(0, 0) == "a", "v6 reloads colors and the table");
        }
        finally { try { Directory.Delete(dir, true); } catch { /* temp */ } }

        // After PR #3, alertBoxes is a known list (doc.AlertBoxes), not an ExtraFields entry.
        // Colors must still round-trip through partColors alone.
        const string post3 = """
            {"version":5,"columns":["Title"],"rows":[["z"]],"rowIds":["r1"],
            "partColors":{"alert:box-9":{"background":"#00ff00","foreground":"#111111"}}}
            """;
        var (postLoad, dirPost) = LoadJson("post3", post3);
        var post = postLoad.Document;
        try
        {
            Ok("PartColors", !post.ExtraFields.Keys.Any(k => string.Equals(k, "alertBoxes", StringComparison.OrdinalIgnoreCase)), "no alertBoxes extra field");
            Ok("PartColors", post.PartColors["alert:box-9"].Background == "#00FF00" && post.PartColors["alert:box-9"].Foreground == "#111111", "alert color loaded with no alertBoxes");
            post.ExtraFields.Clear(); // known-field alertBoxes would not be here
            NoteStore.Save(Path.Combine(dirPost, "note.json"), post);
            var again = NoteStore.Load(Path.Combine(dirPost, "note.json")).Document;
            Ok("PartColors", again.PartColors.TryGetValue("alert:box-9", out var kept) && kept.Background == "#00FF00" && kept.Foreground == "#111111", "alert color survives save when alertBoxes is not in ExtraFields");
            Ok("PartColors", !again.ExtraFields.Keys.Any(k => string.Equals(k, "alertBoxes", StringComparison.OrdinalIgnoreCase)), "save did not invent alertBoxes");
        }
        finally { try { Directory.Delete(dirPost, true); } catch { /* temp */ } }

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
