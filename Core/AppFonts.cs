using Avalonia.Media;

namespace TableNote;

public static class AppFonts
{
    public const string FamilyUri = "avares://TableNote/Assets/Fonts#Noto Sans Thai";
    public static FontFamily Thai { get; } = new(FamilyUri);
}
