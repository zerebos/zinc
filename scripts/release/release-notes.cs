#:project ../../src/Zinc.Core/Zinc.Core.csproj
#:property TargetFramework=net10.0-windows10.0.26100.0
#:property PublishAot=false

// Writes a version's release notes, which are CHANGELOG.md's entry for it, as the app itself reads them (Zinc.Core's
// ReleaseNotes). The release script and the release workflow hand the file to the installer and to the GitHub release.
//
//   dotnet run scripts/release/release-notes.cs -- 0.1.0 --out notes.md
//   dotnet run scripts/release/release-notes.cs -- 0.1.0 --check          (fails if the changelog isn't ready for a release)

using Zinc;

const string Usage = """
    Writes a version's release notes: CHANGELOG.md's entry for it.

    Usage: dotnet run scripts/release/release-notes.cs -- <version> [options]

    Options:
      --changelog <file>  the changelog to read (default: CHANGELOG.md in the repository)
      --out <file>        where to write the notes (default: print them)
      --check             fail when the version has no entry, or when "## Unreleased" still has notes
    """;

var repo = Path.GetFullPath(Path.Combine((string)AppContext.GetData("EntryPointFileDirectoryPath")!, "..", ".."));
var changelog = Path.Combine(repo, "CHANGELOG.md");
string? version = null, outFile = null;
var check = false;

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--changelog" when i + 1 < args.Length:
            changelog = Path.GetFullPath(args[++i]);
            break;
        case "--out" when i + 1 < args.Length:
            outFile = Path.GetFullPath(args[++i]);
            break;
        case "--check":
            check = true;
            break;
        case "--help" or "-h" or "/?":
            Console.WriteLine(Usage);
            return 0;
        case var arg when !arg.StartsWith('-') && version == null:
            version = arg.Split('+')[0]; // without the commit's hash
            break;
        default:
            Console.Error.WriteLine($"Unknown or incomplete option {args[i]}.\n\n{Usage}");
            return 2;
    }
}

if (version == null)
{
    Console.Error.WriteLine($"Which version?\n\n{Usage}");
    return 2;
}

var text = File.ReadAllText(changelog);
if (check && ReleaseNotes.ReleaseProblem(text, version) is { } problem)
{
    Console.Error.WriteLine(problem);
    return 1;
}

if (ReleaseNotes.Entry(text, version) is not { Length: > 0 } notes)
{
    Console.Error.WriteLine($"{Path.GetFileName(changelog)} has no entry for {version}, so this build has no release notes.");
    return 0;
}

if (outFile == null)
{
    Console.WriteLine(notes);
}
else
{
    File.WriteAllText(outFile, notes + "\n"); // UTF-8 without a byte order mark
    Console.WriteLine($"Release notes: {Path.GetFileName(changelog)}'s entry for {version}");
}

return 0;
