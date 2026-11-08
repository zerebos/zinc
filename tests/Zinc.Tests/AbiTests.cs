using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Zinc.Ghostty;

namespace Zinc.Tests;

/// <summary>
/// Checks the hand-written bindings in Native.cs against the ABI manifest that
/// libghostty-vt embeds (ghostty_type_json). The C API is unstable, so after bumping
/// the pinned Ghostty commit this is what turns silent memory corruption into a failure.
/// </summary>
public unsafe partial class AbiTests
{
    private static readonly Assembly Core = typeof(Native).Assembly;
    private static readonly Lazy<JsonElement> Manifest = new(LoadManifest);

    // C# enum -> C enum. Every enum in Zinc.Ghostty must be listed here or in
    // NotInManifest (see EveryBindingEnumIsChecked).
    private static readonly Dictionary<Type, string> Enums = new()
    {
        [typeof(GhosttyResult)] = "GhosttyResult",
        [typeof(GhosttyKey)] = "GhosttyKey",
        [typeof(CursorVisualStyle)] = "GhosttyRenderStateCursorVisualStyle",
        [typeof(TerminalCursorStyle)] = "GhosttyTerminalCursorStyle",
        [typeof(ScrollViewportTag)] = "GhosttyTerminalScrollViewportTag",
        [typeof(PointTag)] = "GhosttyPointTag",
        [typeof(KeyAction)] = "GhosttyKeyAction",
        [typeof(MouseAction)] = "GhosttyMouseAction",
        [typeof(MouseButton)] = "GhosttyMouseButton",
        [typeof(TerminalOption)] = "GhosttyTerminalOption",
        [typeof(TerminalData)] = "GhosttyTerminalData",
        [typeof(RenderStateData)] = "GhosttyRenderStateData",
        [typeof(RenderStateRowData)] = "GhosttyRenderStateRowData",
        [typeof(RenderStateRowCellsData)] = "GhosttyRenderStateRowCellsData",
        [typeof(CellData)] = "GhosttyCellData",
        [typeof(RowData)] = "GhosttyRowData",
        [typeof(RowSemanticPrompt)] = "GhosttyRowSemanticPrompt",
        [typeof(CellWide)] = "GhosttyCellWide",
        [typeof(CellSemanticContent)] = "GhosttyCellSemanticContent",
        [typeof(TerminalProgressState)] = "GhosttyTerminalProgressState",
        [typeof(ColorScheme)] = "GhosttyColorScheme",
        [typeof(KeyEncoderOption)] = "GhosttyKeyEncoderOption",
        [typeof(MouseEncoderOption)] = "GhosttyMouseEncoderOption",
        [typeof(SelectionGestureEventType)] = "GhosttySelectionGestureEventType",
        [typeof(SelectionGestureEventOption)] = "GhosttySelectionGestureEventOption",
        [typeof(ClipboardLocation)] = "GhosttyClipboardLocation",
        [typeof(ClipboardWriteResult)] = "GhosttyClipboardWriteResult",
        [typeof(ClipboardReadResult)] = "GhosttyClipboardReadResult",
        [typeof(SysOption)] = "GhosttySysOption",
        [typeof(KittyGraphicsData)] = "GhosttyKittyGraphicsData",
        [typeof(KittyGraphicsPlacementData)] = "GhosttyKittyGraphicsPlacementData",
        [typeof(KittyPlacementLayer)] = "GhosttyKittyPlacementLayer",
        [typeof(KittyImageFormat)] = "GhosttyKittyImageFormat",
        [typeof(KittyGraphicsImageData)] = "GhosttyKittyGraphicsImageData",
    };

    // Enums that mirror #defines rather than C enums, so the manifest can't describe them.
    private static readonly HashSet<Type> NotInManifest = [typeof(GhosttyMods)];

    public static TheoryData<string> Structs() =>
        new(Core.GetTypes()
            .Where(t => t is { Namespace: "Zinc.Ghostty", IsValueType: true, IsEnum: false, IsPublic: true })
            .Select(t => t.Name));

    public static TheoryData<string> EnumNames() => new(Enums.Keys.Select(t => t.Name));

    [Fact]
    public void ManifestMatchesProcessArchitecture()
    {
        var abi = Manifest.Value.GetProperty("abi");
        Assert.Equal(IntPtr.Size, abi.GetProperty("pointer_size").GetInt32());
        Assert.Equal("windows", abi.GetProperty("os").GetString());
    }

    [Theory]
    [MemberData(nameof(Structs))]
    public void StructLayoutMatchesManifest(string name)
    {
        var type = Core.GetType($"Zinc.Ghostty.{name}")!;
        Assert.True(Types.TryGetProperty(name, out var c), $"{name} is not in the ABI manifest");
        Assert.Equal(c.GetProperty("size").GetInt32(), Marshal.SizeOf(type));

        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            var path = field.GetCustomAttribute<AbiFieldAttribute>()?.Path ?? SnakeCase(field.Name);
            var (offset, size) = Resolve(name, path);
            Assert.True(offset == (int)Marshal.OffsetOf(type, field.Name),
                $"{name}.{field.Name} ({path}): C# offset {Marshal.OffsetOf(type, field.Name)}, C offset {offset}");
            Assert.True(size == SizeOf(field.FieldType),
                $"{name}.{field.Name} ({path}): C# size {SizeOf(field.FieldType)}, C size {size}");
        }
    }

    [Theory]
    [MemberData(nameof(EnumNames))]
    public void EnumValuesMatchManifest(string name)
    {
        var type = Enums.Keys.Single(t => t.Name == name);
        var c = Types.GetProperty(Enums[type]);
        Assert.Equal(c.GetProperty("size").GetInt32(), SizeOf(type));

        // Compare names ignoring underscores and case: DIGIT_0 <-> Digit0, WRITE_PTY <-> WritePty.
        var cValues = c.GetProperty("values").EnumerateObject()
            .ToDictionary(p => p.Name.Replace("_", ""), p => p.Value.GetInt64(), StringComparer.OrdinalIgnoreCase);
        foreach (var member in Enum.GetNames(type))
        {
            Assert.True(cValues.TryGetValue(member, out var value), $"{name}.{member} has no counterpart in {Enums[type]}");
            Assert.Equal(value, Convert.ToInt64(Enum.Parse(type, member)));
        }
    }

    [Fact]
    public void EveryBindingEnumIsChecked()
    {
        var unchecked_ = Core.GetTypes()
            .Where(t => t is { Namespace: "Zinc.Ghostty", IsEnum: true } && !Enums.ContainsKey(t) && !NotInManifest.Contains(t))
            .Select(t => t.Name)
            .ToList();
        Assert.Empty(unchecked_);
    }

    [Fact]
    public void GhosttyKeyCoversEveryCKey()
    {
        // GhosttyKey.g.cs is generated, so this catches forgetting to rerun gen-keys.ps1.
        var values = Types.GetProperty("GhosttyKey").GetProperty("values").EnumerateObject()
            .Where(p => p.Name != "MAX_VALUE");
        Assert.Equal(values.Count(), Enum.GetValues<GhosttyKey>().Length);
    }

    [Fact]
    public void EveryImportedFunctionIsExported()
    {
        var lib = NativeLibrary.Load("ghostty-vt.dll", typeof(AbiTests).Assembly, DllImportSearchPath.AssemblyDirectory);
        var missing = typeof(Native).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Select(m => m.GetCustomAttribute<LibraryImportAttribute>())
            .Where(a => a is { LibraryName: "ghostty-vt" })
            .Select(a => a!.EntryPoint!)
            .Where(entry => !NativeLibrary.TryGetExport(lib, entry, out _))
            .ToList();
        Assert.Empty(missing);
    }

    // ---- helpers ----

    private static JsonElement Types => Manifest.Value.GetProperty("types");

    /// <summary>Walks a dotted field path through nested structs/unions: (absolute offset, size).</summary>
    private static (int Offset, int Size) Resolve(string typeName, string path)
    {
        var offset = 0;
        var current = Types.GetProperty(typeName);
        var segments = path.Split('.');
        for (var i = 0; i < segments.Length; i++)
        {
            Assert.True(current.GetProperty("fields").TryGetProperty(segments[i], out var field),
                $"{typeName}: no C field '{segments[i]}' in path '{path}'");
            offset += field.GetProperty("offset").GetInt32();
            if (i == segments.Length - 1)
            {
                return (offset, field.GetProperty("size").GetInt32());
            }

            current = Types.GetProperty(field.GetProperty("type").GetString()!);
        }

        throw new InvalidOperationException("unreachable");
    }

    private static int SizeOf(Type t) =>
        t.IsEnum ? Marshal.SizeOf(Enum.GetUnderlyingType(t))
        : t == typeof(nint) || t == typeof(nuint) ? IntPtr.Size
        : Marshal.SizeOf(t);

    private static string SnakeCase(string name) =>
        string.Concat(name.Select((ch, i) => i > 0 && char.IsUpper(ch) ? "_" + char.ToLowerInvariant(ch) : char.ToLowerInvariant(ch).ToString()));

    private static JsonElement LoadManifest()
    {
        using var doc = JsonDocument.Parse(Marshal.PtrToStringUTF8((nint)TypeJson())!);
        return doc.RootElement.Clone();
    }

    [LibraryImport("ghostty-vt", EntryPoint = "ghostty_type_json")]
    private static partial byte* TypeJson();
}
