using Zinc.Commands;
using Zinc.Terminal;

namespace Zinc;

// Palette commands that ask for an argument (the palette's second step): a color scheme picked from
// the list with a live preview, a command line to run in a new tab, a folder to open one in.
public sealed partial class MainWindow
{
    /// <summary>The palette's commands that ask for an argument.</summary>
    private IEnumerable<PaletteCommand> ArgumentCommands(WorkspaceTab tab)
    {
        PaletteCommand Asks(string title, Action ask) => new(title, null, ask, AsksArgument: true);
        yield return Asks("Color scheme for this tab…", () => PickColorScheme(tab, tab.Active, asDefault: false));
        yield return Asks("Default color scheme…", () => PickColorScheme(tab, tab.Active, asDefault: true));
        yield return Asks("New tab running…", () => AskPaletteArgument(new PaletteArgument(
            $"A command to run in a new {ShellName(tab.Active)} tab here, like htop or npm test",
            Submit: line => NewShellTabRunning(tab, line))));
        yield return Asks("New tab in folder…", () => AskFolder(tab, "A folder to open a new tab in, like ~/src"));
    }

    // ------------------------------------------------------------ color schemes

    /// <summary>
    /// Lists the color schemes, previewing the selected one on <paramref name="pane"/>. Once one is chosen,
    /// that tab keeps it, or (with <paramref name="asDefault"/>) it becomes config.toml's colors.theme.
    /// If the question is cancelled, the pane gets its colors back.
    /// </summary>
    private void PickColorScheme(WorkspaceTab tab, TerminalTab pane, bool asDefault)
    {
        var themes = _config.Themes;
        // Previews start from the profile's own settings, not a scheme this tab already has.
        var profile = _config.FindProfile(pane.Profile.Id) ?? pane.Profile;
        var choices = themes.Names().Select(name => new PaletteCommand(name, null,
            () => ChooseScheme(tab, pane, profile, name, asDefault), Remember: false)).ToList();
        if (!asDefault && pane.ColorScheme != null)
        {
            // The scheme the profile's section names, else the global one, as in force for Zinc's light or dark mode.
            var written = (_config.ValueOf(profile.Id, "colors.theme") ?? _config.ValueOf("colors.theme")) as string ?? ThemeLibrary.ZincPair;
            var own = $"The profile's scheme ({ThemeLibrary.Resolve(written, _config.Appearance)})";
            choices.Insert(0, new PaletteCommand(own, null, () =>
            {
                pane.ColorScheme = null;
                SetPaneProfile(tab, pane, profile);
            }, Remember: false));
        }

        AskPaletteArgument(new PaletteArgument(
            asDefault ? "Default color scheme (previewed on this tab)" : "Color scheme for this tab",
            choices,
            Highlight: choice =>
            {
                var preview = asDefault
                    ? DefaultSchemePreview(pane, choice.Title)
                    : themes.Apply(profile.Config, choice.Title) ?? profile.Config;
                pane.Control.ApplyConfig(preview);
            },
            Cancel: () => pane.Control.ApplyConfig(pane.Profile.Config)));
    }

    /// <summary>
    /// What the pane would look like with <paramref name="name"/> as config.toml's colors.theme.
    /// It reads the file again with just that changed, so colors it sets explicitly (background,
    /// foreground, palette entries, a profile's own scheme) still win, as they will after saving.
    /// </summary>
    private Config DefaultSchemePreview(TerminalTab pane, string name)
    {
        try
        {
            var text = File.Exists(_config.Path) ? File.ReadAllText(_config.Path) : "";
            var edited = ConfigFile.Parse(ConfigEditor.Set(text, ["colors", "theme"], name), _config.Path);
            // A profile without its own section (a discovered one) has the global colors.
            var colors = (edited.FindProfile(pane.Profile.Id)?.Config ?? edited.Global).Theme;
            return pane.Profile.Config with { Theme = colors };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Log.Write($"palette: preview of {name} as default: {ex.Message}");
            return _config.Themes.Apply(pane.Profile.Config, name) ?? pane.Profile.Config;
        }
    }

    private void ChooseScheme(WorkspaceTab tab, TerminalTab pane, Profile profile, string name, bool asDefault)
    {
        if (!asDefault)
        {
            if (_config.Themes.Apply(profile.Config, name) is { } themed)
            {
                pane.ColorScheme = name;
                SetPaneProfile(tab, pane, profile with { Config = themed });
            }

            return;
        }

        // The scheme is written to the config, and the reload that follows recolors every pane that
        // doesn't choose its own. Until then this one keeps the preview.
        try
        {
            ConfigEditor.SetInFile(_config.Path, ["colors", "theme"], name);
            Log.Write($"config: colors.theme = {name}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Write($"config: couldn't set colors.theme: {ex.Message}");
            pane.Control.ApplyConfig(pane.Profile.Config);
        }
    }

    /// <summary>Gives a pane new settings, and everything that shows its colors (strip, sidebar, dimming) follows.</summary>
    private void SetPaneProfile(WorkspaceTab tab, TerminalTab pane, Profile profile)
    {
        pane.Profile = profile;
        pane.Control.ApplyConfig(profile.Config);
        UpdateFocusBorders(tab);
        ApplyTabBackground(tab, force: true);
        RefreshWorkspaceList();
    }

    // ------------------------------------------------------------ new tabs

    /// <summary>
    /// A new tab next to the focused one (in its pane with pane tabs on, else in a new workspace),
    /// with the focused tab's profile running <paramref name="command"/> in <paramref name="folder"/>.
    /// </summary>
    private void NewTabWith(WorkspaceTab tab, string command, string? folder)
    {
        var profile = _config.FindProfile(tab.Active.Profile.Id) ?? tab.Active.Profile;
        var directory = folder ?? profile.Config.ResolveWorkingDirectory();
        Log.Write($"palette: new tab {profile.Name}: {command} in {directory}");
        if (_config.Global.PaneTabs && tab.Workspace.GroupOf(tab.Active) is { } group)
        {
            NewTabInGroup(tab, group, profile, command, directory);
        }
        else
        {
            OpenTab(profile, command, directory);
        }
    }

    /// <summary>The focused tab's profile name, for prompts ("a new Fedora tab").</summary>
    private string ShellName(TerminalTab pane) => (_config.FindProfile(pane.Profile.Id) ?? pane.Profile).Name;

    /// <summary>
    /// A new tab of the focused tab's shell, in the folder that shell is in, which then types
    /// <paramref name="line"/> as if by hand. So builtins (dir), aliases and WSL programs all work,
    /// and the tab stays at the shell afterwards, with the command in its history.
    /// </summary>
    private void NewShellTabRunning(WorkspaceTab tab, string line)
    {
        // The same shell as the focused tab (its command, not necessarily its profile's).
        var command = tab.Active.Command;
        string? folder = null;
        if (ShellDirectory.Parse(tab.Active.Control.ReportedDirectory) is { } reported &&
            ShellDirectory.LaunchIn(command, reported) is { } launch &&
            (launch.WorkingDirectory is not { } windows || Directory.Exists(windows)))
        {
            (command, folder) = (launch.Command, launch.WorkingDirectory);
        }

        _startupInput = line;
        try
        {
            NewTabWith(tab, command, folder);
        }
        finally
        {
            _startupInput = null;
        }
    }

    private void AskFolder(WorkspaceTab tab, string prompt) =>
        AskPaletteArgument(new PaletteArgument(prompt, Submit: typed => OpenInFolder(tab, typed)));

    /// <summary>
    /// A new tab of the focused tab's profile in <paramref name="typed"/>. <c>~</c> is home (WSL's own
    /// in WSL), and a relative path starts from where the focused shell is. Asks again if the folder
    /// doesn't exist or doesn't fit the shell.
    /// </summary>
    private void OpenInFolder(WorkspaceTab tab, string typed)
    {
        var profile = _config.FindProfile(tab.Active.Profile.Id) ?? tab.Active.Profile;
        var command = tab.Active.Command; // the focused tab's shell
        var folder = ShellDirectory.ResolveTyped(typed, command,
            ShellDirectory.Parse(tab.Active.Control.ReportedDirectory) ?? profile.Config.ResolveWorkingDirectory());
        if (folder != null && ShellDirectory.LaunchIn(command, folder) is { } launch &&
            (launch.WorkingDirectory is not { } windows || Directory.Exists(windows)))
        {
            NewTabWith(tab, launch.Command, launch.WorkingDirectory);
            return;
        }

        AskFolder(tab, $"There's no folder \"{typed}\" for {profile.Name}. Try another one.");
    }
}
