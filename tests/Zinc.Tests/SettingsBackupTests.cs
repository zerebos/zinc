using System.IO.Compression;
using Zinc.Session;
using Zinc.Settings;

namespace Zinc.Tests;

public sealed class SettingsBackupTests : IDisposable
{
    // TODO: should probably use a better eye-catcher style date
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 15, 30, 0, TimeSpan.Zero);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "zinc-backup-" + Guid.NewGuid().ToString("N"));

    public SettingsBackupTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Folder(string name)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static void Put(string folder, string relative, string text)
    {
        var path = Path.Combine(folder, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private static MemoryStream Zip(params (string Entry, string Text)[] entries)
    {
        var zip = new MemoryStream();
        using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (entry, text) in entries)
            {
                using var writer = new StreamWriter(archive.CreateEntry(entry).Open());
                writer.Write(text);
            }
        }

        zip.Position = 0;
        return zip;
    }

    [Fact]
    public void ABackupHoldsTheConfigSchemesShadersAndSessionAndNothingElse()
    {
        var config = Folder("config");
        Put(config, "config.toml", "[font]\nsize = 12\n");
        Put(config, "work.toml", "[colors]\n");
        Put(config, "profiles/ssh.toml", "[profile.x]\n");
        Put(config, "logs/old.toml", "not this");
        Put(config, "themes/Mine", "background = #000000\n");
        Put(config, "shaders/glow.glsl", "void mainImage() {}\n");
        Put(config, "shaders/more/crt.glsl", "// crt\n");
        Put(config, "logs/zinc.log", "not this");
        Put(config, "crash.log", "nor this");
        Put(config, "backups/before-import-x.zip", "nor this");
        Put(config, "session.json", "{}");

        var zip = new MemoryStream();
        var count = SettingsBackup.Write(zip, config, Path.Combine(config, "session.json"), "0.1.0-dev.1", Now);
        zip.Position = 0;
        var (contents, problem) = SettingsBackup.Read(zip);

        Assert.Null(problem);
        Assert.Equal(7, count);
        Assert.Equal(["config.toml", "profiles/ssh.toml", "work.toml", "themes/Mine", "shaders/glow.glsl", "shaders/more/crt.glsl"], contents!.Files);
        Assert.True(contents.HasSession);
        Assert.Equal(1, contents.Count("themes"));
        Assert.Equal(2, contents.Count("shaders"));
        Assert.Equal("0.1.0-dev.1", contents.Version);
        Assert.Equal(Now, contents.Made);
    }

    [Fact]
    public void ImportSavesWhatWasThereFirstThenWritesTheBackupsFilesOverAndKeepsTheRest()
    {
        var from = Folder("from");
        Put(from, "config.toml", "[font]\nsize = 14\n");
        Put(from, "themes/Theirs", "background = #111111\n");
        var zip = new MemoryStream();
        SettingsBackup.Write(zip, from, null, "v", Now);
        zip.Position = 0;

        var to = Folder("to");
        Put(to, "config.toml", "[font]\nsize = 9\n");
        Put(to, "themes/Mine", "background = #222222\n");

        var result = SettingsBackup.Import(zip, to, null, "v", Now);

        Assert.Equal(2, result.Files);
        Assert.False(result.Session);
        Assert.Equal("[font]\nsize = 14\n", File.ReadAllText(Path.Combine(to, "config.toml")));
        Assert.True(File.Exists(Path.Combine(to, "themes", "Theirs")));
        Assert.True(File.Exists(Path.Combine(to, "themes", "Mine"))); // not in the backup: stays
        Assert.Equal(Path.Combine(to, "backups", "before-import-20261007-153000.zip"), result.SafetyCopy);

        // The copy made first holds the config as it was.
        using var safety = File.OpenRead(result.SafetyCopy);
        using var archive = new ZipArchive(safety);
        using var reader = new StreamReader(archive.GetEntry("config.toml")!.Open());
        Assert.Equal("[font]\nsize = 9\n", reader.ReadToEnd());
    }

    [Fact]
    public void AnImportedSessionWaitsForTheNextStart()
    {
        var to = Folder("to");
        var session = Path.Combine(to, "session.json");
        File.WriteAllText(session, "current");

        var result = SettingsBackup.Import(Zip(("config.toml", ""), ("session.json", "imported")), to, session, "v", Now);

        Assert.True(result.Session);
        Assert.Equal("current", File.ReadAllText(session)); // the running app keeps saving its own
        Assert.True(SessionStore.TakeImported(session));
        Assert.Equal("imported", File.ReadAllText(session));
        Assert.False(File.Exists(SettingsBackup.TakenAtStart(session)));
        Assert.False(SessionStore.TakeImported(session));
    }

    [Fact]
    public void AnImportedSessionThatCantBeTakenDoesntStopTheStart()
    {
        var session = Path.Combine(Folder("locked"), "session.json");
        File.WriteAllText(session, "current");
        File.WriteAllText(SettingsBackup.TakenAtStart(session), "imported");
        File.SetAttributes(session, FileAttributes.ReadOnly); // File.Move over it: UnauthorizedAccessException
        try
        {
            Assert.False(SessionStore.TakeImported(session));
            Assert.Equal("current", File.ReadAllText(session));
        }
        finally
        {
            File.SetAttributes(session, FileAttributes.Normal);
        }
    }

    [Fact]
    public void ARunWithoutASessionImportsEverythingButTheSession()
    {
        var to = Folder("to");
        var result = SettingsBackup.Import(Zip(("config.toml", "x"), ("session.json", "s")), to, null, "v", Now);
        Assert.Equal(1, result.Files);
        Assert.False(result.Session);
        Assert.Empty(Directory.GetFiles(to, "session.json*"));
    }

    [Theory]
    [InlineData("config.toml", true)]
    [InlineData("work.TOML", true)]
    [InlineData("session.json", true)]
    [InlineData("themes/Mine", true)]
    [InlineData("shaders/a/b.glsl", true)]
    [InlineData("../config.toml", false)]
    [InlineData("themes/../../evil", false)]
    [InlineData("/config.toml", false)]
    [InlineData("C:/Windows/evil.toml", false)]
    [InlineData("themes\\Mine", false)]
    [InlineData("logs/zinc.log", false)]
    [InlineData("zinc.exe", false)]
    [InlineData("profiles/ssh.toml", true)]
    [InlineData("backups/x.toml", false)]
    [InlineData("logs/x.toml", false)]
    [InlineData("profiles/x.exe", false)]
    [InlineData("themes//x", false)]
    [InlineData("themes/CON", false)] // names Windows can't make: an import would stop halfway
    [InlineData("themes/nul.txt", false)]
    [InlineData("themes/a<b", false)]
    [InlineData("themes/trailing.", false)]
    [InlineData("themes/CONSOLE", true)]
    public void OnlyFilesInsideTheConfigFolderComeBack(string entry, bool safe) =>
        Assert.Equal(safe, SettingsBackup.IsSafe(entry));

    [Fact]
    public void ZipsThatArentBackupsAreRefusedBeforeAnythingChanges()
    {
        Assert.Equal("It isn't a Zinc settings backup.", SettingsBackup.Read(Zip(("readme.txt", "hi"))).Problem);
        Assert.Equal("It isn't a zip file, or it's damaged.", SettingsBackup.Read(new MemoryStream([1, 2, 3])).Problem);

        var to = Folder("to");
        Assert.Throws<InvalidDataException>(() => SettingsBackup.Import(Zip(("../x.toml", "")), to, null, "v", Now));
        Assert.Empty(Directory.GetFileSystemEntries(to)); // no safety copy, nothing written
    }

    [Fact]
    public void FilesZincDoesntRestoreAreLeftOut()
    {
        // Despite a newer Zinc's backup, a README someone added and a path out of the folder, the rest still comes back.
        var backup = Zip(("zinc-backup.json", "{}"), ("config.toml", "a"), ("README.txt", "hi"), ("keys/x.json", "k"), ("../evil.toml", "e"));
        Assert.Equal(["config.toml"], SettingsBackup.Read(backup).Contents!.Files);

        var to = Folder("to");
        var result = SettingsBackup.Import(backup, Path.Combine(to, "config"), null, "v", Now);
        Assert.Equal((1, 3), (result.Files, result.Skipped));
        Assert.Equal("a", File.ReadAllText(Path.Combine(to, "config", "config.toml")));
        Assert.False(File.Exists(Path.Combine(to, "evil.toml")));
        Assert.False(Directory.Exists(Path.Combine(to, "config", "keys")));
    }

    [Fact]
    public void TwoImportsInTheSameSecondKeepBothSafetyCopies()
    {
        var config = Folder("config");
        Put(config, "config.toml", "mine");
        var first = SettingsBackup.Import(Zip(("config.toml", "one")), config, null, "v", Now).SafetyCopy;
        var second = SettingsBackup.Import(Zip(("config.toml", "two")), config, null, "v", Now).SafetyCopy;

        Assert.NotEqual(first, second);
        Assert.EndsWith("before-import-20261007-153000-2.zip", second);
        using var original = new ZipArchive(File.OpenRead(first));
        using var reader = new StreamReader(original.GetEntry("config.toml")!.Open());
        Assert.Equal("mine", reader.ReadToEnd()); // the first copy, still the original settings
    }

    [Fact]
    public void AnImportThatStopsPartwaySaysWhereTheOldSettingsAre()
    {
        var config = Folder("config");
        Put(config, "config.toml", "mine");
        Put(config, "themes/Locked", "old");
        // Open in another app that lets it be read (the safety copy) but not replaced
        using (new FileStream(Path.Combine(config, "themes", "Locked"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var failed = Assert.Throws<ImportFailedException>(() =>
                SettingsBackup.Import(Zip(("config.toml", "new"), ("themes/Locked", "x")), config, null, "v", Now));
            Assert.Equal(("themes/Locked", 1), (failed.Entry, failed.Written));
            Assert.True(File.Exists(failed.SafetyCopy));
        }

        Assert.Equal("new", File.ReadAllText(Path.Combine(config, "config.toml"))); // written before it stopped
        Assert.Empty(Directory.GetFiles(config, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public void ZincCoreDoesntNeedTheZipFileAssembly()
    {
        // A trimmed publish leaves it out of zinc.deps.json, so it can't load there
        Assert.DoesNotContain(typeof(SettingsBackup).Assembly.GetReferencedAssemblies(), a => a.Name == "System.IO.Compression.ZipFile");
    }

    [Fact]
    public void AnExportNamesTheFilesItLeavesOut()
    {
        var config = Folder("config");
        var dotfiles = Folder("dotfiles");
        Put(config, "profiles/work.toml", "");
        Put(config, "shaders/mine.glsl", "");
        Put(config, "loose.glsl", "");
        Put(dotfiles, "keys.toml", "");
        Put(dotfiles, "crt.glsl", "");
        var text = $"include = [\"profiles/*.toml\", '{dotfiles}\\keys.toml']\n" +
            $"[shaders]\nfiles = [\"shaders/mine.glsl\", \"loose.glsl\", '{dotfiles}\\crt.glsl']\nfolders = [\"shaders\", '{dotfiles}']\n";
        File.WriteAllText(Path.Combine(config, "config.toml"), text);

        var parsed = ConfigFile.Parse(text, Path.Combine(config, "config.toml"));
        Assert.Empty(parsed.Warnings);
        Assert.Equal(
            [Path.Combine(config, "loose.glsl"), dotfiles, Path.Combine(dotfiles, "crt.glsl"), Path.Combine(dotfiles, "keys.toml")],
            SettingsBackup.LeftOut(parsed));
    }
}
