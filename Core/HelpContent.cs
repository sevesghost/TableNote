namespace TableNote;

/// <summary>2.6.0: the in-app help, split into topics. Written in <see cref="AppInfo"/> with a small markup:
/// "# Title" starts a topic; "Key :: what it does" is a key / description line; "- text" is a bullet; any other
/// line is a paragraph. Lines before the first topic are the heading at the top (version).</summary>
public sealed record HelpSection(string Title, IReadOnlyList<HelpLine> Lines);

public enum HelpLineKind { Text, Pair, Bullet }

public sealed record HelpLine(HelpLineKind Kind, string Text, string? Key = null);

public static class HelpContent
{
    public static (string Heading, List<HelpSection> Sections) Parse(string text)
    {
        var heading = new List<string>();
        var sections = new List<HelpSection>();
        string? title = null;
        var lines = new List<HelpLine>();
        void Flush()
        {
            if (title != null) sections.Add(new HelpSection(title, lines.ToList()));
            lines.Clear();
        }
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("# ", StringComparison.Ordinal))
            {
                Flush();
                title = line[2..].Trim();
                continue;
            }
            if (title == null)
            {
                if (line.Length > 0) heading.Add(line);
                continue;
            }
            if (line.Length == 0) continue;
            if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                lines.Add(new HelpLine(HelpLineKind.Bullet, line[2..].Trim()));
                continue;
            }
            var sep = line.IndexOf(" :: ", StringComparison.Ordinal);
            lines.Add(sep > 0
                ? new HelpLine(HelpLineKind.Pair, line[(sep + 4)..].Trim(), line[..sep].Trim())
                : new HelpLine(HelpLineKind.Text, line));
        }
        Flush();
        return (string.Join("\n", heading), sections);
    }
}
