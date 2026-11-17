using System.Text.Json;
using TableNote;

// Alert boxes (feature 2.4): stable ids, order, lenient load, unknown properties kept for a later colour.
public static partial class T
{
    public static void TestAlertBoxMoveOps()
    {
        var boxes = new List<AlertBox>
        {
            AlertBoxOps.Create("A", "1"),
            AlertBoxOps.Create("B", "2"),
            AlertBoxOps.Create("C", "3")
        };
        var ids = boxes.Select(b => b.Id).ToList();
        Ok("new alert ids are distinct", ids.Distinct().Count() == 3 && ids.All(id => id.Length == 32));
        Ok("move down", AlertBoxOps.Move(boxes, 0, 1) && boxes.Select(b => b.Id).SequenceEqual(new[] { ids[1], ids[0], ids[2] }));
        Ok("move up", AlertBoxOps.Move(boxes, 1, 0) && boxes.Select(b => b.Id).SequenceEqual(ids));
        Ok("move to end", AlertBoxOps.Move(boxes, 0, 2) && boxes[2].Id == ids[0]);
        Ok("no-op move", !AlertBoxOps.Move(boxes, 1, 1));
        Ok("drop onto itself is a no-op", AlertBoxOps.TargetForSlot(1, 1, 3) < 0 && AlertBoxOps.TargetForSlot(1, 2, 3) < 0);
        Ok("drop before first", AlertBoxOps.TargetForSlot(2, 0, 3) == 0);
        Ok("drop after last from first", AlertBoxOps.TargetForSlot(0, 3, 3) == 2);
        var ordered = new[] { ids[0], ids[1], ids[2] }.Select(id => boxes.First(b => b.Id == id)).ToList();
        var to = AlertBoxOps.TargetForSlot(0, 3, ordered.Count);
        Ok("slot math matches Move", to == 2 && AlertBoxOps.Move(ordered, 0, to) && ordered.Select(b => b.Id).SequenceEqual(new[] { ids[1], ids[2], ids[0] }));
    }

    public static void TestAlertDeleteConfirmMessage()
    {
        var prev = Localizer.Current;
        try
        {
            Localizer.SetLanguage("en");
            var titled = AlertBoxOps.Create("Remember", "call");
            Ok("confirm names the title and says it can't be undone",
                AlertBoxOps.ConfirmMessage(titled, 0) == "Delete alert box \"Remember\"? This can't be undone.");
            var blank = AlertBoxOps.Create("  ", "\n");
            Ok("blank box uses Alert n",
                AlertBoxOps.ConfirmMessage(blank, 2) == "Delete alert box \"Alert 3\"? This can't be undone.");
            var fromText = AlertBoxOps.Create("", "line one\nline two");
            Ok("empty title uses the first line of text",
                AlertBoxOps.ConfirmLabel(fromText, 0) == "line one");
            var longTitle = AlertBoxOps.Create(new string('a', 50), "");
            Ok("long title is cut at 40",
                AlertBoxOps.ConfirmMessage(longTitle, 0).Contains("\"" + new string('a', 40) + "…\""));
            Localizer.SetLanguage("th");
            var th = AlertBoxOps.ConfirmMessage(titled, 0);
            Ok("thai confirm keeps the title and the placeholder",
                th.Contains("Remember") && th.Contains("กล่องแจ้งเตือน") && !th.Contains("{0}"));
        }
        finally { Localizer.SetLanguage(prev); }
    }

    public static void TestAlertBoxesRoundTripAndUnknownFields()
    {
        var p = Path.Combine(Dir("alerts"), "note.json");
        var d = Sample(2);
        d.AlertBoxes.Add(AlertBoxOps.Create("Remember", "call back"));
        d.AlertBoxes.Add(AlertBoxOps.Create("Later", "line\n2"));
        var ids = d.AlertBoxes.Select(b => b.Id).ToList();
        d.AlertBoxes[0].Extra["color"] = JsonDocument.Parse("\"#112233\"").RootElement.Clone();
        NoteStore.Save(p, d);
        AlertBoxOps.Move(d.AlertBoxes, 0, 1);
        d.AlertBoxes[0].Title = "Later edited";
        NoteStore.Save(p, d);
        var again = NoteStore.Load(p);
        var loaded = again.Document;
        Ok("clean alert file loads with no issues", again.Issues.Count == 0, string.Join(" | ", again.Issues));
        Ok("save → reopen keeps order, text, and ids",
            loaded.AlertBoxes.Count == 2 &&
            loaded.AlertBoxes[0].Id == ids[1] && loaded.AlertBoxes[0].Title == "Later edited" && loaded.AlertBoxes[0].Text == "line\n2" &&
            loaded.AlertBoxes[1].Id == ids[0] && loaded.AlertBoxes[1].Title == "Remember" && loaded.AlertBoxes[1].Text == "call back");
        Ok("unknown color property is kept for a later feature",
            loaded.AlertBoxes[1].Extra.TryGetValue("color", out var color) && color.GetString() == "#112233");
        using var json = JsonDocument.Parse(File.ReadAllText(p));
        var arr = json.RootElement.GetProperty("alertBoxes");
        Ok("format version is 6 and alertBoxes is the saved order",
            json.RootElement.GetProperty("version").GetInt32() == NoteDocument.CurrentVersion &&
            arr.GetArrayLength() == 2 &&
            arr[0].GetProperty("id").GetString() == ids[1] &&
            arr[1].GetProperty("color").GetString() == "#112233");
        Ok("rows were not touched", loaded.Rows.Count == 2 && loaded.Rows[0][0] == "งาน 0");
    }

    public static void TestAlertBoxesMissingAndCorrupt()
    {
        var (old, oldDir) = LoadJson("alert-old", "{\"version\":5,\"columns\":[\"หัวข้อ\",\"รายละเอียด\",\"สถานะ\",\"กำหนดส่ง\"],\"rows\":[[\"a\",\"b\",\"\",\"\"]],\"rowIds\":[\"r1\"],\"columnRoles\":[\"title\",\"detail\",\"status\",\"due\"]}");
        Ok("v5 file with no alertBoxes → empty list, no alert issue",
            old.Document.AlertBoxes.Count == 0 && old.Document.Version == NoteDocument.CurrentVersion && old.Document.Rows[0][0] == "a" &&
            !old.Issues.Any(i => i.StartsWith("alertBoxes", StringComparison.Ordinal)));
        old.Document.AlertBoxes.Add(AlertBoxOps.Create("Hi", "there"));
        var oldPath = Path.Combine(oldDir, "note.json");
        NoteStore.Save(oldPath, old.Document);
        Ok("saving a v5 file writes version 6",
            NoteDocument.CurrentVersion == 6 &&
            JsonDocument.Parse(File.ReadAllText(oldPath)).RootElement.GetProperty("version").GetInt32() == 6);
        var reloaded = NoteStore.Load(oldPath).Document;
        Ok("a file written by this build reloads the box",
            reloaded.Version == 6 && reloaded.AlertBoxes.Count == 1 && reloaded.AlertBoxes[0].Title == "Hi" && reloaded.Rows[0][0] == "a");

        var issues = new List<string>();
        const string bad = """
        [
          null,
          4,
          {"title":"only"},
          {"id":"b1","title":1,"text":["x"],"color":"#abc"},
          {"id":"b1","title":"dup","text":"ok"},
          {"id":"  spaced  ","title":"S","text":"T","color":"red"}
        ]
        """;
        using (var doc = JsonDocument.Parse(bad))
        {
            var list = AlertBoxOps.Parse(doc.RootElement, issues.Add);
            Ok("bad entries skipped, good ones kept", list.Count == 4, string.Join(" | ", list.Select(b => b.Title)));
            Ok("missing id gets a new one", list[0].Title == "only" && list[0].Id.Length == 32 && list[0].Text == "");
            Ok("bad title/text become empty but id and color stay",
                list[1].Id == "b1" && list[1].Title == "" && list[1].Text == "" &&
                list[1].Extra.TryGetValue("color", out var c) && c.GetString() == "#abc");
            Ok("duplicate id is replaced", list[2].Title == "dup" && list[2].Text == "ok" && list[2].Id != "b1" && list[2].Id.Length == 32);
            Ok("id is trimmed and extra color kept", list[3].Id == "spaced" && list[3].Title == "S" && list[3].Extra["color"].GetString() == "red");
        }
        Ok("corrupt alert entries are logged in English", issues.Count >= 4 && issues.All(i => i.StartsWith("alertBoxes", StringComparison.Ordinal)));

        var (wrong, _) = LoadJson("alert-type", "{\"version\":5,\"columns\":[\"Title\",\"Details\",\"Status\",\"Due date\"],\"columnRoles\":[\"title\",\"detail\",\"status\",\"due\"],\"rows\":[[\"keep\",\"\",\"\",\"\"]],\"rowIds\":[\"r1\"],\"alertBoxes\":\"nope\"}");
        Ok("alertBoxes that is not an array → empty, table kept",
            wrong.Document.AlertBoxes.Count == 0 && wrong.Document.Rows[0][0] == "keep" &&
            wrong.Issues.Any(i => i == "alertBoxes: not an array, all skipped"));

        var many = string.Join(",", Enumerable.Range(0, 40).Select(i => "{\"id\":\"id" + i + "\",\"title\":\"t" + i + "\",\"text\":\"\"}"));
        var (capped, _) = LoadJson("alert-cap", "{\"version\":5,\"columns\":[\"Title\",\"Details\",\"Status\",\"Due date\"],\"columnRoles\":[\"title\",\"detail\",\"status\",\"due\"],\"rows\":[[\"z\",\"\",\"\",\"\"]],\"rowIds\":[\"r1\"],\"alertBoxes\":[" + many + "]}");
        Ok("more than 30 boxes → first 30 kept, in order",
            capped.Document.AlertBoxes.Count == 30 &&
            capped.Document.AlertBoxes[0].Id == "id0" && capped.Document.AlertBoxes[29].Id == "id29" &&
            capped.Issues.Any(i => i.Contains("more than 30")));
        Ok("alert issue text has no Thai", !capped.Issues.Any(i => i.Any(ch => ch >= '\u0E00' && ch <= '\u0E7F')));
    }

    public static void TestAlertEditSurvivesTableUndo()
    {
        var d = Sample(2);
        d.AlertBoxes.Add(AlertBoxOps.Create("Keep", "me"));
        var id = d.AlertBoxes[0].Id;
        var before = Snapshot.CloneLite(d);
        d.SetCellRaw(0, 0, "changed");
        var undo = new UndoStack();
        undo.Push(new SnapshotAction("edit", before, Snapshot.CloneLite(d)));
        d.AlertBoxes[0].Title = "Still";
        d.AlertBoxes.Add(AlertBoxOps.Create("Second", ""));
        undo.Undo(d);
        Ok("undo of a table edit does not drop or revert alert boxes",
            d.Rows[0][0] == "งาน 0" && d.AlertBoxes.Count == 2 && d.AlertBoxes[0].Id == id && d.AlertBoxes[0].Title == "Still");
    }

    public static void TestDeleteAnyAlertAndEmptySave()
    {
        var p = Path.Combine(Dir("alert-del"), "note.json");
        var d = Sample(1);
        var a = AlertBoxOps.Create("one", "x");
        var b = AlertBoxOps.Create("", "");
        d.AlertBoxes.Add(a);
        d.AlertBoxes.Add(b);
        d.AlertBoxes.RemoveAt(AlertBoxOps.IndexOf(d.AlertBoxes, a.Id));
        NoteStore.Save(p, d);
        var again = NoteStore.Load(p);
        var loaded = again.Document;
        Ok("deleted box stays deleted; an empty box is kept",
            loaded.AlertBoxes.Count == 1 && loaded.AlertBoxes[0].Id == b.Id && loaded.AlertBoxes[0].Title == "" && again.Issues.Count == 0);
    }
}
