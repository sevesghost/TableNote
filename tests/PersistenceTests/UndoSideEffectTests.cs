using TableNote;

// Undo of deletes that removed data the undo step did not hold (detail tables, colours, reminder acknowledgements).
public static partial class T
{
    /// <summary>The UI's SnapshotStep (MainWindow.SnapshotStepCore): snapshot + side state around one action.</summary>
    static void SnapshotStep(NoteDocument d, UndoStack undo, string label, Action action)
    {
        var before = Snapshot.CloneLite(d);
        var side = UndoSideState.Capture(d);
        action();
        undo.Push(new SnapshotAction(label, before, Snapshot.CloneLite(d), side.RemovedBy(d)));
    }

    public static void TestRowDeleteUndoKeepsCellDetails()
    {
        var d = Sample(3);
        var rowId = d.RowIdAt(1);
        var colId = d.ColumnIdAt(0);
        var table = CellDetailOps.CreateDefault();
        table.Rows[0][0] = "keep me";
        CellDetailOps.Set(d, rowId, colId, table);
        CellDetailOps.SetCellButton(d, 1, 0, true);
        CellDetailOps.SetColumnButton(d, 1, true);
        CellDetailOps.SetCellButton(d, 1, 1, false); // a per-cell hide on a column-wide button

        // The real Delete-row path: TableSession.DeleteRowAsync + RowDeleteAction (not a snapshot step).
        var undo = new UndoStack();
        var del = RowDeleteAction.Capture(d, 1);
        d.RemoveRow(1);
        undo.Push(del);
        Ok("row delete drops the row's detail table (as before)", CellDetailOps.Get(d, rowId, colId) == null);

        undo.Undo(d);
        var row = d.RowIds.IndexOf(rowId);
        Ok("undo row delete: the detail table comes back", row == 1 && CellDetailOps.Get(d, rowId, colId)?.Rows[0][0] == "keep me");
        Ok("undo row delete: the per-cell button comes back", CellDetailOps.ShowsButton(d, row, 0));
        Ok("undo row delete: the per-cell hide comes back", !CellDetailOps.ShowsButton(d, row, 1) && CellDetailOps.ShowsButton(d, 0, 1));

        undo.Redo(d);
        Ok("redo row delete: the detail is gone again", !d.RowIds.Contains(rowId) && CellDetailOps.Get(d, rowId, colId) == null);
        undo.Undo(d);
        Ok("undo again: the detail is back again", CellDetailOps.Get(d, rowId, colId)?.Rows[0][0] == "keep me");

        var p = Path.Combine(Dir("rowdel-detail"), "note.json");
        NoteStore.Save(p, d);
        Ok("undo row delete: the detail table is saved to note.json", CellDetailOps.Get(NoteStore.Load(p).Document, rowId, colId)?.Rows[0][0] == "keep me");
    }

    public static void TestColumnDeleteUndoKeepsColours()
    {
        var d = Sample(2);
        var colId = d.ColumnIdAt(1);
        var rowId = d.RowIdAt(0);
        PartColorOps.Assign(d.PartColors, PartColorKeys.HeaderColumn(colId), "#FF0000", null);
        PartColorOps.Assign(d.PartColors, PartColorKeys.Cell(rowId, colId), "#00FF00", "#112233");
        PartColorOps.Assign(d.PartColors, PartColorKeys.Window, "#ABCDEF", null);
        var undo = new UndoStack();
        SnapshotStep(d, undo, "delete column", () => d.RemoveColumn(1));
        Ok("column delete drops its colours (as before)", !d.PartColors.ContainsKey(PartColorKeys.HeaderColumn(colId)));

        undo.Undo(d);
        Ok("undo column delete: the header colour comes back", d.PartColors.TryGetValue(PartColorKeys.HeaderColumn(colId), out var h) && h.Background == "#FF0000");
        Ok("undo column delete: the cell colour comes back", d.PartColors.TryGetValue(PartColorKeys.Cell(rowId, colId), out var c) && c.Background == "#00FF00" && c.Foreground == "#112233");
        Ok("undo column delete: other colours untouched", d.PartColors.Count == 3);

        undo.Redo(d);
        Ok("redo column delete: its colours are dropped again", !d.PartColors.ContainsKey(PartColorKeys.HeaderColumn(colId)) && d.PartColors.ContainsKey(PartColorKeys.Window));

        var plain = Sample(2);
        PartColorOps.Assign(plain.PartColors, PartColorKeys.Window, "#ABCDEF", null);
        var side = UndoSideState.Capture(plain);
        plain.AddColumn();
        Ok("a step that removes nothing carries no side state", side.RemovedBy(plain) == null);
    }

    public static void TestDueColumnDeleteUndoKeepsAcknowledgements()
    {
        var d = Sample(4);
        var now = DateTime.Now;
        var due = d.DueColumnIndex();
        var status = d.StatusColumnIndex();
        for (var r = 0; r < 4; r++)
        {
            d.Rows[r][status] = "";
            d.Rows[r][due] = DueDates.Format(now.AddDays(-(r + 1)));
        }
        var fires = ReminderPlan.Collect(d, now);
        foreach (var t in fires.Take(3)) ReminderPlan.Acknowledge(d, t); // the user closed three popups
        ReminderPlan.Snooze(d, fires[3], now);                            // and snoozed the fourth
        PartColorOps.Assign(d.PartColors, PartColorKeys.Notify(d.RowIdAt(0)), "#FFEEDD", null);
        Ok("setup: nothing due right now", ReminderPlan.Collect(d, now).Count == 0);

        var undo = new UndoStack();
        SnapshotStep(d, undo, "delete column", () => d.RemoveColumn(due));
        Ok("due column deleted", d.DueColumnIndex() < 0 && d.Snoozes.Count == 0 && d.Reminders.Count == 0);

        undo.Undo(d);
        var again = ReminderPlan.Collect(d, now);
        Ok("undo due column delete: acknowledged tasks do not pop up again", d.DueColumnIndex() >= 0 && again.Count == 0,
            $"{again.Count} reminders: " + string.Join(", ", again.Select(t => t.Title)));
        Ok("undo due column delete: the snooze comes back", ReminderPlan.Collect(d, now.AddMinutes(ReminderPlan.SnoozeMinutes + 1)).Count == 1);
        Ok("undo due column delete: the reminder card colour comes back", d.PartColors.ContainsKey(PartColorKeys.Notify(d.RowIdAt(0))));

        undo.Redo(d);
        Ok("redo due column delete: acks and snoozes are dropped again", d.DueColumnIndex() < 0 && d.Snoozes.Count == 0 &&
            d.Reminders.Values.All(i => i.Acked.Count == 0));
        undo.Undo(d);
        Ok("undo again: still nothing pops up", ReminderPlan.Collect(d, now).Count == 0);
    }
}
