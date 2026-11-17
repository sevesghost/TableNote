using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace TableNote;

/// <summary>Paints a control from a <see cref="PartColor"/>. Future notification boxes can call
/// <see cref="Apply(Control?, string)"/> with <see cref="PartColorKeys.Notify"/>.</summary>
public static class PartColorPainter
{
    private static readonly Dictionary<string, IBrush> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static IBrush? TryBrush(string? hex)
    {
        var n = PartColorOps.NormalizeHex(hex);
        if (n == null) return null;
        if (!Cache.TryGetValue(n, out var b))
            Cache[n] = b = new SolidColorBrush(Color.Parse(n));
        return b;
    }

    public static void Apply(Control? control, string key) => Apply(control, PartColorStore.Get(key));

    public static void Apply(Control? control, PartColor? part)
    {
        if (control == null || part == null) return;
        if (TryBrush(part.Background) is { } bg) SetBackground(control, bg);
        if (TryBrush(part.Foreground) is { } fg) SetForeground(control, fg);
    }

    private static void SetBackground(Control control, IBrush brush)
    {
        switch (control)
        {
            case Border border: border.Background = brush; break;
            case Panel panel: panel.Background = brush; break;
            case TemplatedControl tc: tc.Background = brush; break;
        }
    }

    private static void SetForeground(Control control, IBrush brush)
    {
        switch (control)
        {
            case TextBox tb: tb.Foreground = brush; break;
            case ComboBox cb: cb.Foreground = brush; break;
            case TextBlock tx: tx.Foreground = brush; break;
            case CheckBox ck: ck.Foreground = brush; break;
            case Button button: button.Foreground = brush; break;
            case Panel panel:
                foreach (var child in panel.Children)
                    if (child is Control c) SetForeground(c, brush);
                break;
            case Decorator d when d.Child is Control child:
                SetForeground(child, brush);
                break;
        }
    }
}
