using Zinc.Session;
using Zinc.Shell;
using Zinc.Terminal;

namespace Zinc.Tests;

public class JumpListTests
{
    private static readonly JumpIcon Folder = new(@"C:\Windows\System32\shell32.dll", 3);

    private static readonly JumpProfile[] Profiles =
    [
        new("pwsh", "PowerShell", new(@"C:\Program Files\PowerShell\7\pwsh.exe")),
        new("cmd", "Command Prompt", new(@"C:\Windows\System32\cmd.exe")),
        new("wsl:Fedora", "Fedora", new(@"C:\wsl\Fedora\shortcut.ico")),
        new("git-bash", "Git Bash", new(null)),
        new("ssh:buildbox", "buildbox", new(null)),
        new("msys2", "MSYS2", new(null)),
    ];

    private static JumpListContent Build(string[]? recent = null, RecentFolder[]? folders = null, bool closedWorkspace = false,
        bool closedWindow = false, string[]? removed = null) =>
        JumpListPlan.Build(Profiles, recent ?? [], folders ?? [], closedWorkspace, closedWindow, new HashSet<string>(removed ?? []), Folder);

    private static List<string> Titles(JumpListContent content, string category) =>
        [.. content.Categories.Single(c => c.Title == category).Items.Select(i => i.Title)];

    [Fact]
    public void RecentProfilesComeFirstAndTheProfileOrderFillsUp()
    {
        Assert.Equal(["PowerShell", "Command Prompt", "Fedora", "Git Bash", "buildbox"], Titles(Build(), JumpListPlan.RecentProfilesTitle));
        Assert.Equal(["MSYS2", "Fedora", "PowerShell", "Command Prompt", "Git Bash"],
            Titles(Build(recent: ["msys2", "wsl:Fedora", "gone-profile"]), JumpListPlan.RecentProfilesTitle));
    }

    [Fact]
    public void AProfileItemOpensItsProfile()
    {
        var fedora = Build().Categories[0].Items.Single(i => i.Title == "Fedora");
        Assert.Equal((LaunchAction.Profile, "wsl:Fedora"), Parsed(fedora));
        Assert.Equal(@"C:\wsl\Fedora\shortcut.ico", fedora.Icon.Path);
    }

    [Fact]
    public void RecentFoldersOpenWithTheProfileThatWasThere()
    {
        var now = DateTimeOffset.UtcNow;
        var content = Build(folders:
        [
            new("/home/jdoe/dev/zinc", "wsl:Fedora", "Fedora", now),
            new(@"C:\Users\JDoe\dev\zinc", "pwsh", null, now.AddMinutes(-1)),
            new("/home/jdoe/dev/zinc", "msys2", null, now.AddMinutes(-2)), // the same path again: once, newest
            new("/tmp", "gone-profile", null, now.AddMinutes(-3)), // a profile that's gone: left out
        ]);
        var items = content.Categories.Single(c => c.Title == JumpListPlan.RecentFoldersTitle).Items;
        Assert.Equal(["zinc", "zinc"], items.Select(i => i.Title));
        Assert.Equal("/home/jdoe/dev/zinc (Fedora)", items[0].Description);
        var request = LaunchRequest.Parse(Split(items[0].Arguments));
        Assert.Equal((LaunchAction.Folder, "wsl:Fedora", "/home/jdoe/dev/zinc"), (request.Action, request.Profile, request.Folder));
        Assert.Equal(Folder, items[0].Icon);
    }

    [Fact]
    public void ACategoryHoldsFiveAndNoneWhenEmpty()
    {
        var now = DateTimeOffset.UtcNow;
        var folders = Enumerable.Range(0, 8).Select(i => new RecentFolder($@"C:\f{i}", "pwsh", null, now.AddMinutes(-i))).ToArray();
        Assert.Equal(JumpListPlan.PerCategory, Build(folders: folders).Categories[1].Items.Count);
        Assert.DoesNotContain(Build().Categories, c => c.Title == JumpListPlan.RecentFoldersTitle);
    }

    [Fact]
    public void RemovedItemsStayOutAndTheNextMoveUp()
    {
        var removed = LaunchRequest.Arguments(LaunchAction.Profile, "cmd");
        Assert.Equal(["PowerShell", "Fedora", "Git Bash", "buildbox", "MSYS2"], Titles(Build(removed: [removed]), JumpListPlan.RecentProfilesTitle));
    }

    [Fact]
    public void TasksOfferReopeningOnlyWithSomethingToReopen()
    {
        Assert.Equal(["New window", "Quick terminal", "Settings"], Build().Tasks.Select(t => t.Title));
        Assert.Equal(["New window", "Quick terminal", "Settings", "Reopen closed workspace", "Reopen closed window"],
            Build(closedWorkspace: true, closedWindow: true).Tasks.Select(t => t.Title));
        Assert.All(Build(closedWorkspace: true, closedWindow: true).Tasks, t => Assert.NotEqual(LaunchAction.None, LaunchRequest.Parse(Split(t.Arguments)).Action));
    }

    [Fact]
    public void TheKeyChangesWithTheContent()
    {
        Assert.Equal(JumpListPlan.Key(Build()), JumpListPlan.Key(Build()));
        Assert.NotEqual(JumpListPlan.Key(Build()), JumpListPlan.Key(Build(recent: ["msys2"])));
        Assert.NotEqual(JumpListPlan.Key(Build()), JumpListPlan.Key(Build(closedWindow: true)));
    }

    [Fact]
    public void RecentProfilesKeepTheNewestFirst()
    {
        var recent = new RecentProfiles(["a", "b", "a"]);
        Assert.Equal(["a", "b"], recent.All);
        recent.Note("b");
        recent.Note("c");
        Assert.Equal(["c", "b", "a"], recent.All);
        for (var i = 0; i < 20; i++)
        {
            recent.Note($"p{i}");
        }

        Assert.Equal(RecentProfiles.Capacity, recent.All.Count);
    }

    private static (LaunchAction, string?) Parsed(JumpItem item)
    {
        var request = LaunchRequest.Parse(Split(item.Arguments));
        return (request.Action, request.Profile);
    }

    // Splits like Windows does for the simple cases here (quoted values without inner quotes).
    private static string[] Split(string args)
    {
        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        var quoted = false;
        foreach (var c in args)
        {
            if (c == '"')
            {
                quoted = !quoted;
            }
            else if (c == ' ' && !quoted)
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
            }
            else
            {
                current.Append(c);
            }
        }

        if (current.Length > 0)
        {
            result.Add(current.ToString());
        }

        return [.. result];
    }
}
