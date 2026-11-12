using Zinc.Terminal;

namespace Zinc.Shell;

/// <summary>A profile as the jump list shows it: its id, name and icon (visible profiles only, in profile order).</summary>
public sealed record JumpProfile(string Id, string Name, JumpIcon Icon);

/// <summary>
/// What the taskbar button's jump list holds (pure, and App writes it with JumpListWriter):
/// <list type="bullet">
/// <item>Recent profiles: the ones opened lately, filled up from the profile order when there aren't enough yet.</item>
/// <item>Recent folders: the folders shells reported lately, each opening a workspace there with the profile that was
/// there.</item>
/// <item>Tasks: New window, Quick terminal, Settings, and the reopen commands while there's something to reopen.</item>
/// </list>
/// Items the user removed from the list are left out, because Windows refuses a category holding one, and the next
/// ones move up.
/// </summary>
public static class JumpListPlan
{
    /// <summary>Items per category, so two categories make about the ten Windows shows by default.</summary>
    public const int PerCategory = 5;

    public const string RecentProfilesTitle = "Recent profiles", RecentFoldersTitle = "Recent folders";

    public static JumpListContent Build(IReadOnlyList<JumpProfile> profiles, IReadOnlyList<string> recentProfiles,
        IReadOnlyList<RecentFolder> recentFolders, bool closedWorkspace, bool closedWindow, IReadOnlySet<string> removed,
        JumpIcon folderIcon)
    {
        var byId = profiles.ToDictionary(p => p.Id);

        var profileItems = recentProfiles.Where(byId.ContainsKey).Concat(profiles.Select(p => p.Id))
            .Distinct()
            .Select(id => byId[id])
            .Select(p => new JumpItem(p.Name, LaunchRequest.Arguments(LaunchAction.Profile, p.Id), $"New workspace: {p.Name}", p.Icon))
            .Where(i => !removed.Contains(i.Arguments))
            .Take(PerCategory)
            .ToList();

        var folderItems = recentFolders
            .Where(f => byId.ContainsKey(f.Profile))
            .DistinctBy(f => f.Path)
            .Select(f => new JumpItem(new WorkspaceFolder(f.Path, f.WslDistro).Name,
                LaunchRequest.Arguments(LaunchAction.Folder, f.Profile, f.Path), $"{f.Path} ({byId[f.Profile].Name})", folderIcon))
            .Where(i => !removed.Contains(i.Arguments))
            .Take(PerCategory)
            .ToList();

        var categories = new List<JumpCategory>();
        if (profileItems.Count > 0)
        {
            categories.Add(new JumpCategory(RecentProfilesTitle, profileItems));
        }

        if (folderItems.Count > 0)
        {
            categories.Add(new JumpCategory(RecentFoldersTitle, folderItems));
        }

        var zinc = new JumpIcon(null);
        var tasks = new List<JumpItem>
        {
            new("New window", LaunchRequest.Arguments(LaunchAction.NewWindow), "Open a new Zinc window", zinc),
            new("Quick terminal", LaunchRequest.Arguments(LaunchAction.QuickTerminal), "Show or hide the quick terminal", zinc),
            new("Settings", LaunchRequest.Arguments(LaunchAction.Settings), "Open Zinc's settings", zinc),
        };
        if (closedWorkspace)
        {
            tasks.Add(new("Reopen closed workspace", LaunchRequest.Arguments(LaunchAction.ReopenWorkspace), "Bring back the workspace closed last", zinc));
        }

        if (closedWindow)
        {
            tasks.Add(new("Reopen closed window", LaunchRequest.Arguments(LaunchAction.ReopenWindow), "Bring back the window closed last", zinc));
        }

        return new JumpListContent(categories, tasks);
    }

    /// <summary>A key that changes when the content does (the list is written again only then).</summary>
    public static string Key(JumpListContent content) => string.Join('\n',
        content.Categories.SelectMany(c => c.Items.Select(i => $"{c.Title}|{i.Title}|{i.Arguments}|{i.Description}|{i.Icon}"))
            .Concat(content.Tasks.Select(i => $"task|{i.Title}|{i.Arguments}")));
}
