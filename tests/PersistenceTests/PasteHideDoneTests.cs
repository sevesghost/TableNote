using TableNote;

// A multi-line paste while "Hide done" is on must not write into the hidden done rows.
public static partial class T
{
    public static void TestPasteSkipsRowsHiddenByHideDone()
    {
        var d = Sample(5); // row index 2 is done
        d.HideDone = true;
        var hiddenTitle = d.Rows[2][0];
        var r = ExcelPaste.Apply(d, 1, 0, "x1\r\nx2\r\nx3\r\n");
        Ok("paste with Hide done: accepted", r.Kind == GridPasteKind.Applied, r.Message ?? "");
        Ok("paste with Hide done: lines fill the visible rows 2, 4, 5 (row 3 is done and hidden)",
            d.Rows[1][0] == "x1" && d.Rows[3][0] == "x2" && d.Rows[4][0] == "x3", string.Join(" | ", d.Rows.Select(x => x[0])));
        Ok("paste with Hide done: the hidden done row keeps its text and stays done",
            d.Rows[2][0] == hiddenTitle && TaskStatuses.IsDone(d.Rows[2][d.StatusColumnIndex()]));

        var end = Sample(3); // the done row is the last one
        end.HideDone = true;
        ExcelPaste.Apply(end, 1, 0, "a\nb\nc");
        Ok("paste with Hide done near the end: new rows are added after the hidden row",
            end.Rows.Count == 5 && end.Rows[1][0] == "a" && end.Rows[2][0] == "งาน 2" && end.Rows[3][0] == "b" && end.Rows[4][0] == "c",
            string.Join(" | ", end.Rows.Select(x => x[0])));

        var shown = Sample(4); // Hide done off: done rows are on screen, so they are pasted over as before
        ExcelPaste.Apply(shown, 1, 0, "p\nq");
        Ok("paste with Hide done off: fills the next rows as before", shown.Rows[1][0] == "p" && shown.Rows[2][0] == "q");

        var limit = Sample(3);
        limit.HideDone = true;
        while (limit.Rows.Count < TableLimits.MaxRows) limit.AppendRow();
        var last = TableLimits.MaxRows - 1;
        limit.Rows[last - 1][limit.StatusColumnIndex()] = TaskStatuses.DoneCode; // a hidden row just before the last
        var fits = ExcelPaste.Apply(limit, last - 2, 0, "1\n2");
        Ok("paste with Hide done at the row limit: two lines skip the hidden row and fit",
            fits.Kind == GridPasteKind.Applied && limit.Rows[last - 2][0] == "1" && limit.Rows[last][0] == "2" &&
            limit.Rows.Count == TableLimits.MaxRows, fits.Message ?? "");
        var over = ExcelPaste.Apply(limit, last - 2, 0, "x\ny\nz");
        Ok("paste with Hide done at the row limit: a third line is refused and nothing changes",
            over.Kind == GridPasteKind.Rejected && limit.Rows[last - 2][0] == "1" && limit.Rows[last][0] == "2" &&
            limit.Rows.Count == TableLimits.MaxRows, over.Message ?? "");
    }
}
