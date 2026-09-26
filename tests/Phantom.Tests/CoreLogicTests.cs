using System.Runtime.InteropServices;
using Phantom.Ghostty;
using Phantom.Input;
using Phantom.Terminal;
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
    public void MousePointerDefaultsToArrow()
    {
        Assert.False(Load("").TextPointer);
        Assert.True(Load("mouse-pointer = text").TextPointer);
        Assert.False(Load("mouse-pointer = text\nmouse-pointer = arrow").TextPointer);
        Assert.False(Load("mouse-pointer = sparkly").TextPointer);
    }

    [Fact]
    public void QuitAfterLastWindowDefaultsToAutomatic()
    {
        Assert.Null(Load("").QuitAfterLastWindowClosed);
        Assert.True(Load("quit-after-last-window-closed = true").QuitAfterLastWindowClosed);
        Assert.False(Load("quit-after-last-window-closed = false").QuitAfterLastWindowClosed);
        Assert.Null(Load("quit-after-last-window-closed = sometimes").QuitAfterLastWindowClosed);
    }

    [Fact]
    public void ClipboardWritesAllowedReadsDeniedByDefault()
    {
        Assert.True(Load("").ClipboardWrite);
        Assert.False(Load("").ClipboardRead);
        Assert.False(Load("clipboard-write = deny").ClipboardWrite);
        Assert.True(Load("clipboard-read = allow").ClipboardRead);
        Assert.False(Load("clipboard-read = ask").ClipboardRead); // Ghostty's "ask" isn't supported (yet)
    }

    [Fact]
    public void FontSizeIsInPoints()
    {
        var config = Load("font-size = 12");
        Assert.Equal(12f, config.FontSize);
        Assert.Equal(16f, config.FontSizeDip);
    }

    [Fact]
    public void ParsesGhosttyColors()
    {
        var config = Load("""
            background = #181818
            foreground = d8d8d8
            cursor-color = #FCFCFA
            palette = 1=#AC4242
            palette = 15 = #f8f8f8
            palette = 256=#000000
            palette = 3=nothex
            background = #12345
            """);
        Assert.Equal(new GhosttyColorRgb(0x18, 0x18, 0x18), config.Theme.Background);
        Assert.Equal(new GhosttyColorRgb(0xD8, 0xD8, 0xD8), config.Theme.Foreground);
        Assert.Equal(new GhosttyColorRgb(0xFC, 0xFC, 0xFA), config.Theme.Cursor);
        Assert.Equal(2, config.Theme.Palette.Count);
        Assert.Equal(new GhosttyColorRgb(0xAC, 0x42, 0x42), config.Theme.Palette[1]);
        Assert.Equal(new GhosttyColorRgb(0xF8, 0xF8, 0xF8), config.Theme.Palette[15]);
    }

    [Fact]
    public void ParsesGhosttyPaddingInPoints()
    {
        var config = Load("""
            window-padding-x = 15
            window-padding-y = 6,12
            """);
        Assert.Equal(20f, config.PaddingLeft);
        Assert.Equal(20f, config.PaddingRight);
        Assert.Equal(8f, config.PaddingTop);
        Assert.Equal(16f, config.PaddingBottom);
    }

    [Fact]
    public void PaletteOverridesReachTerminal()
    {
        var theme = TerminalTheme.Default with { Palette = new Dictionary<int, GhosttyColorRgb> { [1] = new(1, 2, 3) } };
        using var session = new TerminalSession(20, 2, theme);
        session.WriteToScreen("\x1b[31mX");

        // Read the rendered color back through the render state API.
        Assert.Equal(new GhosttyColorRgb(1, 2, 3), FirstCellForeground(session));
    }

    private static unsafe GhosttyColorRgb FirstCellForeground(TerminalSession session)
    {
        Native.RenderStateNew(0, out var state);
        Native.RowIteratorNew(0, out var rows);
        Native.RowCellsNew(0, out var cells);
        try
        {
            lock (session.Lock)
            {
                Native.RenderStateUpdate(state, session.Handle);
            }

            Native.RenderStateGet(state, RenderStateData.RowIterator, &rows);
            Native.RowIteratorNext(rows);
            Native.RowGet(rows, RenderStateRowData.Cells, &cells);
            Native.RowCellsNext(cells);
            GhosttyColorRgb fg;
            Assert.Equal(GhosttyResult.Success, Native.RowCellsGet(cells, RenderStateRowCellsData.FgColor, &fg));
            return fg;
        }
        finally
        {
            Native.RowCellsFree(cells);
            Native.RowIteratorFree(rows);
            Native.RenderStateFree(state);
        }
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
