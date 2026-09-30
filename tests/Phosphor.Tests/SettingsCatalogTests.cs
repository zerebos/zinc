using Phosphor.Settings;

namespace Phosphor.Tests;

public class SettingsCatalogTests
{
    public static TheoryData<string> Keys() => new(SettingsCatalog.All.Select(s => s.Key));

    [Fact]
    public void EverySettingTheReaderKnowsIsInTheWindowOrFileOnly()
    {
        var shown = SettingsCatalog.All.Select(s => s.Key).Concat(SettingsCatalog.FileOnly.Keys).ToHashSet();
        Assert.Equal([], ConfigFile.SettingKeys.Where(k => !shown.Contains(k)).ToArray());
        Assert.Equal([], shown.Where(k => !ConfigFile.SettingKeys.Contains(k)).ToArray());
        Assert.Equal(shown.Count, SettingsCatalog.All.Count + SettingsCatalog.FileOnly.Count); // no duplicates
    }

    [Theory]
    [MemberData(nameof(Keys))]
    public void DefaultsAreWhatAnEmptyConfigGives(string key)
    {
        var setting = SettingsCatalog.Find(key)!;
        if (setting.Default is not { } value)
        {
            return; // nothing written: the app's own behavior
        }

        var path = setting.Path;
        var written = ConfigFile.Parse($"[{path[0]}]\n{path[1]} = {ConfigEditor.FormatValue(value)}\n");
        Assert.Empty(written.Warnings);
        Assert.Equal(ConfigFile.Parse("").Global, written.Global);
    }

    [Theory]
    [MemberData(nameof(Keys))]
    public void EveryChoiceReadsBack(string key)
    {
        var setting = SettingsCatalog.Find(key)!;
        foreach (var choice in setting.Choices.Where(c => c.Value != null))
        {
            Assert.Null(SettingsCatalog.Problem(setting, choice.Value!));
        }

        // The default is one of the choices (so the window can show it selected).
        if (setting.Kind == SettingKind.Choice)
        {
            Assert.Contains(setting.Choices, c => SettingsCatalog.Same(c.Value, setting.Default));
        }
    }

    [Fact]
    public void SectionsAreKnown()
    {
        var ids = SettingsCatalog.Sections.Select(s => s.Id).ToHashSet();
        Assert.All(SettingsCatalog.All, s => Assert.Contains(s.Section, ids));
    }

    [Theory]
    [InlineData("font.size", "12", 12L)]
    [InlineData("font.size", "12.5", 12.5)]
    [InlineData("font.size", "", null)]
    [InlineData("notify.command-finished-after", "30", 30L)]
    [InlineData("notify.command-finished-after", "2m", "2m")]
    [InlineData("quick-terminal.size", "50%", "50%")]
    [InlineData("window.unfocused-split-opacity", "1", 1L)]
    [InlineData("window.unfocused-split-opacity", "0.5", 0.5)]
    public void TypedTextBecomesTheRightValue(string key, string text, object? expected) =>
        Assert.Equal(expected, SettingsCatalog.FromText(SettingsCatalog.Find(key)!, text));

    [Fact]
    public void ProblemsComeFromTheReader()
    {
        Assert.Null(SettingsCatalog.Problem(SettingsCatalog.Find("font.size")!, 12L));
        Assert.NotNull(SettingsCatalog.Problem(SettingsCatalog.Find("font.size")!, "big"));
        Assert.NotNull(SettingsCatalog.Problem(SettingsCatalog.Find("colors.background")!, "blue-ish"));
        Assert.NotNull(SettingsCatalog.Problem(SettingsCatalog.Find("quick-terminal.hotkey")!, "not a key"));
    }

    [Fact]
    public void GlobalValuesAreAsWritten()
    {
        var config = ConfigFile.Parse("[font]\nsize = 12\n\n[profile.x]\ncommand = \"cmd.exe\"\nfont.size = 14\n");
        Assert.Equal(12L, config.ValueOf("font.size"));
        Assert.Null(config.ValueOf("font.family"));
    }

    [Theory]
    [InlineData(11L, 11.0, true)]
    [InlineData("Top", "top", true)]
    [InlineData(null, null, true)]
    [InlineData(null, "x", false)]
    [InlineData(true, false, false)]
    public void SameValueNormalizes(object? a, object? b, bool same) => Assert.Equal(same, SettingsCatalog.Same(a, b));
}

public class SettingsWriterTests
{
    private static string TempConfig(string text)
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory("phosphor-settings-").FullName, "config.toml");
        File.WriteAllText(path, text);
        return path;
    }

    [Fact]
    public void WritesOneKeyAndKeepsTheRest()
    {
        var path = TempConfig("# mine\n[font]\nfamily = \"Iosevka\"  # just right\n");
        var writer = new SettingsWriter(path);
        writer.Set(SettingsCatalog.Find("font.size")!, 12L);
        var text = File.ReadAllText(path);
        Assert.Contains("# mine", text);
        Assert.Contains("family = \"Iosevka\"  # just right", text);
        Assert.Contains("size = 12", text);
        Assert.True(writer.Changed);
    }

    [Fact]
    public void CountsTheSettingsUndoWouldPutBack()
    {
        var path = TempConfig("[font]\nsize = 12\n");
        var writer = new SettingsWriter(path);
        Assert.Equal(0, writer.ChangeCount());
        writer.Set(["font", "size"], 13L);
        writer.Set(["font", "size"], 14L); // the same setting twice: one change
        writer.Set(["colors", "theme"], "Nord");
        writer.Set(["profile", "pwsh", "font", "size"], 9L);
        Assert.Equal(3, writer.ChangeCount());
        writer.Set(["font", "size"], 12L); // back as it was: not a change
        Assert.Equal(2, writer.ChangeCount());
    }

    [Fact]
    public void TheDefaultIsWrittenAsNothing()
    {
        var path = TempConfig("[font]\nsize = 12\n");
        new SettingsWriter(path).Set(SettingsCatalog.Find("font.size")!, 10.5);
        Assert.DoesNotContain("size", File.ReadAllText(path));
    }

    [Fact]
    public void AChoiceWithoutAValueRemovesTheKey()
    {
        var path = TempConfig("[window]\nquit-after-last-window-closed = true\n");
        new SettingsWriter(path).Set(SettingsCatalog.Find("window.quit-after-last-window-closed")!, null);
        Assert.DoesNotContain("quit-after", File.ReadAllText(path));
    }

    [Fact]
    public void UndoRestoresTheFileAsItWasOpened()
    {
        const string original = "# mine\n[colors]\ntheme = \"Dracula\"\n";
        var path = TempConfig(original);
        var writer = new SettingsWriter(path);
        writer.Set(SettingsCatalog.Find("colors.theme")!, "Nord");
        writer.Set(SettingsCatalog.Find("font.size")!, 13L);
        writer.Undo();
        Assert.Equal(original, File.ReadAllText(path));
        Assert.False(writer.Changed);
    }
}

public class ProfileSettingsTests
{
    [Fact]
    public void ProfileFieldsAndDiscoveryReadWithoutWarnings()
    {
        var fields = string.Join("\n", SettingsCatalog.ProfileFields.Select(f =>
            $"{f.Key} = {(f.Kind == SettingKind.Toggle ? "true" : "\"x\"")}"));
        Assert.DoesNotContain(ConfigFile.Parse($"[profile.x]\n{fields}\n").Warnings, w => !w.Contains("default-profile"));

        var discovery = string.Join("\n", SettingsCatalog.Discovery.Select(d => $"{d.Path[1]} = false"));
        Assert.Empty(ConfigFile.Parse($"[discover]\n{discovery}\n").Warnings);
    }

    [Fact]
    public void KnowsFoundProfilesFromYourOwn()
    {
        static IReadOnlyList<DiscoveredProfile> Found(DiscoverySettings _) =>
            [new("wsl:Fedora", "Fedora", "wsl.exe -d Fedora"), new("pwsh", "PowerShell", "pwsh.exe")];
        var config = ConfigFile.Parse("[profile.mine]\ncommand = \"cmd.exe\"\n\n[profile.pwsh]\nfont.size = 14\n",
            discover: Found);
        var mine = config.Profiles.Single(p => p.Id == "mine");
        var pwsh = config.Profiles.Single(p => p.Id == "pwsh");
        var fedora = config.Profiles.Single(p => p.Id == "wsl:Fedora");
        Assert.Equal((false, true), (mine.Found, mine.InFile));
        Assert.Equal((true, true, "pwsh.exe"), (pwsh.Found, pwsh.InFile, pwsh.FoundCommand));
        Assert.Equal((true, false), (fedora.Found, fedora.InFile));
        Assert.Equal(14L, config.ValueOf("pwsh", "font.size"));
        Assert.Equal("cmd.exe", config.ValueOf("mine", "command"));
        Assert.Null(config.ValueOf("wsl:Fedora", "command"));
    }

    private static IReadOnlyList<DiscoveredProfile> Three(DiscoverySettings _) =>
        [new("wsl:Fedora", "Fedora", "wsl.exe -d Fedora"), new("pwsh", "PowerShell", "pwsh.exe"), new("cmd", "Command Prompt", "cmd.exe")];

    [Fact]
    public void ProfileOrderSortsTheListAndTheRestFollow()
    {
        var config = ConfigFile.Parse("profile-order = [\"cmd\", \"mine\", \"gone\"]\n\n[profile.mine]\ncommand = \"x.exe\"\n", discover: Three);
        Assert.Empty(config.Warnings);
        // Listed first (an id that no longer exists is skipped), then the others in their usual order.
        Assert.Equal(["cmd", "mine", "wsl:Fedora", "pwsh"], config.Profiles.Select(p => p.Id));
        Assert.Contains(ConfigFile.Parse("profile-order = \"cmd\"\n").Warnings, w => w.Contains("profile-order"));
    }

    [Fact]
    public void AChangedProfileThatCantOpenSaysWhy()
    {
        // The ssh search finds "homelab" only; "gone" was removed from ~/.ssh/config.
        static IReadOnlyList<DiscoveredProfile> Ssh(DiscoverySettings s) =>
            s.Ssh ? [new("ssh:homelab", "SSH - homelab", "ssh.exe homelab")] : [];
        const string changes = "[profile.\"ssh:homelab\"]\nfont.size = 14\n\n[profile.\"ssh:gone\"]\nfont.size = 12\nhidden = false\n";

        var on = ConfigFile.Parse(changes, discover: Ssh);
        Assert.Equal((ProfileMissing.None, false, "ssh"), Missing(on, "ssh:homelab"));
        Assert.Equal((ProfileMissing.NotFound, true, "ssh"), Missing(on, "ssh:gone")); // hidden even with hidden = false

        var off = ConfigFile.Parse("[discover]\nssh = false\n\n" + changes, discover: Ssh);
        Assert.Equal((ProfileMissing.SearchOff, true, "ssh"), Missing(off, "ssh:homelab"));
        Assert.Equal("SSH - homelab", off.Profiles.Single(p => p.Id == "ssh:homelab").Name); // the name the search gives it

        // A profile of your own belongs to no search.
        Assert.Null(ConfigFile.Parse("[profile.mine]\ncommand = \"x.exe\"\n").Profiles.Single(p => p.Id == "mine").Source);

        static (ProfileMissing, bool, string?) Missing(ConfigFile config, string id) =>
            config.Profiles.Single(p => p.Id == id) is var p ? (p.Missing, p.Hidden, p.Source) : default;
    }

    [Fact]
    public void ChangingAFoundProfileDoesntMoveIt()
    {
        // Changes to cmd (found last) leave it last, and leave the default alone.
        var changed = ConfigFile.Parse("[profile.cmd]\nfont.size = 14\n", discover: Three);
        Assert.Equal(["wsl:Fedora", "pwsh", "cmd"], changed.Profiles.Select(p => p.Id));
        Assert.Equal("wsl:Fedora", changed.DefaultProfile.Id);

        // Your own profiles come first, in file order.
        var mine = ConfigFile.Parse("[profile.cmd]\nfont.size = 14\n\n[profile.b]\ncommand = \"b.exe\"\n\n[profile.a]\ncommand = \"a.exe\"\n", discover: Three);
        Assert.Equal(["b", "a", "wsl:Fedora", "pwsh", "cmd"], mine.Profiles.Select(p => p.Id));
    }

    [Fact]
    public void ProfileOrderIsWrittenAtTheTop()
    {
        var text = ConfigEditor.Set("[profile.mine]\ncommand = \"x.exe\"\n", ["profile-order"], new List<object> { "pwsh", "mine" });
        Assert.StartsWith("profile-order = [\"pwsh\", \"mine\"]", text);
        Assert.Equal(["pwsh", "mine", "wsl:Fedora", "cmd"], ConfigFile.Parse(text, discover: Three).Profiles.Select(p => p.Id));
    }
}
