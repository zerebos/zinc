using Zinc.Settings;

namespace Zinc.Tests;

public class SchemeFileTests
{
    private const string Dracula = "# Dracula\npalette = 0=#21222c\npalette = 1=#ff5555\nbackground = #282a36\nforeground = #f8f8f2\ncursor-color = #f8f8f2\n";

    [Fact]
    public void ReadsColorsAndPaletteEntries()
    {
        Assert.Equal("#282a36", SchemeFile.Get(Dracula, "background"));
        Assert.Equal("#ff5555", SchemeFile.Get(Dracula, "palette.1"));
        Assert.Null(SchemeFile.Get(Dracula, "palette.2"));
        Assert.Null(SchemeFile.Get(Dracula, "selection-background"));
    }

    [Fact]
    public void EditsInPlaceAndKeepsEverythingElse()
    {
        var edited = SchemeFile.Set(Dracula, "palette.1", "#ee4444");
        Assert.Equal(Dracula.Replace("1=#ff5555", "1=#ee4444"), edited);
        Assert.Equal(Dracula.Replace("#282a36", "#101010"), SchemeFile.Set(Dracula, "background", "#101010"));
    }

    [Fact]
    public void AddsWhatIsMissingAndRemovesOnNull()
    {
        var added = SchemeFile.Set(Dracula, "selection-background", "#44475a");
        Assert.EndsWith("selection-background = #44475a\n", added);
        Assert.Equal("#44475a", SchemeFile.Get(added, "selection-background"));
        Assert.Equal(Dracula, SchemeFile.Set(added, "selection-background", null));
        Assert.EndsWith("palette = 7=#bbbbbb\n", SchemeFile.Set(Dracula, "palette.7", "#bbbbbb"));
    }

    [Fact]
    public void KeepsWindowsLineEndingsAndDropsDuplicates()
    {
        var crlf = "background = #000000\r\nforeground = #ffffff\r\nbackground = #111111\r\n";
        Assert.Equal("#111111", SchemeFile.Get(crlf, "background")); // the last one wins, as when read
        Assert.Equal("foreground = #ffffff\r\nbackground = #222222\r\n", SchemeFile.Set(crlf, "background", "#222222"));
    }

    [Fact]
    public void WhatItWritesReadsBackAsTheScheme()
    {
        var file = Path.Combine(Path.GetTempPath(), $"zinc-scheme-{Guid.NewGuid():N}");
        try
        {
            File.WriteAllText(file, SchemeFile.Set(SchemeFile.Set(Dracula, "palette.1", "#ee4444"), "cursor-text", "#000000"));
            var theme = ThemeLibrary.ApplyFile(new Config { Theme = Zinc.Terminal.TerminalTheme.SchemeBase }, file).Theme;
            Assert.Equal(new Zinc.Ghostty.GhosttyColorRgb(0xEE, 0x44, 0x44), theme.Palette[1]);
            Assert.Equal(new Zinc.Ghostty.GhosttyColorRgb(0, 0, 0), theme.CursorText);
        }
        finally
        {
            File.Delete(file);
        }
    }
}

public sealed class CustomizeSchemeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"zinc-themes-{Guid.NewGuid():N}");
    private readonly ThemeLibrary _themes;

    public CustomizeSchemeTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "bundled"));
        File.WriteAllText(Path.Combine(_root, "bundled", "Dracula"), "background = #282a36\n");
        _themes = new ThemeLibrary(Path.Combine(_root, "themes"), Path.Combine(_root, "bundled"));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void ABundledSchemeIsCopiedToBeEditedAndCanGoBack()
    {
        Assert.False(_themes.IsUsers("Dracula"));
        var file = _themes.Customize("dracula")!; // names match case-insensitively
        Assert.Equal(Path.Combine(_root, "themes", "Dracula"), file);
        Assert.True(_themes.ReplacesBundled("Dracula"));

        File.WriteAllText(file, "background = #000000\n");
        Assert.Equal(file, _themes.Customize("Dracula")); // the copy, not a new one
        Assert.Equal(file, _themes.Find("Dracula"));       // and it's what's used

        _themes.BackToOriginal("Dracula");
        Assert.False(_themes.IsUsers("Dracula"));
        Assert.Equal(Path.Combine(_root, "bundled", "Dracula"), _themes.Find("Dracula"));
        Assert.Null(_themes.Customize("Nope"));
    }

    [Fact]
    public void ANewSchemeStartsAsACopyAndCanBeDeleted()
    {
        Assert.Equal("Give the scheme a name.", _themes.NameProblem("  "));
        Assert.NotNull(_themes.NameProblem("a/b"));
        Assert.NotNull(_themes.NameProblem("light:x"));
        Assert.Equal("There's a scheme called dracula already.", _themes.NameProblem("dracula"));

        var file = _themes.Create(" Night Shift ", "Dracula")!;
        Assert.Equal(Path.Combine(_root, "themes", "Night Shift"), file);
        Assert.Contains("background = #282a36", File.ReadAllText(file));
        Assert.Equal(file, _themes.Find("Night Shift"));
        Assert.Null(_themes.Create("Night Shift", "Dracula")); // taken now
        Assert.Null(_themes.Create("Other", "Nope"));

        _themes.Delete("Night Shift");
        Assert.Null(_themes.Find("Night Shift"));

        _themes.Customize("Dracula");
        _themes.Delete("Dracula"); // a copy of a bundled one: Back to the original, not Delete
        Assert.True(_themes.ReplacesBundled("Dracula"));
    }
}
