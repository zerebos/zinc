#:project ../../src/Zinc.Core/Zinc.Core.csproj
#:property TargetFramework=net10.0-windows10.0.26100.0
#:property PublishAot=false

// Writes two files for the website: zinc-reference.json, this checkout's settings, actions and open-source notices for
// its reference pages, and config.schema.json, a JSON Schema for config.toml. Run it with `dotnet run`:
//
//   dotnet run scripts/dev/export-reference.cs                       (writes both to dist\reference)
//   dotnet run scripts/dev/export-reference.cs -- --out <folder>
//
// Zinc.Core's ReferenceExport and ConfigSchema make them. License texts are read from this checkout, where the app's
// build takes them from.

using Zinc;
using Zinc.Settings;

const string Usage = """
    Writes zinc-reference.json (settings, actions, open-source notices) and config.schema.json for the website.

    Options:
      --out <folder>   where to write them (default: dist\reference)
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
foreach (var (name, json) in new[] { ("zinc-reference.json", reference), ("config.schema.json", ConfigSchema.Build()) })
{
    var file = Path.Combine(outFolder, name);
    File.WriteAllText(file, ReferenceExport.ToJson(json));
    Console.WriteLine($"Wrote {file}.");
}

Console.WriteLine($"Zinc {ReferenceExport.Version}.");
return 0;
