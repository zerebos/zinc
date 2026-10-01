using System.Text.Json;
using System.Text.Json.Serialization;
using Phosphor.Panes;

namespace Phosphor.Session;

// What Phosphor remembers between launches (session.json): which windows, workspaces, panes and
// tabs exist, and what each tab runs and where — not what the terminals showed.

/// <summary>
/// Everything to reopen: the normal windows (not the quick terminal), the command palette's recent
/// commands, where the settings window was, whether its preview was hidden, and the folders shells
/// reported lately and the workspaces closed lately (the new-workspace page lists both), and the
/// windows closed lately.
/// </summary>
public sealed record SessionState(int Version, List<WindowState> Windows, List<string>? RecentCommands = null, WindowBounds? Settings = null,
    bool SettingsPreviewHidden = false, List<Phosphor.Terminal.RecentFolder>? RecentFolders = null,
    List<ClosedWorkspace>? ClosedWorkspaces = null, List<ClosedWindow>? ClosedWindows = null)
{
    public const int CurrentVersion = 1;
}

/// <summary>A window's place and size (physical pixels, as AppWindow reports them).</summary>
public sealed record WindowBounds(int X, int Y, int Width, int Height);

/// <summary>
/// A window: its bounds (physical pixels, as AppWindow reports them), whether it was maximized,
/// its workspaces in order and which was shown, and (with the workspace list) its sidebar and title bar.
/// </summary>
public sealed record WindowState(int X, int Y, int Width, int Height, bool Maximized, int Selected, List<WorkspaceState> Workspaces,
    WindowChrome? Chrome = null);

/// <summary>
/// A window's sidebar and title bar modes as toggled, the config's modes when it was saved, and
/// what focus mode replaced (while it's on).
/// </summary>
public sealed record WindowChrome(SidebarMode Sidebar, TitleBarMode TitleBar, SidebarMode ConfigSidebar, TitleBarMode ConfigTitleBar,
    SidebarMode? FocusSidebar = null, TitleBarMode? FocusTitleBar = null)
{
    /// <summary>
    /// Whether to reopen with these modes: only while the config still sets what it did when they
    /// were saved. An edited config wins, as it does over the toggles on a live reload.
    /// </summary>
    public bool Applies(SidebarMode configSidebar, TitleBarMode configTitleBar) =>
        ConfigSidebar == configSidebar && ConfigTitleBar == configTitleBar;
}

/// <summary>A workspace: its custom title and color (if set), its layout, its own folder and icon (if it has them).</summary>
public sealed record WorkspaceState(string? Title, string? Color, LayoutNode Layout, Phosphor.Terminal.WorkspaceFolder? Folder = null,
    string? Icon = null);

/// <summary>A layout node: a split (<see cref="Orientation"/> set, <see cref="Ratio"/> = the first side's share) or a pane (<see cref="Pane"/>).</summary>
public sealed record LayoutNode(SplitOrientation? Orientation = null, double? Ratio = null, LayoutNode? First = null, LayoutNode? Second = null,
    PaneState? Pane = null);

/// <summary>A pane: its tabs, which one is shown, and whether it had focus in its workspace.</summary>
public sealed record PaneState(List<TabState> Tabs, int Shown, bool Focused);

/// <summary>
/// A tab: its profile, the command it ran, the folder it started in, and the folder its shell
/// last reported (decoded; POSIX for WSL), which it reopens in when usable.
/// </summary>
public sealed record TabState(string Profile, string Command, string? StartDirectory, string? Directory, string? ColorScheme = null);

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, UseStringEnumConverter = true)]
[JsonSerializable(typeof(SessionState))]
internal sealed partial class SessionJson : JsonSerializerContext;

/// <summary>Maps workspaces to and from their saved form. Pure, generic over the tab type, so it's unit-testable.</summary>
public static class SessionLayout
{
    /// <summary>The saved form of <paramref name="workspace"/>'s layout; <paramref name="describe"/> saves one tab.</summary>
    public static LayoutNode Capture<T>(Workspace<T> workspace, Func<T, TabState> describe) where T : class =>
        Capture(workspace.Layout.Root!, workspace, describe);

    private static LayoutNode Capture<T>(PaneNode node, Workspace<T> workspace, Func<T, TabState> describe) where T : class =>
        node switch
        {
            PaneSplit split => new LayoutNode(split.Orientation, split.Ratio,
                Capture(split.First, workspace, describe), Capture(split.Second, workspace, describe)),
            PaneLeaf<TabGroup<T>> leaf => new LayoutNode(Pane: new PaneState(
                leaf.Content.Tabs.Select(describe).ToList(),
                IndexOf(leaf.Content.Tabs, leaf.Content.Active),
                leaf.Content == workspace.ActiveGroup)),
            _ => throw new InvalidOperationException($"Unexpected layout node {node.GetType().Name}."),
        };

    /// <summary>
    /// Rebuilds a workspace, opening each tab with <paramref name="open"/>. Tabs it can't open
    /// (null) are left out, and panes left without tabs close up like a closed pane. Null when
    /// nothing could be opened.
    /// </summary>
    public static Workspace<T>? Restore<T>(LayoutNode layout, Func<TabState, T?> open) where T : class
    {
        TabGroup<T>? focused = null, first = null;
        var root = Build(layout);
        if (root == null)
        {
            return null;
        }

        return new Workspace<T>(new PaneTree<TabGroup<T>>(root), focused ?? first!);

        PaneNode? Build(LayoutNode node)
        {
            if (node.Pane is { } pane)
            {
                TabGroup<T>? group = null;
                T? shown = null;
                for (var i = 0; i < pane.Tabs.Count; i++)
                {
                    if (open(pane.Tabs[i]) is not { } tab)
                    {
                        continue;
                    }

                    if (group == null)
                    {
                        group = new TabGroup<T>(tab);
                    }
                    else
                    {
                        group.Add(tab, group.Tabs.Count);
                    }

                    if (i == pane.Shown || shown == null)
                    {
                        shown = tab;
                    }
                }

                if (group == null)
                {
                    return null;
                }

                group.Activate(shown!);
                first ??= group;
                if (pane.Focused)
                {
                    focused = group;
                }

                return new PaneLeaf<TabGroup<T>>(group);
            }

            var a = node.First is { } f ? Build(f) : null;
            var b = node.Second is { } s ? Build(s) : null;
            return a == null ? b : b == null ? a : PaneTree<TabGroup<T>>.Join(node.Orientation ?? SplitOrientation.Horizontal, a, b, node.Ratio ?? 0.5);
        }
    }

    private static int IndexOf<T>(IReadOnlyList<T> list, T item)
    {
        for (var i = 0; i < list.Count; i++)
        {
            if (EqualityComparer<T>.Default.Equals(list[i], item))
            {
                return i;
            }
        }

        return 0;
    }
}

/// <summary>Reads and writes session.json.</summary>
public static class SessionStore
{
    // Readable when opened by hand: quotes and non-ASCII as themselves, not "-style escapes.
    private static readonly SessionJson Json = new(new JsonSerializerOptions(SessionJson.Default.Options)
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    });

    /// <summary>
    /// Where the session lives: <c>PHOSPHOR_SESSION</c> if set; otherwise next to the config in
    /// %APPDATA%\Phosphor — unless <c>PHOSPHOR_CONFIG</c> points elsewhere (a test run), where
    /// nothing is saved or restored, so test instances never touch the real session.
    /// </summary>
    public static string? DefaultPath(Func<string, string?>? getEnv = null)
    {
        getEnv ??= Environment.GetEnvironmentVariable;
        if (getEnv("PHOSPHOR_SESSION") is { Length: > 0 } explicitPath)
        {
            return explicitPath;
        }

        if (getEnv("PHOSPHOR_CONFIG") is { Length: > 0 })
        {
            return null;
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Phosphor", "session.json");
    }

    /// <summary>
    /// The name that one running Phosphor per session file goes by: its single-instance key,
    /// and the name of the lock guarding the file. Same file, same key, however the path is cased.
    /// </summary>
    public static string InstanceKey(string path) =>
        "Phosphor." + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToLowerInvariant())))[..16];

    public static string Serialize(SessionState state) => JsonSerializer.Serialize(state, Json.SessionState);

    /// <summary>The session in <paramref name="json"/>; null if it's unreadable or from a newer version.</summary>
    public static SessionState? Deserialize(string json)
    {
        try
        {
            var state = JsonSerializer.Deserialize(json, Json.SessionState);
            return state is { Version: <= SessionState.CurrentVersion, Windows: not null } ? state : null;
        }
        catch (JsonException ex)
        {
            Log.Write($"session: unreadable ({ex.Message})");
            return null;
        }
    }

    public static SessionState? Load(string path)
    {
        try
        {
            return File.Exists(path) ? Deserialize(File.ReadAllText(path)) : null;
        }
        catch (IOException ex)
        {
            Log.Write($"session: can't read {path}: {ex.Message}");
            return null;
        }
    }

    /// <summary>Writes atomically (a temp file moved over the old one), so a crash mid-write can't lose the session.</summary>
    public static void Save(string path, string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, path, overwrite: true);
    }
}
