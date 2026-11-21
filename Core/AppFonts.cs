using Avalonia.Media;

namespace TableNote;

/// <summary>
/// The one UI font: Noto Sans Thai with the Latin of Noto Sans and a few Noto symbols (← → ✓ ▸ ▾ ⚠ ≈) merged in
/// (tools/fonts/merge_noto.py). Used by the window styles, as the default font (Program.BuildAvaloniaApp) and by
/// image export / print / the wallpaper picture. Only colour emoji (📅 🔁 ⏰) come from the system.
/// </summary>
public static class AppFonts
{
    public const string FamilyUri = "avares://TableNote/Assets/Fonts#Noto Sans Thai";
    public static FontFamily Thai { get; } = new(FamilyUri);

    /// <summary>2.6.0: makes the bundled font the default for every control, including menus, tool tips and
    /// pop-ups that have no style of their own. Used by Program.BuildAvaloniaApp and the UI tests.</summary>
    public static FontManagerOptions ManagerOptions => new() { DefaultFamilyName = FamilyUri };
}
