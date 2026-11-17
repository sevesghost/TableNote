using System.Text.Json;
using TableNote;

// Feature 2.1: collapsible column groups. Persistence, old files, and collapse must not drop child cells.
public static partial class T
{
    static string Cell(NoteDocument d, int row, string columnId) => d.GetCell(row, d.ColumnIds.IndexOf(columnId));

    static Dictionary<string, string> AllCells(NoteDocument d)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var r = 0; r < d.Rows.Count; r++)
            for (var c = 0; c < d.Columns.Count; c++)
                map[r + ":" + d.ColumnIdAt(c)] = d.GetCell(r, c);
        return map;
    }

    public static void TestColumnGroupRoundTrip()
    {
        var p = Path.Combine(Dir("groups"), "note.json");
        var d = Sample(3);
        d.Columns[1] = "ข้อมูล";
        d.SetCellRaw(0, 1, "parent-cell");
        var created = ColumnGroupOps.Create(d, 1, 3);
        Ok("create group of 3", created.Ok && d.ColumnGroups.Count == 1 && d.ColumnGroups[0].ChildIds.Count == 3 && d.ColumnGroups[0].Expanded, created.Error ?? "");
        Ok("children inserted after the parent", d.Columns[1] == "ข้อมูล" && d.Columns[2] == "ข้อมูล 1" && d.Columns[3] == "ข้อมูล 2" && d.Columns[4] == "ข้อมูล 3");
        Ok("parent cell kept when the group is created", d.GetCell(0, 1) == "parent-cell");
        var parentId = d.ColumnIdAt(1);
        var childIds = d.ColumnGroups[0].ChildIds.ToList();
        d.SetCellRaw(0, d.ColumnIds.IndexOf(childIds[0]), "alpha");
        d.SetCellRaw(1, d.ColumnIds.IndexOf(childIds[1]), "beta");
        d.SetCellRaw(2, d.ColumnIds.IndexOf(childIds[2]), "gamma");
        var beforeCollapse = AllCells(d);
        ColumnGroupOps.SetExpanded(d, 1, false);
        Ok("collapse hides children and keeps every cell",
            !d.ColumnGroups[0].Expanded && ColumnGroupOps.VisibleCount(d) == d.Columns.Count - 3 &&
            !ColumnGroupOps.IsHidden(d, 1) && childIds.All(id => ColumnGroupOps.IsHidden(d, d.ColumnIds.IndexOf(id))) &&
            AllCells(d).OrderBy(kv => kv.Key).SequenceEqual(beforeCollapse.OrderBy(kv => kv.Key)));

        NoteStore.Save(p, d);
        using (var json = JsonDocument.Parse(File.ReadAllText(p)))
        {
            var root = json.RootElement;
            var groups = root.GetProperty("columnGroups");
            Ok("note.json writes columnGroups expanded=false",
                groups.GetArrayLength() == 1 && groups[0].GetProperty("parentId").GetString() == parentId &&
                groups[0].GetProperty("expanded").GetBoolean() == false &&
                groups[0].GetProperty("childIds").GetArrayLength() == 3);
            var rows = root.GetProperty("rows");
            Ok("collapsed save still stores child cells", rows[0][2].GetString() == "alpha" && rows[1][3].GetString() == "beta" && rows[2][4].GetString() == "gamma");
        }

        var loaded = NoteStore.Load(p).Document;
        var lp = loaded.ColumnIds.IndexOf(parentId);
        Ok("reopen keeps the group collapsed",
            loaded.ColumnGroups.Count == 1 && loaded.ColumnGroups[0].ParentId == parentId &&
            loaded.ColumnGroups[0].ChildIds.SequenceEqual(childIds) && !loaded.ColumnGroups[0].Expanded &&
            ColumnGroupOps.IsHidden(loaded, loaded.ColumnIds.IndexOf(childIds[0])));
        Ok("reopen keeps parent and child text while collapsed",
            Cell(loaded, 0, parentId) == "parent-cell" && Cell(loaded, 0, childIds[0]) == "alpha" &&
            Cell(loaded, 1, childIds[1]) == "beta" && Cell(loaded, 2, childIds[2]) == "gamma");
        ColumnGroupOps.SetExpanded(loaded, lp, true);
        Ok("expand after reopen still has the child text",
            loaded.ColumnGroups[0].Expanded && !ColumnGroupOps.IsHidden(loaded, loaded.ColumnIds.IndexOf(childIds[2])) &&
            Cell(loaded, 0, childIds[0]) == "alpha" && Cell(loaded, 2, childIds[2]) == "gamma");

        ColumnGroupOps.RenameChildren(loaded, lp, ["หนึ่ง", "สอง", "สาม"]);
        NoteStore.Save(p, loaded);
        var renamed = NoteStore.Load(p).Document;
        Ok("renamed children round-trip and data stays on the same ids",
            renamed.Columns[renamed.ColumnIds.IndexOf(childIds[0])] == "หนึ่ง" &&
            renamed.Columns[renamed.ColumnIds.IndexOf(childIds[1])] == "สอง" &&
            Cell(renamed, 0, childIds[0]) == "alpha" && Cell(renamed, 1, childIds[1]) == "beta");
    }

    public static void TestOldFileWithoutGroups()
    {
        const string oldFile = """
            {"version":5,"columns":["หัวข้อ","รายละเอียด","สถานะ","กำหนดส่ง"],"rows":[["หัวข้อแถว","รายละเอียดแถว","",""]],
            "columnIds":["c-title","c-detail","c-status","c-due"],"columnRoles":["title","detail","status","due"],"rowIds":["r1"],
            "customFlag":{"keep":true}}
            """;
        var (r, dir) = LoadJson("old-nogroup", oldFile);
        var d = r.Document;
        Ok("file with no columnGroups loads", d.ColumnGroups.Count == 0 && d.Columns.Count == 4 && d.Rows.Count == 1, r.Warning ?? "");
        Ok("old cells are unchanged", d.Rows[0][0] == "หัวข้อแถว" && d.Rows[0][1] == "รายละเอียดแถว" && d.ColumnIds[1] == "c-detail");
        Ok("unknown field is kept beside the new group field", d.ExtraFields.ContainsKey("customFlag"));
        Ok("no group means nothing is hidden", Enumerable.Range(0, d.Columns.Count).All(i => !ColumnGroupOps.IsHidden(d, i)));
        var p = Path.Combine(dir, "note.json");
        NoteStore.Save(p, d);
        var again = NoteStore.Load(p).Document;
        Ok("save of an old file still has the same cells and no group",
            again.ColumnGroups.Count == 0 && again.Rows[0][0] == "หัวข้อแถว" && again.Rows[0][1] == "รายละเอียดแถว" && again.ExtraFields.ContainsKey("customFlag"));
    }

    public static void TestCollapseDoesNotDropData()
    {
        var d = Sample(4);
        d.Columns[0] = "ข้อมูล";
        d.SetCellRaw(0, 0, "หัวกลุ่ม");
        Ok("create", ColumnGroupOps.Create(d, 0, 3).Ok);
        var ids = d.ColumnGroups[0].ChildIds.ToList();
        for (var r = 0; r < d.Rows.Count; r++)
            foreach (var (id, n) in ids.Select((id, n) => (id, n)))
                d.SetCellRaw(r, d.ColumnIds.IndexOf(id), "r" + r + "c" + n);
        var snapshot = AllCells(d);
        var cols = d.Columns.Count;
        var undo = new UndoStack();
        var beforeToggle = Snapshot.CloneLite(d);
        ColumnGroupOps.Toggle(d, d.ColumnIds.IndexOf(d.ColumnGroups[0].ParentId));
        undo.Push(new SnapshotAction(Localizer.T("undo.colgroup_toggle"), beforeToggle, Snapshot.CloneLite(d)));
        Ok("toggle collapse does not change column count or any cell",
            !d.ColumnGroups[0].Expanded && d.Columns.Count == cols &&
            AllCells(d).OrderBy(kv => kv.Key).SequenceEqual(snapshot.OrderBy(kv => kv.Key)));
        undo.Undo(d);
        Ok("undo collapse shows the children again and keeps the text",
            d.ColumnGroups.Count == 1 && d.ColumnGroups[0].Expanded &&
            AllCells(d).OrderBy(kv => kv.Key).SequenceEqual(snapshot.OrderBy(kv => kv.Key)));
        undo.Redo(d);
        Ok("redo collapse hides again without dropping text",
            !d.ColumnGroups[0].Expanded && Cell(d, 3, ids[2]) == "r3c2");

        var beforeUngroup = Snapshot.CloneLite(d);
        var parentIndex = d.ColumnIds.IndexOf(d.ColumnGroups[0].ParentId);
        ColumnGroupOps.Ungroup(d, parentIndex);
        undo.Push(new SnapshotAction(Localizer.T("undo.colgroup_ungroup"), beforeUngroup, Snapshot.CloneLite(d)));
        Ok("ungroup keeps columns and child text",
            d.ColumnGroups.Count == 0 && d.Columns.Count == cols && Cell(d, 0, ids[0]) == "r0c0" && !ColumnGroupOps.IsHidden(d, d.ColumnIds.IndexOf(ids[0])));
        undo.Undo(d);
        Ok("undo ungroup restores the group and the text",
            d.ColumnGroups.Count == 1 && d.ColumnGroups[0].ChildIds.SequenceEqual(ids) && !d.ColumnGroups[0].Expanded && Cell(d, 1, ids[1]) == "r1c1");

        var plain = Sample(2);
        plain.Columns[1] = "ข้อมูล";
        plain.SetCellRaw(0, 1, "stay");
        var beforeCreate = Snapshot.CloneLite(plain);
        ColumnGroupOps.Create(plain, 1, 3);
        var added = plain.ColumnGroups[0].ChildIds.ToList();
        plain.SetCellRaw(0, plain.ColumnIds.IndexOf(added[0]), "child");
        undo.Push(new SnapshotAction(Localizer.T("undo.colgroup_create"), beforeCreate, Snapshot.CloneLite(plain)));
        undo.Undo(plain);
        Ok("undo create removes the child columns and keeps the parent cell",
            plain.ColumnGroups.Count == 0 && plain.Columns.Count == 4 && plain.GetCell(0, 1) == "stay" && !plain.Columns.Contains("ข้อมูล 1"));
        undo.Redo(plain);
        Ok("redo create brings the children and their text back",
            plain.ColumnGroups.Count == 1 && plain.ColumnGroups[0].ChildIds.SequenceEqual(added) && Cell(plain, 0, added[0]) == "child");
    }

    public static void TestColumnGroupLoadIsLenient()
    {
        const string bad = """
            {"version":5,"columns":["หัวข้อ","รายละเอียด","สถานะ","กำหนดส่ง"],"rows":[["keep-me","x","",""]],
            "columnIds":["c1","c2","c3","c4"],"rowIds":["r1"],"columnGroups":"nope"}
            """;
        var badLoad = LoadJson("bad-groups", bad).r.Document;
        Ok("columnGroups that is not an array is skipped", badLoad.ColumnGroups.Count == 0 && badLoad.Rows[0][0] == "keep-me");
        Ok("that skip is reported and is not a corrupt file", NoteStore.LastLoadIssues.Any(i => i.StartsWith("columnGroups", StringComparison.Ordinal)));

        const string shaped = """
            {"version":5,"columns":["หัวข้อ","ข้อมูล","สถานะ","กำหนดส่ง","ข้อมูล 1","ข้อมูล 2","ข้อมูล 3"],
            "columnIds":["t","p","s","d","a","b","c"],"columnRoles":["title","","status","due","", "", ""],
            "rows":[["title","parent","todo","2026-12-01 09:00","alpha","beta","gamma"]],"rowIds":["r1"],
            "columnGroups":[5, {"parentId":"p","childIds":["a","b","c"]}]}
            """;
        var (shapedLoad, _) = LoadJson("shaped-groups", shaped);
        var doc = shapedLoad.Document;
        Ok("a bad entry is skipped and a group with no expanded flag stays shown",
            doc.ColumnGroups.Count == 1 && doc.ColumnGroups[0].Expanded && doc.ColumnGroups[0].ChildIds.SequenceEqual(["a", "b", "c"]));
        Ok("hand-written group file keeps child cells", doc.GetCell(0, doc.ColumnIds.IndexOf("a")) == "alpha" && doc.GetCell(0, doc.ColumnIds.IndexOf("p")) == "parent");
        ColumnGroupOps.SetExpanded(doc, doc.ColumnIds.IndexOf("p"), false);
        Ok("collapsing a loaded group hides children without clearing them",
            ColumnGroupOps.IsHidden(doc, doc.ColumnIds.IndexOf("b")) && doc.GetCell(0, doc.ColumnIds.IndexOf("b")) == "beta" && doc.GetCell(0, doc.ColumnIds.IndexOf("c")) == "gamma");
    }

    public static void TestMoveAndDeleteKeepGroupData()
    {
        var d = Sample(2);
        d.Columns[1] = "ข้อมูล";
        ColumnGroupOps.Create(d, 1, 3);
        var parentId = d.ColumnGroups[0].ParentId;
        var childId = d.ColumnGroups[0].ChildIds[0];
        d.SetCellRaw(0, d.ColumnIds.IndexOf(childId), "moves-with-column");
        var parentAt = d.ColumnIds.IndexOf(parentId);
        Ok("move parent", d.MoveColumn(parentAt, parentAt + 1));
        Ok("child text follows the group when the parent moves", Cell(d, 0, childId) == "moves-with-column" && d.ColumnGroups[0].ChildIds.Contains(childId));
        var parentIdx = d.ColumnIds.IndexOf(parentId);
        var childIdx = d.ColumnGroups[0].ChildIds.Select(id => d.ColumnIds.IndexOf(id)).ToList();
        Ok("children stay packed behind the parent",
            childIdx.Count == 3 && childIdx[0] == parentIdx + 1 && childIdx[1] == parentIdx + 2 && childIdx[2] == parentIdx + 3);

        var other = d.ColumnGroups[0].ChildIds[1];
        d.SetCellRaw(1, d.ColumnIds.IndexOf(other), "sibling");
        var removed = d.ColumnIds.IndexOf(parentId);
        Ok("delete parent", d.RemoveColumn(removed));
        Ok("deleting the parent drops the group and keeps child cells",
            d.ColumnGroups.Count == 0 && Cell(d, 0, childId) == "moves-with-column" && Cell(d, 1, other) == "sibling");

        var again = Sample(2);
        again.Columns[0] = "ข้อมูล";
        ColumnGroupOps.Create(again, 0, 3);
        var keptId = again.ColumnGroups[0].ChildIds[0];
        again.SetCellRaw(0, again.ColumnIds.IndexOf(keptId), "keep");
        var more = ColumnGroupOps.AddChild(again, 0);
        var newId = again.ColumnGroups[0].ChildIds[^1];
        Ok("add child", more.Ok && again.ColumnGroups[0].ChildIds.Count == 4, more.Error ?? "");
        again.SetCellRaw(1, again.ColumnIds.IndexOf(newId), "new-cell");
        ColumnGroupOps.SetExpanded(again, 0, false);
        Ok("add child then collapse keeps old and new child text",
            Cell(again, 0, keptId) == "keep" && Cell(again, 1, newId) == "new-cell" &&
            again.ColumnIds.IndexOf(newId) == again.ColumnIds.IndexOf(again.ColumnGroups[0].ParentId) + 4);

        var full = NoteDocument.CreateDefault();
        while (full.Columns.Count < NoteDocument.MaxUserColumns) full.AddColumn();
        var denied = ColumnGroupOps.Create(full, 0, 3);
        Ok("no room for a group at the column cap", !denied.Ok && full.Columns.Count == NoteDocument.MaxUserColumns && full.ColumnGroups.Count == 0);
    }
}
