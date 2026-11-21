using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace TableNote;

public sealed class DialogDeleteConfirm(Window owner)
{
    public Task<bool> ConfirmAsync(string message) => MessageBox.Show(owner, message, Localizer.T("delete.title"));
}

internal static class MessageBox
{
    /// <summary>Yes / no question. Without button texts it is the delete confirmation (Delete / Cancel, fixed size);
    /// with them the window grows with a longer message.</summary>
    public static async Task<bool> Show(Window owner, string message, string title, string? okText = null, string? cancelText = null)
    {
        var tcs = new TaskCompletionSource<bool>();
        var custom = okText != null;
        var win = new Window
        {
            Title = title,
            Width = custom ? 440 : 380,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            ShowInTaskbar = false,
            FontFamily = AppFonts.Thai,
            Background = new SolidColorBrush(Color.Parse("#FFF6C2"))
        };
        if (custom) win.SizeToContent = SizeToContent.Height;
        else win.Height = 180;

        var ok = new Button { Content = okText ?? Localizer.T("delete.button_ok"), MinWidth = 80, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = cancelText ?? Localizer.T("delete.button_cancel"), MinWidth = 80 };
        if (custom) ok.Margin = new Thickness(0, 14, 8, 0);
        if (custom) cancel.Margin = new Thickness(0, 14, 0, 0);
        ok.Click += (_, _) => { tcs.TrySetResult(true); win.Close(); };
        cancel.Click += (_, _) => { tcs.TrySetResult(false); win.Close(); };
        win.Closed += (_, _) => tcs.TrySetResult(false);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { ok, cancel }
        };
        DockPanel.SetDock(buttons, Dock.Bottom);

        var dock = new DockPanel { Margin = new Thickness(16) };
        dock.Children.Add(buttons);
        dock.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = AppFonts.Thai
        });
        win.Content = dock;
        await win.ShowDialog(owner);
        return await tcs.Task;
    }
}
