using System.IO.Compression;
using System.Text.Json;

namespace Zinc.Settings;

/// <summary>What a backup holds: its files (config, color schemes, shaders), whether it has a session, and its manifest.</summary>
public sealed record BackupContents(IReadOnlyList<string> Files, bool HasSession, string? Version, DateTimeOffset? Made)
{
    public int Count(string folder) => Files.Count(f => f.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// What an import did: files written, the session kept for the next start, the backup made first, and files in the
/// backup it doesn't restore (a newer Zinc's, or not settings).
/// </summary>
public sealed record ImportResult(int Files, bool Session, string SafetyCopy, int Skipped = 0);

/// <summary>An import that stopped after replacing <see cref="Written"/> files: the settings from before are in <see cref="SafetyCopy"/>.</summary>
public sealed class ImportFailedException(string entry, int written, string safetyCopy, Exception inner)
    : IOException($"couldn't write {entry} ({inner.Message.TrimEnd('.', ' ')})", inner)
{
    public string Entry { get; } = entry;
    public int Written { get; } = written;
    public string SafetyCopy { get; } = safetyCopy;
}

/// <summary>
/// Settings backup (Settings > About): one zip with config.toml (and other .toml files under its folder, for includes), the
/// user's color schemes (themes\) and shaders (shaders\), and the session (session.json). Paths inside are relative to
/// the config folder, with forward slashes. An import first saves what's there into backups\, then writes the
/// backup's files over (files it doesn't have stay); its session waits next to session.json for the next start
/// (<see cref="Zinc.Session.SessionStore.TakeImported"/>), since the running app keeps saving its own.
/// </summary>
public static partial class SettingsBackup
{
    public const string Manifest = "zinc-backup.json";
    public const string SessionEntry = "session.json";
    public const string BackupsFolder = "backups";

    private static readonly string[] Folders = ["themes", "shaders"];

    /// <summary>Folders Zinc writes itself next to the config: never in a backup.</summary>
    private static readonly string[] NotSettings = [BackupsFolder, "logs", "cache"];

    /// <summary>The files a backup of <paramref name="configFolder"/> holds: entry name and full path.</summary>
    public static IReadOnlyList<(string Entry, string Path)> Gather(string configFolder, string? sessionPath)
    {
        var files = new List<(string, string)>();
        if (Directory.Exists(configFolder))
        {
            // .toml files anywhere (includes like profiles/*.toml), except in folders Zinc writes itself.
            foreach (var toml in Directory.GetFiles(configFolder, "*.toml", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase))
            {
                var entry = Path.GetRelativePath(configFolder, toml).Replace('\\', '/');
                if (IsSafe(entry) && !Folders.Contains(entry.Split('/')[0], StringComparer.OrdinalIgnoreCase))
                {
                    files.Add((entry, toml));
                }
            }

            foreach (var folder in Folders)
            {
                var root = Path.Combine(configFolder, folder);
                if (Directory.Exists(root))
                {
                    foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase))
                    {
                        files.Add((folder + "/" + Path.GetRelativePath(root, file).Replace('\\', '/'), file));
                    }
                }
            }
        }

        if (sessionPath != null && File.Exists(sessionPath))
        {
            files.Add((SessionEntry, sessionPath));
        }

        return files;
    }

    /// <summary>Writes a backup into <paramref name="zip"/>; returns how many files it holds.</summary>
    public static int Write(Stream zip, string configFolder, string? sessionPath, string version, DateTimeOffset now)
    {
        var files = Gather(configFolder, sessionPath);
        using var archive = new ZipArchive(zip, ZipArchiveMode.Create, leaveOpen: true);
        foreach (var (entry, path) in files)
        {
            // Shared reads: the session and the config may be mid-save by the app or an editor.
            using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var target = archive.CreateEntry(entry, CompressionLevel.Optimal).Open();
            source.CopyTo(target);
        }

        // Written by hand: reflection-based JSON is off in trimmed publishes.
        using (var stream = archive.CreateEntry(Manifest).Open())
        using (var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            json.WriteStartObject();
            json.WriteString("app", "Zinc");
            json.WriteString("version", version);
            json.WriteString("made", now);
            json.WriteEndObject();
        }

        return files.Count;
    }

    /// <summary>
    /// What's in a backup, or why it isn't one: null contents with a reason for a zip that isn't Zinc's (no manifest,
    /// no config.toml) or that names a place outside the config folder.
    /// </summary>
    public static (BackupContents? Contents, string? Problem) Read(Stream zip)
    {
        try
        {
            using var archive = new ZipArchive(zip, ZipArchiveMode.Read, leaveOpen: true);
            var files = new List<string>();
            string? version = null;
            DateTimeOffset? made = null;
            var manifest = false;
            foreach (var entry in archive.Entries)
            {
                if (entry.FullName.EndsWith('/'))
                {
                    continue; // a folder entry
                }

                if (entry.FullName == Manifest)
                {
                    manifest = true;
                    (version, made) = ReadManifest(entry);
                    continue;
                }

                // Anything else is left out, not refused: a newer Zinc's backup may hold more, or a README was added.
                if (IsSafe(entry.FullName))
                {
                    files.Add(entry.FullName);
                }
            }

            if (!manifest && !files.Contains("config.toml"))
            {
                return (null, "It isn't a Zinc settings backup.");
            }

            return (new BackupContents(files.Where(f => f != SessionEntry).ToList(), files.Contains(SessionEntry), version, made), null);
        }
        catch (InvalidDataException)
        {
            return (null, "It isn't a zip file, or it's damaged.");
        }
    }

    /// <summary>
    /// Restores a backup: what's there now goes into backups\before-import-&lt;time&gt;.zip first, then the backup's files
    /// are written (folders made as needed); its session goes to <paramref name="sessionPath"/> + ".import" for the
    /// next start (left out when <paramref name="sessionPath"/> is null: a run that keeps no session).
    /// </summary>
    public static ImportResult Import(Stream zip, string configFolder, string? sessionPath, string version, DateTimeOffset now)
    {
        var (contents, problem) = Read(zip);
        if (contents == null)
        {
            throw new InvalidDataException(problem);
        }

        Directory.CreateDirectory(Path.Combine(configFolder, BackupsFolder));
        var safety = SafetyCopyPath(configFolder, now);
        using (var copy = new FileStream(safety, FileMode.CreateNew, FileAccess.Write))
        {
            Write(copy, configFolder, sessionPath, version, now);
        }

        zip.Position = 0;
        using var archive = new ZipArchive(zip, ZipArchiveMode.Read, leaveOpen: true);
        var written = 0;
        var skipped = 0;
        var session = false;
        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.EndsWith('/') || entry.FullName == Manifest)
            {
                continue;
            }

            if (!IsSafe(entry.FullName))
            {
                skipped++;
                continue;
            }

            string target;
            if (entry.FullName == SessionEntry)
            {
                if (sessionPath == null)
                {
                    continue;
                }

                target = TakenAtStart(sessionPath);
                session = true;
            }
            else
            {
                target = Path.Combine(configFolder, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
            }

            // Through a temp file: the config watcher may read config.toml the moment it changes.
            var temp = target + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(temp, overwrite: true);
                File.Move(temp, target, overwrite: true);
                written += entry.FullName == SessionEntry ? 0 : 1;
            }
            catch (Exception ex)
            {
                // Some files may be replaced already (a locked file, a damaged entry), what was there before is in the safety copy.
                try { File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                throw new ImportFailedException(entry.FullName, written, safety, ex);
            }
        }

        return new ImportResult(written, session, safety, skipped);
    }

    /// <summary>backups\before-import-&lt;time&gt;.zip, numbered if one from the same second is there (it must never be replaced).</summary>
    private static string SafetyCopyPath(string configFolder, DateTimeOffset now)
    {
        var name = Path.Combine(configFolder, BackupsFolder, $"before-import-{now:yyyyMMdd-HHmmss}");
        var path = name + ".zip";
        for (var n = 2; File.Exists(path); n++)
        {
            path = $"{name}-{n}.zip";
        }

        return path;
    }

    /// <summary>Where an imported session waits for the next start.</summary>
    public static string TakenAtStart(string sessionPath) => sessionPath + ".import";

    /// <summary>
    /// An entry Zinc restores: a .toml beside the config, or a file under themes/ or shaders/, or the session; relative,
    /// forward slashes, no "." or ".." parts, no drive or colon (a zip can name any path; this keeps it in the folder).
    /// </summary>
    public static bool IsSafe(string entry)
    {
        if (entry.Length == 0 || entry.Contains('\\') || entry.Contains(':') || entry.StartsWith('/'))
        {
            return false;
        }

        // Nor names Windows can't make (CON, a trailing dot, <>|?*), which would stop an import halfway.
        var parts = entry.Split('/');
        if (parts.Any(p => p is "" or "." or ".." || p.EndsWith('.') || p.EndsWith(' ') || p.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            ReservedName().IsMatch(p)))
        {
            return false;
        }

        if (parts.Length == 1 && entry == SessionEntry)
        {
            return true;
        }

        // Config files anywhere but Zinc's own folders; anything under themes/ and shaders/.
        return Folders.Contains(parts[0], StringComparer.OrdinalIgnoreCase) ||
            (entry.EndsWith(".toml", StringComparison.OrdinalIgnoreCase) && !NotSettings.Contains(parts[0], StringComparer.OrdinalIgnoreCase));
    }

    private static (string?, DateTimeOffset?) ReadManifest(ZipArchiveEntry entry)
    {
        try
        {
            using var stream = entry.Open();
            using var doc = JsonDocument.Parse(stream);
            var root = doc.RootElement;
            var version = root.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            DateTimeOffset? made = root.TryGetProperty("made", out var m) && m.ValueKind == JsonValueKind.String && m.TryGetDateTimeOffset(out var when) ? when : null;
            return (version, made);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])(\..*)?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex ReservedName();

    /// <summary>
    /// Files and folders <paramref name="config"/> uses that a backup of its folder leaves out (full paths): includes,
    /// shaders and shader folders elsewhere (<c>~/dotfiles/keys.toml</c>), for the export to name. Zinc's own shaders
    /// aren't settings.
    /// </summary>
    public static IReadOnlyList<string> LeftOut(ConfigFile config)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(config.Path))!;
        var configs = new[] { config.Global }.Concat(config.Profiles.Select(p => p.Config)).ToList();
        var files = config.Files.Concat(configs.SelectMany(c => c.ShaderFiles).Where(s => !Zinc.Shaders.ShaderPaths.IsBundled(s)));
        var folders = configs.SelectMany(c => c.ShaderFolders.Split('\n', StringSplitOptions.RemoveEmptyEntries));

        // In the backup: what Gather takes (a folder: what's in it, as "<folder>/x" would be).
        bool Kept(string path, string suffix)
        {
            var entry = Path.GetRelativePath(folder, Path.GetFullPath(path)).Replace('\\', '/');
            return !Path.IsPathFullyQualified(entry) && IsSafe(entry + suffix);
        }

        return files.Where(f => !Kept(f, "")).Concat(folders.Where(f => !Kept(f, "/x")))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
