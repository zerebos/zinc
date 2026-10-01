using System.Runtime.InteropServices;
using Phosphor.Ghostty;
using Phosphor.Input;
using Phosphor.Terminal;
using Windows.System;

namespace Phosphor.Tests;

public class ConfigTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"phosphor-{Guid.NewGuid():N}.toml");

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
    public void ReadsGroupedSettings()
    {
        var config = Load("""
            # comment
            [font]
            family = "JetBrainsMono NF"
            size = 13.5
            """);
        Assert.Equal("JetBrainsMono NF", config.FontFamily);
        Assert.Equal(13.5f, config.FontSize);
    }

    [Fact]
    public void BadValuesAreIgnored()
    {
        Assert.Equal(Config.DefaultFontSize, Load("[font]\nsize = \"huge\"").FontSize);
        Assert.Equal(Config.DefaultFontSize, Load("[font]\nsize = -3").FontSize);
        Assert.Equal(12f, Load("[font]\nsize = 12\nunknown = 1").FontSize);
    }

    [Fact]
    public void MousePointerDefaultsToArrow()
    {
        Assert.False(Load("").TextPointer);
        Assert.True(Load("[terminal]\nmouse-pointer = \"text\"").TextPointer);
        Assert.False(Load("[terminal]\nmouse-pointer = \"sparkly\"").TextPointer);
    }

    [Fact]
    public void TabGroupsOnWithAutomaticStripsByDefault()
    {
        Assert.True(Load("").TabGroups);
        Assert.Equal(GroupTabStrip.Auto, Load("").GroupTabStrip);
        Assert.False(Load("[window]\ntab-groups = false").TabGroups);
        Assert.Equal(GroupTabStrip.Always, Load("[window]\ngroup-tab-strip = \"always\"").GroupTabStrip);
        Assert.Equal(GroupTabStrip.Auto, Load("[window]\ngroup-tab-strip = \"1\"").GroupTabStrip); // not an enum index
        Assert.Equal(GroupTabStrip.Auto, Load("[window]\ngroup-tab-strip = \"sometimes\"").GroupTabStrip);
    }

    [Fact]
    public void UnfocusedPanesAreDimmedLikeGhosttyByDefault()
    {
        Assert.Equal(0.7, Load("").UnfocusedSplitOpacity);
        Assert.Equal(1.0, Load("[window]\nunfocused-split-opacity = 1").UnfocusedSplitOpacity);
        Assert.Equal(0.15, Load("[window]\nunfocused-split-opacity = 0").UnfocusedSplitOpacity); // clamped, like Ghostty
        Assert.Equal(1.0, Load("[window]\nunfocused-split-opacity = 3.0").UnfocusedSplitOpacity);
        Assert.Equal(0.7, Load("[window]\nunfocused-split-opacity = \"dim\"").UnfocusedSplitOpacity);
        Assert.Null(Load("").UnfocusedSplitFill);
        Assert.Equal(new GhosttyColorRgb { R = 0, G = 0, B = 0 }, Load("[window]\nunfocused-split-fill = \"#000000\"").UnfocusedSplitFill);
    }

    [Fact]
    public void SessionsAreRestoredUnlessTurnedOff()
    {
        Assert.True(Load("").RestoreSession);
        Assert.True(Load("[window]\nsave-state = \"always\"").RestoreSession);
        Assert.False(Load("[window]\nsave-state = \"never\"").RestoreSession);
        Assert.False(Load("[window]\nsave-state = false").RestoreSession);
        Assert.True(Load("[window]\nsave-state = \"sometimes\"").RestoreSession);
    }

    [Fact]
    public void WorkspaceTabsOnTopByDefault()
    {
        Assert.Equal(WorkspaceTabs.Top, Load("").WorkspaceTabs);
        Assert.Equal(WorkspaceTabs.Left, Load("[window]\nworkspace-tabs = \"left\"").WorkspaceTabs);
        Assert.Equal(WorkspaceTabs.Left, Load("[window]\nworkspace-tabs = \"Left\"").WorkspaceTabs);
        Assert.Equal(WorkspaceTabs.Top, Load("[window]\nworkspace-tabs = \"bottom\"").WorkspaceTabs);
    }

    [Fact]
    public void SidebarAndTitleBarModes()
    {
        Assert.Equal(SidebarMode.Full, Load("").Sidebar);
        Assert.Equal(TitleBarMode.Show, Load("").TitleBar);
        Assert.Equal(SidebarMode.Rail, Load("[window]\nsidebar = \"rail\"").Sidebar);
        Assert.Equal(SidebarMode.AutoHide, Load("[window]\nsidebar = \"auto-hide\"").Sidebar);
        Assert.Equal(TitleBarMode.AutoHide, Load("[window]\ntitle-bar = \"auto-hide\"").TitleBar);
        Assert.Equal(SidebarMode.Full, Load("[window]\nsidebar = \"2\"").Sidebar);        // not by number
        Assert.Equal(TitleBarMode.Show, Load("[window]\ntitle-bar = \"hidden\"").TitleBar);
    }

    [Fact]
    public void CommandFinishedNotifications()
    {
        var defaults = Load("");
        Assert.Equal(NotifyMode.Badge, defaults.NotifyCommandFinished);
        Assert.Equal(NotifyMode.Badge, defaults.NotifyPrograms);
        Assert.Equal(NotifyMode.Toast, Load("[notify]\ncommand-finished = \"toast\"").NotifyCommandFinished);
        Assert.Equal(NotifyMode.ToastAlways, Load("[notify]\nprograms = \"toast-always\"").NotifyPrograms);
        Assert.Equal(NotifyMode.Never, Load("[notify]\nprograms = \"never\"").NotifyPrograms);
        Assert.Equal(TimeSpan.FromSeconds(10), defaults.NotifyCommandFinishedAfter);
        Assert.Equal(NotifyMode.Never, Load("[notify]\ncommand-finished = \"never\"").NotifyCommandFinished);
        Assert.Equal(TimeSpan.FromSeconds(30), Load("[notify]\ncommand-finished-after = 30").NotifyCommandFinishedAfter);
        Assert.Equal(TimeSpan.FromSeconds(90), Load("[notify]\ncommand-finished-after = \"1m30s\"").NotifyCommandFinishedAfter);
    }

    [Theory]
    [InlineData("5", 5)]
    [InlineData("2.5", 2.5)]
    [InlineData("30s", 30)]
    [InlineData("2m", 120)]
    [InlineData("1h", 3600)]
    [InlineData("1m 30s", 90)]
    public void ReadsDurations(string text, double seconds)
    {
        Assert.True(Config.TryDuration(text, out var d));
        Assert.Equal(TimeSpan.FromSeconds(seconds), d);
    }

    [Theory]
    [InlineData("")]
    [InlineData("soon")]
    [InlineData("10x")]
    [InlineData("-5")]
    [InlineData("5s later")]
    public void RejectsWhatIsNotADuration(string text) => Assert.False(Config.TryDuration(text, out _));

    [Fact]
    public void QuitAfterLastWindowDefaultsToAutomatic()
    {
        Assert.Null(Load("").QuitAfterLastWindowClosed);
        Assert.True(Load("[window]\nquit-after-last-window-closed = true").QuitAfterLastWindowClosed);
        Assert.False(Load("[window]\nquit-after-last-window-closed = false").QuitAfterLastWindowClosed);
        Assert.Null(Load("[window]\nquit-after-last-window-closed = \"sometimes\"").QuitAfterLastWindowClosed);
    }

    [Fact]
    public void ClipboardWriteAllowedReadAsks()
    {
        Assert.True(Load("").ClipboardWrite);
        Assert.Equal(ClipboardAccess.Ask, Load("").ClipboardRead);
        Assert.False(Load("[terminal]\nclipboard-write = \"deny\"").ClipboardWrite);
        Assert.Equal(ClipboardAccess.Allow, Load("[terminal]\nclipboard-read = \"allow\"").ClipboardRead);
        Assert.Equal(ClipboardAccess.Allow, Load("[terminal]\nclipboard-read = true").ClipboardRead);
        Assert.Equal(ClipboardAccess.Deny, Load("[terminal]\nclipboard-read = false").ClipboardRead);
    }

    [Fact]
    public void FontSizeIsInPoints()
    {
        var config = Load("[font]\nsize = 12");
        Assert.Equal(12f, config.FontSize);
        Assert.Equal(16f, config.FontSizeDip);
    }

    [Fact]
    public void ReadsColors()
    {
        var config = Load("""
            [colors]
            background = "#181818"
            foreground = "d8d8d8"
            cursor = "#FCFCFA"
            palette.1 = "#AC4242"
            palette.15 = "#f8f8f8"
            palette.256 = "#000000"
            palette.3 = "nothex"
            """);
        Assert.Equal(new GhosttyColorRgb(0x18, 0x18, 0x18), config.Theme.Background);
        Assert.Equal(new GhosttyColorRgb(0xD8, 0xD8, 0xD8), config.Theme.Foreground);
        Assert.Equal(new GhosttyColorRgb(0xFC, 0xFC, 0xFA), config.Theme.Cursor);
        Assert.Equal(new GhosttyColorRgb(0xAC, 0x42, 0x42), config.Theme.Palette[1]);
        Assert.Equal(TerminalTheme.Default.Palette[2], config.Theme.Palette[2]); // the rest stay Phosphor's own
        Assert.Equal(new GhosttyColorRgb(0xF8, 0xF8, 0xF8), config.Theme.Palette[15]);
    }

    [Fact]
    public void PaletteCanBeAnArray()
    {
        var config = Load("""
            [colors]
            palette = ["#000000", "#aa0000", "#00aa00"]
            """);
        Assert.Equal(new GhosttyColorRgb(0, 0, 0), config.Theme.Palette[0]);
        Assert.Equal(new GhosttyColorRgb(0xAA, 0, 0), config.Theme.Palette[1]);
        Assert.Equal(new GhosttyColorRgb(0, 0xAA, 0), config.Theme.Palette[2]);
    }

    [Fact]
    public void ReadsPaddingInPoints()
    {
        var config = Load("""
            [terminal]
            padding-x = 15
            padding-y = [6, 12]
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

    [Fact]
    public void ThemeChangesReachTextAlreadyOnScreen()
    {
        var theme = TerminalTheme.Default with { Palette = new Dictionary<int, GhosttyColorRgb> { [1] = new(1, 2, 3) } };
        using var session = new TerminalSession(20, 2, theme);
        session.WriteToScreen("\x1b[31mX");

        session.SetTheme(TerminalTheme.Default with { Palette = new Dictionary<int, GhosttyColorRgb> { [1] = new(4, 5, 6) } });
        Assert.Equal(new GhosttyColorRgb(4, 5, 6), FirstCellForeground(session));

        // An entry the new theme doesn't set goes back to libghostty's default.
        session.SetTheme(TerminalTheme.Default);
        Assert.NotEqual(new GhosttyColorRgb(4, 5, 6), FirstCellForeground(session));
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
    public void NumbersIgnoreCulture()
    {
        var previous = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
        try
        {
            Assert.Equal(13.5f, Load("[font]\nsize = 13.5").FontSize);
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
            Env(("PHOSPHOR_SHELL", "from-env")));
        Assert.Equal("wsl.exe -d \"Fedora Linux\"", command);
    }

    [Fact]
    public void EnvironmentWinsOverConfig()
    {
        var command = ShellResolver.Resolve(new Config { Command = "from-config" }, [], Env(("PHOSPHOR_SHELL", "from-env")));
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
