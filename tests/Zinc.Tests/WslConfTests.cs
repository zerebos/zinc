using Zinc.Terminal;

namespace Zinc.Tests;

public class WslConfTests
{
    [Theory]
    [InlineData(null, "/mnt/")]
    [InlineData("", "/mnt/")]
    [InlineData("[boot]\nsystemd=true\n", "/mnt/")]
    [InlineData("[automount]\nroot = /\n", "/")]
    [InlineData("[automount]\r\nroot=/windir  # drives here\r\n", "/windir/")]
    [InlineData("[Automount]\nroot = \"/win/\"\n", "/win/")]
    [InlineData("[network]\nroot = /nope/\n", "/mnt/")] // another section's key
    [InlineData("[automount]\nenabled = false\n", null)]
    public void MountRootFollowsAutomount(string? conf, string? expected) =>
        Assert.Equal(expected, WslConf.MountRoot(conf));

    [Theory]
    [InlineData(null, null)]
    [InlineData("[automount]\nroot = /\n", null)]
    [InlineData("[network]\nhostname = fedora-box\n", "fedora-box")]
    [InlineData("[Network]\r\nHostname=\"devbox\" # mine\r\n", "devbox")]
    [InlineData("[boot]\nhostname = nope\n", null)] // another section's key
    [InlineData("[network]\nhostname =\n", null)]
    public void HostnameFollowsNetwork(string? conf, string? expected) =>
        Assert.Equal(expected, WslConf.Hostname(conf));

    [Theory]
    [InlineData("/mnt/c/Users/Zack", "/mnt/", @"C:\Users\Zack")]
    [InlineData("/mnt/d", "/mnt/", @"D:\")]
    [InlineData("/c/Users", "/", @"C:\Users")]
    [InlineData("/mnt/wsl/x", "/mnt/", null)] // not a drive letter
    [InlineData("/home/zack", "/", null)]
    public void DriveFoldersMapBack(string linux, string root, string? windows) =>
        Assert.Equal(windows, WslConf.WindowsPath(linux, root));

    [Fact]
    public void DropsAndLinksUseTheDistrosRoot()
    {
        var context = new LinkContext("PC", WslDistro: "Ubuntu", WslMountRoot: "/");
        Assert.Equal("/c/Users/Zack/a.txt", DroppedPaths.Translate(@"C:\Users\Zack\a.txt", context));
        Assert.Equal(LinkAction.Reveal, LinkTarget.Classify("file:///c/Users/Zack", context, out var target));
        Assert.Equal(@"C:\Users\Zack", target);

        var unmounted = context with { WslMountRoot = null };
        Assert.Equal(@"C:\a.txt", DroppedPaths.Translate(@"C:\a.txt", unmounted)); // nothing better to give
    }
}
