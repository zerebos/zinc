using System.Text;
using System.Text.RegularExpressions;

namespace Zinc;

/// <summary>A piece of release notes as shown: a heading ("### Fixed"), a bullet, or a paragraph.</summary>
public enum NoteKind { Heading, Bullet, Paragraph }

/// <summary>
/// Reads CHANGELOG.md's entries. It finds one version's notes (what a release hands the installer, and what a
/// downloaded update shows under "What's new"), and splits those notes into headings, bullets and paragraphs for
/// showing without a Markdown renderer. The file starts with <c>## Unreleased</c>, then has <c>## 0.1.0 (date)</c>
/// per release, each with <c>### Section</c>s of <c>- bullets</c> (continued on indented lines) or plain paragraphs.
/// </summary>
public static partial class ReleaseNotes
{
    /// <summary>
    /// The entry for <paramref name="version"/> ("0.1.0" or "0.1.0-rc.1", no commit hash), which is what's under its
    /// heading.
    /// </summary>
    public static string? Entry(string changelog, string version)
    {
        var lines = changelog.Replace("\r\n", "\n").Split('\n');
        var start = Array.FindIndex(lines, l => l.StartsWith($"## {version} (", StringComparison.Ordinal) || l.TrimEnd() == $"## {version}");
        if (start < 0)
        {
            return null;
        }

        var end = Array.FindIndex(lines, start + 1, l => l.StartsWith("## ", StringComparison.Ordinal));
        return string.Join('\n', lines[(start + 1)..(end < 0 ? lines.Length : end)]).Trim();
    }

    /// <summary>The version of the newest released entry (the first after "Unreleased"), if any.</summary>
    public static string? Newest(string changelog) =>
        VersionHeading().Matches(changelog).Select(m => m.Groups[1].Value).FirstOrDefault(v => v != "Unreleased");

    /// <summary>Whether "## Unreleased" has anything under it (a release should have taken it under its own heading).</summary>
    public static bool HasUnreleased(string changelog) => Entry(changelog, "Unreleased") is { Length: > 0 };

    /// <summary>
    /// Why <paramref name="version"/> can't be released from this changelog, or null when it can. A release needs an
    /// entry of its own, and nothing left under "## Unreleased", whose notes belong under the release's heading.
    /// </summary>
    public static string? ReleaseProblem(string changelog, string version) =>
        HasUnreleased(changelog)
            ? $"The changelog has notes under \"## Unreleased\". Rename that heading to \"## {version} (date)\" and start a new empty \"## Unreleased\" above it."
            : Entry(changelog, version) is { Length: > 0 } ? null
            : $"The changelog has no entry for {version}.";

    [GeneratedRegex(@"^## (\S+)", RegexOptions.Multiline)]
    private static partial Regex VersionHeading();

    /// <summary>
    /// Notes as pieces to show: "### X" a heading, "- x" a bullet (indented lines that follow continue it), other lines
    /// paragraphs (consecutive ones joined). Backticks are dropped, so the text is plain.
    /// </summary>
    public static IReadOnlyList<(NoteKind Kind, string Text)> Pieces(string notes)
    {
        var pieces = new List<(NoteKind Kind, string Text)>();
        StringBuilder? current = null;
        var kind = NoteKind.Paragraph;

        void Finish()
        {
            if (current is { Length: > 0 })
            {
                pieces.Add((kind, current.ToString().Replace("`", "")));
            }

            current = null;
        }

        foreach (var raw in notes.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.Length == 0)
            {
                Finish();
            }
            else if (line.StartsWith('#'))
            {
                Finish();
                pieces.Add((NoteKind.Heading, line.TrimStart('#').Trim().Replace("`", "")));
            }
            else if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                Finish();
                (kind, current) = (NoteKind.Bullet, new StringBuilder(line[2..].Trim()));
            }
            else if (current != null && (char.IsWhiteSpace(raw[0]) || kind == NoteKind.Paragraph))
            {
                current.Append(' ').Append(line.Trim()); // a bullet's indented continuation, or a paragraph's next line
            }
            else
            {
                Finish();
                (kind, current) = (NoteKind.Paragraph, new StringBuilder(line.Trim()));
            }
        }

        Finish();
        return pieces;
    }
}
