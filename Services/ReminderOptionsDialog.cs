using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace TableNote;

/// <summary>⏰ dialog: choose up to 5 advance reminders (15 minutes / 1 hour / 1 day / custom).</summary>
public sealed class ReminderOptionsDialog : Window
{
    private readonly List<int> _offsets;
    private readonly StackPanel _chips = new() { Spacing = 4 };
    private readonly TextBlock _msg = new() { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, FontFamily = AppFonts.Thai };
    private readonly Dictionary<int, CheckBox> _presetBoxes = new();
    private bool _sync;
    private bool _maxMsgShown; // set when the "max 5" message is showing (no string comparison)

    public ReminderOptionsDialog(string taskTitle, IEnumerable<int> current, bool dueValid)
    {
        _offsets = ReminderPlan.CleanOffsets(current);
        Title = Localizer.T("remind.title");
        Width = 380;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = AppFonts.Thai;

        var root = new StackPanel { Margin = new Thickness(14), Spacing = 8 };
        root.Children.Add(new TextBlock { Text = Localizer.T("remind.task", taskTitle), FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap });
        root.Children.Add(new TextBlock { Text = Localizer.T("remind.info"), TextWrapping = TextWrapping.Wrap, Opacity = 0.8 });
        if (!dueValid)
            root.Children.Add(new TextBlock { Text = Localizer.T("remind.no_due"), Foreground = Brushes.DarkOrange, TextWrapping = TextWrapping.Wrap });

        var presets = new WrapPanel();
        foreach (var (min, label) in ReminderPlan.Presets)
        {
            var cb = new CheckBox { Content = Localizer.T("remind.before", label), Margin = new Thickness(0, 0, 12, 0), IsChecked = _offsets.Contains(min) };
            var m = min;
            cb.IsCheckedChanged += (_, _) =>
            {
                if (_sync) return;
                if (cb.IsChecked == true) { if (!TryAdd(m)) { _sync = true; cb.IsChecked = false; _sync = false; } }
                else { _offsets.Remove(m); Refresh(); }
            };
            _presetBoxes[min] = cb;
            presets.Children.Add(cb);
        }
        root.Children.Add(presets);

        var num = new NumericUpDown { Minimum = 1, Maximum = 999, Value = 30, Increment = 1, FormatString = "0", Width = 120 };
        var unit = new ComboBox { ItemsSource = new[] { Localizer.T("unit.minutes"), Localizer.T("unit.hours"), Localizer.T("unit.days") }, SelectedIndex = 0, Width = 90, Margin = new Thickness(6, 0) };
        var add = new Button { Content = Localizer.T("remind.add") };
        add.Click += (_, _) =>
        {
            var v = (int)(num.Value ?? 0);
            var mult = unit.SelectedIndex switch { 1 => 60, 2 => 1440, _ => 1 };
            var minutes = v * mult;
            if (v <= 0) { SetMsg(Localizer.T("remind.positive")); return; }
            if (minutes > ReminderPlan.MaxOffsetMinutes) { SetMsg(Localizer.T("remind.max_days")); return; }
            if (_offsets.Contains(minutes)) { SetMsg(Localizer.T("remind.duplicate", ReminderPlan.OffsetLabel(minutes))); return; }
            TryAdd(minutes);
        };
        var custom = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        custom.Children.Add(new TextBlock { Text = Localizer.T("remind.custom"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        custom.Children.Add(num); custom.Children.Add(unit); custom.Children.Add(add);
        root.Children.Add(custom);

        root.Children.Add(new TextBlock { Text = Localizer.T("remind.current"), FontWeight = FontWeight.SemiBold });
        root.Children.Add(_chips);
        root.Children.Add(_msg);

        var ok = new Button { Content = Localizer.T("common.save"), IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = Localizer.T("common.cancel"), IsCancel = true };
        ok.Click += (_, _) => Close(ReminderPlan.CleanOffsets(_offsets));
        cancel.Click += (_, _) => Close(null);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 6, 0, 0) };
        buttons.Children.Add(ok); buttons.Children.Add(cancel);
        root.Children.Add(buttons);

        Content = root;
        Refresh();
    }

    private bool TryAdd(int minutes)
    {
        if (_offsets.Contains(minutes)) { Refresh(); return true; }
        if (_offsets.Count >= ReminderPlan.MaxOffsets)
        {
            SetMsg(Localizer.T("remind.max")); _maxMsgShown = true;
            return false;
        }
        _offsets.Add(minutes);
        Refresh();
        return true;
    }

    private void Refresh()
    {
        _sync = true;
        foreach (var (m, cb) in _presetBoxes) cb.IsChecked = _offsets.Contains(m);
        _sync = false;
        _chips.Children.Clear();
        var sorted = _offsets.OrderBy(o => o).ToList();
        if (sorted.Count == 0)
            _chips.Children.Add(new TextBlock { Text = Localizer.T("remind.only_on_time"), Opacity = 0.7 });
        foreach (var m in sorted)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            row.Children.Add(new TextBlock { Text = "⏰ " + Localizer.T("remind.before", ReminderPlan.OffsetLabel(m)), VerticalAlignment = VerticalAlignment.Center });
            var rm = new Button { Content = "✕", Padding = new Thickness(6, 0) };
            var mm = m;
            rm.Click += (_, _) => { _offsets.Remove(mm); SetMsg(""); Refresh(); };
            ToolTip.SetTip(rm, Localizer.T("remind.remove"));
            row.Children.Add(rm);
            _chips.Children.Add(row);
        }
        if (_offsets.Count < ReminderPlan.MaxOffsets && _maxMsgShown) SetMsg("");
    }

    private void SetMsg(string text)
    {
        _msg.Text = text;
        _maxMsgShown = false;
    }
}
