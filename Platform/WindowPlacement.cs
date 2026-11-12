namespace TableNote;

public static class WindowPlacement
{
    public readonly record struct IntRect(int X, int Y, int Width, int Height);
    public readonly record struct RestoreResult(int X, int Y, int Width, int Height, bool Recentered);

    public const int TitleBand = 32;
    public const int MinTitleReach = 48;

    public static bool TitleReachable(int x, int y, int width, IntRect area)
    {
        var left = Math.Max(x, area.X);
        var right = Math.Min(x + Math.Max(width, 1), area.X + area.Width);
        var top = Math.Max(y, area.Y);
        var bottom = Math.Min(y + TitleBand, area.Y + area.Height);
        return right - left >= MinTitleReach && bottom - top >= 8;
    }

    public static RestoreResult Restore(int x, int y, int width, int height, IReadOnlyList<IntRect> workAreas, IntRect preferred)
    {
        if (workAreas.Count == 0)
            workAreas = [preferred.Width > 0 ? preferred : new IntRect(0, 0, 720, 480)];
        if (preferred.Width <= 0 || preferred.Height <= 0)
            preferred = workAreas[0];

        foreach (var area in workAreas)
        {
            if (!TitleReachable(x, y, width, area)) continue;
            var w = FitSize(width, area.Width, 340, 720);
            var h = FitSize(height, area.Height, 300, 480);
            if (TitleReachable(x, y, w, area))
                return new RestoreResult(x, y, w, h, false);
        }
        return Center(preferred, width, height);
    }

    public static RestoreResult Center(IntRect area, int width, int height)
    {
        if (area.Width <= 0 || area.Height <= 0)
            area = new IntRect(0, 0, Math.Max(width, 720), Math.Max(height, 480));
        var w = FitSize(width, area.Width, 340, 720);
        var h = FitSize(height, area.Height, 300, 480);
        var x = area.X + Math.Max(0, (area.Width - w) / 2);
        var y = area.Y + Math.Max(0, (area.Height - h) / 2);
        return new RestoreResult(x, y, w, h, true);
    }

    private static int FitSize(int value, int limit, int min, int fallback)
    {
        var max = Math.Max(1, limit);
        var lo = Math.Min(min, max);
        if (value <= 0 || value > 100000) value = Math.Min(fallback, max);
        return Math.Clamp(value, lo, max);
    }
}
