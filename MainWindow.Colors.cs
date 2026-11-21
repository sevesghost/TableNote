using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace TableNote;

public partial class MainWindow
{
    private Button? _colorButton;

    /// <summary>v2.5: toolbar Colors button + restore partColors from note.json.</summary>
    private void InitColors()
    {
        PartColorStore.Lookup = key =>
            _session.Doc.PartColors.TryGetValue(key, out var c) ? c : null;
        _colorButton = new Button { Classes = { "tool" } };
        _colorButton.Click += async (_, _) => await OpenColorDialog();
        EditTools.Children.Insert(EditTools.Children.IndexOf(AddColumnButton) + 1, _colorButton);
        ApplyChromeColors();
    }

    private async Task OpenColorDialog()
    {
        if (_view != ViewMode.Edit)
        {
            StatusText.Text = Localizer.T("color.edit_only");
            return;
        }
        var suggested = _session.HasSelection ? _session.Doc.RowIdAt(_session.ActiveRow) : "";
        var dlg = new ColorPickerDialog(ColorKeyFor, PartColorStore.Get, suggested, _session.HasSelection);
        var result = await dlg.ShowDialog<ColorPickerDialog.Pick?>(this);
        if (result == null) return;
        PartColorOps.Assign(_session.Doc.PartColors, result.Key, result.Background, result.Foreground);
        PrunePartColors();
        FlushSave();
        PartColorStore.RaiseChanged();
        ApplyChromeColors();
        Rebuild();
        RenderAlertBoxes();
        ReminderPopup.Restyle();
        StatusText.Text = result.Background == null && result.Foreground == null
            ? Localizer.T("color.cleared")
            : Localizer.T("color.saved");
    }

    private string? ColorKeyFor(PartTarget target, string? notifyText)
    {
        var doc = _session.Doc;
        switch (target)
        {
            case PartTarget.Cell:
                if (!_session.HasSelection) return null;
                return PartColorKeys.Cell(doc.RowIdAt(_session.ActiveRow), doc.ColumnIdAt(_session.ActiveCol));
            case PartTarget.RowNum:
                if (!_session.HasSelection) return null;
                return PartColorKeys.RowNum(doc.RowIdAt(_session.ActiveRow));
            case PartTarget.Header:
                if (!_session.HasSelection) return null;
                return PartColorKeys.HeaderColumn(doc.ColumnIdAt(_session.ActiveCol));
            case PartTarget.Notify:
                // Alert boxes (format 6) are colored by their own id. An empty id uses the selected row,
                // which is also how a reminder card is keyed.
                var typed = (notifyText ?? "").Trim();
                if (typed.Length == 0)
                    return _session.HasSelection ? PartColorKeys.Alert(doc.RowIdAt(_session.ActiveRow)) : null;
                var id = PartColorOps.CleanId(typed);
                return id == null ? null : PartColorKeys.Alert(id);
            default:
                return PartColorKeys.Fixed(target);
        }
    }

    private void ApplyChromeColors()
    {
        var window = PartColorStore.Get(PartColorKeys.Window);
        Background = PartColorPainter.TryBrush(window?.Background) ?? new SolidColorBrush(Color.Parse("#FFF6C2"));

        var bar = PartColorStore.Get(PartColorKeys.Toolbar);
        var barBg = PartColorPainter.TryBrush(bar?.Background);
        var barFg = PartColorPainter.TryBrush(bar?.Foreground);
        ToolScroll.Background = barBg ?? Brushes.Transparent;
        EditTools.Background = barBg ?? Brushes.Transparent;
        PaintToolbar(EditTools, barBg, barFg);

        var status = PartColorStore.Get(PartColorKeys.Status);
        StatusText.Foreground = PartColorPainter.TryBrush(status?.Foreground) ?? new SolidColorBrush(Color.Parse("#6B5E48"));
        if (PartColorPainter.TryBrush(status?.Background) is { } statusBg) StatusText.Background = statusBg;
        else StatusText.ClearValue(TextBlock.BackgroundProperty);

        var notice = PartColorStore.Get(PartColorKeys.Notice);
        ModeNotice.Foreground = PartColorPainter.TryBrush(notice?.Foreground) ?? new SolidColorBrush(Color.Parse("#A05A12"));
        if (PartColorPainter.TryBrush(notice?.Background) is { } noticeBg) ModeNotice.Background = noticeBg;
        else ModeNotice.ClearValue(TextBlock.BackgroundProperty);

        var table = PartColorStore.Get(PartColorKeys.Table);
        TableHost.Background = PartColorPainter.TryBrush(table?.Background) ?? Brushes.Transparent;
        EmptyText.Foreground = PartColorPainter.TryBrush(table?.Foreground) ?? new SolidColorBrush(Color.Parse("#3B3222"));
    }

    private IBrush RowNumberBackground(int row)
    {
        var status = RowBackground(row).label;
        if (row < 0 || row >= _session.Doc.Rows.Count) return status;
        return PartColorPainter.TryBrush(PartColorStore.Get(PartColorKeys.RowNum(_session.Doc.RowIdAt(row)))?.Background) ?? status;
    }

    /// <summary>Drop cell / header / row-number colors whose row or column was deleted.
    /// notify: keys are kept: a box added later may reuse an id that is not a row.</summary>
    private void PrunePartColors()
    {
        var rows = new HashSet<string>(_session.Doc.RowIds, StringComparer.Ordinal);
        var cols = new HashSet<string>(_session.Doc.ColumnIds, StringComparer.Ordinal);
        var drop = new List<string>();
        foreach (var key in _session.Doc.PartColors.Keys)
        {
            if (key.StartsWith("cell:", StringComparison.Ordinal))
            {
                var rest = key["cell:".Length..];
                var split = rest.IndexOf(':');
                if (split <= 0 || !rows.Contains(rest[..split]) || !cols.Contains(rest[(split + 1)..])) drop.Add(key);
            }
            else if (key.StartsWith("header:", StringComparison.Ordinal) && !cols.Contains(key["header:".Length..])) drop.Add(key);
            else if (key.StartsWith("rownum:", StringComparison.Ordinal) && !rows.Contains(key["rownum:".Length..])) drop.Add(key);
        }
        foreach (var key in drop) _session.Doc.PartColors.Remove(key);
    }

    private PartColor? ColumnHeaderColor(int column) =>
        PartColorOps.Merge(
            PartColorStore.Get(PartColorKeys.HeaderColumn(_session.Doc.ColumnIdAt(column))),
            PartColorStore.Get(PartColorKeys.Header));

    /// <summary>
    /// The only place a whole column is painted (its header, including the "all headers" color).
    /// MakeHeader returns a placeholder for a collapsed child before calling this, so a hidden column is not recolored.
    /// </summary>
    private void PaintColumnHeader(TextBox box, int column) =>
        PartColorPainter.Apply(box, ColumnHeaderColor(column));

    /// <summary>Per-cell color. Number columns paint themselves before the "not a number" check.</summary>
    private void PaintDataCell(Control control, int row, int column)
    {
        if (ColumnSettingsOps.Get(_session.Doc, column).Type == ColumnTypes.Number) return;
        PartColorPainter.Apply(control, PartColorKeys.Cell(_session.Doc.RowIdAt(row), _session.Doc.ColumnIdAt(column)));
    }

    private void PaintNumberCell(TextBox box, int row, int column) =>
        PartColorPainter.Apply(box, PartColorKeys.Cell(_session.Doc.RowIdAt(row), _session.Doc.ColumnIdAt(column)));

    private (IBrush Background, IBrush Foreground) RowLabelColors(int row)
    {
        IBrush? fg = null;
        if (row >= 0 && row < _session.Doc.Rows.Count)
            fg = PartColorPainter.TryBrush(PartColorStore.Get(PartColorKeys.RowNum(_session.Doc.RowIdAt(row)))?.Foreground);
        return (LabelBrushFor(row, RowNumberBackground(row)), fg ?? TextFg);
    }

    /// <summary>Paints every button and check box of the toolbar row (the separators keep their own colour).</summary>
    private static void PaintToolbar(Control root, IBrush? barBg, IBrush? barFg)
    {
        if (root is TemplatedControl control)
        {
            if (barBg != null) control.Background = barBg;
            else control.ClearValue(TemplatedControl.BackgroundProperty);
            if (barFg != null) control.Foreground = barFg;
            else control.ClearValue(TemplatedControl.ForegroundProperty);
        }
        if (root is Panel panel)
        {
            foreach (var child in panel.Children)
                PaintToolbar(child, barBg, barFg);
        }
        else if (root is Decorator { Child: Control child })
            PaintToolbar(child, barBg, barFg);
    }

    private void ApplyColorButtonText()
    {
        if (_colorButton == null) return;
        _colorButton.Content = Localizer.T("color.button");
        ToolTip.SetTip(_colorButton, Localizer.T("tip.color"));
    }
}

/// <summary>RGB / hex picker for one part. Empty channels mean the built-in color.</summary>
public sealed class ColorPickerDialog : Window
{
    public sealed record Pick(string Key, string? Background, string? Foreground);

    private readonly Func<PartTarget, string?, string?> _resolve;
    private readonly Func<string, PartColor?> _lookup;
    private bool _sync;

    public ColorPickerDialog(Func<PartTarget, string?, string?> resolve, Func<string, PartColor?> lookup, string? suggestedNotifyId, bool preferCell)
    {
        _resolve = resolve;
        _lookup = lookup;
        Title = Localizer.T("color.title");
        Width = 520;
        MaxHeight = 680;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = AppFonts.Thai;
        Background = new SolidColorBrush(Color.Parse("#FFF6C2"));

        var choices = new[]
        {
            new Choice(PartTarget.Cell, Localizer.T("color.cell")),
            new Choice(PartTarget.RowNum, Localizer.T("color.rownum")),
            new Choice(PartTarget.Header, Localizer.T("color.header")),
            new Choice(PartTarget.Headers, Localizer.T("color.headers")),
            new Choice(PartTarget.Window, Localizer.T("color.window")),
            new Choice(PartTarget.Toolbar, Localizer.T("color.toolbar")),
            new Choice(PartTarget.Status, Localizer.T("color.status")),
            new Choice(PartTarget.Notice, Localizer.T("color.notice")),
            new Choice(PartTarget.Table, Localizer.T("color.table")),
            new Choice(PartTarget.Popup, Localizer.T("color.popup")),
            new Choice(PartTarget.PopupCard, Localizer.T("color.popup_card")),
            new Choice(PartTarget.Notify, Localizer.T("color.notify")),
        };
        var targetBox = new ComboBox { ItemsSource = choices, SelectedIndex = preferCell ? 0 : 4, HorizontalAlignment = HorizontalAlignment.Stretch };
        var idBox = new TextBox { Text = suggestedNotifyId ?? "", PlaceholderText = Localizer.T("color.notify_ph") };
        var idPanel = new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new TextBlock { Text = Localizer.T("color.notify_id") },
                idBox
            }
        };
        var keyLabel = new TextBlock { FontSize = 12, Opacity = 0.75, TextWrapping = TextWrapping.Wrap };
        var previewText = new TextBlock
        {
            Text = Localizer.T("color.preview"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 16
        };
        var preview = new Border
        {
            Width = 120, Height = 36, CornerRadius = new CornerRadius(6),
            BorderBrush = new SolidColorBrush(Color.Parse("#D4B86A")), BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Left, Child = previewText
        };
        var bg = NewChannel(Localizer.T("color.background"));
        var fg = NewChannel(Localizer.T("color.text"));
        var error = new TextBlock { Foreground = new SolidColorBrush(Color.Parse("#B03A2E")), TextWrapping = TextWrapping.Wrap, IsVisible = false };

        void ShowError(string text) { error.Text = text; error.IsVisible = true; }

        void UpdatePreview()
        {
            preview.Background = PartColorPainter.TryBrush(bg.Hex.Text) ?? new SolidColorBrush(Color.Parse("#FFFDF6"));
            previewText.Foreground = PartColorPainter.TryBrush(fg.Hex.Text) ?? new SolidColorBrush(Color.Parse("#3B3222"));
        }

        void LoadHex(Channel ch, string? hex)
        {
            _sync = true;
            try
            {
                if (PartColorOps.TryRgb(hex, out var r, out var g, out var b))
                {
                    ch.Hex.Text = PartColorOps.FromRgb(r, g, b);
                    ch.R.Text = r.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    ch.G.Text = g.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    ch.B.Text = b.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    ch.Rs.Value = r; ch.Gs.Value = g; ch.Bs.Value = b;
                }
                else
                {
                    ch.Hex.Text = "";
                    ch.R.Text = ch.G.Text = ch.B.Text = "";
                    ch.Rs.Value = ch.Gs.Value = ch.Bs.Value = 0;
                }
            }
            finally { _sync = false; }
            UpdatePreview();
        }

        void RefreshKey()
        {
            var choice = targetBox.SelectedItem as Choice;
            idPanel.IsVisible = choice?.Target == PartTarget.Notify;
            var key = choice == null ? null : _resolve(choice.Target, idBox.Text);
            keyLabel.Text = key == null
                ? (choice?.Target == PartTarget.Notify ? Localizer.T("color.need_id") : Localizer.T("color.need_target"))
                : Localizer.T("color.key", key);
            var part = key == null ? null : _lookup(key);
            LoadHex(bg, part?.Background);
            LoadHex(fg, part?.Foreground);
            error.IsVisible = false;
        }

        // Sliders rewrite the boxes. Typing in a box updates the sliders and the other boxes,
        // but never replaces the box being typed in — "#102" is a valid short hex and a prefix of "#102030".
        void Wire(Channel ch)
        {
            void SetSliders(byte r, byte g, byte b)
            {
                ch.Rs.Value = r; ch.Gs.Value = g; ch.Bs.Value = b;
            }
            void FromSliders()
            {
                if (_sync) return;
                var r = (byte)Math.Round(ch.Rs.Value);
                var g = (byte)Math.Round(ch.Gs.Value);
                var b = (byte)Math.Round(ch.Bs.Value);
                _sync = true;
                try
                {
                    ch.Hex.Text = PartColorOps.FromRgb(r, g, b);
                    ch.R.Text = r.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    ch.G.Text = g.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    ch.B.Text = b.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
                finally { _sync = false; }
                UpdatePreview();
            }
            ch.Rs.PropertyChanged += (_, e) => { if (e.Property == RangeBase.ValueProperty) FromSliders(); };
            ch.Gs.PropertyChanged += (_, e) => { if (e.Property == RangeBase.ValueProperty) FromSliders(); };
            ch.Bs.PropertyChanged += (_, e) => { if (e.Property == RangeBase.ValueProperty) FromSliders(); };
            void FromRgbBoxes()
            {
                if (_sync) return;
                var empty = string.IsNullOrWhiteSpace(ch.R.Text) && string.IsNullOrWhiteSpace(ch.G.Text) && string.IsNullOrWhiteSpace(ch.B.Text);
                if (empty)
                {
                    _sync = true;
                    try { ch.Hex.Text = ""; SetSliders(0, 0, 0); }
                    finally { _sync = false; }
                    UpdatePreview();
                    return;
                }
                if (!byte.TryParse(ch.R.Text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var r) ||
                    !byte.TryParse(ch.G.Text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var g) ||
                    !byte.TryParse(ch.B.Text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var b))
                    return;
                _sync = true;
                try { ch.Hex.Text = PartColorOps.FromRgb(r, g, b); SetSliders(r, g, b); }
                finally { _sync = false; }
                UpdatePreview();
            }
            ch.R.TextChanged += (_, _) => FromRgbBoxes();
            ch.G.TextChanged += (_, _) => FromRgbBoxes();
            ch.B.TextChanged += (_, _) => FromRgbBoxes();
            ch.Hex.TextChanged += (_, _) =>
            {
                if (_sync) return;
                var t = (ch.Hex.Text ?? "").Trim();
                if (t.Length == 0)
                {
                    _sync = true;
                    try { ch.R.Text = ch.G.Text = ch.B.Text = ""; SetSliders(0, 0, 0); }
                    finally { _sync = false; }
                    UpdatePreview();
                    return;
                }
                if (!IsFinishedColor(t) || !PartColorOps.TryRgb(t, out var r, out var g, out var b))
                {
                    UpdatePreview();
                    return;
                }
                _sync = true;
                try
                {
                    ch.R.Text = r.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    ch.G.Text = g.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    ch.B.Text = b.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    SetSliders(r, g, b);
                }
                finally { _sync = false; }
                UpdatePreview();
            };
        }

        Wire(bg);
        Wire(fg);
        targetBox.SelectionChanged += (_, _) => RefreshKey();
        idBox.TextChanged += (_, _) => { if (targetBox.SelectedItem is Choice { Target: PartTarget.Notify }) RefreshKey(); };
        RefreshKey();

        bool TryRead(Channel ch, out string? color)
        {
            color = null;
            var t = (ch.Hex.Text ?? "").Trim();
            if (t.Length == 0) return true;
            color = PartColorOps.NormalizeHex(t);
            if (color != null) return true;
            ShowError(Localizer.T("color.bad_hex", t));
            return false;
        }

        string? CurrentKey() => targetBox.SelectedItem is Choice c ? _resolve(c.Target, idBox.Text) : null;

        var ok = new Button { Content = Localizer.T("common.ok"), MinWidth = 80, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var reset = new Button { Content = Localizer.T("color.reset"), MinWidth = 120, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = Localizer.T("common.cancel"), MinWidth = 80, IsCancel = true };
        ok.Click += (_, _) =>
        {
            var key = CurrentKey();
            if (key == null)
            {
                ShowError(targetBox.SelectedItem is Choice { Target: PartTarget.Notify }
                    ? Localizer.T("color.need_id")
                    : Localizer.T("color.need_target"));
                return;
            }
            if (!TryRead(bg, out var background) || !TryRead(fg, out var foreground)) return;
            Close(new Pick(key, background, foreground));
        };
        reset.Click += (_, _) =>
        {
            var key = CurrentKey();
            if (key == null)
            {
                ShowError(targetBox.SelectedItem is Choice { Target: PartTarget.Notify }
                    ? Localizer.T("color.need_id")
                    : Localizer.T("color.need_target"));
                return;
            }
            Close(new Pick(key, null, null));
        };
        cancel.Click += (_, _) => Close(null);

        var root = new StackPanel { Margin = new Thickness(16), Spacing = 6 };
        root.Children.Add(new TextBlock { Text = Localizer.T("color.target") });
        root.Children.Add(targetBox);
        root.Children.Add(idPanel);
        root.Children.Add(keyLabel);
        root.Children.Add(preview);
        root.Children.Add(bg.Panel);
        root.Children.Add(fg.Panel);
        root.Children.Add(new TextBlock { Text = Localizer.T("color.hint"), TextWrapping = TextWrapping.Wrap, Opacity = 0.75, FontSize = 12 });
        root.Children.Add(error);
        root.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0), Children = { reset, ok, cancel }
        });
        Content = new ScrollViewer { Content = root };
    }

    private sealed class Choice
    {
        public Choice(PartTarget target, string label) { Target = target; Label = label; }
        public PartTarget Target { get; }
        public string Label { get; }
        public override string ToString() => Label;
    }

    private sealed class Channel
    {
        public required StackPanel Panel { get; init; }
        public required TextBox Hex { get; init; }
        public required TextBox R { get; init; }
        public required TextBox G { get; init; }
        public required TextBox B { get; init; }
        public required Slider Rs { get; init; }
        public required Slider Gs { get; init; }
        public required Slider Bs { get; init; }
    }

    private static Channel NewChannel(string title)
    {
        var hex = new TextBox { PlaceholderText = "#RRGGBB", Width = 96 };
        var r = RgbBox(); var g = RgbBox(); var b = RgbBox();
        var rs = RgbSlider(); var gs = RgbSlider(); var bs = RgbSlider();
        var numbers = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 6,
            Children =
            {
                Labeled(Localizer.T("color.hex"), hex),
                Labeled(Localizer.T("color.r"), r),
                Labeled(Localizer.T("color.g"), g),
                Labeled(Localizer.T("color.b"), b)
            }
        };
        var sliders = new StackPanel { Spacing = 2, Children = { rs, gs, bs } };
        var panel = new StackPanel
        {
            Spacing = 4, Margin = new Thickness(0, 6, 0, 0),
            Children = { new TextBlock { Text = title }, numbers, sliders }
        };
        return new Channel { Panel = panel, Hex = hex, R = r, G = g, B = b, Rs = rs, Gs = gs, Bs = bs };
    }

    /// <summary>A color the user has finished typing. Three-digit #RGB is accepted on OK, not while typing,
    /// because it is also the start of #RRGGBB.</summary>
    private static bool IsFinishedColor(string text)
    {
        var t = text.Trim();
        if (t.StartsWith("rgb", StringComparison.OrdinalIgnoreCase)) return t.Contains(')');
        var parts = t.Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 3) return true;
        var hex = t.Length > 0 && t[0] == '#' ? t[1..] : t;
        return hex.Length is 6 or 8 && hex.All(Uri.IsHexDigit);
    }

    private static TextBox RgbBox() => new() { Width = 48, PlaceholderText = "0" };

    private static Slider RgbSlider() => new()
    {
        Minimum = 0, Maximum = 255, TickFrequency = 1, IsSnapToTickEnabled = true, Height = 18
    };

    private static StackPanel Labeled(string name, Control box) => new()
    {
        Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center,
        Children = { new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center, MinWidth = 14 }, box }
    };
}
