using System.Runtime.InteropServices;

namespace Zinc.Tests;

public partial class LaunchRequestTests
{
    [Fact]
    public void ArgumentsWithoutAFlagAreACommand()
    {
        Assert.Equal(LaunchAction.None, LaunchRequest.Parse([]).Action);
        var command = LaunchRequest.Parse(["wsl.exe", "-d", "Fedora"]);
        Assert.Equal(LaunchAction.None, command.Action);
        Assert.Equal(["wsl.exe", "-d", "Fedora"], command.Command);
        Assert.Equal(["--settings"], LaunchRequest.Parse(["--", "--settings"]).Command);
    }

    [Theory]
    [InlineData("--new-window", LaunchAction.NewWindow)]
    [InlineData("--quick-terminal", LaunchAction.QuickTerminal)]
    [InlineData("--SETTINGS", LaunchAction.Settings)]
    [InlineData("--reopen-workspace", LaunchAction.ReopenWorkspace)]
    [InlineData("--reopen-window", LaunchAction.ReopenWindow)]
    public void FlagsAskForActions(string flag, LaunchAction action)
    {
        var request = LaunchRequest.Parse([flag]);
        Assert.Equal(action, request.Action);
        Assert.Empty(request.Command);
    }

    [Fact]
    public void ProfileAndFolderTakeValues()
    {
        Assert.Equal(new LaunchRequest(LaunchAction.Profile, "wsl:Fedora", null, []).Profile, LaunchRequest.Parse(["--profile", "wsl:Fedora"]).Profile);
        Assert.Equal(LaunchAction.Profile, LaunchRequest.Parse(["--profile", "pwsh"]).Action);

        var folder = LaunchRequest.Parse(["--profile", "wsl:Fedora", "--folder", "/home/zack/dev"]);
        Assert.Equal((LaunchAction.Folder, "wsl:Fedora", "/home/zack/dev"), (folder.Action, folder.Profile, folder.Folder));
        Assert.Equal((LaunchAction.Folder, (string?)null), (LaunchRequest.Parse(["--folder", @"C:\dev"]).Action, LaunchRequest.Parse(["--folder", @"C:\dev"]).Profile));

        // Without its value a flag asks for nothing (not for a program called "--profile").
        Assert.Equal(LaunchAction.None, LaunchRequest.Parse(["--profile"]).Action);
        Assert.Empty(LaunchRequest.Parse(["--profile"]).Command);
    }

    [Theory]
    [InlineData(LaunchAction.Profile, "pwsh", null)]
    [InlineData(LaunchAction.Profile, "ssh:git.example.com", null)]
    [InlineData(LaunchAction.Folder, "pwsh", @"C:\Users\Zack\dev\my project")]
    [InlineData(LaunchAction.Folder, "cmd", @"C:\trailing slash\")]
    [InlineData(LaunchAction.Folder, "wsl:Fedora", "/home/zack/odd \"quoted\" dir")]
    [InlineData(LaunchAction.Folder, null, @"\\wsl.localhost\Fedora\home\zack")]
    [InlineData(LaunchAction.NewWindow, null, null)]
    [InlineData(LaunchAction.ReopenWindow, null, null)]
    public void ArgumentsComeBackAsWritten(LaunchAction action, string? profile, string? folder)
    {
        // As Windows splits a command line (what a jump list item's arguments become).
        var args = Split("zinc.exe " + LaunchRequest.Arguments(action, profile, folder)).Skip(1).ToList();
        var request = LaunchRequest.Parse(args);
        Assert.Equal((action, profile, folder), (request.Action, request.Profile, request.Folder));
    }

    private static string[] Split(string commandLine)
    {
        var argv = CommandLineToArgvW(commandLine, out var count);
        try
        {
            var result = new string[count];
            for (var i = 0; i < count; i++)
            {
                result[i] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * IntPtr.Size))!;
            }

            return result;
        }
        finally
        {
            LocalFree(argv);
        }
    }

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CommandLineToArgvW(string commandLine, out int count);

    [LibraryImport("kernel32.dll")]
    private static partial nint LocalFree(nint memory);
}
