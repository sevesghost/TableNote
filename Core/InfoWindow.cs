using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace TableNote;

public static class InfoWindow
{
    public static void Show(string title, string body)
    {
        var window = new Window
        {
            Title = title,
            Width = 440,
            Height = 560,
            MinWidth = 320,
            MinHeight = 240,
            Topmost = true,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = new SolidColorBrush(Color.Parse("#FFF6C2")),
            FontFamily = AppFonts.Thai
        };
        var content = new TextBlock
        {
            Text = body,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = AppFonts.Thai,
            FontSize = 14,
            Foreground = new SolidColorBrush(Color.Parse("#3B3222")),
            Margin = new Thickness(16)
        };
        var button = new Button
        {
            Content = Localizer.T("ui.close"),
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(16, 0, 16, 16),
            MinWidth = 88,
            FontFamily = AppFonts.Thai
        };
        button.Click += (_, _) => window.Close();
        var dock = new DockPanel();
        DockPanel.SetDock(button, Dock.Bottom);
        dock.Children.Add(button);
        dock.Children.Add(new ScrollViewer
        {
            Content = content,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        });
        window.Content = dock;
        window.Show();
    }
}
