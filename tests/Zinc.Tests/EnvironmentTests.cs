using Zinc.Pty;

namespace Zinc.Tests;

public sealed class EnvironmentTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("zinc-env-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static Dictionary<string, string> Vars(params (string Name, string Value)[] vars) =>
        vars.ToDictionary(v => v.Name, v => v.Value, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void ParsesABlockLeavingOutTheHiddenDriveEntries()
    {
        var parsed = ChildEnvironment.Parse("=C:=C:\\src\0Path=C:\\Windows;C:\\tools\0EMPTY=\0A=b=c\0");
        Assert.Equal(["Path", "EMPTY", "A"], parsed.Keys);
        Assert.Equal("C:\\Windows;C:\\tools", parsed["PATH"]); // any case
        Assert.Equal("", parsed["EMPTY"]);
        Assert.Equal("b=c", parsed["A"]);
    }

    [Fact]
    public void FreshVariablesAddOrReplaceOursButRemoveNothing()
    {
        var ours = Vars(("PATH", "C:\\old"), ("SAME", "1"), ("ONLY_OURS", "kept"));
        var fresh = Vars(("Path", "C:\\old;C:\\new"), ("SAME", "1"), ("NEW", "x"));
        var changes = ChildEnvironment.Changes(ours, fresh, Vars());
        Assert.Equal(2, changes.Count);
        Assert.Equal("C:\\old;C:\\new", changes["PATH"]);
        Assert.Equal("x", changes["NEW"]);
        Assert.False(changes.ContainsKey("ONLY_OURS"));
        Assert.Empty(ChildEnvironment.Changes(ours, null, Vars())); // reload-environment = false
    }

    [Fact]
    public void ConfiguredVariablesComeLastExpandAndAnEmptyOneRemoves()
    {
        var ours = Vars(("PATH", "C:\\old"), ("HOME_ISH", "C:\\me"), ("GONE", "y"));
        var fresh = Vars(("PATH", "C:\\new"));
        var changes = ChildEnvironment.Changes(ours, fresh,
            Vars(("PATH", "%PATH%;C:\\tools"), ("EDITOR", "%HOME_ISH%\\bin\\vim %UNSET%"), ("GONE", "")));
        Assert.Equal("C:\\new;C:\\tools", changes["PATH"]); // the fresh PATH, not the old one
        Assert.Equal("C:\\me\\bin\\vim %UNSET%", changes["EDITOR"]);
        Assert.True(changes.ContainsKey("GONE"));
        Assert.Null(changes["GONE"]);
    }

    [Fact]
    public void ReadsWindowsOwnEnvironment()
    {
        var fresh = ChildEnvironment.Fresh();
        Assert.NotNull(fresh);
        Assert.Equal(Environment.GetEnvironmentVariable("SystemRoot"), fresh["SystemRoot"], ignoreCase: true);
        Assert.Contains("PATH", fresh.Keys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConfigReadsEnvironmentTablesAProfilesOverEveryProfiles()
    {
        var config = ConfigFile.Parse("""
            [environment]
            EDITOR = "nvim"
            LANG = "en_US.UTF-8"

            [terminal]
            reload-environment = false

            [profile.work]
            command = "pwsh"

            [profile.work.environment]
            EDITOR = "code --wait"
            LANG = ""

            [profile.home]
            command = "cmd"
            """, Path.Combine(_dir, "config.toml"), _dir);
        Assert.Empty(config.Warnings);
        Assert.False(config.Global.ReloadEnvironment);
        Assert.Equal("nvim", config.Global.EnvironmentVariables["EDITOR"]);
        var work = config.FindProfile("work")!.Config.EnvironmentVariables;
        Assert.Equal("code --wait", work["EDITOR"]);
        Assert.Equal("", work["LANG"]); // removed for that profile
        Assert.Equal("nvim", config.FindProfile("home")!.Config.EnvironmentVariables["editor"]);
        Assert.True(new Config().ReloadEnvironment); // on by default
    }

    [Fact]
    public void ConfigWarnsAboutAnEnvironmentValueThatIsNotText()
    {
        var config = ConfigFile.Parse("[environment]\nCOUNT = 3\n", Path.Combine(_dir, "config.toml"), _dir);
        Assert.Contains(config.Warnings, w => w.Contains("[environment]"));
        Assert.Empty(config.Global.EnvironmentVariables);
    }
}
