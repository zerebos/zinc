using Zinc.Terminal;

namespace Zinc.Tests;

/// <summary>
/// The command list read while output streams is kept between writes and only checked, instead of being made anew
/// every frame. Read after every write or only at the end, it must say the same.
/// </summary>
public class CommandSnapshotTests
{
    private static string Command(int i, int exit, int lines)
    {
        var output = string.Concat(Enumerable.Range(0, lines).Select(n => $"out {i}.{n}\r\n"));
        return $"\e]133;A\a$ \e]133;B\acmd {i}\r\n\e]133;C\a{output}\e]133;D;{exit}\a";
    }

    /// <summary>
    /// The output into a session whose list is read after every write (kept and checked). At checkpoints, that list is
    /// compared with the list of a new session given everything so far and read once (its list made from scratch).
    /// </summary>
    private static void Compare(int scrollback, Action<Action<string>> script)
    {
        using var often = new TerminalSession(40, 8, TerminalTheme.Default);
        often.SetScrollback(scrollback);
        var all = new List<string>();

        void Check()
        {
            using var fresh = new TerminalSession(40, 8, TerminalTheme.Default);
            fresh.SetScrollback(scrollback);
            all.ForEach(fresh.WriteToScreen);
            Assert.Equal(Shape(fresh.Commands()), Shape(often.Commands()));
        }

        script(text =>
        {
            often.WriteToScreen(text);
            all.Add(text);
            _ = often.Commands(); // the kept list, checked after this write
            if (all.Count % 25 == 0)
            {
                Check();
            }
        });
        Check();
    }

    // Everything but the clock times (each session got the output a moment apart).
    private static List<(long, long?, long, string?, int?, bool)> Shape(IReadOnlyList<ShellCommand> commands) =>
        [.. commands.Select(c => (c.PromptRow, c.OutputRow, c.LastRow, c.Text, c.ExitCode, c.Running))];

    [Fact]
    public void RunningCommandFollowsItsOutput()
    {
        using var session = new TerminalSession(40, 8, TerminalTheme.Default);
        session.WriteToScreen(Command(0, 0, 1) + "\e]133;A\a$ \e]133;B\abuild\r\n\e]133;C\a");
        var start = session.Commands()[^1].LastRow;
        for (var i = 0; i < 5; i++)
        {
            session.WriteToScreen($"step {i}\r\n");
            Assert.Equal(start + i + 1, session.Commands()[^1].LastRow);
        }

        Assert.True(session.Commands()[^1].Running);
    }

    [Fact]
    public void SameAsAFreshListWhileTheScrollbackGrows() => Compare(10_000, write =>
    {
        for (var i = 0; i < 40; i++)
        {
            write(Command(i, i % 3, 1 + i % 4));
            for (var n = 0; n < 3; n++)
            {
                write($"\e]133;A\a$ \e]133;B\a"); // a prompt waiting, then more of the same command's output
                write($"noise {i}.{n}\r\n");
            }
        }
    });

    [Fact]
    public void SameAsAFreshListWhileTheScrollbackPrunes() => Compare(50, write =>
    {
        // A small scrollback: old rows (and the commands on them) are dropped, and every row moves up.
        for (var i = 0; i < 120; i++)
        {
            write(Command(i, i % 2, 2 + i % 5));
        }

        write("\e]133;A\a$ \e]133;B\along\r\n\e]133;C\a");
        for (var n = 0; n < 200; n++)
        {
            write($"line {n}\r\n");
        }
    });
}
