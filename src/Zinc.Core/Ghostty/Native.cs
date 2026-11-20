using System.Runtime.InteropServices;

namespace Zinc.Ghostty;

// Raw bindings for libghostty-vt (ghostty-vt.dll).
//
// Struct layouts here use explicit offsets taken from the library's own ABI manifest (`ghostty_type_json()`), which
// AbiTests compares them with. Within libghostty-vt 1.x the C ABI only gains functions, enum values, and fields at the
// end of sized structs, but rerun the tests after bumping the commit in scripts/build/libghostty.ps1.
// C `bool` is mapped to `byte` everywhere to keep every signature blittable.

/// <summary>
/// Maps a flattened field to its dotted path in the C struct (through unions), for the
/// ABI check in Zinc.Tests. Fields without it map to the snake_case of their name.
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class AbiFieldAttribute(string path) : Attribute
{
    public string Path { get; } = path;
}

public enum GhosttyResult : int
{
    Success = 0,
    OutOfMemory = -1,
    InvalidValue = -2,
    OutOfSpace = -3,
    NoValue = -4,
    IoError = -5,
    LimitExceeded = -6,
    Rejected = -7,

    /// <summary>The loaded library doesn't know the option or key, or was built without its feature.</summary>
    Unsupported = -8,
}

[StructLayout(LayoutKind.Sequential, Size = 3)]
public struct GhosttyColorRgb
{
    public byte R, G, B;

    public GhosttyColorRgb(byte r, byte g, byte b)
    {
        R = r; G = g; B = b;
    }

    public static GhosttyColorRgb FromHex(uint rgb) =>
        new((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
}

[StructLayout(LayoutKind.Sequential)]
public struct GhosttyString
{
    public nint Ptr;
    public nuint Len;
}

/// <summary>
/// A program's request for a desktop notification (OSC 9 or OSC 777). Strings are borrowed for the callback.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 32)]
public struct GhosttyTerminalDesktopNotification
{
    [FieldOffset(0)] public GhosttyString Title;
    [FieldOffset(16)] public GhosttyString Body;
}

/// <summary>Light or dark, as reported to programs (CSI ? 996 n, mode 2031).</summary>
public enum ColorScheme : int
{
    Light = 0,
    Dark = 1,
}

public enum TerminalProgressState : int
{
    Remove = 0,
    Set = 1,
    Error = 2,
    Indeterminate = 3,
    Pause = 4,
}

/// <summary>A program's progress (OSC 9;4).</summary>
[StructLayout(LayoutKind.Explicit, Size = 8)]
public struct GhosttyTerminalProgressReport
{
    [FieldOffset(0)] public TerminalProgressState State;

    /// <summary>0 to 100, or -1 when omitted.</summary>
    [FieldOffset(4)] public sbyte Progress;
}

public enum ClipboardLocation : int
{
    Standard = 0,
    Selection = 1,
    Primary = 2,
}

public enum ClipboardWriteResult : int
{
    Success = 0,
    Denied = 1,
    Unsupported = 2,
    Busy = 3,
    InvalidData = 4,
    IoError = 5,
}

public enum ClipboardReadResult : int
{
    Success = 0,
    Denied = 1,
    Unsupported = 2,
    Busy = 3,
    IoError = 4,
}

[StructLayout(LayoutKind.Explicit, Size = 32)]
public struct GhosttyClipboardContent
{
    [FieldOffset(0)] public GhosttyString Mime;
    [FieldOffset(16)] public GhosttyString Data;
}

/// <summary>An OSC 52 / 1337 / 5522 clipboard write, which is answered by calling <see cref="Reply"/>.</summary>
[StructLayout(LayoutKind.Explicit, Size = 64)]
public struct GhosttyClipboardWrite
{
    [FieldOffset(0)] public ClipboardLocation Location;
    [FieldOffset(8)] public nint Contents; // GhosttyClipboardContent*
    [FieldOffset(16)] public nuint ContentsLen;
    [FieldOffset(24)] public GhosttyString Name;
    [FieldOffset(40)] public byte Granted;
    [FieldOffset(41)] public byte CanRemember;
    [FieldOffset(48)] public nint Ctx;
    [FieldOffset(56)] public nint Reply; // void (*)(const GhosttyClipboardWrite*, const GhosttyClipboardWriteReply*)
}

[StructLayout(LayoutKind.Explicit, Size = 16)]
public struct GhosttyClipboardWriteReply
{
    [FieldOffset(0)] public nuint Size;
    [FieldOffset(8)] public ClipboardWriteResult Result;
    [FieldOffset(12)] public byte Remember;
}

/// <summary>An OSC 52 "?" / 5522 clipboard read, which is answered by calling <see cref="Reply"/>.</summary>
[StructLayout(LayoutKind.Explicit, Size = 72)]
public struct GhosttyClipboardRead
{
    [FieldOffset(0)] public ClipboardLocation Location;
    [FieldOffset(8)] public nint Mimes; // GhosttyString*
    [FieldOffset(16)] public nuint MimesLen;
    [FieldOffset(24)] public byte List;
    [FieldOffset(32)] public GhosttyString Name;
    [FieldOffset(48)] public byte Granted;
    [FieldOffset(49)] public byte CanRemember;
    [FieldOffset(56)] public nint Ctx;
    [FieldOffset(64)] public nint Reply; // void (*)(const GhosttyClipboardRead*, const GhosttyClipboardReadReply*)
}

[StructLayout(LayoutKind.Explicit, Size = 56)]
public struct GhosttyClipboardReadReply
{
    [FieldOffset(0)] public nuint Size;
    [FieldOffset(8)] public ClipboardReadResult Result;
    [FieldOffset(16)] public nint Contents; // GhosttyClipboardContent*
    [FieldOffset(24)] public nuint ContentsLen;
    [FieldOffset(32)] public nint Available; // GhosttyString*
    [FieldOffset(40)] public nuint AvailableLen;
    [FieldOffset(48)] public byte Remember;
}

[StructLayout(LayoutKind.Explicit, Size = 24)]
public struct GhosttyRenderStateCursor
{
    [FieldOffset(0)] public nuint Size;
    [FieldOffset(8)] public byte ViewportHasValue;
    [FieldOffset(10)] public ushort ViewportX;
    [FieldOffset(12)] public ushort ViewportY;
    [FieldOffset(14)] public byte WideTail;
    [FieldOffset(15)] public byte Visible;
    [FieldOffset(16)] public byte Blinking;
    [FieldOffset(17)] public byte PasswordInput;
    [FieldOffset(20)] public CursorStyle VisualStyle;
}

/// <summary>
/// A cursor's shape, both the one the render state draws and the default that programs get until they pick one (DECSCUSR).
/// </summary>
public enum CursorStyle : int
{
    Bar = 0,
    Block = 1,
    Underline = 2,
    BlockHollow = 3,
}

[StructLayout(LayoutKind.Explicit, Size = 792)]
public unsafe struct GhosttyRenderStateColors
{
    [FieldOffset(0)] public nuint Size;
    [FieldOffset(8)] public GhosttyColorRgb Background;
    [FieldOffset(11)] public GhosttyColorRgb Foreground;
    [FieldOffset(14)] public GhosttyColorRgb Cursor;
    [FieldOffset(17)] public byte CursorHasValue;
    [FieldOffset(18)] public fixed byte Palette[768];
}

/// <summary>
/// A cell's style as the program set it. Each color is a tag (0 for none, 1 for a palette color, 2 for RGB) and three
/// bytes, and a palette color's index is the first byte. Renderers get resolved colors from the row-cells API, but
/// bold-is-bright needs to know whether the program picked one of the first eight palette colors.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 16)]
public struct GhosttyStyle
{
    [FieldOffset(0), AbiField("fg_color.tag")] public byte FgColorTag;
    [FieldOffset(1), AbiField("fg_color.value.rgb")] public GhosttyColorRgb FgColor;
    [FieldOffset(4), AbiField("bg_color.tag")] public byte BgColorTag;
    [FieldOffset(5), AbiField("bg_color.value.rgb")] public GhosttyColorRgb BgColor;
    [FieldOffset(8), AbiField("underline_color.tag")] public byte UnderlineColorTag;
    [FieldOffset(9), AbiField("underline_color.value.rgb")] public GhosttyColorRgb UnderlineColor;

    /// <summary>Bits 0 to 7 are the on/off attributes (GHOSTTY_STYLE_FLAG_*), and bits 8 to 10 the underline style.</summary>
    [FieldOffset(12)] public ushort Flags;

    [FieldOffset(14)] public ushort Reserved;

    public readonly bool Bold => (Flags & (1 << 0)) != 0;
    public readonly bool Italic => (Flags & (1 << 1)) != 0;
    public readonly bool Faint => (Flags & (1 << 2)) != 0;
    public readonly bool Blink => (Flags & (1 << 3)) != 0;
    public readonly bool Inverse => (Flags & (1 << 4)) != 0;
    public readonly bool Invisible => (Flags & (1 << 5)) != 0;
    public readonly bool Strikethrough => (Flags & (1 << 6)) != 0;
    public readonly bool Overline => (Flags & (1 << 7)) != 0;

    /// <summary>The SGR underline style (GhosttySgrUnderline): 0 none, 1 single, 2 double, 3 curly, 4 dotted, 5 dashed.</summary>
    public readonly int Underline => (Flags >> 8) & 0x7;
}

[StructLayout(LayoutKind.Explicit, Size = 16)]
public struct GhosttyRenderStateRowSelection
{
    [FieldOffset(0)] public nuint Size;
    [FieldOffset(8)] public ushort StartX;
    [FieldOffset(10)] public ushort EndX;
}

[StructLayout(LayoutKind.Sequential)]
public struct GhosttyTerminalScrollbar
{
    public ulong Total;
    public ulong Offset;
    public ulong Len;
}

public enum ScrollViewportTag : int
{
    Top = 0,
    Bottom = 1,
    Delta = 2,
    Row = 3,
}

[StructLayout(LayoutKind.Explicit, Size = 24)]
public struct GhosttyTerminalScrollViewport
{
    [FieldOffset(0)] public ScrollViewportTag Tag;
    [FieldOffset(8), AbiField("value.delta")] public nint Value;
}

[StructLayout(LayoutKind.Explicit, Size = 16)]
public struct GhosttyGridRef
{
    [FieldOffset(0)] public nint Node;
    [FieldOffset(8)] public ushort X;
    [FieldOffset(10)] public ushort Y;
}

public enum PointTag : int
{
    Active = 0,
    Viewport = 1,
    Screen = 2,
    History = 3,
}

[StructLayout(LayoutKind.Explicit, Size = 8)]
public struct GhosttyPointCoordinate
{
    [FieldOffset(0)] public ushort X;
    [FieldOffset(4)] public uint Y;
}

[StructLayout(LayoutKind.Explicit, Size = 24)]
public struct GhosttyPoint
{
    [FieldOffset(0)] public PointTag Tag;
    [FieldOffset(8), AbiField("value.coordinate.x")] public ushort X;
    [FieldOffset(12), AbiField("value.coordinate.y")] public uint Y;
}

[StructLayout(LayoutKind.Explicit, Size = 40)]
public struct GhosttySelection
{
    [FieldOffset(0)] public GhosttyGridRef Start;
    [FieldOffset(16)] public GhosttyGridRef End;
    [FieldOffset(32)] public byte Rectangle;
}

/// <summary>What a formatter writes (GhosttyFormatterFormat).</summary>
public enum FormatterFormat : int
{
    Plain = 0,
    Vt = 1,
    Html = 2,
}

/// <summary>A formatter setting, set with <see cref="Native.FormatterSet"/>. The comment on each is the value's type.</summary>
public enum FormatterOption : int
{
    Emit = 1, // FormatterFormat
    Unwrap = 2, // bool
    Trim = 3, // bool
    Selection = 4, // GhosttySelection, or NULL for the whole screen
    Extras = 5, // uint bit flags (GHOSTTY_FORMATTER_EXTRA_*), where NULL restores the default of the palette, and the cursor's style and hyperlink
}

[StructLayout(LayoutKind.Sequential)]
public struct GhosttySelectionGestureGeometry
{
    public uint Columns;
    public uint CellWidth;
    public uint PaddingLeft;
    public uint ScreenHeight;
}

/// <summary>
/// A caller's buffer that the library fills with codepoints. <see cref="Cap"/> is its size in codepoints, and the
/// library sets <see cref="Len"/> to how many there are in total. When that's more than fit, the call returns OutOfSpace.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct GhosttyCodepointBuffer
{
    public uint* Ptr;
    public nuint Cap;
    public nuint Len;
}

/// <summary>A list of Unicode scalar values (word boundaries), borrowed for the call that takes it.</summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct GhosttyCodepoints
{
    public uint* Ptr;
    public nuint Len;
}

[StructLayout(LayoutKind.Sequential)]
public struct GhosttySurfacePosition
{
    public double X;
    public double Y;
}

[StructLayout(LayoutKind.Explicit, Size = 40)]
public struct GhosttyMouseEncoderSize
{
    [FieldOffset(0)] public nuint Size;
    [FieldOffset(8)] public uint ScreenWidth;
    [FieldOffset(12)] public uint ScreenHeight;
    [FieldOffset(16)] public uint CellWidth;
    [FieldOffset(20)] public uint CellHeight;
    [FieldOffset(24)] public uint PaddingTop;
    [FieldOffset(28)] public uint PaddingBottom;
    [FieldOffset(32)] public uint PaddingRight;
    [FieldOffset(36)] public uint PaddingLeft;
}

[StructLayout(LayoutKind.Sequential)]
public struct GhosttySizeReportSize
{
    public ushort Rows;
    public ushort Columns;
    public uint CellWidth;
    public uint CellHeight;
}

[StructLayout(LayoutKind.Sequential)]
public struct GhosttyMousePosition
{
    public float X;
    public float Y;
}

[StructLayout(LayoutKind.Explicit, Size = 4)]
public struct GhosttyTerminalModeConfig
{
    [FieldOffset(0)] public ushort Mode;
    [FieldOffset(2)] public byte Value;
}

public static class GhosttyMode
{
    // Mirrors the static inline ghostty_mode_new() from modes.h.
    public static ushort New(ushort value, bool ansi) => (ushort)((value & 0x7FFF) | (ansi ? 0x8000 : 0));

    public static readonly ushort BracketedPaste = New(2004, false);
    public static readonly ushort FocusEvent = New(1004, false);
    public static readonly ushort AltScroll = New(1007, false);
    public static readonly ushort DecCkm = New(1, false);
    public static readonly ushort GraphemeCluster = New(2027, false);
    public static readonly ushort SyncOutput = New(2026, false);
    public static readonly ushort ColorSchemeReport = New(2031, false);
}

[Flags]
public enum GhosttyMods : ushort
{
    None = 0,
    Shift = 1 << 0,
    Ctrl = 1 << 1,
    Alt = 1 << 2,
    Super = 1 << 3,
    CapsLock = 1 << 4,
    NumLock = 1 << 5,
    ShiftSide = 1 << 6,
    CtrlSide = 1 << 7,
    AltSide = 1 << 8,
    SuperSide = 1 << 9,
}

public enum KeyAction : int
{
    Release = 0,
    Press = 1,
    Repeat = 2,
}

public enum MouseAction : int
{
    Press = 0,
    Release = 1,
    Motion = 2,
}

public enum MouseButton : int
{
    Unknown = 0,
    Left = 1,
    Right = 2,
    Middle = 3,
    Four = 4,
    Five = 5,
}

public enum TerminalOption : int
{
    Userdata = 1,
    WritePty = 2,
    Bell = 3,
    Xtversion = 5,
    TitleChanged = 6,
    Size = 7,
    ColorScheme = 8,
    ColorForeground = 12,
    ColorBackground = 13,
    ColorCursor = 14,
    ColorPalette = 15,
    KittyImageStorageLimit = 16,
    KittyImageMediumFile = 17,
    KittyImageMediumTempFile = 18,
    KittyImageMediumSharedMem = 19,
    Selection = 22,
    DefaultCursorStyle = 23,
    DefaultCursorBlink = 24,
    PwdChanged = 26,
    ClipboardWrite = 27,
    ScrollbackMaxBytes = 28,
    ScrollbackMaxLines = 29,
    DesktopNotification = 30,
    ProgressReport = 31,
    ModeDefault = 34,
    Mode = 35,
    ClipboardRead = 39,
    ResizePullScrollback = 41,
    RenderHold = 42,
}

public enum SysOption : int
{
    Userdata = 1,
    DecodePng = 2,
}

[StructLayout(LayoutKind.Explicit, Size = 24)]
public struct GhosttySysImage
{
    [FieldOffset(0)] public uint Width;
    [FieldOffset(4)] public uint Height;
    [FieldOffset(8)] public nint Data; // uint8_t*, allocated with the callback's allocator
    [FieldOffset(16)] public nuint DataLen;
}

public enum KittyGraphicsData : int
{
    Generation = 2,
}

public enum KittyImageFormat : int
{
    Rgb = 0,
    Rgba = 1,
    Png = 2,
    GrayAlpha = 3,
    Gray = 4,
}

public enum KittyGraphicsImageData : int
{
    Id = 1,
    Number = 2,
    Width = 3,
    Height = 4,
    Format = 5,
    Compression = 6,

    /// <summary>
    /// The decoded pixels (GhosttyString), valid until the terminal next changes, so read them under the lock. The get
    /// returns NoValue until they've arrived.
    /// </summary>
    Pixels = 7,

    Generation = 9,
}

public enum TerminalData : int
{
    Cols = 1,
    Rows = 2,
    CursorX = 3,
    CursorY = 4,
    ActiveScreen = 6,
    KittyKeyboardFlags = 8,
    Scrollbar = 9,
    MouseTracking = 11,
    Title = 12,
    Pwd = 13,
    ScrollbackRows = 15,
    ColorForeground = 18,
    ColorBackground = 19,
    ColorPaletteDefault = 25,
    Selection = 31,
    ViewportActive = 32,
    KittyGraphics = 30,
    Mode = 37,
    CursorAtPrompt = 39,
}

public enum RenderStateData : int
{
    Dirty = 3,
    RowIterator = 4,
    Cursor = 18,
    Colors = 19,
    ImageIterator = 22,

    /// <summary>
    /// Changes when the image list does (uint64). Compare it only for equality, and only with the last value read from
    /// the same render state, because it wraps around.
    /// </summary>
    ImagePlacementsGeneration = 23,
}

/// <summary>The protocol an image in the render state's list came from.</summary>
public enum ImageProtocol : int
{
    Kitty = 0,
}

/// <summary>Where an image is drawn: below the cell backgrounds, between them and the text, or above the text.</summary>
public enum ImageLayer : int
{
    BelowBackground = 0,
    BelowText = 1,
    AboveText = 2,
}

/// <summary>What <c>ghostty_render_state_image_get_multi</c> reads from an image list entry.</summary>
public enum RenderStateImageData : int
{
    Protocol = 1,
    ImageId = 2,
    ImageGeneration = 3,
    Layer = 4,
    Z = 5,

    /// <summary>The image's left column in the viewport (int32), negative when it starts left of it.</summary>
    ViewportCol = 6,

    /// <summary>The image's top row in the viewport (int32), negative when it starts above it.</summary>
    ViewportRow = 7,

    CellOffsetX = 10,
    CellOffsetY = 11,
    PixelWidth = 12,
    PixelHeight = 13,
    SourceX = 14,
    SourceY = 15,
    SourceWidth = 16,
    SourceHeight = 17,
}

public enum RenderStateRowData : int
{
    Dirty = 1,
    Cells = 3,
    Selection = 4,
}

public enum RenderStateRowCellsData : int
{
    Raw = 1,
    Style = 2,
    GraphemesLen = 3,
    GraphemesBuf = 4,
    BgColor = 5,
    FgColor = 6,
    Selected = 7,
    HasStyling = 8,
}

/// <summary>Screen row data (ghostty_row_get on a GhosttyRow from a grid ref).</summary>
public enum RowData : int
{
    Wrap = 1,
    WrapContinuation = 2,
    SemanticPrompt = 6,
}

/// <summary>
/// Whether a row holds a prompt, and if so its first line or a continuation line (GhosttyRowSemanticPrompt).
/// </summary>
public enum RowSemanticPrompt : int
{
    None = 0,
    Prompt = 1,
    PromptContinuation = 2,
}

public enum CellData : int
{
    Wide = 3,
    SemanticContent = 9,
}

/// <summary>What OSC 133 says a cell is (GhosttyCellSemanticContent).</summary>
public enum CellSemanticContent : int
{
    Output = 0,
    Input = 1,
    Prompt = 2,
}

public enum CellWide : int
{
    Narrow = 0,
    Wide = 1,
    SpacerTail = 2,
    SpacerHead = 3,
}

public enum KeyEncoderOption : int
{
    KittyFlags = 6,
}

public enum MouseEncoderOption : int
{
    Size = 3,
    AnyButtonPressed = 4,
    TrackLastCell = 5,
}

public enum SelectionGestureEventType : int
{
    Press = 0,
    Release = 1,
    Drag = 2,
    AutoscrollTick = 3,
    DeepPress = 4,
}

public enum SelectionGestureEventOption : int
{
    Ref = 1,
    Position = 2,
    RepeatDistance = 3,
    TimeNs = 4,
    RepeatIntervalNs = 5,

    /// <summary>Characters that end a word (GhosttyCodepoints*), or Ghostty's defaults when unset.</summary>
    WordBoundaryCodepoints = 6,
    Geometry = 9,
}

/// <summary>What <see cref="Native.BuildInfo"/> reports (GhosttyBuildInfo). The comment on each is the output's type.</summary>
public enum BuildInfoData : int
{
    VersionString = 5, // GhosttyString, such as "1.0.1"
    VersionMajor = 6, // ulong
    VersionMinor = 7, // ulong
}

public static unsafe partial class Native
{
    private const string Lib = "ghostty-vt";

    // ---- build info ----
    [LibraryImport(Lib, EntryPoint = "ghostty_build_info")]
    public static partial GhosttyResult BuildInfo(BuildInfoData data, void* value);

    // ---- memory ----
    [LibraryImport(Lib, EntryPoint = "ghostty_free")]
    public static partial void Free(nint allocator, byte* ptr, nuint len);

    // ---- terminal ----
    [LibraryImport(Lib, EntryPoint = "ghostty_terminal_new")]
    public static partial GhosttyResult TerminalNew(nint allocator, out nint terminal, ushort cols, ushort rows);

    [LibraryImport(Lib, EntryPoint = "ghostty_terminal_free")]
    public static partial void TerminalFree(nint terminal);

    [LibraryImport(Lib, EntryPoint = "ghostty_terminal_resize")]
    public static partial GhosttyResult TerminalResize(nint terminal, ushort cols, ushort rows, uint cellWidthPx, uint cellHeightPx);

    [LibraryImport(Lib, EntryPoint = "ghostty_terminal_set")]
    public static partial GhosttyResult TerminalSet(nint terminal, TerminalOption option, void* value);

    [LibraryImport(Lib, EntryPoint = "ghostty_terminal_get")]
    public static partial GhosttyResult TerminalGet(nint terminal, TerminalData data, void* value);

    [LibraryImport(Lib, EntryPoint = "ghostty_terminal_vt_write")]
    public static partial void TerminalVtWrite(nint terminal, byte* data, nuint len);

    [LibraryImport(Lib, EntryPoint = "ghostty_terminal_scroll_viewport")]
    public static partial GhosttyResult TerminalScrollViewport(nint terminal, GhosttyTerminalScrollViewport behavior);

    [LibraryImport(Lib, EntryPoint = "ghostty_terminal_grid_ref")]
    public static partial GhosttyResult TerminalGridRef(nint terminal, GhosttyPoint point, GhosttyGridRef* outRef);

    [LibraryImport(Lib, EntryPoint = "ghostty_terminal_grid_ref_track")]
    public static partial GhosttyResult TerminalGridRefTrack(nint terminal, GhosttyPoint point, out nint trackedRef);

    [LibraryImport(Lib, EntryPoint = "ghostty_tracked_grid_ref_point")]
    public static partial GhosttyResult TrackedGridRefPoint(nint trackedRef, PointTag tag, out GhosttyPointCoordinate point);

    [LibraryImport(Lib, EntryPoint = "ghostty_tracked_grid_ref_free")]
    public static partial void TrackedGridRefFree(nint trackedRef);

    [LibraryImport(Lib, EntryPoint = "ghostty_grid_ref_row")]
    public static partial GhosttyResult GridRefRow(GhosttyGridRef* gridRef, out ulong row);

    [LibraryImport(Lib, EntryPoint = "ghostty_row_get")]
    public static partial GhosttyResult ScreenRowGet(ulong row, RowData data, void* value);

    [LibraryImport(Lib, EntryPoint = "ghostty_grid_ref_cell")]
    public static partial GhosttyResult GridRefCell(GhosttyGridRef* gridRef, out ulong cell);

    [LibraryImport(Lib, EntryPoint = "ghostty_grid_ref_graphemes")]
    public static partial GhosttyResult GridRefGraphemes(GhosttyGridRef* gridRef, uint* buf, nuint bufLen, out nuint outLen);

    [LibraryImport(Lib, EntryPoint = "ghostty_grid_ref_hyperlink_uri")]
    public static partial GhosttyResult GridRefHyperlinkUri(GhosttyGridRef* gridRef, byte* buf, nuint bufLen, out nuint outLen);

    // ---- formatter ----
    [LibraryImport(Lib, EntryPoint = "ghostty_formatter_terminal_new")]
    public static partial GhosttyResult FormatterTerminalNew(nint allocator, out nint formatter, nint terminal);

    [LibraryImport(Lib, EntryPoint = "ghostty_formatter_set")]
    public static partial GhosttyResult FormatterSet(nint formatter, FormatterOption option, void* value);

    [LibraryImport(Lib, EntryPoint = "ghostty_formatter_format_alloc")]
    public static partial GhosttyResult FormatterFormatAlloc(nint formatter, nint allocator, out byte* outPtr, out nuint outLen);

    [LibraryImport(Lib, EntryPoint = "ghostty_formatter_free")]
    public static partial void FormatterFree(nint formatter);

    // ---- render state ----
    [LibraryImport(Lib, EntryPoint = "ghostty_render_state_new")]
    public static partial GhosttyResult RenderStateNew(nint allocator, out nint state);

    [LibraryImport(Lib, EntryPoint = "ghostty_render_state_free")]
    public static partial void RenderStateFree(nint state);

    [LibraryImport(Lib, EntryPoint = "ghostty_render_state_update")]
    public static partial GhosttyResult RenderStateUpdate(nint state, nint terminal);

    [LibraryImport(Lib, EntryPoint = "ghostty_render_state_get")]
    public static partial GhosttyResult RenderStateGet(nint state, RenderStateData data, void* value);

    [LibraryImport(Lib, EntryPoint = "ghostty_render_state_clean")]
    public static partial GhosttyResult RenderStateClean(nint state);

    [LibraryImport(Lib, EntryPoint = "ghostty_render_state_row_iterator_new")]
    public static partial GhosttyResult RowIteratorNew(nint allocator, out nint iterator);

    [LibraryImport(Lib, EntryPoint = "ghostty_render_state_row_iterator_free")]
    public static partial void RowIteratorFree(nint iterator);

    [LibraryImport(Lib, EntryPoint = "ghostty_render_state_row_iterator_next")]
    public static partial byte RowIteratorNext(nint iterator);

    [LibraryImport(Lib, EntryPoint = "ghostty_render_state_row_get")]
    public static partial GhosttyResult RowGet(nint iterator, RenderStateRowData data, void* value);

    [LibraryImport(Lib, EntryPoint = "ghostty_render_state_image_iterator_new")]
    public static partial GhosttyResult ImageIteratorNew(nint allocator, out nint iterator);

    [LibraryImport(Lib, EntryPoint = "ghostty_render_state_image_iterator_free")]
    public static partial void ImageIteratorFree(nint iterator);

    [LibraryImport(Lib, EntryPoint = "ghostty_render_state_image_iterator_next")]
    public static partial byte ImageIteratorNext(nint iterator);

    [LibraryImport(Lib, EntryPoint = "ghostty_render_state_image_get_multi")]
    public static partial GhosttyResult ImageGetMulti(nint iterator, nuint count, RenderStateImageData* keys, void** values,
        nuint* written);

    [LibraryImport(Lib, EntryPoint = "ghostty_render_state_row_cells_new")]
    public static partial GhosttyResult RowCellsNew(nint allocator, out nint cells);

    [LibraryImport(Lib, EntryPoint = "ghostty_render_state_row_cells_free")]
    public static partial void RowCellsFree(nint cells);

    [LibraryImport(Lib, EntryPoint = "ghostty_render_state_row_cells_next")]
    public static partial byte RowCellsNext(nint cells);

    [LibraryImport(Lib, EntryPoint = "ghostty_render_state_row_cells_get")]
    public static partial GhosttyResult RowCellsGet(nint cells, RenderStateRowCellsData data, void* value);

    [LibraryImport(Lib, EntryPoint = "ghostty_cell_get")]
    public static partial GhosttyResult CellGet(ulong cell, CellData data, void* value);

    // ---- sys / allocation ----
    [LibraryImport(Lib, EntryPoint = "ghostty_sys_set")]
    public static partial GhosttyResult SysSet(SysOption option, void* value);

    [LibraryImport(Lib, EntryPoint = "ghostty_alloc")]
    public static partial byte* Alloc(nint allocator, nuint len);

    // ---- kitty graphics ----
    [LibraryImport(Lib, EntryPoint = "ghostty_kitty_graphics_get")]
    public static partial GhosttyResult KittyGraphicsGet(nint graphics, KittyGraphicsData data, void* value);

    [LibraryImport(Lib, EntryPoint = "ghostty_kitty_graphics_image")]
    public static partial nint KittyGraphicsImage(nint graphics, uint imageId);

    [LibraryImport(Lib, EntryPoint = "ghostty_kitty_graphics_image_get")]
    public static partial GhosttyResult KittyImageGet(nint image, KittyGraphicsImageData data, void* value);

    /// <summary>
    /// Moves playing animations to the frame due at <paramref name="nowMs"/>. Success sets <paramref name="nextMs"/> to
    /// when the next frame is due, on the same clock, and NoValue means nothing is playing. It changes the storage, so
    /// image handles and pixel pointers from before it are invalid.
    /// </summary>
    [LibraryImport(Lib, EntryPoint = "ghostty_kitty_graphics_animation_tick")]
    public static partial GhosttyResult KittyAnimationTick(nint graphics, ulong nowMs, ulong* nextMs);

    // ---- key encoding ----
    [LibraryImport(Lib, EntryPoint = "ghostty_key_encoder_new")]
    public static partial GhosttyResult KeyEncoderNew(nint allocator, out nint encoder);

    [LibraryImport(Lib, EntryPoint = "ghostty_key_encoder_free")]
    public static partial void KeyEncoderFree(nint encoder);

    [LibraryImport(Lib, EntryPoint = "ghostty_key_encoder_set_from_terminal")]
    public static partial GhosttyResult KeyEncoderSetFromTerminal(nint encoder, nint terminal);

    [LibraryImport(Lib, EntryPoint = "ghostty_key_encoder_encode")]
    public static partial GhosttyResult KeyEncoderEncode(nint encoder, nint keyEvent, byte* outBuf, nuint outBufSize, out nuint outLen);

    [LibraryImport(Lib, EntryPoint = "ghostty_key_event_new")]
    public static partial GhosttyResult KeyEventNew(nint allocator, out nint keyEvent);

    [LibraryImport(Lib, EntryPoint = "ghostty_key_event_free")]
    public static partial void KeyEventFree(nint keyEvent);

    [LibraryImport(Lib, EntryPoint = "ghostty_key_event_set_action")]
    public static partial GhosttyResult KeyEventSetAction(nint keyEvent, KeyAction action);

    [LibraryImport(Lib, EntryPoint = "ghostty_key_event_set_key")]
    public static partial GhosttyResult KeyEventSetKey(nint keyEvent, GhosttyKey key);

    [LibraryImport(Lib, EntryPoint = "ghostty_key_event_set_mods")]
    public static partial GhosttyResult KeyEventSetMods(nint keyEvent, GhosttyMods mods);

    [LibraryImport(Lib, EntryPoint = "ghostty_key_event_set_consumed_mods")]
    public static partial GhosttyResult KeyEventSetConsumedMods(nint keyEvent, GhosttyMods mods);

    [LibraryImport(Lib, EntryPoint = "ghostty_key_event_set_utf8")]
    public static partial GhosttyResult KeyEventSetUtf8(nint keyEvent, GhosttyString utf8);

    [LibraryImport(Lib, EntryPoint = "ghostty_key_event_set_unshifted_codepoint")]
    public static partial GhosttyResult KeyEventSetUnshiftedCodepoint(nint keyEvent, uint codepoint);

    // ---- mouse encoding ----
    [LibraryImport(Lib, EntryPoint = "ghostty_mouse_encoder_new")]
    public static partial GhosttyResult MouseEncoderNew(nint allocator, out nint encoder);

    [LibraryImport(Lib, EntryPoint = "ghostty_mouse_encoder_free")]
    public static partial void MouseEncoderFree(nint encoder);

    [LibraryImport(Lib, EntryPoint = "ghostty_mouse_encoder_set")]
    public static partial GhosttyResult MouseEncoderSet(nint encoder, MouseEncoderOption option, void* value);

    [LibraryImport(Lib, EntryPoint = "ghostty_mouse_encoder_set_from_terminal")]
    public static partial GhosttyResult MouseEncoderSetFromTerminal(nint encoder, nint terminal);

    [LibraryImport(Lib, EntryPoint = "ghostty_mouse_encoder_encode")]
    public static partial GhosttyResult MouseEncoderEncode(nint encoder, nint mouseEvent, byte* outBuf, nuint outBufSize, out nuint outLen);

    [LibraryImport(Lib, EntryPoint = "ghostty_mouse_event_new")]
    public static partial GhosttyResult MouseEventNew(nint allocator, out nint mouseEvent);

    [LibraryImport(Lib, EntryPoint = "ghostty_mouse_event_free")]
    public static partial void MouseEventFree(nint mouseEvent);

    [LibraryImport(Lib, EntryPoint = "ghostty_mouse_event_set_action")]
    public static partial GhosttyResult MouseEventSetAction(nint mouseEvent, MouseAction action);

    [LibraryImport(Lib, EntryPoint = "ghostty_mouse_event_set_button")]
    public static partial GhosttyResult MouseEventSetButton(nint mouseEvent, MouseButton button);

    [LibraryImport(Lib, EntryPoint = "ghostty_mouse_event_clear_button")]
    public static partial GhosttyResult MouseEventClearButton(nint mouseEvent);

    [LibraryImport(Lib, EntryPoint = "ghostty_mouse_event_set_mods")]
    public static partial GhosttyResult MouseEventSetMods(nint mouseEvent, GhosttyMods mods);

    [LibraryImport(Lib, EntryPoint = "ghostty_mouse_event_set_position")]
    public static partial GhosttyResult MouseEventSetPosition(nint mouseEvent, GhosttyMousePosition position);

    // ---- selection gestures ----
    [LibraryImport(Lib, EntryPoint = "ghostty_selection_gesture_new")]
    public static partial GhosttyResult SelectionGestureNew(nint allocator, out nint gesture);

    [LibraryImport(Lib, EntryPoint = "ghostty_selection_gesture_free")]
    public static partial void SelectionGestureFree(nint gesture, nint terminal);

    [LibraryImport(Lib, EntryPoint = "ghostty_selection_gesture_reset")]
    public static partial void SelectionGestureReset(nint gesture, nint terminal);

    [LibraryImport(Lib, EntryPoint = "ghostty_selection_gesture_event")]
    public static partial GhosttyResult SelectionGestureEvent(nint gesture, nint terminal, nint gestureEvent, GhosttySelection* outSelection);

    [LibraryImport(Lib, EntryPoint = "ghostty_selection_gesture_event_new")]
    public static partial GhosttyResult SelectionGestureEventNew(nint allocator, out nint gestureEvent, SelectionGestureEventType type);

    [LibraryImport(Lib, EntryPoint = "ghostty_selection_gesture_event_free")]
    public static partial void SelectionGestureEventFree(nint gestureEvent);

    [LibraryImport(Lib, EntryPoint = "ghostty_selection_gesture_event_set")]
    public static partial GhosttyResult SelectionGestureEventSet(nint gestureEvent, SelectionGestureEventOption option, void* value);

    // ---- paste ----
    [LibraryImport(Lib, EntryPoint = "ghostty_paste_encode")]
    public static partial GhosttyResult PasteEncode(byte* data, nuint dataLen, byte bracketed, byte* buf, nuint bufLen, out nuint outWritten);

    /// <summary>
    /// Derives the 216-color cube and grayscale ramp (16-255) from the base 16 and the colors. Set bits in
    /// <paramref name="skip"/> (4 words) keep those entries.
    /// </summary>
    [LibraryImport(Lib, EntryPoint = "ghostty_color_palette_generate")]
    public static partial void ColorPaletteGenerate(GhosttyColorRgb* basePalette, ulong* skip, GhosttyColorRgb* background, GhosttyColorRgb* foreground,
        byte harmonious, GhosttyColorRgb* output);

    [LibraryImport(Lib, EntryPoint = "ghostty_color_scheme_report_encode")]
    public static partial GhosttyResult ColorSchemeReportEncode(ColorScheme scheme, byte* buf, nuint bufLen, out nuint outWritten);
}
