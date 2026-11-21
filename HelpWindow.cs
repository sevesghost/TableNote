using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace TableNote;

/// <summary>2.6.0: Help as topics that open and close (click a title; click it again to close it), instead of one long
/// page. "Open all" / "Close all" at the top. Content: <see cref="AppInfo.HelpText"/>, parsed by <see cref="HelpContent"/>.</summary>
public sealed class HelpWindow : Window
{
    private static readonly IBrush TextFg = new SolidColorBrush(Color.Parse("#3B3222"));
    private static readonly IBrush SoftFg = new SolidColorBrush(Color.Parse("#7A6A48"));
    private static readonly IBrush KeyBg = new SolidColorBrush(Color.Parse("#FBEFC4"));
    private static readonly IBrush PanelBg = new SolidColorBrush(Color.Parse("#FFFBE8"));
    private static readonly IBrush Line = new SolidColorBrush(Color.Parse("#E5D49A"));

    private static HelpWindow? _open;

    public List<Expander> Topics { get; } = [];

    /// <summary>Shows the help (one window: a second call brings the open one forward).</summary>
    public static HelpWindow Show(string title, string text, double zoom = 1)
    {
        if (_open is { IsVisible: true } w) { w.Activate(); return w; }
        _open = new HelpWindow(title, text, zoom);
        _open.Closed += (_, _) => _open = null;
        _open.Show();
        return _open;
    }

    public HelpWindow(string title, string text, double zoom = 1)
    {
        zoom = Math.Clamp(zoom, 0.8, 2);
        Title = title;
        Width = 620 * zoom;
        Height = 640 * zoom;
        MinWidth = 380;
        MinHeight = 300;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.Parse("#FFF6C2"));
        FontFamily = AppFonts.Thai;
        Foreground = TextFg;

        var (heading, sections) = HelpContent.Parse(text);
        var top = new StackPanel { Spacing = 6, Margin = new Thickness(16, 14, 16, 8) };
        top.Children.Add(new TextBlock { Text = heading, FontSize = 18, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap });
        var openAll = new Button { Content = Localizer.T("help.expand_all"), Padding = new Thickness(10, 3) };
        var closeAll = new Button { Content = Localizer.T("help.collapse_all"), Padding = new Thickness(10, 3) };
        openAll.Click += (_, _) => { foreach (var t in Topics) t.IsExpanded = true; };
        closeAll.Click += (_, _) => { foreach (var t in Topics) t.IsExpanded = false; };
        var hintRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto") };
        var hint = new TextBlock { Text = Localizer.T("help.hint"), Foreground = SoftFg, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        Grid.SetColumn(openAll, 1);
        Grid.SetColumn(closeAll, 2);
        closeAll.Margin = new Thickness(6, 0, 0, 0);
        hintRow.Children.Add(hint);
        hintRow.Children.Add(openAll);
        hintRow.Children.Add(closeAll);
        top.Children.Add(hintRow);

        var list = new StackPanel { Spacing = 6, Margin = new Thickness(16, 0, 16, 8) };
        foreach (var section in sections)
        {
            var expander = new Expander
            {
                Header = new TextBlock { Text = section.Title, FontSize = 15, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap },
                Content = Body(section),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Background = PanelBg,
                FontFamily = AppFonts.Thai
            };
            Topics.Add(expander);
            list.Children.Add(expander);
        }

        var close = new Button
        {
            Content = Localizer.T("ui.close"), HorizontalAlignment = HorizontalAlignment.Right,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(16, 4, 16, 14), MinWidth = 88, IsCancel = true
        };
        close.Click += (_, _) => Close();

        var dock = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(close, Dock.Bottom);
        dock.Children.Add(top);
        dock.Children.Add(close);
        dock.Children.Add(new ScrollViewer { Content = list, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        Content = Math.Abs(zoom - 1) < 0.001 ? dock : new LayoutTransformControl { LayoutTransform = new ScaleTransform(zoom, zoom), Child = dock };
    }

    /// <summary>A topic's lines: key / description pairs in two columns, bullets, paragraphs.</summary>
    private static Control Body(HelpSection section)
    {
        var panel = new StackPanel { Spacing = 4, Margin = new Thickness(2, 2, 2, 4) };
        Grid? pairs = null;
        foreach (var line in section.Lines)
        {
            if (line.Kind == HelpLineKind.Pair)
            {
                if (pairs == null)
                {
                    pairs = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
                    panel.Children.Add(pairs);
                }
                var row = pairs.RowDefinitions.Count;
                pairs.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                var key = new Border
                {
                    Background = KeyBg, BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(6, 1), Margin = new Thickness(0, 2, 10, 2), MaxWidth = 230,
                    HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                    Child = new TextBlock { Text = line.Key, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, FontSize = 13 }
                };
                var text = new TextBlock { Text = line.Text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 3), FontSize = 14 };
                Grid.SetRow(key, row);
                Grid.SetRow(text, row);
                Grid.SetColumn(text, 1);
                pairs.Children.Add(key);
                pairs.Children.Add(text);
                continue;
            }
            pairs = null;
            panel.Children.Add(line.Kind == HelpLineKind.Bullet
                ? new TextBlock { Text = "•  " + line.Text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6, 0, 0, 0), FontSize = 14 }
                : new TextBlock { Text = line.Text, TextWrapping = TextWrapping.Wrap, FontSize = 14, Foreground = TextFg });
        }
        return panel;
    }
}
