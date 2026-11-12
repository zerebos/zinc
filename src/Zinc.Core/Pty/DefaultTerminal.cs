using Microsoft.Win32;

namespace Zinc.Pty;

/// <summary>
/// Zinc as Windows' default terminal, so console programs started outside a terminal (a script
/// double-clicked, Run, an installer) open in a Zinc tab. Windows hands each new console to the
/// two COM classes named in <c>HKCU\Console\%%Startup</c>: a console host (<c>DelegationConsole</c>,
/// which is Windows Terminal's OpenConsole) and a terminal (<c>DelegationTerminal</c>), which receives the
/// console as pipes (ITerminalHandoff3). Zinc is the terminal half only, registered for this user
/// without a package. The console half stays an installed Windows Terminal's, so Windows Terminal must be set as
/// the default terminal first. No package check is involved, because packages only fill Windows' Settings dropdown.
/// </summary>
public sealed class DefaultTerminal(RegistryKey root)
{
    /// <summary>Zinc's terminal class (what OpenConsole creates to hand a console over).</summary>
    public static readonly Guid Clsid = new("8E3F6C4B-2D71-4A59-9B0E-5C7A1D2F3B64");

    /// <summary>What test runs (ZINC_DEFTERM_TEST=1) listen as, so they never take a console meant for the installed Zinc.</summary>
    public static readonly Guid TestClsid = new("8E3F6C4B-2D71-4A59-9B0E-5C7A1D2F3B65");

    private static readonly Guid Default = Guid.Empty;
    private static readonly Guid Conhost = new("B23D10C0-E52E-411E-9D5B-C09FDF709C7D");

    private const string StartupKey = @"Console\%%Startup";
    private const string ConsoleValue = "DelegationConsole", TerminalValue = "DelegationTerminal";
    private static readonly string ClassKey = $@"Software\Classes\CLSID\{{{Clsid.ToString().ToUpperInvariant()}}}";

    /// <summary>Where the terminal half Zinc replaced is kept, to give it back.</summary>
    private const string ZincKey = @"Software\Zinc", PreviousValue = "PreviousDelegationTerminal";

    /// <summary>The current user's registry.</summary>
    public static DefaultTerminal ForUser() => new(Registry.CurrentUser);

    public enum State
    {
        /// <summary>Zinc opens console programs.</summary>
        Zinc,

        /// <summary>Another terminal does, with a console host Zinc can work with (Windows Terminal's).</summary>
        Other,

        /// <summary>Windows decides, or the old console window, so there's no console host to hand over to Zinc.</summary>
        NoConsoleHost,
    }

    public State Current()
    {
        var (console, terminal) = Pair();
        return terminal == Clsid ? State.Zinc
            : console is not { } c || c == Default || c == Conhost ? State.NoConsoleHost
            : State.Other;
    }

    private (Guid? Console, Guid? Terminal) Pair()
    {
        using var startup = root.OpenSubKey(StartupKey);
        static Guid? Read(RegistryKey? key, string name) => Guid.TryParse(key?.GetValue(name) as string, out var g) ? g : null;
        return (Read(startup, ConsoleValue), Read(startup, TerminalValue));
    }

    /// <summary>
    /// Makes Zinc the terminal half (the console host stays). <paramref name="exe"/> is what
    /// Windows starts when Zinc isn't running. False if there's no console host to keep.
    /// </summary>
    public bool Enable(string exe)
    {
        var state = Current();
        if (state == State.NoConsoleHost)
        {
            return false;
        }

        if (state == State.Other)
        {
            using var startup = root.OpenSubKey(StartupKey)!;
            using var zinc = root.CreateSubKey(ZincKey);
            zinc.SetValue(PreviousValue, startup.GetValue(TerminalValue) as string ?? "");
        }

        Register(exe);
        using (var startup = root.CreateSubKey(StartupKey))
        {
            startup.SetValue(TerminalValue, Clsid.ToString("B").ToUpperInvariant());
        }

        return true;
    }

    /// <summary>Gives the terminal half back to what Zinc replaced, and removes Zinc's class.</summary>
    public void Disable()
    {
        if (Current() == State.Zinc)
        {
            using var zinc = root.OpenSubKey(ZincKey);
            var previous = zinc?.GetValue(PreviousValue) as string;
            using var startup = root.CreateSubKey(StartupKey);
            if (Guid.TryParse(previous, out _))
            {
                startup.SetValue(TerminalValue, previous!);
            }
            else
            {
                // Nothing to give back, so let Windows decide (both halves, as its Settings page writes them).
                startup.SetValue(ConsoleValue, Default.ToString("B"));
                startup.SetValue(TerminalValue, Default.ToString("B"));
            }
        }

        root.DeleteSubKeyTree(ClassKey, throwOnMissingSubKey: false);
    }

    /// <summary>
    /// Points Zinc's class at <paramref name="exe"/>. Every start does this while Zinc is the
    /// default, because installs are versioned folders.
    /// </summary>
    public void Register(string exe)
    {
        using var server = root.CreateSubKey(ClassKey + @"\LocalServer32");
        var command = $"\"{exe}\" {EmbeddingFlag}";
        if (server.GetValue(null) as string != command)
        {
            server.SetValue(null, command);
        }
    }

    /// <summary>The exe Zinc's class starts, or null if the class isn't registered.</summary>
    public string? RegisteredExe()
    {
        using var server = root.OpenSubKey(ClassKey + @"\LocalServer32");
        return InstallCleanup.ExeOf(server?.GetValue(null) as string);
    }

    /// <summary>What COM adds to the command line when it starts Zinc to receive a console.</summary>
    public const string EmbeddingFlag = "-Embedding";
}
