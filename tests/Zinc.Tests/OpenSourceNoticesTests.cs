using System.Text.RegularExpressions;

namespace Zinc.Tests;

/// <summary>Settings > About's list of open-source software stays complete as dependencies change.</summary>
public class OpenSourceNoticesTests
{
    /// <summary>The repository's root folder (the one with Zinc.slnx).</summary>
    internal static readonly string Root = FindRoot();

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Zinc.slnx")))
        {
            dir = dir.Parent;
        }

        return dir!.FullName;
    }

    private static string Source(string shipped) => OpenSourceNotices.SourceIn(Root, shipped);

    /// <summary>The NuGet packages the app ships: what its projects reference, minus build-time tools.</summary>
    private static Dictionary<string, string> ShippedPackages()
    {
        string[] buildOnly = ["Microsoft.Windows.SDK.BuildTools"];
        var files = new[] { "src/Zinc/Zinc.csproj", "src/Zinc.Core/Zinc.Core.csproj", "src/Zinc.Native.targets" };
        var packages = new Dictionary<string, string>();
        foreach (var file in files)
        {
            foreach (Match m in Regex.Matches(File.ReadAllText(Path.Combine(Root, file)), "<PackageReference Include=\"([^\"]+)\" Version=\"([^\"]+)\""))
            {
                if (!buildOnly.Contains(m.Groups[1].Value))
                {
                    packages[m.Groups[1].Value] = m.Groups[2].Value;
                }
            }
        }

        return packages;
    }

    [Fact]
    public void ListedAlphabetically()
    {
        var names = OpenSourceNotices.Components.Select(c => c.Name).ToList();
        Assert.Equal(names.Order(StringComparer.OrdinalIgnoreCase), names);
    }

    [Fact]
    public void EveryLicenseTextExists()
    {
        foreach (var component in OpenSourceNotices.Components.Prepend(OpenSourceNotices.Zinc))
        {
            Assert.True(File.Exists(Source(component.LicenseFile)), $"{component.Name}: {component.LicenseFile}");
        }
    }

    [Fact]
    public void EveryLicenseFileBelongsToSomething()
    {
        var listed = OpenSourceNotices.Components.Select(c => Path.GetFullPath(Source(c.LicenseFile))).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.GetFiles(Path.Combine(Root, "assets", "licenses")))
        {
            Assert.Contains(Path.GetFullPath(file), listed);
        }
    }

    [Fact]
    public void ZincShipsItsOwnLicense()
    {
        var project = File.ReadAllText(Path.Combine(Root, "src", "Zinc", "Zinc.csproj"));
        Assert.Contains(@"<Content Include=""..\..\LICENSE"" Link=""Licenses\Zinc.txt""", project);
        Assert.Contains("Apache License", File.ReadAllText(Path.Combine(Root, "LICENSE")));
    }

    [Fact]
    public void EveryShippedPackageIsListed()
    {
        var named = OpenSourceNotices.Components.SelectMany(c => c.Packages).ToHashSet();
        foreach (var package in ShippedPackages().Keys)
        {
            Assert.True(named.Contains(package), $"{package} ships but isn't in OpenSourceNotices");
        }
    }

    [Theory]
    [InlineData("Win2D", "Microsoft.Graphics.Win2D")]
    [InlineData("ConPTY and OpenConsole", "Microsoft.Windows.Console.ConPTY")]
    [InlineData("Tomlyn", "Tomlyn")]
    public void VersionsMatchThePackages(string name, string package) =>
        Assert.Equal(ShippedPackages()[package], OpenSourceNotices.Components.Single(c => c.Name == name).Version);

    [Fact]
    public void GhosttyIsThePinnedCommit()
    {
        var script = File.ReadAllText(Path.Combine(Root, "scripts", "build", "libghostty.ps1"));
        var commit = Regex.Match(script, @"\[string\]\$Commit = '([0-9a-f]{40})'").Groups[1].Value;
        Assert.NotEmpty(commit);
        Assert.Equal(commit[..7], OpenSourceNotices.Components.Single(c => c.Name.StartsWith("Ghostty")).Version);
    }
}
