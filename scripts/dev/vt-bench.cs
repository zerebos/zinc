#:project ../../src/Zinc.Core/Zinc.Core.csproj
#:property TargetFramework=net10.0-windows10.0.26100.0
#:property PublishAot=false
#:property AllowUnsafeBlocks=true

// How fast ghostty-vt.dll parses terminal output, for comparing Ghostty commits. Run it with `dotnet run`:
//
//   dotnet run scripts/dev/vt-bench.cs -- --save before       (before bumping the Ghostty commit)
//   dotnet run scripts/dev/vt-bench.cs -- --compare before    (after rebuilding the DLL)
//
// It feeds output through ghostty_terminal_vt_write, the call Zinc makes for every read of a program's output, 64 KB
// at a time, into a 120x80 terminal with Zinc's scrollback. Each workload gets a warm-up run, then the best of 7 counts.
// The workloads are made in memory on every run from a fixed seed (plain text, mixed UTF-8 and styled text, in the
// style of Ghostty's own ghostty-gen), so every run and every machine measures the same bytes.

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Zinc;
using Zinc.Ghostty;

const string Usage = """
    How fast ghostty-vt.dll parses terminal output (MB/s), for comparing Ghostty commits.

    Before bumping the Ghostty commit:   dotnet run scripts/dev/vt-bench.cs -- --save before
    After rebuilding the DLL:            dotnet run scripts/dev/vt-bench.cs -- --compare before

    Options:
      --save <name>       keep the results as <name> in the results folder
      --compare <name>    show the change from the results kept as <name> (more than 10% slower is flagged)
      --dll <path>        the DLL to measure (default: native\x64\ghostty-vt.dll)
      --results <folder>  where results are kept (default: dist\bench)
      --file <path>       measure this file instead of the generated workloads, for example a ZINC_TRACE
                          recording of a real program (repeat it for several files)
    """;

var repo = Path.GetFullPath(Path.Combine((string)AppContext.GetData("EntryPointFileDirectoryPath")!, "..", ".."));
var dll = Path.Combine(repo, @"native\x64\ghostty-vt.dll");
var resultsFolder = Path.Combine(repo, @"dist\bench");
string? save = null, compare = null;
var files = new List<string>();

for (var i = 0; i < args.Length; i++)
{
    string Value() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value.");
    try
    {
        switch (args[i])
        {
            case "--save": save = Value(); break;
            case "--compare": compare = Value(); break;
            case "--dll": dll = Path.GetFullPath(Value()); break;
            case "--results": resultsFolder = Path.GetFullPath(Value()); break;
            case "--file": files.Add(Path.GetFullPath(Value())); break;
            case "--help" or "-h" or "/?": Console.WriteLine(Usage); return 0;
            default: throw new ArgumentException($"Unknown option {args[i]}.");
        }
    }
    catch (ArgumentException e)
    {
        Console.Error.WriteLine($"{e.Message}\n\n{Usage}");
        return 1;
    }
}

if (!File.Exists(dll))
{
    Console.Error.WriteLine($"{dll} doesn't exist. Build it with: pwsh scripts/build/libghostty.ps1");
    return 1;
}

// Zinc.Core's bindings load `ghostty-vt` by name, so point that name at the DLL being measured
System.Runtime.InteropServices.NativeLibrary.SetDllImportResolver(typeof(Native).Assembly,
    (name, _, _) => name == "ghostty-vt" ? System.Runtime.InteropServices.NativeLibrary.Load(dll) : 0);

var baseline = new Dictionary<string, double>();
if (compare != null)
{
    var path = Path.Combine(resultsFolder, compare + ".json");
    if (!File.Exists(path))
    {
        Console.Error.WriteLine($"No results named {compare} in {resultsFolder}.");
        return 1;
    }

    baseline = JsonSerializer.Deserialize<Dictionary<string, double>>(File.ReadAllText(path))!;
}

Console.WriteLine(dll);
var results = new JsonObject();
var workloads = files.Count > 0
    ? files.Select(f => (Name: Path.GetFileNameWithoutExtension(f), Data: File.ReadAllBytes(f)))
    : Workloads.All();
var nameWidth = files.Count > 0 ? files.Max(f => Path.GetFileNameWithoutExtension(f).Length) : Workloads.NameWidth;
foreach (var (name, data) in workloads)
{
    var speed = Measure(data);
    results[name] = Math.Round(speed, 1);
    var line = $"{name.PadRight(nameWidth)} {speed,8:F0} MB/s";
    if (baseline.TryGetValue(name, out var before))
    {
        var change = (speed - before) / before * 100;
        line += $"   {compare}: {before:F0} MB/s ({change:+0;-0}%){(change < -10 ? "   <-- slower" : "")}";
    }

    Console.WriteLine(line);
}

if (save != null)
{
    Directory.CreateDirectory(resultsFolder);
    File.WriteAllText(Path.Combine(resultsFolder, save + ".json"), results.ToJsonString(new() { WriteIndented = true }));
    Console.WriteLine($"saved as {save} in {resultsFolder}");
}

return 0;

// MB/s for one workload, from the best of 7 runs after a warm-up (cold caches and CPU clocks make the first one slower)
static unsafe double Measure(byte[] data)
{
    const int Chunk = 64 * 1024;
    var best = double.MaxValue;
    fixed (byte* start = data)
    {
        for (var run = 0; run < 8; run++)
        {
            if (Native.TerminalNew(0, out var terminal, 120, 80) != GhosttyResult.Success)
            {
                throw new InvalidOperationException("ghostty_terminal_new failed.");
            }

            nuint lines = Config.DefaultScrollbackLines;
            Native.TerminalSet(terminal, TerminalOption.ScrollbackMaxLines, &lines);
            var noByteLimit = nuint.MaxValue; // no byte limit, as in Zinc
            Native.TerminalSet(terminal, TerminalOption.ScrollbackMaxBytes, &noByteLimit);

            var clock = Stopwatch.StartNew();
            for (var offset = 0; offset < data.Length; offset += Chunk)
            {
                Native.TerminalVtWrite(terminal, start + offset, (nuint)Math.Min(Chunk, data.Length - offset));
            }

            var seconds = clock.Elapsed.TotalSeconds;
            Native.TerminalFree(terminal);
            if (run > 0)
            {
                best = Math.Min(best, seconds);
            }
        }
    }

    return data.Length / best / (1024 * 1024);
}

/// <summary>The generated workloads, 20 MB each. Changing them changes every number, so old results stop comparing.</summary>
static class Workloads
{
    private const int Size = 20 * 1024 * 1024;
    private const ulong Seed = 7;
    private const string Plain = "abcdefghijklmnopqrstuvwxyz0123456789 .,;:-_()[]{}";
    private const string Words = "abcdefghijklmnopqrstuvwxyz0123456789 ";

    /// <summary>The longest workload name, for lining up the output.</summary>
    public const int NameWidth = 6;

    public static IEnumerable<(string Name, byte[] Data)> All()
    {
        yield return ("ascii", Make(Ascii));
        yield return ("utf8", Make(Utf8));
        yield return ("styled", Make(Styled));
    }

    // Lines until there are Size bytes, cut to exactly Size (which can split the last character, as output can)
    private static byte[] Make(Func<SplitMix, string> line)
    {
        var random = new SplitMix(Seed);
        var data = new MemoryStream(Size + 1024);
        while (data.Length < Size)
        {
            var bytes = Encoding.UTF8.GetBytes(line(random) + "\r\n");
            data.Write(bytes);
        }

        data.SetLength(Size);
        return data.ToArray();
    }

    private static string Ascii(SplitMix random) => random.Text(Plain, random.Between(10, 118));

    private static readonly string[][] Pools =
    [
        [.. "abcdefghijklmnopqrstuvwxyz ".Select(c => c.ToString())],
        [.. "àéîõüßçñ ".Select(c => c.ToString())],
        [.. "日本語漢字のテキスト".Select(c => c.ToString())],
        [.. "가나다라마바사".Select(c => c.ToString())],
        [.. "αβγδεζηθ".Select(c => c.ToString())],
        ["🙂", "🚀", "✨", "🎉"],
    ];

    // A line from one script at a time: Latin, accented, CJK, Hangul, Greek or emoji
    private static string Utf8(SplitMix random)
    {
        var pool = random.Pick(Pools);
        var line = new StringBuilder();
        for (var n = random.Between(5, 50); n > 0; n--)
        {
            line.Append(random.Pick(pool));
        }

        return line.ToString();
    }

    private static readonly int[] Attributes = [1, 3, 4, 7, 31, 32, 33, 34, 35, 36, 91, 92, 93, 94];
    private static readonly int[] Planes = [38, 48]; // foreground, background

    // 2 to 8 runs of text, each in a basic attribute, a 256-color or a 24-bit color, and reset after
    private static string Styled(SplitMix random)
    {
        var line = new StringBuilder();
        for (var n = random.Between(2, 8); n > 0; n--)
        {
            var kind = random.Fraction();
            var sgr = kind < 0.4 ? random.Pick(Attributes).ToString()
                : kind < 0.7 ? $"38;5;{random.Between(0, 255)}"
                : $"{random.Pick(Planes)};2;{random.Between(0, 255)};{random.Between(0, 255)};{random.Between(0, 255)}";
            line.Append($"\e[{sgr}m{random.Text(Words, random.Between(4, 16))}\e[0m");
        }

        return line.ToString();
    }
}

/// <summary>
/// SplitMix64, a small seeded random number generator written out here instead of System.Random. .NET doesn't promise
/// that a seeded System.Random gives the same numbers in every version, and the workloads must never change by themselves.
/// </summary>
sealed class SplitMix(ulong seed)
{
    private ulong _state = seed;

    private ulong Next()
    {
        var z = _state += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>0 to <paramref name="count"/> - 1 (the modulo's slight bias doesn't matter here).</summary>
    public int Below(int count) => (int)(Next() % (ulong)count);

    /// <summary><paramref name="low"/> to <paramref name="high"/>, both included.</summary>
    public int Between(int low, int high) => low + Below(high - low + 1);

    /// <summary>0 (included) to 1 (excluded).</summary>
    public double Fraction() => (Next() >> 11) * (1.0 / (1UL << 53));

    public T Pick<T>(IReadOnlyList<T> items) => items[Below(items.Count)];

    public string Text(string characters, int length) =>
        string.Create(length, (this, characters), static (span, state) =>
        {
            for (var i = 0; i < span.Length; i++)
            {
                span[i] = state.characters[state.Item1.Below(state.characters.Length)];
            }
        });
}
