using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace TableNote;

/// <summary>
/// 2.6.0: formula bar (no calculation): the address of the selected cell ("B3", or "B3:D7" for a block) and its whole
/// text in a long box. Typing there edits that cell: Enter or leaving the box keeps it (one undo step), Esc puts the
/// text back, Alt+Enter starts a new line. Status and reminder-date cells are shown but edited with their own controls.
/// </summary>
public partial class MainWindow
{
    private (string RowId, string ColumnId)? _barTarget;
    private string _barStart = "";

    private void InitFormulaBar()
    {
        FormulaBox.GotFocus += (_, _) => BeginBarEdit();
        FormulaBox.LostFocus += (_, _) => CommitBar();
        FormulaBox.AddHandler(KeyDownEvent, OnFormulaKey, RoutingStrategies.Tunnel);
        ApplyFormulaBarTexts();
        SyncFormulaBar();
    }

    private void ApplyFormulaBarTexts()
    {
        FormulaBox.PlaceholderText = Localizer.T("bar.placeholder");
        ToolTip.SetTip(FormulaBox, Localizer.T("bar.tip"));
        ToolTip.SetTip(NameBox, Localizer.T("bar.name_tip"));
    }

    /// <summary>Shows the selected cell in the bar (not while the user is typing in the bar).</summary>
    private void SyncFormulaBar()
    {
        FormulaBar.IsVisible = _view == ViewMode.Edit;
        if (FormulaBox.IsFocused) return;
        {
            var doc = _session.Doc;
            if (SelectionRect() is not { } s || _session.AnchorRow >= doc.Rows.Count || _session.AnchorCol >= doc.Columns.Count)
            {
                NameBox.Text = "";
                FormulaBox.Text = "";
                FormulaBox.IsReadOnly = true;
                return;
            }
            int r = _session.AnchorRow, c = _session.AnchorCol;
            NameBox.Text = s.Top == s.Bottom && s.Left == s.Right
                ? ColumnLetter(c) + (r + 1)
                : ColumnLetter(s.Left) + (s.Top + 1) + ":" + ColumnLetter(s.Right) + (s.Bottom + 1);
            var raw = doc.GetCell(r, c);
            FormulaBox.Text = c == doc.StatusColumnIndex() && raw.Length > 0 ? TaskStatuses.Label(TaskStatuses.DisplayCode(raw)) : raw;
            FormulaBox.IsReadOnly = ColumnSettingsOps.IsSystemColumn(doc, c);
        }
    }

    /// <summary>The active cell's text changed in the cell: the bar follows.</summary>
    private void SyncFormulaBarText(int row, int column, string value)
    {
        if (FormulaBox.IsFocused || row != _session.AnchorRow || column != _session.AnchorCol || !_session.HasSelection) return;
        FormulaBox.Text = value;
    }

    private void BeginBarEdit()
    {
        _barTarget = null;
        if (FormulaBox.IsReadOnly || SelectionRect() == null) return;
        var doc = _session.Doc;
        _barTarget = (doc.RowIdAt(_session.AnchorRow), doc.ColumnIdAt(_session.AnchorCol));
        _barStart = doc.GetCell(_session.AnchorRow, _session.AnchorCol);
    }

    /// <summary>Writes the bar's text into its cell (one undo step). Returns the cell, or null when there was none.</summary>
    private (int Row, int Column)? CommitBar()
    {
        if (_barTarget is not { } t) return null;
        _barTarget = null;
        var doc = _session.Doc;
        var row = FindRowById(t.RowId);
        var col = doc.ColumnIds.IndexOf(t.ColumnId);
        if (row < 0 || col < 0) return null;
        var text = TableLimits.Clip((FormulaBox.Text ?? "").Replace("\r\n", "\n"), TableLimits.MaxCellChars);
        var before = doc.GetCell(row, col);
        if (text != before && _session.SetCell(row, col, text))
        {
            _undo.Push(CellEditAction.For(doc, t.RowId, col, before, text));
            ScheduleSave();
            RefreshRowsKeepingSelection([row]);
            StatusText.Text = Localizer.T("bar.saved", ColumnLetter(col) + (row + 1));
        }
        return (row, col);
    }

    private void OnFormulaKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Return && e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            e.Handled = true;
            if (FormulaBox.IsReadOnly) return;
            var t = FormulaBox.Text ?? "";
            var a = Math.Clamp(Math.Min(FormulaBox.SelectionStart, FormulaBox.SelectionEnd), 0, t.Length);
            var b = Math.Clamp(Math.Max(FormulaBox.SelectionStart, FormulaBox.SelectionEnd), a, t.Length);
            FormulaBox.Text = t[..a] + "\n" + t[b..];
            FormulaBox.SelectionStart = FormulaBox.SelectionEnd = FormulaBox.CaretIndex = a + 1;
            return;
        }
        if (e.Key == Key.Return)
        {
            e.Handled = true;
            var cell = CommitBar();
            if (cell is { } c) FocusCell(c.Row, c.Column);
            else if (_session.HasSelection && _session.AnchorRow >= 0) FocusCell(_session.AnchorRow, _session.AnchorCol);
            return;
        }
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            _barTarget = null;
            FormulaBox.Text = _barStart;
            if (_session.HasSelection && _session.AnchorRow >= 0) FocusCell(_session.AnchorRow, _session.AnchorCol);
        }
    }
}
