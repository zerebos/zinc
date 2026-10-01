using Microsoft.Win32;
using Phosphor.Pty;

namespace Phosphor.Tests;

/// <summary>Default-terminal registration, against a scratch key standing in for HKCU.</summary>
public sealed class DefaultTerminalTests : IDisposable
{
    private const string Canary = "{A854D02A-F2FE-44A5-BB24-D03F4CF830D4}", CanaryTerminal = "{1706609C-A4CE-4C0D-B7D2-C19BF66398A5}";
    private readonly string _path = $@"Software\Phosphor.Tests\{Guid.NewGuid():N}";
    private readonly RegistryKey _root;

    public DefaultTerminalTests() => _root = Registry.CurrentUser.CreateSubKey(_path);

    public void Dispose()
    {
        _root.Dispose();
        Registry.CurrentUser.DeleteSubKeyTree(_path, throwOnMissingSubKey: false);
    }

    private void SetPair(string console, string terminal)
    {
        using var startup = _root.CreateSubKey(@"Console\%%Startup");
        startup.SetValue("DelegationConsole", console);
        startup.SetValue("DelegationTerminal", terminal);
    }

    private string? Terminal() => _root.OpenSubKey(@"Console\%%Startup")?.GetValue("DelegationTerminal") as string;

    private string? Server() =>
        _root.OpenSubKey($@"Software\Classes\CLSID\{{{DefaultTerminal.Clsid.ToString().ToUpperInvariant()}}}\LocalServer32")?.GetValue(null) as string;

    [Fact]
    public void TakesTheTerminalHalfAndGivesItBack()
    {
        SetPair(Canary, CanaryTerminal);
        var dt = new DefaultTerminal(_root);
        Assert.Equal(DefaultTerminal.State.Other, dt.Current());

        Assert.True(dt.Enable(@"C:\Apps\Phosphor\Phosphor.exe"));
        Assert.Equal(DefaultTerminal.State.Phosphor, dt.Current());
        Assert.Equal(DefaultTerminal.Clsid, Guid.Parse(Terminal()!));
        Assert.Equal(@"""C:\Apps\Phosphor\Phosphor.exe"" -Embedding", Server());
        Assert.Equal(Canary, _root.OpenSubKey(@"Console\%%Startup")!.GetValue("DelegationConsole")); // the console half stays

        dt.Register(@"C:\Apps\Phosphor\v2\Phosphor.exe"); // a newer install
        Assert.Equal(@"""C:\Apps\Phosphor\v2\Phosphor.exe"" -Embedding", Server());

        dt.Disable();
        Assert.Equal(CanaryTerminal, Terminal());
        Assert.Null(Server());
        Assert.Equal(DefaultTerminal.State.Other, dt.Current());
    }

    [Theory]
    [InlineData("{00000000-0000-0000-0000-000000000000}")] // "Let Windows decide"
    [InlineData("{B23D10C0-E52E-411E-9D5B-C09FDF709C7D}")] // the old console window
    public void NeedsAConsoleHostToKeep(string console)
    {
        SetPair(console, console);
        var dt = new DefaultTerminal(_root);
        Assert.Equal(DefaultTerminal.State.NoConsoleHost, dt.Current());
        Assert.False(dt.Enable(@"C:\Phosphor.exe"));
        Assert.Equal(console, Terminal());
    }

    [Fact]
    public void EnablingTwiceKeepsWhatWasReplacedFirst()
    {
        SetPair(Canary, CanaryTerminal);
        var dt = new DefaultTerminal(_root);
        dt.Enable(@"C:\Phosphor.exe");
        dt.Enable(@"C:\Phosphor.exe");
        dt.Disable();
        Assert.Equal(CanaryTerminal, Terminal());
    }
}
