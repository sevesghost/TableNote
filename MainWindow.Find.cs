using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace TableNote;

/// <summary>
/// 2.6.0: find (Ctrl+F). A bar above the table: what to look for, "3 / 12", previous / next (Shift+Enter / Enter, F3),
/// close (Esc). Every cell on screen that contains the text is marked; the current one is selected and scrolled to.
/// Case is ignored; a status is found by its label, a reminder date also as dd/MM/yyyy, a days cell also as "150 days".
/// </summary>
public partial class MainWindow
{
    private readonly List<(int Row, int Column)> _findMatches = [];
    private int _findIndex = -1;

    private void InitFind()
    {
        FindBox.TextChanged += (_, _) => RunFind();
        FindBox.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Return) { e.Handled = true; FindStep(e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1); }
            else if (e.Key == Key.Escape) { e.Handled = true; CloseFind(); }
        }, RoutingStrategies.Tunnel);
        FindNextButton.Click += (_, _) => FindStep(1);
        FindPrevButton.Click += (_, _) => FindStep(-1);
        FindCloseButton.Click += (_, _) => CloseFind();
        ApplyFindTexts();
    }

    private void ApplyFindTexts()
    {
        FindLabel.Text = Localizer.T("find.label");
        FindBox.PlaceholderText = Localizer.T("find.placeholder");
        ToolTip.SetTip(FindNextButton, Localizer.T("find.next"));
        ToolTip.SetTip(FindPrevButton, Localizer.T("find.previous"));
        ToolTip.SetTip(FindCloseButton, Localizer.T("find.close"));
        UpdateFindCount();
    }

    private bool FindOpen => FindBar.IsVisible;

    private void OpenFind()
    {
        if (_view != ViewMode.Edit) return;
        CommitFocusedEdit();
        FindBar.IsVisible = true;
        FindBox.Focus();
        FindBox.SelectAll();
        RunFind();
    }

    private void CloseFind()
    {
        if (!FindBar.IsVisible) return;
        FindBar.IsVisible = false;
        _findMatches.Clear();
        _findIndex = -1;
        RefreshSelectionVisuals();
        if (_session.HasSelection && _session.AnchorRow >= 0 && _session.AnchorRow < _session.Doc.Rows.Count)
            FocusCell(_session.AnchorRow, _session.AnchorCol);
    }

    /// <summary>Ctrl+F opens the bar (or selects its text), F3 / Shift+F3 step, Esc closes it. True when handled.</summary>
    private bool HandleFindKey(KeyEventArgs e)
    {
        if (_view != ViewMode.Edit) return false;
        if (e.Key == Key.F && e.KeyModifiers == KeyModifiers.Control) { e.Handled = true; OpenFind(); return true; }
        if (e.Key == Key.F3) { e.Handled = true; FindStep(e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1); return true; }
        if (e.Key == Key.Escape && FindBar.IsVisible && !(FocusedCellBox() is { IsReadOnly: false }) && !FormulaBox.IsFocused)
        {
            e.Handled = true;
            CloseFind();
            return true;
        }
        return false;
    }

    /// <summary>The table changed while the bar is open: the marks follow, the selection stays where it is.</summary>
    private void RefreshFindMatches()
    {
        var current = _findIndex >= 0 && _findIndex < _findMatches.Count ? _findMatches[_findIndex] : ((int, int)?)null;
        CollectMatches();
        _findIndex = current is { } c ? _findMatches.IndexOf(c) : -1;
        if (_findIndex < 0 && _findMatches.Count > 0) _findIndex = 0;
        UpdateFindCount();
    }

    private void CollectMatches()
    {
        _findMatches.Clear();
        var query = (FindBox.Text ?? "").Trim();
        if (!FindBar.IsVisible || query.Length == 0) return;
        var doc = _session.Doc;
        foreach (var r in _visibleRows)
            for (var c = 0; c < doc.Columns.Count; c++)
                if (!ColumnGroupOps.IsHidden(doc, c) && CellSearchText(r, c).Contains(query, StringComparison.CurrentCultureIgnoreCase))
                    _findMatches.Add((r, c));
    }

    /// <summary>Looks again after typing: the first match from the selected cell on is selected.</summary>
    private void RunFind()
    {
        _findIndex = -1;
        CollectMatches();
        if (_findMatches.Count > 0)
        {
            // From the selected cell on (reading order), like Excel; otherwise from the top.
            var here = _session.HasSelection ? (_visibleRows.IndexOf(_session.AnchorRow), _session.AnchorCol) : (-1, -1);
            _findIndex = _findMatches.FindIndex(m => (_visibleRows.IndexOf(m.Row), m.Column).CompareTo(here) >= 0);
            if (_findIndex < 0) _findIndex = 0;
            ShowMatch();
        }
        UpdateFindCount();
        RefreshSelectionVisuals();
    }

    private void FindStep(int step)
    {
        if (!FindBar.IsVisible) { OpenFind(); return; }
        if (_findMatches.Count == 0) return;
        _findIndex = ((_findIndex + step) % _findMatches.Count + _findMatches.Count) % _findMatches.Count;
        ShowMatch();
        UpdateFindCount();
        RefreshSelectionVisuals();
    }

    private void UpdateFindCount()
    {
        var query = (FindBox.Text ?? "").Trim();
        FindCount.Text = query.Length == 0 ? ""
            : _findMatches.Count == 0 ? Localizer.T("find.none")
            : Localizer.T("find.count", _findIndex + 1, _findMatches.Count);
    }

    /// <summary>The current match becomes the selected cell and is scrolled into view; the find box keeps the focus.</summary>
    private void ShowMatch()
    {
        var (r, c) = _findMatches[_findIndex];
        CommitFocusedEdit();
        _session.ChooseCell(r, c);
        ScrollCellIntoView(r, c);
    }

    private void ScrollCellIntoView(int row, int column)
    {
        var vi = _visibleRows.IndexOf(row);
        if (vi < 0) return;
        RowsList.ScrollIntoView(vi);
        Dispatcher.UIThread.Post(() =>
        {
            if (RowsList.ContainerFromIndex(vi) is not ContentPresenter { Child: Grid g }) return;
            var cell = g.Children.FirstOrDefault(ch => Grid.GetColumn(ch) == column + 1 && ch is not CellOverlay);
            cell?.BringIntoView();
        }, DispatcherPriority.Loaded);
    }

    /// <summary>What a cell is found by: its text, plus what it shows (status label, date as dd/MM/yyyy, days).</summary>
    private string CellSearchText(int row, int column)
    {
        var doc = _session.Doc;
        var raw = doc.GetCell(row, column);
        if (column == doc.StatusColumnIndex())
            return raw + "\n" + TaskStatuses.Label(TaskStatuses.DisplayCode(raw));
        if (column == doc.DueColumnIndex() && DueDates.TryParse(raw, out var due))
            return raw + "\n" + due.ToString("dd/MM/yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        if (DaysCounter.IsDaysColumn(doc, column))
            return raw + "\n" + DaysCounter.CellText(doc, row, column, DateTime.Now);
        return raw;
    }
}
