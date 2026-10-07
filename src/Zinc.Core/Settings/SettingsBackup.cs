using System.IO.Compression;
using System.Text.Json;

namespace Zinc.Settings;

/// <summary>What a backup holds: its files (config, color schemes, shaders), whether it has a session, and its manifest.</summary>
public sealed record BackupContents(IReadOnlyList<string> Files, bool HasSession, string? Version, DateTimeOffset? Made)
{
    public int Count(string folder) => Files.Count(f => f.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase));
}

/// <summary>What an import did: files written, the session kept for the next start, and the backup made first.</summary>
public sealed record ImportResult(int Files, bool Session, string SafetyCopy);

/// <summary>
/// Settings backup (Settings > About): one zip with config.toml (and other .toml files under its folder, for includes), the
/// user's color schemes (themes\) and shaders (shaders\), and the session (session.json). Paths inside are relative to
/// the config folder, with forward slashes. An import first saves what's there into backups\, then writes the
/// backup's files over (files it doesn't have stay); its session waits next to session.json for the next start
/// (<see cref="Zinc.Session.SessionStore.TakeImported"/>), since the running app keeps saving its own.
/// </summary>
public static class SettingsBackup
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
            string? unsafeEntry = null;
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

                unsafeEntry ??= IsSafe(entry.FullName) ? null : entry.FullName;
                files.Add(entry.FullName);
            }

            if (!manifest && !files.Contains("config.toml"))
            {
                return (null, "It isn't a Zinc settings backup.");
            }

            if (unsafeEntry != null)
            {
                return (null, $"It has a file Zinc doesn't restore: {unsafeEntry}");
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
        var safety = Path.Combine(configFolder, BackupsFolder, $"before-import-{now:yyyyMMdd-HHmmss}.zip");
        using (var copy = new FileStream(safety, FileMode.Create, FileAccess.Write))
        {
            Write(copy, configFolder, sessionPath, version, now);
        }

        zip.Position = 0;
        using var archive = new ZipArchive(zip, ZipArchiveMode.Read, leaveOpen: true);
        var written = 0;
        var session = false;
        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.EndsWith('/') || entry.FullName == Manifest || !IsSafe(entry.FullName))
            {
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
                written++;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            // Through a temp file: the config watcher may read config.toml the moment it changes.
            var temp = target + ".tmp";
            entry.ExtractToFile(temp, overwrite: true);
            File.Move(temp, target, overwrite: true);
        }

        return new ImportResult(written, session, safety);
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

        var parts = entry.Split('/');
        if (parts.Any(p => p is "" or "." or ".."))
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
}
