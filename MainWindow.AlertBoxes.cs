using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace TableNote;

/// <summary>
/// User-managed alert boxes above the table. Add, edit title and text, delete, drag the grip to reorder,
/// or use the Up / Down buttons (keyboard reachable; no drag required). Order and text are the list on
/// <see cref="NoteDocument.AlertBoxes"/> and are saved with note.json. Ids are stable.
/// A box's color is <c>partColors["alert:{id}"]</c> only; it is not stored on the box.
/// </summary>
public partial class MainWindow
{
    private static readonly IBrush AlertDropBrush = new SolidColorBrush(Color.FromRgb(0xD3, 0x2F, 0x2F));
    private static readonly Cursor AlertDragCursor = new(StandardCursorType.SizeNorthSouth);
    private const double AlertDragThreshold = 4;

    private bool _alertRendering;
    private string? _alertDragId;
    private Point _alertDragStart;
    private bool _alertDragging;
    private int _alertDropSlot = -1;

    private void InitAlertBoxes()
    {
        AddAlertButton.Click += (_, _) => AddAlert();
    }

    /// <summary>Alert title/text boxes keep their own Ctrl+Z / Ctrl+Y. Grid cells still use <see cref="FocusedEditingBox"/>.</summary>
    private TextBox? FocusedAlertTextBox()
    {
        if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is not TextBox box || box.IsReadOnly)
            return null;
        for (var v = (Visual?)box; v != null; v = v.GetVisualParent())
            if (ReferenceEquals(v, AlertList)) return box;
        return null;
    }

    private void RenderAlertBoxes()
    {
        EndAlertDrag();
        var doc = _session.Doc;
        doc.AlertBoxes ??= [];
        var edit = _view == ViewMode.Edit;
        var boxes = doc.AlertBoxes;

        AlertHeading.Text = Localizer.T("alert.heading");
        AddAlertButton.Content = Localizer.T("alert.add");
        ToolTip.SetTip(AddAlertButton, Localizer.T("alert.add_tip"));
        AddAlertButton.IsVisible = edit;
        // 2.6.0: no empty panel any more; the first box comes from Table → Add alert box.
        AlertHost.IsVisible = boxes.Count > 0;

        _alertRendering = true;
        try
        {
            AlertList.Children.Clear();
            for (var i = 0; i < boxes.Count; i++)
                AlertList.Children.Add(BuildAlertCard(boxes[i], i, boxes.Count, edit));
        }
        finally
        {
            _alertRendering = false;
        }
    }

    private Border BuildAlertCard(AlertBox box, int index, int count, bool edit)
    {
        var id = box.Id;
        var card = new Border
        {
            Background = CellBg,
            BorderBrush = GridLine,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(6, 4),
            Margin = new Thickness(0, 0, 0, 6),
            Tag = id
        };
        AutomationProperties.SetAutomationId(card, "alert-" + id);

        var grid = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(new GridLength(1, GridUnitType.Star)), new ColumnDefinition(GridLength.Auto) } };

        var grip = new Border
        {
            Width = 28,
            MinHeight = 36,
            Margin = new Thickness(0, 0, 6, 0),
            Background = RowNumBg,
            CornerRadius = new CornerRadius(6),
            Cursor = edit ? AlertDragCursor : Cursor.Default,
            IsVisible = edit,
            Child = new TextBlock
            {
                Text = "⋮⋮",
                FontSize = 12,
                Foreground = TextFg,
                Opacity = 0.75,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        ToolTip.SetTip(grip, Localizer.T("alert.drag_tip"));
        AutomationProperties.SetName(grip, Localizer.T("alert.drag_tip"));
        if (edit) AttachAlertDrag(grip, id);

        var fields = new StackPanel { Spacing = 4 };
        var title = MakeAlertTextBox(box.Title, multiline: false);
        title.FontWeight = FontWeight.SemiBold;
        title.PlaceholderText = Localizer.T("alert.title_ph");
        title.MaxLength = AlertBoxOps.MaxTitleChars;
        title.Tag = "title:" + id;
        title.TextChanged += (_, _) => UpdateAlertField(id, title: true, title.Text);
        var body = MakeAlertTextBox(box.Text, multiline: true);
        body.PlaceholderText = Localizer.T("alert.text_ph");
        body.MaxLength = AlertBoxOps.MaxTextChars;
        body.MinHeight = 44;
        body.MaxHeight = 88;
        body.AcceptsReturn = true;
        body.TextWrapping = TextWrapping.Wrap;
        body.Tag = "text:" + id;
        body.TextChanged += (_, _) => UpdateAlertField(id, title: false, body.Text);
        fields.Children.Add(title);
        fields.Children.Add(body);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0),
            Spacing = 4,
            IsVisible = edit
        };
        buttons.Children.Add(MakeAlertAction(Localizer.T("alert.up"), Localizer.T("alert.move_up"), index > 0, () => MoveAlertBy(id, -1)));
        buttons.Children.Add(MakeAlertAction(Localizer.T("alert.down"), Localizer.T("alert.move_down"), index < count - 1, () => MoveAlertBy(id, 1)));
        buttons.Children.Add(MakeAlertAction(Localizer.T("alert.delete"), Localizer.T("alert.delete_tip"), true, () => _ = DeleteAlertAsync(id)));

        Grid.SetColumn(grip, 0);
        Grid.SetColumn(fields, 1);
        Grid.SetColumn(buttons, 2);
        grid.Children.Add(grip);
        grid.Children.Add(fields);
        grid.Children.Add(buttons);
        card.Child = grid;
        var part = PartColorStore.Get(PartColorKeys.Alert(id));
        if (PartColorPainter.TryBrush(part?.Background) is { } bg) card.Background = bg;
        if (PartColorPainter.TryBrush(part?.Foreground) is { } fg)
        {
            title.Foreground = fg;
            body.Foreground = fg;
        }
        return card;
    }

    private TextBox MakeAlertTextBox(string text, bool multiline)
    {
        var edit = _view == ViewMode.Edit;
        return new TextBox
        {
            Text = text ?? "",
            FontFamily = AppFonts.Thai,
            FontSize = 13,
            Foreground = TextFg,
            Background = Brushes.White,
            BorderBrush = GridLine,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(6, 4),
            CornerRadius = new CornerRadius(6),
            IsReadOnly = !edit,
            IsHitTestVisible = edit,
            AcceptsReturn = multiline
        };
    }

    private Button MakeAlertAction(string content, string tip, bool enabled, Action action)
    {
        var button = new Button
        {
            Content = content,
            IsEnabled = enabled,
            Padding = new Thickness(8, 4),
            FontSize = 12,
            MinHeight = 28
        };
        button.Classes.Add("tool");
        ToolTip.SetTip(button, tip);
        AutomationProperties.SetName(button, tip);
        button.Click += (_, _) => action();
        return button;
    }

    private void UpdateAlertField(string id, bool title, string? value)
    {
        if (_alertRendering) return;
        var box = FindAlert(id);
        if (box == null) return;
        var next = value ?? "";
        if (title)
        {
            if (box.Title == next) return;
            box.Title = next;
        }
        else
        {
            if (box.Text == next) return;
            box.Text = next;
        }
        ScheduleSave();
    }

    private AlertBox? FindAlert(string id)
    {
        var i = AlertBoxOps.IndexOf(_session.Doc.AlertBoxes, id);
        return i < 0 ? null : _session.Doc.AlertBoxes[i];
    }

    private void AddAlert()
    {
        if (_view != ViewMode.Edit || _busy) return;
        var boxes = _session.Doc.AlertBoxes ??= [];
        if (boxes.Count >= AlertBoxOps.MaxBoxes)
        {
            StatusText.Text = Localizer.T("alert.limit", AlertBoxOps.MaxBoxes);
            return;
        }
        var box = AlertBoxOps.Create();
        boxes.Add(box);
        RenderAlertBoxes();
        FlushSave();
        StatusText.Text = Localizer.T("alert.added");
        FocusAlertTitle(box.Id);
    }

    /// <summary>Same confirm dialog as row/column delete. Cancel leaves the box. There is no Ctrl+Z for this.</summary>
    private async Task DeleteAlertAsync(string id)
    {
        if (_view != ViewMode.Edit || _busy) return;
        var boxes = _session.Doc.AlertBoxes;
        var i = AlertBoxOps.IndexOf(boxes, id);
        if (i < 0) return;
        var message = AlertBoxOps.ConfirmMessage(boxes[i], i);
        _busy = true;
        try
        {
            if (!await _confirm.ConfirmAsync(message))
            {
                StatusText.Text = Localizer.T("delete.canceled");
                return;
            }
            boxes = _session.Doc.AlertBoxes;
            i = AlertBoxOps.IndexOf(boxes, id);
            if (i < 0)
            {
                StatusText.Text = Localizer.T("delete.canceled_alert_changed");
                RenderAlertBoxes();
                return;
            }
            boxes.RemoveAt(i);
            RenderAlertBoxes();
            FlushSave();
            StatusText.Text = Localizer.T("alert.deleted");
        }
        finally { _busy = false; }
    }

    private void MoveAlertBy(string id, int delta)
    {
        if (_view != ViewMode.Edit || _busy) return;
        var boxes = _session.Doc.AlertBoxes;
        var from = AlertBoxOps.IndexOf(boxes, id);
        if (from < 0) return;
        if (!AlertBoxOps.Move(boxes, from, from + delta))
        {
            StatusText.Text = Localizer.T("alert.cannot_move");
            return;
        }
        RenderAlertBoxes();
        FlushSave();
        StatusText.Text = Localizer.T("alert.moved");
    }

    private void FocusAlertTitle(string id)
    {
        Dispatcher.UIThread.Post(() =>
        {
            foreach (var tb in AlertList.GetVisualDescendants().OfType<TextBox>())
            {
                if (tb.Tag as string == "title:" + id)
                {
                    tb.Focus();
                    tb.CaretIndex = (tb.Text ?? "").Length;
                    return;
                }
            }
        }, DispatcherPriority.Loaded);
    }

    private void AttachAlertDrag(Border grip, string id)
    {
        grip.PointerPressed += (_, e) =>
        {
            if (_view != ViewMode.Edit || _busy) return;
            if (!e.GetCurrentPoint(grip).Properties.IsLeftButtonPressed) return;
            _alertDragId = id;
            _alertDragging = false;
            _alertDropSlot = -1;
            _alertDragStart = e.GetPosition(AlertList);
            e.Pointer.Capture(grip);
            e.Handled = true;
        };
        grip.PointerMoved += (_, e) =>
        {
            if (_alertDragId != id) return;
            var p = e.GetPosition(AlertList);
            if (!_alertDragging)
            {
                if (Math.Abs(p.Y - _alertDragStart.Y) < AlertDragThreshold) return;
                _alertDragging = true;
                ToolTip.SetIsOpen(grip, false);
            }
            AutoScrollAlerts(e.GetPosition(AlertScroll));
            ShowAlertDropSlot(AlertDropSlotAt(p.Y));
            e.Handled = true;
        };
        grip.PointerReleased += (_, e) =>
        {
            if (_alertDragId != id) return;
            var dragged = _alertDragging;
            var slot = _alertDropSlot;
            EndAlertDrag();
            e.Pointer.Capture(null);
            if (!dragged) return;
            e.Handled = true;
            var boxes = _session.Doc.AlertBoxes;
            var from = AlertBoxOps.IndexOf(boxes, id);
            var to = AlertBoxOps.TargetForSlot(from, slot, boxes.Count);
            if (to < 0 || !AlertBoxOps.Move(boxes, from, to)) return;
            RenderAlertBoxes();
            FlushSave();
            StatusText.Text = Localizer.T("alert.moved");
        };
        grip.PointerCaptureLost += (_, _) =>
        {
            if (_alertDragId == id) EndAlertDrag();
        };
    }

    private int AlertDropSlotAt(double y)
    {
        var n = AlertList.Children.Count;
        if (n == 0) return -1;
        for (var i = 0; i < n; i++)
        {
            if (AlertList.Children[i] is not Control c) continue;
            var top = c.TranslatePoint(new Point(0, 0), AlertList);
            if (top is null) continue;
            var mid = top.Value.Y + Math.Max(1, c.Bounds.Height) / 2;
            if (y < mid) return i;
        }
        return n;
    }

    private void ShowAlertDropSlot(int slot)
    {
        if (slot == _alertDropSlot) return;
        ClearAlertDropMarks();
        _alertDropSlot = slot;
        if (slot < 0 || AlertList.Children.Count == 0) return;
        var atBottom = slot >= AlertList.Children.Count;
        if (AlertList.Children[atBottom ? AlertList.Children.Count - 1 : slot] is Border card)
        {
            card.BorderBrush = AlertDropBrush;
            card.BorderThickness = atBottom ? new Thickness(1, 1, 1, 4) : new Thickness(1, 4, 1, 1);
        }
    }

    private void ClearAlertDropMarks()
    {
        _alertDropSlot = -1;
        foreach (var child in AlertList.Children)
        {
            if (child is not Border card) continue;
            card.BorderBrush = GridLine;
            card.BorderThickness = new Thickness(1);
        }
    }

    private void EndAlertDrag()
    {
        _alertDragId = null;
        _alertDragging = false;
        ClearAlertDropMarks();
    }

    private void AutoScrollAlerts(Point pInScroll)
    {
        if (AlertScroll.Bounds.Height <= 1) return;
        var y = AlertScroll.Offset.Y;
        if (pInScroll.Y < 18) AlertScroll.Offset = new Vector(AlertScroll.Offset.X, Math.Max(0, y - 14));
        else if (pInScroll.Y > AlertScroll.Bounds.Height - 18)
            AlertScroll.Offset = new Vector(AlertScroll.Offset.X, y + 14);
    }
}
