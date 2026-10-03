using Microsoft.Win32;
using Zinc.Pty;

namespace Zinc.Tests;

/// <summary>zn's folder on the user's PATH ([cli] path), against a scratch key standing in for HKCU.</summary>
public sealed class UserPathTests : IDisposable
{
    private const string Installed = @"C:\Users\Someone\AppData\Local\Zinc";
    private readonly string _path = $@"Software\Zinc.Tests\{Guid.NewGuid():N}";
    private readonly RegistryKey _root;

    public UserPathTests() => _root = Registry.CurrentUser.CreateSubKey(_path);

    public void Dispose()
    {
        _root.Dispose();
        Registry.CurrentUser.DeleteSubKeyTree(_path, throwOnMissingSubKey: false);
    }

    private void SetPath(string value, RegistryValueKind kind = RegistryValueKind.ExpandString)
    {
        using var environment = _root.CreateSubKey("Environment");
        environment.SetValue("Path", value, kind);
    }

    private string? Path() =>
        _root.OpenSubKey("Environment")?.GetValue("Path", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;

    [Fact]
    public void AddsAtTheEndAndTakesBackOnlyItsOwn()
    {
        SetPath(@"%USERPROFILE%\bin;C:\Tools");
        var path = new UserPath(_root);
        Assert.True(path.Apply(@"C:\Zinc\v1\bin"));
        Assert.Equal(@"%USERPROFILE%\bin;C:\Tools;C:\Zinc\v1\bin", Path()); // unexpanded entries stay as written
        Assert.Equal(RegistryValueKind.ExpandString, _root.OpenSubKey("Environment")!.GetValueKind("Path"));
        Assert.Equal(@"C:\Zinc\v1\bin", path.Entry);

        Assert.False(path.Apply(@"C:\Zinc\v1\bin")); // already there: nothing to announce

        // A new install's folder replaces the old one.
        Assert.True(path.Apply(@"C:\Zinc\v2\bin"));
        Assert.Equal(@"%USERPROFILE%\bin;C:\Tools;C:\Zinc\v2\bin", Path());

        Assert.True(path.Apply(null));
        Assert.Equal(@"%USERPROFILE%\bin;C:\Tools", Path());
        Assert.Null(path.Entry);
    }

    [Fact]
    public void AnEntryTheUserAlsoHasIsNotDuplicated()
    {
        SetPath(@"C:\Tools;C:\Zinc\bin\", RegistryValueKind.String);
        var path = new UserPath(_root);
        path.Apply(@"C:\Zinc\bin");
        Assert.Equal(@"C:\Tools;C:\Zinc\bin", Path());
        Assert.Equal(RegistryValueKind.String, _root.OpenSubKey("Environment")!.GetValueKind("Path")); // its kind kept
    }

    [Fact]
    public void NoPathYet()
    {
        var path = new UserPath(_root);
        Assert.False(path.Apply(null));
        Assert.Null(Path());
        Assert.True(path.Apply(@"C:\Zinc\bin"));
        Assert.Equal(@"C:\Zinc\bin", Path());
    }

    [Fact]
    public void UninstallTakesItBackFromItsOwnInstallOnly()
    {
        SetPath(@"C:\Tools");
        var path = new UserPath(_root);
        path.Apply(Installed + @"\current\bin");
        Assert.Contains("took zn off PATH", InstallCleanup.Uninstall(_root, Installed));
        Assert.Equal(@"C:\Tools", Path());

        path.Apply(@"C:\Users\Someone\AppData\Local\Programs\Zinc\versions\20261003-0.1.0\bin"); // the dev install's
        Assert.Empty(InstallCleanup.Uninstall(_root, Installed));
        Assert.EndsWith(@"20261003-0.1.0\bin", Path());
    }
}
