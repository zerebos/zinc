using System.Text.RegularExpressions;

namespace Phantom;

/// <summary>A named set of settings: the global settings with the section's lines applied on top.</summary>
public sealed record Profile(string Name, Config Config);

/// <summary>
/// The config file: global <c>key = value</c> lines, then optional profile sections.
/// <code>
/// font-family = JetBrainsMono NF
/// default-profile = Fedora
///
/// [profile "Fedora"]
/// command = wsl.exe -d Fedora
/// font-size = 11
/// </code>
/// Global lines may appear anywhere outside a section and apply to every profile;
/// section lines override them for that profile only. Interim format: a Phantom-specific
/// format (possibly with profiles in separate files) is planned.
/// </summary>
public sealed partial class ConfigFile
{
    private ConfigFile(Config global, IReadOnlyList<Profile> profiles, Profile defaultProfile, IReadOnlyList<string> warnings)
    {
        Global = global;
        Profiles = profiles;
        DefaultProfile = defaultProfile;
        Warnings = warnings;
    }

    /// <summary>Settings outside any profile section.</summary>
    public Config Global { get; }

    /// <summary>Profiles in file order; never empty (an implicit "Default" profile if none are defined).</summary>
    public IReadOnlyList<Profile> Profiles { get; }

    public Profile DefaultProfile { get; }

    /// <summary>Problems found while parsing, as "config:LINE: message".</summary>
    public IReadOnlyList<string> Warnings { get; }

    public static ConfigFile Load(string? path = null)
    {
        path ??= Environment.GetEnvironmentVariable("PHANTOM_CONFIG") is { Length: > 0 } fromEnv ? fromEnv : Config.DefaultPath;
        return Parse(File.Exists(path) ? File.ReadAllLines(path) : []);
    }

    public static ConfigFile Parse(IReadOnlyList<string> lines)
    {
        var warnings = new List<string>();
        var globalLines = new List<(int Number, string Key, string Value)>();
        var sections = new List<(string Name, List<(int Number, string Key, string Value)> Lines)>();
        string? defaultName = null;

        for (var i = 0; i < lines.Count; i++)
        {
            var number = i + 1;
            var line = lines[i].Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith('['))
            {
                var header = SectionHeader().Match(line);
                if (!header.Success)
                {
                    warnings.Add($"config:{number}: expected [profile \"Name\"]");
                    // Skip the malformed section's lines rather than applying them globally.
                    sections.Add(("", []));
                    continue;
                }

                var name = header.Groups["quoted"].Success ? header.Groups["quoted"].Value : header.Groups["bare"].Value;
                if (sections.Any(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                {
                    warnings.Add($"config:{number}: duplicate profile \"{name}\"; later sections are ignored");
                    sections.Add(("", []));
                    continue;
                }

                sections.Add((name, []));
                continue;
            }

            var eq = line.IndexOf('=');
            if (eq < 0)
            {
                warnings.Add($"config:{number}: expected 'key = value'");
                continue;
            }

            var key = line[..eq].Trim();
            var value = Config.Unquote(line[(eq + 1)..].Trim());
            if (key == "default-profile")
            {
                if (sections.Count > 0)
                {
                    warnings.Add($"config:{number}: default-profile belongs outside profile sections");
                }
                else
                {
                    defaultName = value;
                }

                continue;
            }

            (sections.Count == 0 ? globalLines : sections[^1].Lines).Add((number, key, value));
        }

        var global = ApplyAll(new Config(), globalLines, warnings);
        var profiles = sections
            .Where(s => s.Name.Length > 0)
            .Select(s => new Profile(s.Name, ApplyAll(global, s.Lines, warnings)))
            .ToList();

        if (profiles.Count == 0)
        {
            profiles.Add(new Profile("Default", global));
        }

        var defaultProfile = profiles[0];
        if (defaultName != null)
        {
            var match = profiles.FirstOrDefault(p => p.Name.Equals(defaultName, StringComparison.OrdinalIgnoreCase));
            if (match == null)
            {
                warnings.Add($"config: default-profile \"{defaultName}\" does not exist; using \"{defaultProfile.Name}\"");
            }
            else
            {
                defaultProfile = match;
            }
        }

        return new ConfigFile(global, profiles, defaultProfile, warnings);
    }

    private static Config ApplyAll(Config start, List<(int Number, string Key, string Value)> lines, List<string> warnings)
    {
        var config = start;
        foreach (var (number, key, value) in lines)
        {
            if (config.Apply(key, value) is { } next)
            {
                config = next;
            }
            else
            {
                warnings.Add($"config:{number}: ignored '{key} = {value}'");
            }
        }

        return config;
    }

    [GeneratedRegex("""^\[\s*profile\s+(?:"(?<quoted>[^"]+)"|(?<bare>[^\s"\]]+))\s*\]$""")]
    private static partial Regex SectionHeader();
}
