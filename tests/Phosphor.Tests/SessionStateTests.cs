using Phosphor.Panes;
using Phosphor.Session;
using Phosphor.Terminal;

namespace Phosphor.Tests;

public class SessionStateTests
{
    private sealed record T(string Name);

    private static TabState Saved(T t) => new("Default", t.Name, null, null);

    [Fact]
    public void LayoutRoundTripsWithRatiosTabsAndFocus()
    {
        // [a b*] | ([c] over [d]), b shown in the first pane, d's pane focused, dividers moved.
        T a = new("a"), b = new("b"), c = new("c"), d = new("d");
        var ws = new Workspace<T>(a);
        ws.ActiveGroup.Add(b);
        var right = ws.Split(ws.ActiveGroup, PaneDirection.Right, c);
        ws.Split(right, PaneDirection.Down, d);
        ws.Resize(ws.GroupOf(a)!, PaneDirection.Right, 0.1);

        var saved = SessionLayout.Capture(ws, Saved);
        var restored = SessionLayout.Restore(saved, s => new T(s.Command))!;

        Assert.Equal(["a", "b", "c", "d"], restored.Tabs.Select(t => t.Name));
        Assert.Equal("d", restored.Active.Name);
        Assert.Equal("b", restored.GroupOf(restored.Tabs.First())!.Active.Name);
        Assert.Equal(ws.Layout.Layout().Values.OrderBy(r => r.X).ThenBy(r => r.Y),
            restored.Layout.Layout().Values.OrderBy(r => r.X).ThenBy(r => r.Y));
    }

    [Fact]
    public void TabsThatCantOpenAreLeftOutAndEmptyPanesClose()
    {
        T a = new("a"), b = new("b"), c = new("c");
        var ws = new Workspace<T>(a);
        ws.Split(ws.ActiveGroup, PaneDirection.Right, b);
        ws.ActiveGroup.Add(c);
        var saved = SessionLayout.Capture(ws, Saved);

        // b and c's pane can't reopen at all: a is all that's left, alone.
        var restored = SessionLayout.Restore(saved, s => s.Command == "a" ? new T("a") : null)!;
        Assert.Equal(["a"], restored.Tabs.Select(t => t.Name));
        Assert.Equal(1, restored.Layout.Count);

        Assert.Null(SessionLayout.Restore(saved, _ => (T?)null));
    }

    [Fact]
    public void ShownTabFallsBackWhenItCantOpen()
    {
        T a = new("a"), b = new("b");
        var ws = new Workspace<T>(a);
        ws.ActiveGroup.Add(b); // b shown
        var restored = SessionLayout.Restore(SessionLayout.Capture(ws, Saved), s => s.Command == "a" ? new T("a") : null)!;
        Assert.Equal("a", restored.Active.Name);
    }

    [Fact]
    public void JsonRoundTrips()
    {
        var ws = new Workspace<T>(new T("a"));
        ws.Split(ws.ActiveGroup, PaneDirection.Down, new T("b"));
        var state = new SessionState(SessionState.CurrentVersion,
        [
            new WindowState(10, 20, 1100, 700, Maximized: false, Selected: 0,
                [new WorkspaceState("Build", "#FF8800", SessionLayout.Capture(ws, t => new TabState("WSL", t.Name, @"C:\x", "/home/zack")))]),
        ]);

        var json = SessionStore.Serialize(state);
        Assert.Contains("\"orientation\": \"Vertical\"", json);
        var back = SessionStore.Deserialize(json)!;
        Assert.Equal(json, SessionStore.Serialize(back));
        Assert.Equal("/home/zack", back.Windows[0].Workspaces[0].Layout.First!.Pane!.Tabs[0].Directory);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"version\": 99, \"windows\": []}")] // from a newer Phosphor
    [InlineData("{\"version\": 1}")]
    public void UnreadableSessionsAreIgnored(string json) => Assert.Null(SessionStore.Deserialize(json));

    [Fact]
    public void TestRunsNeverTouchTheRealSession()
    {
        Assert.Null(SessionStore.DefaultPath(n => n == "PHOSPHOR_CONFIG" ? @"C:\tmp\config" : null));
        Assert.Equal(@"C:\tmp\s.json", SessionStore.DefaultPath(n => n switch
        {
            "PHOSPHOR_CONFIG" => @"C:\tmp\config",
            "PHOSPHOR_SESSION" => @"C:\tmp\s.json",
            _ => null,
        }));
        Assert.EndsWith(@"Phosphor\session.json", SessionStore.DefaultPath(_ => null));
    }

    [Theory]
    [InlineData("wsl.exe", "/home/zack", "wsl.exe --cd \"/home/zack\"", null)]
    [InlineData(@"C:\WINDOWS\system32\wsl.exe --distribution-id {1} --cd ~", "/srv/app",
        "C:\\WINDOWS\\system32\\wsl.exe --cd \"/srv/app\" --distribution-id {1}", null)]
    [InlineData("\"C:\\Program Files\\PowerShell\\7\\pwsh.exe\" -NoLogo", @"C:\src", "\"C:\\Program Files\\PowerShell\\7\\pwsh.exe\" -NoLogo", @"C:\src")]
    [InlineData(@"C:\Program Files\Git\bin\bash.exe", "/c/Users/Zack", @"C:\Program Files\Git\bin\bash.exe", @"C:\Users\Zack")]
    [InlineData("bash.exe", "/d", "bash.exe", @"D:\")]
    public void LaunchesInTheReportedDirectory(string command, string dir, string expectedCommand, string? expectedCwd) =>
        Assert.Equal((expectedCommand, expectedCwd), ShellDirectory.LaunchIn(command, dir));

    [Theory]
    [InlineData("wsl.exe", @"C:\Users")]           // WSL reported a Windows path: use the tab's own start
    [InlineData("pwsh.exe", "/home/zack")]          // a POSIX path means nothing to pwsh
    public void UnusableDirectoriesFallBack(string command, string dir) => Assert.Null(ShellDirectory.LaunchIn(command, dir));
}
