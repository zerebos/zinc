using System.Text.RegularExpressions;

namespace Zinc.Tests;

/// <summary>
/// The developer docs name code in backticks, and a rename or a deletion leaves them wrong without anyone noticing.
/// This checks that each name that looks like Zinc's own still exists. It can't tell whether a mechanism still works
/// the way the doc says, only that the things it names are still there.
/// </summary>
public class DocsNamesTests
{
    private static readonly string[] Docs = ["docs/architecture.md", "docs/development.md"];

    // Folders that hold build output or someone else's code, which a doc may name but the repo doesn't track.
    private static readonly string[] Skipped = ["bin", "obj", "external", "native", "dist", ".git", "node_modules", "zig-out", "zig-pkg", ".zig-cache"];

    private static readonly string[] SourceExtensions = [".cs", ".xaml", ".ps1", ".zig", ".csproj", ".targets", ".props", ".slnx", ".yml"];

    // A file name or path with one of these extensions, or a folder path that ends in a slash, has to exist in the repo.
    private static readonly Regex PathLike = new(@"^[\w./-]*\w\.(cs|ps1|zig|xaml|props|targets|csproj|slnx|yml|md)$|^[\w.-]+(/[\w.-]+)*/$");

    // Names a doc mentions on purpose without Zinc using them: framework APIs Zinc avoids, and naming examples.
    private static readonly HashSet<string> Allowed = ["Monitor.TryEnter", "DefaultStyleKey", "HasFocus", "IsOpen"];

    // Code names are `Type.Member` chains, PascalCase words with at least two humps, `_fields` and `ZINC_` variables.
    private static readonly Regex CodeLike = new(@"^([A-Z][A-Za-z0-9]*(\.[A-Za-z_][A-Za-z0-9_]*)+|[A-Z][a-z0-9]+[A-Z][A-Za-z0-9]*|_[a-z][A-Za-z0-9]*|ZINC_[A-Z_]+)$");

    [Fact]
    public void EveryNamedThingStillExists()
    {
        var root = OpenSourceNoticesTests.Root;
        var files = SourceFiles(root).ToList();
        var relative = files.Select(f => Path.GetRelativePath(root, f).Replace('\\', '/')).ToList();
        var sources = files.Where(f => SourceExtensions.Contains(Path.GetExtension(f))).ToDictionary(f => f, File.ReadAllText);
        var words = new HashSet<string>(StringComparer.Ordinal);
        foreach (var text in sources.Values)
        {
            foreach (Match m in Regex.Matches(text, @"[A-Za-z_][A-Za-z0-9_]*"))
            {
                words.Add(m.Value);
            }
        }

        // For one of Zinc's own types (it has files named after it), Type.Member is there when the code writes it out,
        // or when the type's own files have the member. Someone else's type only gets the word check.
        bool HasMember(string name)
        {
            var parts = name.Split('.');
            var own = sources.Where(s => Path.GetFileName(s.Key).StartsWith(parts[0] + ".", StringComparison.Ordinal)).ToList();
            return own.Count == 0 ||
                   sources.Values.Any(t => t.Contains(name, StringComparison.Ordinal)) ||
                   own.Any(s => Regex.IsMatch(s.Value, $@"\b{Regex.Escape(parts[^1])}\b"));
        }

        var missing = new List<string>();
        foreach (var doc in Docs)
        {
            var text = File.ReadAllText(Path.Combine(root, doc));
            text = Regex.Replace(text, "```.*?```", "", RegexOptions.Singleline); // code blocks are examples, not names
            foreach (var name in Regex.Matches(text, "`([^`\n]+)`").Select(m => m.Groups[1].Value.TrimEnd('(', ')').Replace("()", "")).Distinct())
            {
                if (name.Contains('*') || name.Contains('<') || name.Contains('%') || name.Contains(' ') || Allowed.Contains(name))
                {
                    continue; // a pattern, a placeholder, an environment path or a command line
                }

                if (PathLike.IsMatch(name))
                {
                    var path = name.TrimEnd('/');
                    if (Skipped.Contains(path.Split('/')[0]))
                    {
                        continue;
                    }

                    if (!relative.Any(r => r == path || r.EndsWith("/" + path, StringComparison.Ordinal) || r.StartsWith(path + "/", StringComparison.Ordinal) || r.Contains("/" + path + "/", StringComparison.Ordinal)))
                    {
                        missing.Add($"{doc}: `{name}` (no such file or folder)");
                    }
                }
                else if (CodeLike.IsMatch(name) && name.Split('.').FirstOrDefault(part => !words.Contains(part)) is { } absent)
                {
                    missing.Add($"{doc}: `{name}` (no `{absent}` in the source)");
                }
                else if (CodeLike.IsMatch(name) && name.Contains('.') && !HasMember(name))
                {
                    missing.Add($"{doc}: `{name}` (the type has no such member)");
                }
            }
        }

        Assert.True(missing.Count == 0, "Names in the docs that the code no longer has:\n" + string.Join('\n', missing));
    }

    private static IEnumerable<string> SourceFiles(string root)
    {
        var pending = new Stack<string>([root]);
        while (pending.TryPop(out var dir))
        {
            foreach (var file in Directory.EnumerateFiles(dir))
            {
                yield return file;
            }

            foreach (var sub in Directory.EnumerateDirectories(dir))
            {
                var name = Path.GetFileName(sub);
                if (!Skipped.Contains(name) && !name.StartsWith('.') || name is ".github")
                {
                    pending.Push(sub);
                }
            }
        }
    }
}
