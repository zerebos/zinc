namespace Zinc.Tests;

public class ReleaseNotesTests
{
    private const string Changelog = """
        # Changelog

        Intro.

        ## Unreleased

        ## 0.1.0-dev.229 (2026-10-04)

        ### New
        - Zinc comes with six shaders: `crt`, `glow`,
          and more.
        - Another.

        ## 0.1.0-dev.198 (2026-10-02)

        The first release with an installer: it
        keeps itself up to date.
        """;

    [Fact]
    public void FindsAnEntry()
    {
        Assert.Equal("### New\n- Zinc comes with six shaders: `crt`, `glow`,\n  and more.\n- Another.", ReleaseNotes.Entry(Changelog, "0.1.0-dev.229"));
        Assert.StartsWith("The first release", ReleaseNotes.Entry(Changelog, "0.1.0-dev.198"));
        Assert.Null(ReleaseNotes.Entry(Changelog, "0.1.0-dev.22")); // not a prefix match
        Assert.Equal("0.1.0-dev.229", ReleaseNotes.Newest(Changelog));
        Assert.False(ReleaseNotes.HasUnreleased(Changelog));
        Assert.True(ReleaseNotes.HasUnreleased(Changelog.Replace("## Unreleased\n", "## Unreleased\n\n- Something new.\n")));
    }

    [Fact]
    public void PiecesForShowing()
    {
        Assert.Equal(
        [
            (NoteKind.Heading, "New"),
            (NoteKind.Bullet, "Zinc comes with six shaders: crt, glow, and more."),
            (NoteKind.Bullet, "Another."),
        ], ReleaseNotes.Pieces(ReleaseNotes.Entry(Changelog, "0.1.0-dev.229")!));
        Assert.Equal([(NoteKind.Paragraph, "The first release with an installer: it keeps itself up to date.")],
            ReleaseNotes.Pieces(ReleaseNotes.Entry(Changelog, "0.1.0-dev.198")!));
    }

    [Fact]
    public void FindsReleasesAndPreReleasesByTheirOwnHeading()
    {
        const string changelog = "## Unreleased\n\n## 0.1.0 (2026-12-01)\n\nThe release.\n\n## 0.1.0-rc.2 (2026-11-20)\n\nSecond.\n\n" +
            "## 0.1.0-rc.1 (2026-11-15)\n\nFirst.\n\n## 0.1.0-dev.428 (2026-10-22)\n\nOlder.\n";
        Assert.Equal("The release.", ReleaseNotes.Entry(changelog, "0.1.0")); // not 0.1.0-rc.2's, whose heading also starts with "## 0.1.0"
        Assert.Equal("Second.", ReleaseNotes.Entry(changelog, "0.1.0-rc.2"));
        Assert.Equal("First.", ReleaseNotes.Entry(changelog, "0.1.0-rc.1"));
        Assert.Equal("Older.", ReleaseNotes.Entry(changelog, "0.1.0-dev.428"));
        Assert.Equal("0.1.0", ReleaseNotes.Newest(changelog));
    }

    [Fact]
    public void AReleaseNeedsItsOwnEntryAndNothingLeftUnreleased()
    {
        Assert.Null(ReleaseNotes.ReleaseProblem(Changelog, "0.1.0-dev.229"));
        Assert.Contains("no entry for 0.1.0", ReleaseNotes.ReleaseProblem(Changelog, "0.1.0"));

        var unreleased = Changelog.Replace("## Unreleased\n", "## Unreleased\n\n- Something new.\n");
        Assert.Contains("Unreleased", ReleaseNotes.ReleaseProblem(unreleased, "0.1.0-dev.229"));

        // A heading with nothing under it isn't an entry.
        Assert.Contains("no entry", ReleaseNotes.ReleaseProblem("## Unreleased\n\n## 0.1.0 (2026-12-01)\n\n## 0.1.0-rc.1 (2026-11-15)\n\nFirst.\n", "0.1.0"));
    }

    /// <summary>The repo's own CHANGELOG.md, in the shape the release and the app expect.</summary>
    [Fact]
    public void TheRealChangelog()
    {
        var text = File.ReadAllText(Path.Combine(OpenSourceNoticesTests.Root, "CHANGELOG.md")).Replace("\r\n", "\n");
        Assert.Contains("## Unreleased\n", text);

        // Every heading is "Unreleased" or a version with its date, so each release's entry can be found.
        var headings = text.Split('\n').Where(l => l.StartsWith("## ", StringComparison.Ordinal)).ToList();
        Assert.Equal("## Unreleased", headings[0]);
        Assert.All(headings.Skip(1), h => Assert.Matches(@"^## \d+\.\d+\.\d+(-[0-9A-Za-z.]+)? \(\d{4}-\d{2}-\d{2}\)$", h));

        var newest = ReleaseNotes.Newest(text);
        Assert.NotNull(newest);
        Assert.NotEmpty(ReleaseNotes.Pieces(ReleaseNotes.Entry(text, newest)!));

        // The newest version is releasable once Unreleased is empty, which it already is when a release is tagged.
        var unreleased = ReleaseNotes.Entry(text, "Unreleased");
        Assert.Null(ReleaseNotes.ReleaseProblem(string.IsNullOrEmpty(unreleased) ? text : text.Replace(unreleased, ""), newest));
    }
}
