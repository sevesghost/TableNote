using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace TableNote;

/// <summary>
/// Modal editor for one cell's detail table: add or remove rows and columns, edit headers and values.
/// Closing saves. Cancel discards. A kind this version cannot edit is shown as-is and left untouched.
/// </summary>
public sealed class CellDetailDialog : Window
{
    private static readonly IBrush HeaderBg = new SolidColorBrush(Color.Parse("#F6E08A"));
    private static readonly IBrush CellBg = new SolidColorBrush(Color.Parse("#FFFDF6"));
    private static readonly IBrush GridLine = new SolidColorBrush(Color.Parse("#E4D08A"));
    private static readonly IBrush TextFg = new SolidColorBrush(Color.Parse("#3B3222"));
    private static readonly IBrush LabelBg = new SolidColorBrush(Color.Parse("#F8E7A4"));
    private static readonly IBrush ActiveBg = new SolidColorBrush(Color.Parse("#FFE08A"));
    private static readonly IBrush ToolBg = new SolidColorBrush(Color.Parse("#F0D78A"));
    private static readonly IBrush ToolBorder = new SolidColorBrush(Color.Parse("#D4B86A"));

    private readonly bool _readOnly;
    private readonly bool _unknown;
    private readonly TextBlock _message;
    private readonly TextBlock _size;
    private readonly ScrollViewer _scroll = new()
    {
        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
    };
    private CellDetail _working;
    private readonly List<TextBox> _boxes = [];
    private int _selRow;
    private int _selCol;
    private bool _discard;
    private bool _done;

    public bool Saved { get; private set; }
    public CellDetail? Result { get; private set; }

    public CellDetailDialog(string heading, CellDetail source, bool readOnly)
    {
        _unknown = !CellDetailOps.IsTable(source);
        _readOnly = readOnly || _unknown;
        _working = _unknown ? CellDetailOps.Clone(source)
            : source.Columns.Count == 0 ? CellDetailOps.CreateDefault()
            : CellDetailOps.Clone(source);
        _selRow = _working.Rows.Count > 0 ? 0 : -1;

        Title = heading;
        Width = 760;
        Height = 520;
        MinWidth = 520;
        MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.Parse("#FFF6C2"));
        FontFamily = AppFonts.Thai;

        var title = new TextBlock
        {
            Text = heading,
            FontWeight = FontWeight.Bold,
            FontSize = 16,
            Foreground = TextFg,
            TextWrapping = TextWrapping.Wrap
        };
        var hint = new TextBlock
        {
            Text = _unknown ? Localizer.T("detail.unknown_kind", source.Kind)
                : _readOnly ? Localizer.T("detail.readonly")
                : Localizer.T("detail.hint"),
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.85,
            Foreground = TextFg
        };
        _size = new TextBlock { Foreground = TextFg, Opacity = 0.75 };
        _message = new TextBlock { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap };

        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0 };
        if (!_unknown)
        {
            toolbar.Children.Add(Tool(Localizer.T("detail.add_row"), AddRow));
            toolbar.Children.Add(Tool(Localizer.T("detail.delete_row"), DeleteRow));
            toolbar.Children.Add(Tool(Localizer.T("detail.add_column"), AddColumn));
            toolbar.Children.Add(Tool(Localizer.T("detail.delete_column"), DeleteColumn));
        }

        var close = Tool(Localizer.T("detail.close"), () => Close());
        var cancel = Tool(Localizer.T("common.cancel"), () => { _discard = true; Close(); });
        cancel.IsCancel = true;
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0),
            Children = { close, cancel }
        };
        if (_readOnly) cancel.IsVisible = false;

        var header = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 0, 8), Children = { title, hint, toolbar, _size, _message } };
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        Content = new DockPanel { Margin = new Thickness(14), Children = { header, buttons, _scroll } };

        if (_unknown)
            _scroll.Content = new TextBlock { Text = Localizer.T("detail.unknown_kind", source.Kind), TextWrapping = TextWrapping.Wrap, Foreground = TextFg };
        else
            RebuildGrid();

        Closing += (_, _) => CommitOnClose();
        // Tunnel so Escape discards even while a cell is focused (the text box would otherwise keep the key).
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            _discard = true;
            e.Handled = true;
            Close();
        }, RoutingStrategies.Tunnel);
    }

    private void CommitOnClose()
    {
        if (_done) return;
        _done = true;
        if (_discard || _readOnly || _unknown) return;
        Result = CellDetailOps.Normalize(Collect());
        Saved = true;
    }

    private CellDetail Collect()
    {
        var detail = CellDetailOps.Clone(_working);
        foreach (var box in _boxes)
        {
            if (box.Tag is not (int r, int c)) continue;
            var text = box.Text ?? "";
            if (r < 0)
            {
                if (c >= 0 && c < detail.Columns.Count) detail.Columns[c] = text;
            }
            else if (r < detail.Rows.Count)
            {
                var row = detail.Rows[r];
                while (row.Count <= c) row.Add("");
                if (c >= 0 && c < row.Count) row[c] = text;
            }
        }
        return detail;
    }

    private void Pull() => _working = Collect();

    private void AddRow()
    {
        if (_readOnly) return;
        Pull();
        if (_working.Rows.Count >= CellDetailLimits.MaxRows)
        {
            SetMsg(Localizer.T("detail.row_limit", CellDetailLimits.MaxRows));
            return;
        }
        var width = Math.Max(1, _working.Columns.Count);
        _working.Rows.Add(Enumerable.Repeat("", width).ToList());
        _selRow = _working.Rows.Count - 1;
        SetMsg("");
        RebuildGrid();
    }

    private void DeleteRow()
    {
        if (_readOnly) return;
        Pull();
        if (_working.Rows.Count == 0) return;
        var at = _selRow >= 0 && _selRow < _working.Rows.Count ? _selRow : _working.Rows.Count - 1;
        _working.Rows.RemoveAt(at);
        _selRow = _working.Rows.Count == 0 ? -1 : Math.Min(at, _working.Rows.Count - 1);
        SetMsg("");
        RebuildGrid();
    }

    private void AddColumn()
    {
        if (_readOnly) return;
        Pull();
        if (_working.Columns.Count >= CellDetailLimits.MaxColumns)
        {
            SetMsg(Localizer.T("detail.col_limit", CellDetailLimits.MaxColumns));
            return;
        }
        _working.Columns.Add(Localizer.T("detail.col_default", _working.Columns.Count + 1));
        foreach (var row in _working.Rows) row.Add("");
        _selCol = _working.Columns.Count - 1;
        SetMsg("");
        RebuildGrid();
    }

    private void DeleteColumn()
    {
        if (_readOnly) return;
        Pull();
        if (_working.Columns.Count <= 1)
        {
            SetMsg(Localizer.T("detail.need_column"));
            return;
        }
        var at = _selCol >= 0 && _selCol < _working.Columns.Count ? _selCol : _working.Columns.Count - 1;
        _working.Columns.RemoveAt(at);
        foreach (var row in _working.Rows)
            if (at < row.Count) row.RemoveAt(at);
        _selCol = Math.Min(at, _working.Columns.Count - 1);
        SetMsg("");
        RebuildGrid();
    }

    private void RebuildGrid()
    {
        _boxes.Clear();
        var cols = _working.Columns.Count;
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        for (var c = 0; c < cols; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)) { MinWidth = 96 });
        for (var r = 0; r <= _working.Rows.Count; r++)
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        var corner = new Border { Background = HeaderBg, BorderBrush = GridLine, BorderThickness = new Thickness(1), MinWidth = 36, MinHeight = 32 };
        grid.Children.Add(corner);

        for (var c = 0; c < cols; c++)
        {
            var box = Editor(_working.Columns[c], -1, c, header: true);
            Grid.SetColumn(box, c + 1);
            grid.Children.Add(box);
        }
        for (var r = 0; r < _working.Rows.Count; r++)
        {
            var label = new Border
            {
                Background = r == _selRow ? ActiveBg : LabelBg,
                BorderBrush = GridLine,
                BorderThickness = new Thickness(1),
                MinWidth = 36,
                MinHeight = 32,
                Child = new TextBlock
                {
                    Text = (r + 1).ToString(),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = TextFg
                }
            };
            var rr = r;
            label.PointerPressed += (_, e) =>
            {
                Pull();
                _selRow = rr;
                e.Handled = true;
                Dispatcher.UIThread.Post(RebuildGrid);
            };
            Grid.SetRow(label, r + 1);
            grid.Children.Add(label);
            for (var c = 0; c < cols; c++)
            {
                var value = c < _working.Rows[r].Count ? _working.Rows[r][c] : "";
                var box = Editor(value, r, c, header: false);
                Grid.SetRow(box, r + 1);
                Grid.SetColumn(box, c + 1);
                grid.Children.Add(box);
            }
        }
        _scroll.Content = grid;
        _size.Text = Localizer.T("detail.size", cols, _working.Rows.Count);
    }

    private TextBox Editor(string text, int row, int column, bool header)
    {
        var box = new TextBox
        {
            Text = text,
            Tag = (row, column),
            IsReadOnly = _readOnly,
            FontFamily = AppFonts.Thai,
            FontWeight = header ? FontWeight.DemiBold : FontWeight.Normal,
            Foreground = TextFg,
            Background = header ? HeaderBg : CellBg,
            BorderBrush = GridLine,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(6, 4),
            MinHeight = 32,
            CornerRadius = new CornerRadius(0)
        };
        if (header) ToolTip.SetTip(box, Localizer.T("detail.header_tip"));
        box.GotFocus += (_, _) =>
        {
            if (row >= 0) _selRow = row;
            if (column >= 0) _selCol = column;
        };
        box.TextChanged += (_, _) =>
        {
            var value = box.Text ?? "";
            if (value.Length <= CellDetailLimits.MaxCellChars) return;
            box.Text = TableLimits.Clip(value, CellDetailLimits.MaxCellChars);
            box.CaretIndex = (box.Text ?? "").Length;
            SetMsg(Localizer.T("detail.cell_too_long", CellDetailLimits.MaxCellChars));
        };
        _boxes.Add(box);
        return box;
    }

    private void SetMsg(string text) => _message.Text = text;

    private static Button Tool(string text, Action click)
    {
        var b = new Button
        {
            Content = text,
            Background = ToolBg,
            Foreground = TextFg,
            BorderBrush = ToolBorder,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 4),
            Margin = new Thickness(0, 0, 6, 0),
            CornerRadius = new CornerRadius(8),
            FontFamily = AppFonts.Thai
        };
        b.Click += (_, _) => click();
        return b;
    }
}
