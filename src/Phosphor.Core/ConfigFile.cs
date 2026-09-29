using System.Globalization;
using Phosphor.Input;
using Tomlyn.Parsing;
using Tomlyn.Syntax;

namespace Phosphor;

/// <summary>
/// A named set of settings: the global settings with the profile's own on top. <see cref="Id"/>
/// is what keys, the default profile and saved sessions refer to; <see cref="Name"/> is shown.
/// </summary>
public sealed record Profile(string Id, string Name, Config Config, bool Hidden = false);

/// <summary>
/// The config file, <c>%APPDATA%\Phosphor\config.toml</c>: settings grouped into sections,
/// profiles that override some of them, and named color schemes from the themes folders.
/// <code>
/// default-profile = "fedora"
///
/// [font]
/// family = "JetBrainsMono NF"
/// size = 11
///
/// [colors]
/// theme = "Alacritty"
///
/// [profile.fedora]
/// name = "Fedora"
/// command = 'wsl.exe -d Fedora'
/// colors.theme = "0x96f"
/// </code>
/// Problems (unknown keys, bad values, TOML errors) become <see cref="Warnings"/> naming the
/// file and line; everything else still applies.
/// </summary>
public sealed class ConfigFile
{
    private ConfigFile(Config global, IReadOnlyList<Profile> profiles, Profile defaultProfile, IReadOnlyList<string> warnings,
        bool hasErrors, string path, Keybindings keybindings, ThemeLibrary themes)
    {
        Themes = themes;
        Keybindings = keybindings;
        Global = global;
        Profiles = profiles;
        DefaultProfile = defaultProfile;
        Warnings = warnings;
        HasErrors = hasErrors;
        Path = path;
    }

    /// <summary>
    /// Whether a file isn't valid TOML. Its valid parts still load at startup, but a reload
    /// keeps the settings in force rather than apply a half-read file (a save mid-edit).
    /// </summary>
    public bool HasErrors { get; }

    /// <summary>The color schemes this config can name (its themes folder, then the bundled ones).</summary>
    public ThemeLibrary Themes { get; }

    /// <summary>The Windows Terminal defaults with the <c>[keys]</c> table applied.</summary>
    public Keybindings Keybindings { get; }

    /// <summary>The file these settings came from.</summary>
    public string Path { get; }

    /// <summary>Settings outside any profile.</summary>
    public Config Global { get; }

    /// <summary>Profiles in file order, hidden ones included; never empty (a "Default" profile if none are defined).</summary>
    public IReadOnlyList<Profile> Profiles { get; }

    /// <summary>Profiles offered in menus: <see cref="Profiles"/> without the hidden ones.</summary>
    public IEnumerable<Profile> VisibleProfiles => Profiles.Where(p => !p.Hidden);

    public Profile DefaultProfile { get; }

    /// <summary>Problems found while reading, as "FILE:LINE: message".</summary>
    public IReadOnlyList<string> Warnings { get; }

    /// <summary>The profile with id <paramref name="id"/>, or (for older saved sessions) with that display name.</summary>
    public Profile? FindProfile(string id) =>
        Profiles.FirstOrDefault(p => p.Id == id) ?? Profiles.FirstOrDefault(p => p.Name == id);

    public static string DefaultPath { get; } =
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Phosphor", "config.toml");

    /// <summary>The color schemes that ship with Phosphor (Themes\ next to the executable).</summary>
    public static string BundledThemesDirectory => System.IO.Path.Combine(AppContext.BaseDirectory, "Themes");

    /// <summary>Reads the config at <paramref name="path"/> (default: PHOSPHOR_CONFIG, else <see cref="DefaultPath"/>).</summary>
    public static ConfigFile Load(string? path = null)
    {
        path ??= Environment.GetEnvironmentVariable("PHOSPHOR_CONFIG") is { Length: > 0 } fromEnv ? fromEnv : DefaultPath;
        path = System.IO.Path.GetFullPath(path);
        string text;
        try
        {
            text = File.Exists(path) ? ReadShared(path) : "";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Treated like invalid TOML: reported, and a reload keeps the settings in force (at
            // startup, the defaults) rather than apply a file it couldn't see.
            var defaults = Parse("", path, discover: ProfileDiscovery.Discover);
            return new ConfigFile(defaults.Global, defaults.Profiles, defaults.DefaultProfile,
                [$"{System.IO.Path.GetFileName(path)}: couldn't be read ({ex.Message})"], hasErrors: true,
                path, defaults.Keybindings, defaults.Themes);
        }

        return Parse(text, path, discover: ProfileDiscovery.Discover);
    }

    /// <summary>
    /// Reads a file another program may have open (an editor mid-save): sharing its access, with a
    /// few short retries while it's locked.
    /// </summary>
    internal static string ReadShared(string path)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }
            catch (IOException) when (attempt < 4 && File.Exists(path))
            {
                Thread.Sleep(50 * attempt);
            }
        }
    }

    /// <summary>
    /// Reads config text. <paramref name="path"/> names it in warnings and anchors <c>include</c>
    /// and the user's themes folder (next to it); <paramref name="bundledThemes"/> is searched after.
    /// <paramref name="discover"/> finds installed profiles (none if null: tests, and anything
    /// that shouldn't look at the machine).
    /// </summary>
    public static ConfigFile Parse(string text, string path = "config.toml", string? bundledThemes = null,
        Func<DiscoverySettings, IReadOnlyList<DiscoveredProfile>>? discover = null)
    {
        var reader = new Reader(path, bundledThemes ?? BundledThemesDirectory, discover);
        reader.Read(text, path, depth: 0);
        return reader.Build();
    }

    // ------------------------------------------------------------------ reading

    /// <summary>One setting as written: its full key path, its value, and where it is.</summary>
    private sealed record Entry(IReadOnlyList<string> Path, object Value, string File, int Line)
    {
        public string Where => $"{System.IO.Path.GetFileName(File)}:{Line}";
        public string Key => string.Join('.', Path);
    }

    /// <summary>The sections a profile may override; the rest belong to the app.</summary>
    private static readonly HashSet<string> ProfileGroups = ["font", "colors", "terminal"];

    private static readonly HashSet<string> Groups = ["font", "colors", "terminal", "window", "notify", "quick-terminal"];


    private sealed class Reader(string mainPath, string bundledThemes, Func<DiscoverySettings, IReadOnlyList<DiscoveredProfile>>? discover)
    {
        private readonly List<Entry> _entries = [];
        private readonly ThemeLibrary _themes = ThemeLibrary.For(mainPath, bundledThemes);
        private readonly List<string> _warnings = [];
        private readonly HashSet<string> _files = new(StringComparer.OrdinalIgnoreCase);
        private bool _hasErrors;

        // [profile.<id>] headers in order, empty ones included: a bare header places a found
        // profile in the menu without changing it.
        private readonly List<string> _profileSections = [];

        public void Read(string text, string file, int depth)
        {
            if (!_files.Add(System.IO.Path.GetFullPath(file)))
            {
                _warnings.Add($"{System.IO.Path.GetFileName(file)}: included more than once; skipped");
                return;
            }

            var doc = SyntaxParser.Parse(text, file);
            _hasErrors |= doc.HasErrors;
            foreach (var d in doc.Diagnostics)
            {
                _warnings.Add($"{System.IO.Path.GetFileName(file)}:{d.Span.Start.Line + 1}: {d.Message}");
            }

            var entries = new List<Entry>();
            foreach (var kv in doc.KeyValues)
            {
                Flatten([], kv, file, entries);
            }

            foreach (var table in doc.Tables)
            {
                if (table is not TableSyntax { Name: { } name } t)
                {
                    _warnings.Add($"{System.IO.Path.GetFileName(file)}:{table.Span.Start.Line + 1}: arrays of tables ([[...]]) aren't used by Phosphor");
                    continue;
                }

                var prefix = ConfigEditor.KeyParts(name);
                if (prefix is ["profile", var sectionId, ..] && !_profileSections.Contains(sectionId))
                {
                    _profileSections.Add(sectionId);
                }

                foreach (var kv in t.Items)
                {
                    Flatten(prefix, kv, file, entries);
                }
            }

            // Included files first: what includes them wins.
            foreach (var include in entries.Where(e => e.Path is ["include"]))
            {
                foreach (var pattern in include.Value is List<object> list ? list : [include.Value])
                {
                    if (pattern is not string p)
                    {
                        _warnings.Add($"{include.Where}: include takes file paths");
                        continue;
                    }

                    foreach (var included in Expand(p, file, include))
                    {
                        if (depth >= 8)
                        {
                            _warnings.Add($"{include.Where}: includes nested too deeply");
                            break;
                        }

                        string includedText;
                        try
                        {
                            includedText = ReadShared(included);
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        {
                            _warnings.Add($"{include.Where}: couldn't read {included} ({ex.Message})");
                            _hasErrors = true; // as with a file that isn't valid TOML: keep what's in force
                            continue;
                        }

                        Read(includedText, included, depth + 1);
                    }
                }
            }

            _entries.AddRange(entries.Where(e => e.Path is not ["include"]));
        }

        /// <summary>Files an include pattern names, relative to the including file; <c>*</c> matches within a folder.</summary>
        private IEnumerable<string> Expand(string pattern, string from, Entry at)
        {
            var baseDir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(from)) ?? ".";
            var full = System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDir, Environment.ExpandEnvironmentVariables(pattern)));
            var dir = System.IO.Path.GetDirectoryName(full)!;
            var name = System.IO.Path.GetFileName(full);
            if (name.Contains('*') || name.Contains('?'))
            {
                return Directory.Exists(dir) ? Directory.GetFiles(dir, name).Order(StringComparer.OrdinalIgnoreCase) : [];
            }

            if (!File.Exists(full))
            {
                _warnings.Add($"{at.Where}: can't find included file {pattern}");
                return [];
            }

            return [full];
        }

        private void Flatten(IReadOnlyList<string> prefix, KeyValueSyntax kv, string file, List<Entry> into)
        {
            if (kv.Key is not { } key || kv.Value is not { } value)
            {
                return;
            }

            var path = prefix.Concat(ConfigEditor.KeyParts(key)).ToList();
            var line = kv.Span.Start.Line + 1;
            if (value is InlineTableSyntax inline)
            {
                foreach (var item in inline.Items)
                {
                    if (item.KeyValue is { } inner)
                    {
                        Flatten(path, inner, file, into);
                    }
                }

                return;
            }

            if (ToValue(value) is { } v)
            {
                into.Add(new Entry(path, v, file, line));
            }
            else
            {
                _warnings.Add($"{System.IO.Path.GetFileName(file)}:{line}: unsupported value for {string.Join('.', path)}");
            }
        }

        private static object? ToValue(ValueSyntax value) => value switch
        {
            StringValueSyntax s => s.Value,
            IntegerValueSyntax i => i.Value,
            FloatValueSyntax f => f.Value,
            BooleanValueSyntax b => b.Value,
            ArraySyntax a => a.Items.Select(item => item.Value is { } v ? ToValue(v) : null).ToList() is var items && items.All(x => x != null)
                ? items.Cast<object>().ToList()
                : null,
            _ => null,
        };

        // --------------------------------------------------------- interpreting

        public ConfigFile Build()
        {
            string? defaultId = null;
            var global = new Config();
            var profileEntries = new Dictionary<string, List<Entry>>();
            var profileOrder = new List<string>();
            var keys = new List<(string Chord, string Action, string Where)>();
            var discovery = new DiscoverySettings();

            foreach (var e in _entries)
            {
                switch (e.Path)
                {
                    case ["default-profile"]:
                        if (e.Value is string chosen)
                        {
                            defaultId = chosen;
                        }
                        else
                        {
                            _warnings.Add($"{e.Where}: default-profile takes a profile id");
                        }

                        break;
                    case ["profile", var profileId, _, ..]:
                        if (!profileEntries.TryGetValue(profileId, out var list))
                        {
                            profileEntries[profileId] = list = [];
                            profileOrder.Add(profileId);
                        }

                        list.Add(e with { Path = e.Path.Skip(2).ToList() });
                        break;
                    case ["keys", var chord]:
                        if (e.Value is string action)
                        {
                            keys.Add((chord, action, e.Where));
                        }
                        else
                        {
                            _warnings.Add($"{e.Where}: a key's action is text, like \"split right\" or \"none\"");
                        }

                        break;
                    case ["keys", ..]:
                        _warnings.Add($"{e.Where}: put a chord in quotes: \"{string.Join('.', e.Path.Skip(1))}\" = ...");
                        break;
                    case ["discover", var source] when e.Value is bool on:
                        discovery = source switch
                        {
                            "wsl" => discovery with { Wsl = on },
                            "powershell" => discovery with { PowerShell = on },
                            "cmd" => discovery with { Cmd = on },
                            "git-bash" => discovery with { GitBash = on },
                            "msys2" => discovery with { Msys2 = on },
                            "ssh" => discovery with { Ssh = on },
                            "visual-studio" => discovery with { VisualStudio = on },
                            _ => Unknown(discovery, e),
                        };
                        break;
                    case ["discover", ..]:
                        _warnings.Add($"{e.Where}: [discover] takes wsl, powershell, cmd, git-bash, msys2, ssh or visual-studio = true or false");
                        break;
                    case [var group, _, ..] when Groups.Contains(group):
                        break; // applied below, themes first
                    default:
                        _warnings.Add($"{e.Where}: unknown setting '{e.Key}'");
                        break;
                }
            }

            global = ApplyGroups(global, _entries.Where(e => e.Path is [var g, _, ..] && Groups.Contains(g)).ToList(), inProfile: false);

            // Empty sections count too, in their place among the others.
            var sectioned = _profileSections.Concat(profileOrder).Distinct().ToList();
            foreach (var id in sectioned.Where(id => !profileEntries.ContainsKey(id)))
            {
                profileEntries[id] = [];
            }

            profileOrder = _profileSections.Count > 0
                ? sectioned.OrderBy(id => _profileSections.IndexOf(id) is var i and >= 0 ? i : int.MaxValue).ToList()
                : profileOrder;

            // Profiles found on this machine: the base for a section with the same id, else listed
            // after the file's own.
            var discovered = (discover?.Invoke(discovery) ?? []).GroupBy(d => d.Id).Select(g => g.First()).ToList();
            var found = discovered.ToDictionary(d => d.Id);

            var profiles = new List<Profile>();
            foreach (var id in profileOrder)
            {
                var entries = profileEntries[id];
                string name = id;
                var hidden = false;
                var config = global;
                if (found.Remove(id, out var basis))
                {
                    name = basis.Name;
                    config = FromDiscovery(config, basis);
                }
                else if (id.Contains(':') && !entries.Any(e => e.Path is ["command"]))
                {
                    // Changes to a found profile that isn't installed (any more): nothing to run.
                    Log.Write($"config: profile '{id}' wasn't found on this machine; hiding it");
                    hidden = true;
                }

                foreach (var e in entries)
                {
                    switch (e.Path)
                    {
                        case ["name"] when e.Value is string n && n.Length > 0: name = n; break;
                        case ["hidden"] when e.Value is bool h: hidden = h; break;
                        case ["command" or "working-directory" or "icon"] when e.Value is string s && s.Length > 0:
                            config = config.Apply(e.Path[0], s)!;
                            break;
                        case [var group, _, ..] when Groups.Contains(group):
                            break;
                        default:
                            _warnings.Add($"{e.Where}: unknown or invalid profile setting '{e.Key}'");
                            break;
                    }
                }

                config = ApplyGroups(config, entries.Where(e => e.Path is [var g, _, ..] && Groups.Contains(g)).ToList(), inProfile: true);
                profiles.Add(new Profile(id, name, config, hidden));
            }

            foreach (var d in discovered.Where(d => found.ContainsKey(d.Id)))
            {
                profiles.Add(new Profile(d.Id, d.Name, FromDiscovery(global, d)));
            }

            if (profiles.Count == 0)
            {
                profiles.Add(new Profile("default", "Default", global));
            }

            var defaultProfile = profiles.FirstOrDefault(p => p.Id == defaultId) ?? profiles.FirstOrDefault(p => !p.Hidden) ?? profiles[0];
            if (defaultId != null && defaultProfile.Id != defaultId)
            {
                _warnings.Add($"{System.IO.Path.GetFileName(mainPath)}: default-profile '{defaultId}' isn't a profile; using '{defaultProfile.Id}'");
            }

            var keybindings = Keybindings.Configure(keys, _warnings);
            return new ConfigFile(global, profiles, defaultProfile, _warnings, _hasErrors, mainPath, keybindings, _themes);
        }

        private static Config FromDiscovery(Config config, DiscoveredProfile found) =>
            config with { Command = found.Command, Icon = found.Icon ?? config.Icon };

        private DiscoverySettings Unknown(DiscoverySettings settings, Entry e)
        {
            _warnings.Add($"{e.Where}: unknown [discover] source '{e.Path[1]}'");
            return settings;
        }

        /// <summary>Applies grouped settings: a color theme first, so the colors written next to it override it.</summary>
        private Config ApplyGroups(Config config, List<Entry> entries, bool inProfile)
        {
            foreach (var e in entries.Where(e => e.Path is ["colors", "theme"]))
            {
                config = e.Value is string theme ? ApplyTheme(config, theme, e) : Warn(config, e, "a theme name");
            }

            foreach (var e in entries.Where(e => e.Path is not ["colors", "theme"]))
            {
                if (inProfile && !ProfileGroups.Contains(e.Path[0]))
                {
                    _warnings.Add($"{e.Where}: [{e.Path[0]}] applies to the whole app, not to one profile");
                    continue;
                }

                config = ApplySetting(config, e);
            }

            return config;
        }

        private Config ApplySetting(Config config, Entry e)
        {
            // Each setting maps onto the (tested) value parsing of Config.Apply.
            (string Key, string? Value)? legacy = e.Path switch
            {
                ["font", "family"] => ("font-family", e.Value as string),
                ["font", "size"] => ("font-size", Number(e.Value)),
                ["colors", "background"] => ("background", e.Value as string),
                ["colors", "foreground"] => ("foreground", e.Value as string),
                ["colors", "cursor"] => ("cursor-color", e.Value as string),
                ["colors", "palette", var index] => ("palette", e.Value is string c ? $"{index}={c}" : null),
                ["terminal", "padding-x"] => ("window-padding-x", Pair(e.Value)),
                ["terminal", "padding-y"] => ("window-padding-y", Pair(e.Value)),
                ["terminal", "mouse-pointer"] => ("mouse-pointer", e.Value as string),
                ["terminal", "clipboard-read"] => ("clipboard-read", Permission(e.Value)),
                ["terminal", "clipboard-write"] => ("clipboard-write", Permission(e.Value)),
                ["terminal", "command-marks"] => ("command-marks", e.Value as string),
                ["window", "workspace-tabs"] => ("workspace-tabs", e.Value as string),
                ["window", "title-bar"] => ("title-bar", e.Value as string),
                ["window", "sidebar"] => ("sidebar", e.Value as string),
                ["window", "tab-groups"] => ("tab-groups", Bool(e.Value)),
                ["window", "group-tab-strip"] => ("group-tab-strip", e.Value as string),
                ["window", "unfocused-split-opacity"] => ("unfocused-split-opacity", Number(e.Value)),
                ["window", "unfocused-split-fill"] => ("unfocused-split-fill", e.Value as string),
                ["window", "save-state"] => ("window-save-state", e.Value is bool b ? (b ? "always" : "never") : e.Value as string),
                ["window", "quit-after-last-window-closed"] => ("quit-after-last-window-closed", Bool(e.Value)),
                ["notify", "command-finished"] => ("notify-command-finished", e.Value as string),
                ["notify", "programs"] => ("notify-programs", e.Value as string),
                ["notify", "command-finished-after"] => ("notify-command-finished-after", e.Value is string d ? d : Number(e.Value)),
                ["quick-terminal", "position"] => ("quick-terminal-position", e.Value as string),
                ["quick-terminal", "screen"] => ("quick-terminal-screen", e.Value as string),
                ["quick-terminal", "size"] => ("quick-terminal-size", Pair(e.Value)),
                ["quick-terminal", "autohide"] => ("quick-terminal-autohide", Bool(e.Value)),
                ["quick-terminal", "hotkey"] => ("quick-terminal-hotkey", e.Value as string),
                _ => null,
            };

            // The whole palette as an array: palette = ["#000000", "#aa0000", ...].
            if (e.Path is ["colors", "palette"] && e.Value is List<object> colors)
            {
                for (var i = 0; i < colors.Count; i++)
                {
                    config = colors[i] is string c && config.Apply("palette", $"{i}={c}") is { } next ? next : Warn(config, e, "colors like \"#rrggbb\"");
                }

                return config;
            }

            if (legacy is not { } l)
            {
                _warnings.Add($"{e.Where}: unknown setting '{e.Key}'");
                return config;
            }

            return l.Value != null && config.Apply(l.Key, l.Value) is { } applied ? applied : Warn(config, e, null);
        }

        /// <summary>Applies a named color scheme: a file in the user's themes folder, else a bundled one.</summary>
        private Config ApplyTheme(Config config, string name, Entry at)
        {
            if (_themes.Apply(config, name) is { } themed)
            {
                return themed;
            }

            _warnings.Add($"{at.Where}: no color scheme named '{name}' (looked in {_themes.UserFolder} and the bundled ones)");
            return config;
        }

        private Config Warn(Config config, Entry e, string? expected)
        {
            _warnings.Add($"{e.Where}: ignored {e.Key} = {Show(e.Value)}{(expected != null ? $" (expected {expected})" : "")}");
            return config;
        }

        private static string Show(object value) => value switch
        {
            string s => $"\"{s}\"",
            bool b => b ? "true" : "false",
            List<object> l => $"[{string.Join(", ", l.Select(Show))}]",
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? "",
        };

        private static string? Number(object value) => value is long or double
            ? Convert.ToString(value, CultureInfo.InvariantCulture)
            : null;

        private static string? Bool(object value) => value is bool b ? (b ? "true" : "false") : null;

        /// <summary>true/false or "allow"/"deny".</summary>
        private static string? Permission(object value) => value switch
        {
            bool b => b ? "allow" : "deny",
            string s => s,
            _ => null,
        };

        /// <summary>One value or two (<c>[a, b]</c>), as Config.Apply's "a,b" form.</summary>
        private static string? Pair(object value) => value switch
        {
            List<object> { Count: 1 or 2 } l when l.All(x => x is long or double or string) =>
                string.Join(",", l.Select(x => Convert.ToString(x, CultureInfo.InvariantCulture))),
            long or double => Convert.ToString(value, CultureInfo.InvariantCulture),
            string s => s,
            _ => null,
        };
    }
}
