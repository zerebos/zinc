#:project ../../src/Zinc.Core/Zinc.Core.csproj
#:property TargetFramework=net10.0-windows10.0.26100.0
#:property PublishAot=false

// Writes zinc-reference.json, this checkout's settings, actions and open-source notices, for the website's reference
// pages. Run it with `dotnet run`:
//
//   dotnet run scripts/dev/export-reference.cs                       (writes dist\reference\zinc-reference.json)
//   dotnet run scripts/dev/export-reference.cs -- --out <folder>
//
// The shape is Zinc.Core's ReferenceExport. License texts are read from this checkout, where the app's build takes
// them from.

using Zinc;

const string Usage = """
    Writes zinc-reference.json (settings, actions, open-source notices) for the website.

    Options:
      --out <folder>   where to write it (default: dist\reference)
    """;

var repo = Path.GetFullPath(Path.Combine((string)AppContext.GetData("EntryPointFileDirectoryPath")!, "..", ".."));
var outFolder = Path.Combine(repo, @"dist\reference");

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--out" when i + 1 < args.Length:
            outFolder = Path.GetFullPath(args[++i]);
            break;
        case "--help" or "-h" or "/?":
            Console.WriteLine(Usage);
            return 0;
        default:
            Console.Error.WriteLine($"{(args[i] == "--out" ? "--out needs a folder." : $"Unknown option {args[i]}.")}\n\n{Usage}");
            return 1;
    }
}

var reference = ReferenceExport.Build(c => File.ReadAllText(OpenSourceNotices.SourceIn(repo, c.LicenseFile)));
Directory.CreateDirectory(outFolder);
var file = Path.Combine(outFolder, "zinc-reference.json");
File.WriteAllText(file, ReferenceExport.ToJson(reference));
Console.WriteLine($"Wrote {file} ({ReferenceExport.Version}).");
return 0;
