namespace Phosphor.Tests;

public class DiscoveryTests
{
    [Fact]
    public void SshHostsSkipPatternsAndDuplicates()
    {
        var hosts = ProfileDiscovery.SshHosts("""
            # comment
            Host *
                ServerAliveInterval 30
            Host homelab contabo
                User zack
            Host=bd
            host "git.zerebos.com" *.internal !bastion
            Match host foo
            Host homelab
            """, _ => []);
        Assert.Equal(["homelab", "contabo", "bd", "git.zerebos.com"], hosts);
    }

    [Fact]
    public void SshIncludesAreReadInPlace()
    {
        var files = new Dictionary<string, string[]>
        {
            ["config.d/*"] = ["Host a\n", "Host b\nInclude nested\n"],
            ["nested"] = ["Host c\n"],
        };
        var hosts = ProfileDiscovery.SshHosts("Host first\nInclude config.d/*\nHost last\n",
            pattern => files.TryGetValue(pattern, out var texts) ? texts : []);
        Assert.Equal(["first", "a", "b", "c", "last"], hosts);
    }

    [Fact]
    public void WslProfilesPutTheDefaultFirstAndSkipDockerDesktop()
    {
        var profiles = ProfileDiscovery.WslProfiles(
        [
            ("{1}", "Ubuntu"), ("{2}", "Fedora"), ("{3}", "docker-desktop"), ("{4}", "docker-desktop-data"),
        ], defaultId: "{2}", @"C:\Windows\System32\wsl.exe").ToList();

        Assert.Equal(["wsl:Fedora", "wsl:Ubuntu"], profiles.Select(p => p.Id));
        Assert.Equal("Fedora", profiles[0].Name);
        Assert.Equal(@"C:\Windows\System32\wsl.exe --distribution-id {2} --cd ~", profiles[0].Command);
    }

    [Fact]
    public void VisualStudioGetsTwoDeveloperShells()
    {
        var profiles = ProfileDiscovery.VisualStudioProfiles([("abc123", "Visual Studio Community 2022", @"C:\VS\2022")]).ToList();
        Assert.Equal(["vs:abc123:pwsh", "vs:abc123:cmd"], profiles.Select(p => p.Id));
        Assert.Contains(@"Import-Module """"""C:\VS\2022\Common7\Tools\Microsoft.VisualStudio.DevShell.dll""""""", profiles[0].Command);
        Assert.Contains("Enter-VsDevShell abc123", profiles[0].Command);
        Assert.Equal(@"cmd.exe /k ""C:\VS\2022\Common7\Tools\VsDevCmd.bat""", profiles[1].Command);
    }

    // ------------------------------------------------------------------ merging

    private static readonly DiscoveredProfile[] Found =
    [
        new("wsl:Fedora", "Fedora", "wsl.exe --distribution-id {2} --cd ~"),
        new("pwsh", "PowerShell", "pwsh.exe"),
        new("ssh:homelab", "SSH - homelab", "ssh.exe homelab"),
    ];

    private static ConfigFile Parse(string text, Func<DiscoverySettings, IReadOnlyList<DiscoveredProfile>>? discover = null) =>
        ConfigFile.Parse(text, discover: discover ?? (_ => Found));

    [Fact]
    public void FoundProfilesFollowTheFilesOwn()
    {
        var config = Parse("""
            [profile.work]
            command = "cmd.exe"
            """);
        Assert.Equal(["work", "wsl:Fedora", "pwsh", "ssh:homelab"], config.Profiles.Select(p => p.Id));
        Assert.Equal("SSH - homelab", config.Profiles[3].Name);
        Assert.Equal("ssh.exe homelab", config.Profiles[3].Config.Command);
        Assert.Empty(config.Warnings);
    }

    [Fact]
    public void ASectionChangesAFoundProfileAndKeepsItsPlace()
    {
        var config = Parse("""
            [profile.pwsh]
            font.size = 13

            [profile."wsl:Fedora"]
            name = "Fedora 42"
            colors.background = "#101010"

            [profile."ssh:homelab"]
            hidden = true
            """);

        Assert.Equal(["pwsh", "wsl:Fedora", "ssh:homelab"], config.Profiles.Select(p => p.Id));
        var pwsh = config.Profiles[0];
        Assert.Equal("PowerShell", pwsh.Name);                 // from discovery
        Assert.Equal("pwsh.exe", pwsh.Config.Command);
        Assert.Equal(13f, pwsh.Config.FontSize);               // from the file
        var fedora = config.Profiles[1];
        Assert.Equal("Fedora 42", fedora.Name);
        Assert.Equal("wsl.exe --distribution-id {2} --cd ~", fedora.Config.Command);
        Assert.Equal(new Phosphor.Ghostty.GhosttyColorRgb(0x10, 0x10, 0x10), fedora.Config.Theme.Background);
        Assert.Equal(["pwsh", "wsl:Fedora"], config.VisibleProfiles.Select(p => p.Id));
    }

    [Fact]
    public void EmptySectionsPlaceFoundProfiles()
    {
        var config = Parse("""
            [profile."ssh:homelab"]

            [profile.work]
            command = "cmd.exe"

            [profile.pwsh]
            """);
        Assert.Equal(["ssh:homelab", "work", "pwsh", "wsl:Fedora"], config.Profiles.Select(p => p.Id));
        Assert.Equal("ssh.exe homelab", config.Profiles[0].Config.Command);
        Assert.Empty(config.Warnings);
    }

    [Fact]
    public void AnExplicitCommandWinsOverTheFoundOne()
    {
        var config = Parse("[profile.\"ssh:homelab\"]\ncommand = \"ssh.exe -p 2222 homelab\"");
        Assert.Equal("ssh.exe -p 2222 homelab", config.Profiles[0].Config.Command);
    }

    [Fact]
    public void ChangesToAProfileThatIsntInstalledAreHidden()
    {
        var config = Parse("[profile.\"wsl:Arch\"]\ncolors.background = \"#000000\"");
        var arch = config.Profiles.Single(p => p.Id == "wsl:Arch");
        Assert.True(arch.Hidden);
        Assert.DoesNotContain(arch, config.VisibleProfiles);
        Assert.Empty(config.Warnings); // not a mistake: the distro may just be uninstalled
    }

    [Fact]
    public void DiscoverSectionPicksSources()
    {
        DiscoverySettings? asked = null;
        var config = Parse("""
            [discover]
            ssh = false
            visual-studio = false
            teleporter = true
            wsl = "yes"
            """, settings =>
        {
            asked = settings;
            return [];
        });

        Assert.Equal(new DiscoverySettings(Ssh: false, VisualStudio: false), asked);
        Assert.Contains("config.toml:4: unknown [discover] source 'teleporter'", config.Warnings);
        Assert.Contains(config.Warnings, w => w.StartsWith("config.toml:5: [discover] takes"));
    }

    [Fact]
    public void NothingIsFoundWithoutADiscoverer()
    {
        var config = ConfigFile.Parse("");
        Assert.Equal(["default"], config.Profiles.Select(p => p.Id));
    }
}
