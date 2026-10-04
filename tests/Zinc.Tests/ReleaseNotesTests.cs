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

    /// <summary>The repo's own CHANGELOG.md: in the shape release.ps1 and the app expect.</summary>
    [Fact]
    public void TheRealChangelog()
    {
        var root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "Zinc.slnx")))
        {
            root = Path.GetDirectoryName(root)!;
        }

        var text = File.ReadAllText(Path.Combine(root, "CHANGELOG.md"));
        Assert.Contains("## Unreleased", text);
        var newest = ReleaseNotes.Newest(text);
        Assert.NotNull(newest);
        Assert.Matches(@"^0\.1\.0-dev\.\d+$", newest);
        Assert.NotEmpty(ReleaseNotes.Pieces(ReleaseNotes.Entry(text, newest)!));
    }
}
