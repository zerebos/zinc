namespace Zinc.Pty;

/// <summary>
/// A program running on a pseudoterminal, which is what a <see cref="Terminal.TerminalSession"/> talks to. Each
/// platform has one: <c>ConPty</c> on Windows and <c>PosixPty</c> everywhere else. Disposing it ends the program.
/// </summary>
public interface IPty : IDisposable
{
    /// <summary>The program's process id.</summary>
    int ProcessId { get; }

    /// <summary>Raised on the reader thread with each chunk of the program's output.</summary>
    event Action<ReadOnlySpan<byte>>? OutputReceived;

    /// <summary>Raised once, on a background thread, with the exit code, after the program exits and its output is drained.</summary>
    event Action<uint>? Exited;

    /// <summary>Starts reading the program's output. Until then the output waits, so none is lost before a handler is attached.</summary>
    void Begin();

    /// <summary>Sends input to the program.</summary>
    void Write(ReadOnlySpan<byte> data);

    /// <summary>Tells the program its terminal's new size, in cells.</summary>
    void Resize(short cols, short rows);
}

/// <summary>Starts programs on this platform's pseudoterminal.</summary>
public static class PseudoTerminal
{
    /// <param name="environment">Variables to set for the program on top of Zinc's own. A null value removes the variable.</param>
    public static IPty Start(string commandLine, string? workingDirectory, short cols, short rows,
        IReadOnlyDictionary<string, string?>? environment = null) =>
#if WINDOWS
        new ConPty(commandLine, workingDirectory, cols, rows, environment);
#else
        new PosixPty(commandLine, workingDirectory, cols, rows, environment);
#endif

    /// <summary>
    /// Whether the pseudoterminal measures a character's width by grapheme cluster. The terminal turns mode 2027 on
    /// to match, so both sides agree on how many cells an emoji sequence takes.
    /// </summary>
    public static bool MeasuresGraphemes =>
#if WINDOWS
        ConPty.UsesBundledConpty;
#else
        true; // the kernel's line discipline doesn't measure widths at all, so the terminal follows Ghostty's default
#endif
}
