using Zinc.Settings;

namespace Zinc.Tests;

public class SettingsCatalogTests
{
    public static TheoryData<string> Keys() => new(SettingsCatalog.All.Select(s => s.Key));

    [Fact]
    public void FoldsHoldNeighboringSettingsOfOneGroup()
    {
        var all = SettingsCatalog.All.Select(s => s.Key).ToList();
        var seen = new HashSet<string>();
        foreach (var fold in SettingsCatalog.Folds)
        {
            // A lead, or a title of its own: not both.
            Assert.True(fold.Lead != null ^ fold.Title != null, $"fold of {fold.Children[0]}");
            List<string> keys = [.. fold.Lead is { } lead ? [lead] : Array.Empty<string>(), .. fold.Children];
            Assert.NotEmpty(fold.Children);
            Assert.All(keys, k => Assert.True(seen.Add(k), $"{k} is in two folds"));

            // Shown where its settings are: consecutive, in catalog order, one group.
            var at = all.IndexOf(keys[0]);
            Assert.True(at >= 0, keys[0]);
            Assert.Equal(keys, all.Skip(at).Take(keys.Count));
            var first = SettingsCatalog.Find(keys[0])!;
            Assert.All(keys, k => Assert.Equal((first.Section, first.Group), (SettingsCatalog.Find(k)!.Section, SettingsCatalog.Find(k)!.Group)));
        }
    }

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

        // The default is one of the choices (so the window can show it selected), for a color with choices too.
        if (setting.Choices.Count > 0)
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
    [InlineData("window.unfocused-pane-opacity", "1", 1L)]
    [InlineData("window.unfocused-pane-opacity", "0.5", 0.5)]
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
    [Fact]
    public void EverySettingHasABinding()
    {
        Assert.Equal(["colors.theme"], SettingsCatalog.All.Where(s => s.Binding == null).Select(s => s.Key));

        // A bound setting's default comes from Config, so a declared one would be ignored without a word.
        Assert.Empty(SettingsCatalog.All.Where(s => s.Binding != null && s.DeclaredDefault != null).Select(s => s.Key));
    }

    [Theory]
    [MemberData(nameof(Keys))]
    public void NumberRangesHoldAtBothEnds(string key)
    {
        var setting = SettingsCatalog.Find(key)!;
        if (setting is not { Kind: SettingKind.Number, Binding: { } binding })
        {
            return;
        }

        // Both ends read, and one step past either is refused or clamped.
        var empty = new Config();
        Assert.True(binding.Read(setting, empty, setting.Minimum) != null, $"{key} refused its minimum {setting.Minimum}");
        if (setting.Maximum < double.MaxValue)
        {
            Assert.True(binding.Read(setting, empty, setting.Maximum) != null, $"{key} refused its maximum {setting.Maximum}");
            var above = binding.Read(setting, empty, setting.Maximum + setting.Step);
            Assert.True(above == null || above == binding.Read(setting, empty, setting.Maximum), $"{key} took {setting.Maximum + setting.Step}");
        }

        var below = binding.Read(setting, empty, setting.Minimum - setting.Step);
        Assert.True(below == null || below == binding.Read(setting, empty, setting.Minimum), $"{key} took {setting.Minimum - setting.Step}");
    }

    /// <summary>
    /// Config properties that no catalog setting sets: a profile's own fields, the color scheme's light/dark side, the
    /// <c>[environment]</c> table and the file-only shader folders, which the reader handles itself.
    /// </summary>
    private static readonly string[] NotSettings =
        ["Command", "WorkingDirectory", "Icon", "PairedDark", "EnvironmentVariables", "ShaderFolders"];

    [Fact]
    public void EveryConfigPropertyIsSetByASettingOrIsListed()
    {
        var properties = typeof(Config).GetProperties().Where(p => p.SetMethod?.IsPublic == true).ToList();
        var empty = new Config();
        var touched = new HashSet<string>();
        foreach (var setting in SettingsCatalog.All.Where(s => s.Binding != null))
        {
            foreach (var value in Samples(setting))
            {
                var read = setting.Binding!.Read(setting, empty, value);
                Assert.True(read != null, $"{setting.Key}'s sample {value} doesn't read");
                touched.UnionWith(properties.Where(p => !Equals(p.GetValue(read), p.GetValue(empty))).Select(p => p.Name));
            }
        }

        Assert.Equal(NotSettings.Order(), properties.Select(p => p.Name).Where(n => !touched.Contains(n)).Order());
    }

    /// <summary>
    /// Sample values for <paramref name="setting"/>'s kind. At least one of them differs from the default, so the
    /// setting's binding changes something.
    /// </summary>
    private static IEnumerable<object> Samples(SettingDef setting) => setting.Kind switch
    {
        SettingKind.Toggle => [true, false],
        SettingKind.Choice => setting.Choices.Where(c => c.Value != null).Select(c => c.Value!),
        SettingKind.Number => [setting.Minimum, Math.Min(setting.Maximum, setting.Minimum + setting.Step)],
        SettingKind.Color => ["#123456", .. setting.Choices.Where(c => c.Value != null).Select(c => c.Value!)],
        SettingKind.Font => ["Consolas"],
        SettingKind.Files => ["a.glsl"],
        _ => [TextSamples.TryGetValue(setting.Key, out var text) ? text : setting.Placeholder ?? "x"],
    };

    private static readonly Dictionary<string, object> TextSamples = new()
    {
        ["font.fallback"] = "Segoe UI Symbol, Noto Sans JP",
        ["font.adjust-cell-height"] = "10%",
        ["font.adjust-box-thickness"] = "1",
        ["quick-terminal.hotkey"] = "ctrl+alt+t",
        ["quick-terminal.size"] = "40%",
        ["notify.command-finished-after"] = 30L,
        ["window.free-hidden-after"] = 30L,
    };
}

public class SettingsWriterTests
{
    private static string TempConfig(string text)
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory("zinc-settings-").FullName, "config.toml");
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
    public void RenamedKeysStillReadAndAreWrittenUnderTheirNewName()
    {
        // Keys under their old names are still read, and Settings shows them under the new key.
        var old = "[window]\ngroup-tab-strip = \"always\"\nunfocused-split-opacity = 0.5\ntab-groups = false\n";
        var file = ConfigFile.Parse(old, "config.toml", "", _ => []);
        Assert.Equal(PaneHeaders.Always, file.Global.PaneHeaders);
        Assert.Equal(0.5, file.Global.UnfocusedPaneOpacity);
        Assert.False(file.Global.PaneTabs);
        Assert.Equal("always", file.ValueOf("window.pane-headers"));
        Assert.Empty(file.Warnings);

        // Changing one in Settings writes the new name and takes the old one out.
        var path = TempConfig(old);
        new SettingsWriter(path).Set(SettingsCatalog.Find("window.pane-headers")!, "never");
        new SettingsWriter(path).Set(SettingsCatalog.Find("window.unfocused-pane-opacity")!, 0.8);
        var text = File.ReadAllText(path);
        Assert.DoesNotContain("group-tab-strip", text);
        Assert.DoesNotContain("unfocused-split-opacity", text);
        Assert.Contains("unfocused-pane-opacity = 0.8", text);
        Assert.Contains("tab-groups = false", text); // untouched keys keep their old names until changed
        Assert.Contains("pane-headers = \"never\"", text);
    }

    [Theory]
    [InlineData("auto", "with-tabs")] // the default: written as nothing, and the old key goes too
    [InlineData("always", "never")]
    [InlineData("always", "auto")]
    public void SettingsWritesPaneHeadersAndRemovesTheOldKey(string old, string chosen)
    {
        var path = TempConfig($"[window]\npane-tab-strip = \"{old}\"\npane-tabs = true\n");
        new SettingsWriter(path).Set(SettingsCatalog.Find("window.pane-headers")!, chosen);
        var text = File.ReadAllText(path);
        Assert.DoesNotContain("pane-tab-strip", text);
        Assert.Contains("pane-tabs = true", text);
        var file = ConfigFile.Parse(text, "config.toml", "", _ => []);
        Assert.Equal(chosen, Assert.IsType<string>(file.ValueOf("window.pane-headers") ?? "with-tabs"));
    }

    [Fact]
    public void ResettingPaneHeadersRemovesTheOldKeyToo()
    {
        // Otherwise the old "always" would come back the moment the new key went.
        var path = TempConfig("[window]\npane-tab-strip = \"always\"\n");
        new SettingsWriter(path).Set(SettingsCatalog.Find("window.pane-headers")!, null);
        Assert.DoesNotContain("pane-tab-strip", File.ReadAllText(path));
    }

    [Fact]
    public void OldNamesFollowRenamesAndReplacements()
    {
        Assert.Equal(["window.pane-tab-strip", "window.group-tab-strip"], ConfigFile.OldNames("window.pane-headers").ToArray());
        Assert.Equal(["window.tab-groups"], ConfigFile.OldNames("window.pane-tabs").ToArray());
        Assert.Empty(ConfigFile.OldNames("font.size"));
    }

    [Fact]
    public void PaneStylesSetAllFiveSettings()
    {
        var keys = SettingsCatalog.All.Where(s => s.Key.StartsWith("window.pane-") && s.Key is not "window.pane-tabs" and not "window.pane-tabs-as" and not "window.pane-resize-step"
                and not "window.pane-animations")
            .Select(s => s.Key);
        Assert.Equal(keys, PaneStyle.Keys);
        Assert.Equal(PaneStyle.Keys, PaneStyle.Fold.Children);
        foreach (var style in new[] { PaneStyleKind.Compact, PaneStyleKind.Cards })
        {
            Assert.Equal(PaneStyle.Keys.Order(), PaneStyle.Values(style).Keys.Order());
            foreach (var (key, value) in PaneStyle.Values(style))
            {
                Assert.Null(SettingsCatalog.Problem(SettingsCatalog.Find(key)!, value)); // each value reads back
            }
        }
    }

    [Fact]
    public void CompactIsTheDefaults()
    {
        foreach (var (key, value) in PaneStyle.Values(PaneStyleKind.Compact))
        {
            Assert.True(SettingsCatalog.Same(SettingsCatalog.Find(key)!.Default, value), key);
        }

        Assert.Equal(PaneStyleKind.Compact, PaneStyle.Of(_ => null));
    }

    [Fact]
    public void ChoosingAStyleWritesItAndReadsBackAsIt()
    {
        var path = TempConfig("# mine\n[window]\npane-tab-strip = \"always\"\n");
        var writer = new SettingsWriter(path);
        writer.Set(PaneStyle.Keys.Select(k => (SettingsCatalog.Find(k)!, (object?)PaneStyle.Values(PaneStyleKind.Cards)[k])));
        var file = ConfigFile.Parse(File.ReadAllText(path), "config.toml", "", _ => []);
        Assert.Equal(PaneStyleKind.Cards, PaneStyle.Of(file.ValueOf));
        Assert.Equal((PaneHeaders.Always, PaneHeaderButtons.Focused, 6, true, PaneFocus.Ring),
            (file.Global.PaneHeaders, file.Global.PaneHeaderButtons, file.Global.PaneGap, file.Global.PaneGapEdges, file.Global.PaneFocus));
        Assert.DoesNotContain("pane-tab-strip", File.ReadAllText(path));
        Assert.Equal(4, writer.ChangeCount()); // pane-tab-strip = "always" already read as pane-headers = "always"

        // Compact is the defaults, so choosing it takes every pane key out of the file.
        writer.Set(PaneStyle.Keys.Select(k => (SettingsCatalog.Find(k)!, (object?)PaneStyle.Values(PaneStyleKind.Compact)[k])));
        Assert.Equal("# mine\n[window]\n", File.ReadAllText(path));
    }

    [Theory]
    [InlineData("window.pane-headers", "never")]
    [InlineData("window.pane-header-buttons", "hover")]
    [InlineData("window.pane-gap", 4L)]
    [InlineData("window.pane-gap-edges", false)]
    [InlineData("window.pane-focus", "none")]
    public void AnyChangeFromAStyleReadsCustom(string key, object value)
    {
        foreach (var style in new[] { PaneStyleKind.Compact, PaneStyleKind.Cards })
        {
            var values = PaneStyle.Values(style);
            if (SettingsCatalog.Same(values[key], value))
            {
                continue; // (Compact already has gap edges off)
            }

            Assert.Equal(PaneStyleKind.Custom, PaneStyle.Of(k => k == key ? value : values[k]));
        }
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
        // The ssh search finds "buildbox" only, because "gone" was removed from ~/.ssh/config.
        static IReadOnlyList<DiscoveredProfile> Ssh(DiscoverySettings s) =>
            s.Ssh ? [new("ssh:buildbox", "buildbox (SSH)", "ssh.exe buildbox")] : [];
        const string changes = "[profile.\"ssh:buildbox\"]\nfont.size = 14\n\n[profile.\"ssh:gone\"]\nfont.size = 12\nhidden = false\n";

        var on = ConfigFile.Parse(changes, discover: Ssh);
        Assert.Equal((ProfileMissing.None, false, "ssh"), Missing(on, "ssh:buildbox"));
        Assert.Equal((ProfileMissing.NotFound, true, "ssh"), Missing(on, "ssh:gone")); // hidden even with hidden = false

        var off = ConfigFile.Parse("[discover]\nssh = false\n\n" + changes, discover: Ssh);
        Assert.Equal((ProfileMissing.SearchOff, true, "ssh"), Missing(off, "ssh:buildbox"));
        Assert.Equal("buildbox (SSH)", off.Profiles.Single(p => p.Id == "ssh:buildbox").Name); // the name the search gives it

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
