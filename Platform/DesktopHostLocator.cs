namespace TableNote;

/// <summary>
/// Pure host selection for desktop (wallpaper-layer) mode. Input: top-level windows in Z order
/// (as EnumWindows returns them) after Progman was sent 0x052C. Output: an ordered list of hosts to try.
///   1. the WorkerW that is the NEXT sibling after the window holding SHELLDLL_DefView (classic Win10 trick:
///      behind the icons, in front of the wallpaper)
///   2. the window that holds SHELLDLL_DefView itself (WorkerW or Progman; we insert at the bottom so icons stay on top)
///   3. Progman
/// </summary>
public static class DesktopHostLocator
{
    public readonly record struct Choice(DesktopHostKind Kind, nint Handle);

    public static Choice Choose(IReadOnlyList<ShellNode> topLevel)
    {
        var c = Candidates(topLevel);
        return c.Count > 0 ? c[0] : new Choice(DesktopHostKind.None, 0);
    }

    public static List<Choice> Candidates(IReadOnlyList<ShellNode> topLevel)
    {
        var list = new List<Choice>();
        var iconIndex = -1;
        for (var i = 0; i < topLevel.Count; i++)
            if (topLevel[i].HasDesktopIcons) { iconIndex = i; break; }

        if (iconIndex >= 0)
        {
            // FindWindowEx(0, iconHost, "WorkerW") == next WorkerW after the icon host in Z order.
            for (var j = iconIndex + 1; j < topLevel.Count; j++)
                if (topLevel[j].ClassName == "WorkerW" && !topLevel[j].HasDesktopIcons)
                {
                    list.Add(new Choice(DesktopHostKind.WorkerWBehindIcons, topLevel[j].Id));
                    break;
                }
            var host = topLevel[iconIndex];
            if (host.ClassName is "WorkerW" or "Progman")
                list.Add(new Choice(host.ClassName == "Progman" ? DesktopHostKind.Progman : DesktopHostKind.IconHost, host.Id));
        }
        var progman = topLevel.FirstOrDefault(n => n.ClassName == "Progman");
        if (progman != null && list.All(c => c.Handle != progman.Id))
            list.Add(new Choice(DesktopHostKind.Progman, progman.Id));
        return list;
    }
}
