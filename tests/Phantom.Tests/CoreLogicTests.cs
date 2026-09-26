using System.Runtime.InteropServices;
using Phantom.Ghostty;
using Phantom.Input;
using Windows.System;

namespace Phantom.Tests;

public class ConfigTests : IDisposable
{
    private readonly string _path = Path.GetTempFileName();

    public void Dispose() => File.Delete(_path);

    private Config Load(string text)
    {
        File.WriteAllText(_path, text);
        return Config.Load(_path);
    }

    [Fact]
    public void MissingFileGivesDefaults()
    {
        var config = Config.Load(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));
        Assert.Equal(new Config(), config);
    }

    [Fact]
    public void ParsesGhosttyStyleKeys()
    {
        var config = Load("""
            # comment
            font-family = JetBrainsMono NF
            font-size = 13.5

            command = "C:\Program Files\Git\bin\bash.exe" --login
            """);
        Assert.Equal("JetBrainsMono NF", config.FontFamily);
        Assert.Equal(13.5f, config.FontSize);
        Assert.Equal("""C:\Program Files\Git\bin\bash.exe" --login""", config.Command![1..]);
    }

    [Fact]
    public void LaterLinesWinAndBadValuesAreIgnored()
    {
        var config = Load("""
            font-size = 12
            font-size = 16
            font-size = huge
            font-size = -3
            no-equals-sign
            unknown-key = 1
            """);
        Assert.Equal(16f, config.FontSize);
    }

    [Fact]
    public void FontSizeParsingIgnoresCulture()
    {
        var previous = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
        try
        {
            Assert.Equal(13.5f, Load("font-size = 13.5").FontSize);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }
}

public class ShellResolverTests
{
    private static Func<string, string?> Env(params (string Key, string Value)[] vars) =>
        key => vars.FirstOrDefault(v => v.Key == key).Value;

    [Fact]
    public void ArgumentsWinOverEverything()
    {
        var command = ShellResolver.Resolve(new Config { Command = "from-config" }, ["wsl.exe", "-d", "Fedora Linux"],
            Env(("PHANTOM_SHELL", "from-env")));
        Assert.Equal("wsl.exe -d \"Fedora Linux\"", command);
    }

    [Fact]
    public void EnvironmentWinsOverConfig()
    {
        var command = ShellResolver.Resolve(new Config { Command = "from-config" }, [], Env(("PHANTOM_SHELL", "from-env")));
        Assert.Equal("from-env", command);
    }

    [Fact]
    public void ConfigWinsOverDefault()
    {
        Assert.Equal("from-config", ShellResolver.Resolve(new Config { Command = "from-config" }, [], Env()));
    }

    [Fact]
    public void FallsBackToCmdWithEmptyPath()
    {
        Assert.Equal("cmd.exe", ShellResolver.Resolve(new Config(), [], Env(("PATH", ""))));
    }

    [Theory]
    [InlineData("plain")]
    [InlineData("has space")]
    [InlineData(@"C:\Program Files\dir\")]
    [InlineData(@"trailing\\")]
    [InlineData("quote\"inside")]
    [InlineData(@"backslash\""quote")]
    [InlineData("")]
    [InlineData(@"\\server\share\x y")]
    public void QuoteRoundTripsThroughCommandLineToArgv(string arg)
    {
        var parsed = CommandLineToArgv("prog.exe " + ShellResolver.Quote(arg));
        Assert.Equal(["prog.exe", arg], parsed);
    }

    private static unsafe string[] CommandLineToArgv(string commandLine)
    {
        var argv = CommandLineToArgvW(commandLine, out var argc);
        try
        {
            return Enumerable.Range(0, argc).Select(i => Marshal.PtrToStringUni(((nint*)argv)[i])!).ToArray();
        }
        finally
        {
            LocalFree(argv);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern nint CommandLineToArgvW(string commandLine, out int argc);

    [DllImport("kernel32.dll")]
    private static extern nint LocalFree(nint mem);
}

public class KeyMapTests
{
    [Theory]
    [InlineData(0x1E, false, GhosttyKey.A)]
    [InlineData(0x02, false, GhosttyKey.Digit1)]
    [InlineData(0x0B, false, GhosttyKey.Digit0)]
    [InlineData(0x1C, false, GhosttyKey.Enter)]
    [InlineData(0x1C, true, GhosttyKey.NumpadEnter)]
    [InlineData(0x48, false, GhosttyKey.Numpad8)]
    [InlineData(0x48, true, GhosttyKey.ArrowUp)]
    [InlineData(0x53, true, GhosttyKey.Delete)]
    [InlineData(0x3B, false, GhosttyKey.F1)]
    [InlineData(0x58, false, GhosttyKey.F12)]
    [InlineData(0x29, false, GhosttyKey.Backquote)]
    [InlineData(0x38, true, GhosttyKey.AltRight)]
    public void ScanCodesMapToPhysicalKeys(uint scan, bool extended, GhosttyKey expected)
    {
        Assert.Equal(expected, KeyMap.FromScanCode(scan, extended, VirtualKey.None));
    }

    [Fact]
    public void VirtualKeyFallbackWithoutScanCode()
    {
        Assert.Equal(GhosttyKey.Q, KeyMap.FromScanCode(0, false, VirtualKey.Q));
        Assert.Equal(GhosttyKey.F13, KeyMap.FromScanCode(0, false, VirtualKey.F13));
        Assert.Equal(GhosttyKey.ArrowLeft, KeyMap.FromScanCode(0, false, VirtualKey.Left));
    }
}
