using Microsoft.Win32;
using Zinc.Pty;

namespace Zinc.Tests;

/// <summary>The installer's side: the uninstall cleanup (against a scratch key standing in for HKCU) and moving old data.</summary>
public sealed class InstallTests : IDisposable
{
    private const string Canary = "{A854D02A-F2FE-44A5-BB24-D03F4CF830D4}", CanaryTerminal = "{1706609C-A4CE-4C0D-B7D2-C19BF66398A5}";
    private const string Installed = @"C:\Users\Someone\AppData\Local\Zinc";
    private readonly string _path = $@"Software\Zinc.Tests\{Guid.NewGuid():N}";
    private readonly RegistryKey _root;
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "zinc-tests-" + Guid.NewGuid().ToString("N"));

    public InstallTests() => _root = Registry.CurrentUser.CreateSubKey(_path);

    public void Dispose()
    {
        _root.Dispose();
        Registry.CurrentUser.DeleteSubKeyTree(_path, throwOnMissingSubKey: false);
        if (Directory.Exists(_temp))
        {
            Directory.Delete(_temp, recursive: true);
        }
    }

    private void SetRun(string command)
    {
        using var run = _root.CreateSubKey(InstallCleanup.RunKey);
        run.SetValue(InstallCleanup.RunValue, command);
    }

    private string? Run() => _root.OpenSubKey(InstallCleanup.RunKey)?.GetValue(InstallCleanup.RunValue) as string;

    private string? Terminal() => _root.OpenSubKey(@"Console\%%Startup")?.GetValue("DelegationTerminal") as string;

    private void MakeDefault(string exe)
    {
        using (var startup = _root.CreateSubKey(@"Console\%%Startup"))
        {
            startup.SetValue("DelegationConsole", Canary);
            startup.SetValue("DelegationTerminal", CanaryTerminal);
        }

        Assert.True(new DefaultTerminal(_root).Enable(exe));
    }

    [Fact]
    public void UninstallTakesBackWhatPointsIntoTheInstall()
    {
        var exe = Installed + @"\current\zinc.exe";
        SetRun($"\"{exe}\" --at-login");
        MakeDefault(exe);

        var done = InstallCleanup.Uninstall(_root, Installed);

        Assert.Equal(2, done.Count);
        Assert.Null(Run());
        Assert.Equal(CanaryTerminal, Terminal());
        Assert.Null(new DefaultTerminal(_root).RegisteredExe());
    }

    [Fact]
    public void UninstallLeavesAnotherCopysRegistrationsAlone()
    {
        // The dev install (versioned folders under Programs\Zinc) owns them.
        var exe = @"C:\Users\Someone\AppData\Local\Programs\Zinc\versions\20261002-0.1.0\zinc.exe";
        SetRun($"\"{exe}\" --at-login");
        MakeDefault(exe);

        Assert.Empty(InstallCleanup.Uninstall(_root, Installed));
        Assert.Equal($"\"{exe}\" --at-login", Run());
        Assert.Equal(DefaultTerminal.State.Zinc, new DefaultTerminal(_root).Current());
        Assert.Equal(exe, new DefaultTerminal(_root).RegisteredExe());
    }

    [Fact]
    public void ExplorerMenuAddsOpenInZincForFoldersBackgroundsAndDrives()
    {
        var exe = Installed + @"\current\zinc.exe";
        var menu = new Zinc.Shell.ExplorerMenu(_root);
        Assert.True(menu.Apply(exe));
        Assert.False(menu.Apply(exe)); // already so
        string? Command(string key) => _root.OpenSubKey($@"Software\Classes\{key}\shell\Zinc\command")?.GetValue(null) as string;
        Assert.Equal($"\"{exe}\" --folder \"%V\"", Command("Directory"));
        Assert.Equal($"\"{exe}\" --folder \"%V\"", Command(@"Directory\Background"));
        Assert.Equal($"\"{exe}\" --folder \"%V.\"", Command("Drive")); // "C:\" would end in an escaped quote
        Assert.Equal("Open in Zinc", _root.OpenSubKey(@"Software\Classes\Directory\shell\Zinc")?.GetValue(null));
        Assert.Equal(exe, menu.RegisteredExe());

        Assert.True(menu.Apply(null));
        Assert.Null(Command("Directory"));
        Assert.Null(Command("Drive"));
        Assert.Null(menu.RegisteredExe());
    }

    [Fact]
    public void UninstallTakesBackTheExplorerMenuOnlyWhenItStartsThisInstall()
    {
        var other = @"C:\Users\Someone\AppData\Local\Programs\Zinc\versions\20261002-0.1.0\zinc.exe";
        new Zinc.Shell.ExplorerMenu(_root).Apply(other);
        Assert.Empty(InstallCleanup.Uninstall(_root, Installed));
        Assert.Equal(other, new Zinc.Shell.ExplorerMenu(_root).RegisteredExe());

        new Zinc.Shell.ExplorerMenu(_root).Apply(Installed + @"\current\zinc.exe");
        Assert.Equal(["removed Open in Zinc from Explorer's menu"], InstallCleanup.Uninstall(_root, Installed));
        Assert.Null(new Zinc.Shell.ExplorerMenu(_root).RegisteredExe());
    }

    [Fact]
    public void UninstallWithNothingRegisteredDoesNothing()
    {
        Assert.Empty(InstallCleanup.Uninstall(_root, Installed));
    }

    [Theory]
    [InlineData(@"C:\Users\Someone\AppData\Local\Zinc\current\zinc.exe", true)]
    [InlineData(@"c:\users\someone\appdata\local\zinc\CURRENT\Zinc.exe", true)]
    [InlineData(@"C:\Users\Someone\AppData\Local\Zinc.Test\current\zinc.exe", false)] // a sibling whose name starts the same
    [InlineData(@"C:\Users\Someone\AppData\Local\Zinc", false)]                        // the folder itself isn't inside it
    [InlineData(null, false)]
    public void InsideMeansUnderTheWholeFolderName(string? path, bool inside)
    {
        Assert.Equal(inside, InstallCleanup.IsInside(path, Installed));
    }

    [Theory]
    [InlineData("\"C:\\A B\\zinc.exe\" --at-login", @"C:\A B\zinc.exe")]
    [InlineData("C:\\Zinc\\zinc.exe -Embedding", @"C:\Zinc\zinc.exe")]
    [InlineData("C:\\Zinc\\zinc.exe", @"C:\Zinc\zinc.exe")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void ExeOfACommandLine(string? command, string? exe)
    {
        Assert.Equal(exe, InstallCleanup.ExeOf(command));
    }

    [Fact]
    public void OldLocalDataMovesAcrossAndTheEmptiedFolderGoes()
    {
        var from = Path.Combine(_temp, "Local", "Zinc");
        var to = Path.Combine(_temp, "Roaming", "Zinc");
        Directory.CreateDirectory(Path.Combine(from, "logs"));
        Directory.CreateDirectory(Path.Combine(from, "cache", "icons"));
        File.WriteAllText(Path.Combine(from, "logs", "zinc.log"), "log");
        File.WriteAllText(Path.Combine(from, "crash.log"), "crash");
        File.WriteAllText(Path.Combine(from, "cache", "icons", "a.png"), "png");
        Directory.CreateDirectory(to);
        File.WriteAllText(Path.Combine(to, "config.toml"), "");

        Assert.Equal(3, AppFolders.MoveOldLocalData(from, to));

        Assert.Equal("log", File.ReadAllText(Path.Combine(to, "logs", "zinc.log")));
        Assert.Equal("crash", File.ReadAllText(Path.Combine(to, "crash.log")));
        Assert.True(File.Exists(Path.Combine(to, "cache", "icons", "a.png")));
        Assert.True(File.Exists(Path.Combine(to, "config.toml")));
        Assert.False(Directory.Exists(from));
    }

    [Fact]
    public void AnInstallInTheOldFolderIsNeverTouched()
    {
        // Once the installer owns %LOCALAPPDATA%\Zinc, only the old items may move; the install stays.
        var from = Path.Combine(_temp, "Local", "Zinc");
        var to = Path.Combine(_temp, "Roaming", "Zinc");
        Directory.CreateDirectory(Path.Combine(from, "current"));
        File.WriteAllText(Path.Combine(from, "current", "zinc.exe"), "exe");
        File.WriteAllText(Path.Combine(from, "Update.exe"), "update");
        File.WriteAllText(Path.Combine(from, "crash.log"), "crash");

        Assert.Equal(1, AppFolders.MoveOldLocalData(from, to));

        Assert.True(File.Exists(Path.Combine(from, "current", "zinc.exe")));
        Assert.True(File.Exists(Path.Combine(from, "Update.exe")));
        Assert.False(File.Exists(Path.Combine(from, "crash.log")));
    }

    [Fact]
    public void AFileAlreadyAtTheNewPlaceWins()
    {
        var from = Path.Combine(_temp, "Local", "Zinc");
        var to = Path.Combine(_temp, "Roaming", "Zinc");
        Directory.CreateDirectory(from);
        Directory.CreateDirectory(to);
        File.WriteAllText(Path.Combine(from, "crash.log"), "old");
        File.WriteAllText(Path.Combine(to, "crash.log"), "new");

        Assert.Equal(0, AppFolders.MoveOldLocalData(from, to));

        Assert.Equal("new", File.ReadAllText(Path.Combine(to, "crash.log")));
        Assert.False(Directory.Exists(from));
    }

    [Fact]
    public void NoOldFolderIsFine()
    {
        Assert.Equal(0, AppFolders.MoveOldLocalData(Path.Combine(_temp, "missing"), Path.Combine(_temp, "to")));
    }
}
