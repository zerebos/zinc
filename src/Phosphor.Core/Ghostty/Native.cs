using System.Runtime.InteropServices;

namespace Phosphor.Ghostty;

// Raw bindings for libghostty-vt (ghostty-vt.dll).
//
// The C API is explicitly unstable. Struct layouts here use explicit offsets
// taken from the library's own ABI manifest (`ghostty_type_json()`), so when
// bumping the pinned commit in scripts/build-libghostty.sh, re-check them.
// C `bool` is mapped to `byte` everywhere to keep every signature blittable.

/// <summary>
/// Maps a flattened field to its dotted path in the C struct (through unions), for the
/// ABI check in Phosphor.Tests. Fields without it map to the snake_case of their name.
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

/// <summary>An OSC 52 / 1337 / 5522 clipboard write; answer it by calling <see cref="Reply"/>.</summary>
[StructLayout(LayoutKind.Explicit, Size = 72)]
public struct GhosttyClipboardWrite
{
    [FieldOffset(0)] public nuint Size;
    [FieldOffset(8)] public ClipboardLocation Location;
    [FieldOffset(16)] public nint Contents; // GhosttyClipboardContent*
    [FieldOffset(24)] public nuint ContentsLen;
    [FieldOffset(32)] public GhosttyString Name;
    [FieldOffset(48)] public byte Granted;
    [FieldOffset(49)] public byte CanRemember;
    [FieldOffset(56)] public nint Ctx;
    [FieldOffset(64)] public nint Reply; // void (*)(const GhosttyClipboardWrite*, const GhosttyClipboardWriteReply*)
}

[StructLayout(LayoutKind.Explicit, Size = 16)]
public struct GhosttyClipboardWriteReply
{
    [FieldOffset(0)] public nuint Size;
    [FieldOffset(8)] public ClipboardWriteResult Result;
    [FieldOffset(12)] public byte Remember;
}

/// <summary>An OSC 52 "?" / 5522 clipboard read; answer it by calling <see cref="Reply"/>.</summary>
[StructLayout(LayoutKind.Explicit, Size = 80)]
public struct GhosttyClipboardRead
{
    [FieldOffset(0)] public nuint Size;
    [FieldOffset(8)] public ClipboardLocation Location;
    [FieldOffset(16)] public nint Mimes; // GhosttyString*
    [FieldOffset(24)] public nuint MimesLen;
    [FieldOffset(32)] public byte List;
    [FieldOffset(40)] public GhosttyString Name;
    [FieldOffset(56)] public byte Granted;
    [FieldOffset(57)] public byte CanRemember;
    [FieldOffset(64)] public nint Ctx;
    [FieldOffset(72)] public nint Reply; // void (*)(const GhosttyClipboardRead*, const GhosttyClipboardReadReply*)
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
    [FieldOffset(20)] public CursorVisualStyle VisualStyle;
}

public enum CursorVisualStyle : int
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

[StructLayout(LayoutKind.Explicit, Size = 72)]
public struct GhosttyStyle
{
    [FieldOffset(0)] public nuint Size;
    // Style colors as set (tag: 0 none, 1 palette, 2 rgb; value: palette index or r,g,b bytes).
    // Renderers get resolved colors from the row-cells API; kitty placeholders need these.
    [FieldOffset(8), AbiField("fg_color.tag")] public int FgColorTag;
    [FieldOffset(16), AbiField("fg_color.value._padding")] public ulong FgColorValue;
    [FieldOffset(40), AbiField("underline_color.tag")] public int UnderlineColorTag;
    [FieldOffset(48), AbiField("underline_color.value._padding")] public ulong UnderlineColorValue;
    [FieldOffset(56)] public byte Bold;
    [FieldOffset(57)] public byte Italic;
    [FieldOffset(58)] public byte Faint;
    [FieldOffset(59)] public byte Blink;
    [FieldOffset(60)] public byte Inverse;
    [FieldOffset(61)] public byte Invisible;
    [FieldOffset(62)] public byte Strikethrough;
    [FieldOffset(63)] public byte Overline;
    [FieldOffset(64)] public int Underline;
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

[StructLayout(LayoutKind.Explicit, Size = 24)]
public struct GhosttyGridRef
{
    [FieldOffset(0)] public nuint Size;
    [FieldOffset(8)] public nint Node;
    [FieldOffset(16)] public ushort X;
    [FieldOffset(18)] public ushort Y;

    public static GhosttyGridRef Init() => new() { Size = 24 };
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

[StructLayout(LayoutKind.Explicit, Size = 64)]
public struct GhosttySelection
{
    [FieldOffset(0)] public nuint Size;
    [FieldOffset(8)] public GhosttyGridRef Start;
    [FieldOffset(32)] public GhosttyGridRef End;
    [FieldOffset(56)] public byte Rectangle;

    public static GhosttySelection Init() => new() { Size = 64 };
}

[StructLayout(LayoutKind.Explicit, Size = 24)]
public struct GhosttyTerminalSelectionFormatOptions
{
    [FieldOffset(0)] public nuint Size;
    [FieldOffset(8)] public int Emit; // GhosttyFormatterFormat: 0 = plain
    [FieldOffset(12)] public byte Unwrap;
    [FieldOffset(13)] public byte Trim;
    [FieldOffset(16)] public nint Selection; // GhosttySelection*, NULL = active selection
}

[StructLayout(LayoutKind.Explicit, Size = 16)]
public struct GhosttyFormatterScreenExtra
{
    [FieldOffset(0)] public nuint Size;
    [FieldOffset(8)] public byte Cursor;
    [FieldOffset(9)] public byte Style;
    [FieldOffset(10)] public byte Hyperlink;
    [FieldOffset(11)] public byte Protection;
    [FieldOffset(12)] public byte KittyKeyboard;
    [FieldOffset(13)] public byte Charsets;
}

[StructLayout(LayoutKind.Explicit, Size = 32)]
public struct GhosttyFormatterTerminalExtra
{
    [FieldOffset(0)] public nuint Size;
    [FieldOffset(8)] public byte Palette;
    [FieldOffset(9)] public byte Modes;
    [FieldOffset(10)] public byte ScrollingRegion;
    [FieldOffset(11)] public byte Tabstops;
    [FieldOffset(12)] public byte Pwd;
    [FieldOffset(13)] public byte Keyboard;
    [FieldOffset(16)] public GhosttyFormatterScreenExtra Screen;
}

[StructLayout(LayoutKind.Explicit, Size = 56)]
public struct GhosttyFormatterTerminalOptions
{
    [FieldOffset(0)] public nuint Size;
    [FieldOffset(8)] public int Emit; // GhosttyFormatterFormat: 0 = plain
    [FieldOffset(12)] public byte Unwrap;
    [FieldOffset(13)] public byte Trim;
    [FieldOffset(16)] public GhosttyFormatterTerminalExtra Extra;
    [FieldOffset(48)] public nint Selection; // const GhosttySelection*, NULL = whole screen

    public static GhosttyFormatterTerminalOptions Init() => new()
    {
        Size = 56,
        Extra = new GhosttyFormatterTerminalExtra { Size = 32, Screen = new GhosttyFormatterScreenExtra { Size = 16 } },
    };
}

[StructLayout(LayoutKind.Sequential)]
public struct GhosttySelectionGestureGeometry
{
    public uint Columns;
    public uint CellWidth;
    public uint PaddingLeft;
    public uint ScreenHeight;
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
    Userdata = 0,
    WritePty = 1,
    Bell = 2,
    TitleChanged = 5,
    Size = 6,
    ColorForeground = 11,
    ColorBackground = 12,
    ColorCursor = 13,
    ColorPalette = 14,
    KittyImageStorageLimit = 15,
    KittyImageMediumFile = 16,
    KittyImageMediumTempFile = 17,
    KittyImageMediumSharedMem = 18,
    Selection = 21,
    PwdChanged = 25,
    ClipboardWrite = 26,
    ScrollbackMaxBytes = 27,
    ScrollbackMaxLines = 28,
    ModeDefault = 33,
    ClipboardRead = 38,
}

public enum SysOption : int
{
    Userdata = 0,
    DecodePng = 1,
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
    PlacementIterator = 1,
    Generation = 2,
}

public enum KittyGraphicsPlacementData : int
{
    ImageId = 1,
    PlacementId = 2,
    IsVirtual = 3,
    XOffset = 4,
    YOffset = 5,
    SourceX = 6,
    SourceY = 7,
    SourceWidth = 8,
    SourceHeight = 9,
    Columns = 10,
    Rows = 11,
    Z = 12,
}

public enum KittyPlacementLayer : int
{
    All = 0,
    BelowBg = 1,
    BelowText = 2,
    AboveText = 3,
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
    DataPtr = 7,
    DataLen = 8,
    Generation = 9,
}

[StructLayout(LayoutKind.Explicit, Size = 56)]
public struct GhosttyKittyGraphicsPlacementRenderInfo
{
    [FieldOffset(0)] public nuint Size;
    [FieldOffset(8)] public uint PixelWidth;
    [FieldOffset(12)] public uint PixelHeight;
    [FieldOffset(16)] public uint GridCols;
    [FieldOffset(20)] public uint GridRows;
    [FieldOffset(24)] public int ViewportCol;
    [FieldOffset(28)] public int ViewportRow;
    [FieldOffset(32)] public byte ViewportVisible;
    [FieldOffset(36)] public uint SourceX;
    [FieldOffset(40)] public uint SourceY;
    [FieldOffset(44)] public uint SourceWidth;
    [FieldOffset(48)] public uint SourceHeight;

    public static GhosttyKittyGraphicsPlacementRenderInfo Init() => new() { Size = 56 };
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
    KittyFlags = 5,
}

public enum MouseEncoderOption : int
{
    Size = 2,
    AnyButtonPressed = 3,
    TrackLastCell = 4,
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
    Ref = 0,
    Position = 1,
    RepeatDistance = 2,
    TimeNs = 3,
    RepeatIntervalNs = 4,
    Geometry = 8,
}

public static unsafe partial class Native
{
    private const string Lib = "ghostty-vt";

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
    public static partial void TerminalScrollViewport(nint terminal, GhosttyTerminalScrollViewport behavior);

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

    [LibraryImport(Lib, EntryPoint = "ghostty_terminal_selection_format_alloc")]
    public static partial GhosttyResult TerminalSelectionFormatAlloc(
        nint terminal, nint allocator, GhosttyTerminalSelectionFormatOptions options, out byte* outPtr, out nuint outLen);

    // ---- formatter ----
    [LibraryImport(Lib, EntryPoint = "ghostty_formatter_terminal_new")]
    public static partial GhosttyResult FormatterTerminalNew(nint allocator, out nint formatter, nint terminal, GhosttyFormatterTerminalOptions options);

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

    [LibraryImport(Lib, EntryPoint = "ghostty_kitty_graphics_placement_iterator_new")]
    public static partial GhosttyResult KittyPlacementIteratorNew(nint allocator, out nint iterator);

    [LibraryImport(Lib, EntryPoint = "ghostty_kitty_graphics_placement_iterator_free")]
    public static partial void KittyPlacementIteratorFree(nint iterator);

    [LibraryImport(Lib, EntryPoint = "ghostty_kitty_graphics_placement_next")]
    public static partial byte KittyPlacementNext(nint iterator);

    [LibraryImport(Lib, EntryPoint = "ghostty_kitty_graphics_placement_get")]
    public static partial GhosttyResult KittyPlacementGet(nint iterator, KittyGraphicsPlacementData data, void* value);

    [LibraryImport(Lib, EntryPoint = "ghostty_kitty_graphics_placement_render_info")]
    public static partial GhosttyResult KittyPlacementRenderInfo(nint iterator, nint image, nint terminal,
        GhosttyKittyGraphicsPlacementRenderInfo* info);

    // ---- key encoding ----
    [LibraryImport(Lib, EntryPoint = "ghostty_key_encoder_new")]
    public static partial GhosttyResult KeyEncoderNew(nint allocator, out nint encoder);

    [LibraryImport(Lib, EntryPoint = "ghostty_key_encoder_free")]
    public static partial void KeyEncoderFree(nint encoder);

    [LibraryImport(Lib, EntryPoint = "ghostty_key_encoder_setopt_from_terminal")]
    public static partial void KeyEncoderSetoptFromTerminal(nint encoder, nint terminal);

    [LibraryImport(Lib, EntryPoint = "ghostty_key_encoder_encode")]
    public static partial GhosttyResult KeyEncoderEncode(nint encoder, nint keyEvent, byte* outBuf, nuint outBufSize, out nuint outLen);

    [LibraryImport(Lib, EntryPoint = "ghostty_key_event_new")]
    public static partial GhosttyResult KeyEventNew(nint allocator, out nint keyEvent);

    [LibraryImport(Lib, EntryPoint = "ghostty_key_event_free")]
    public static partial void KeyEventFree(nint keyEvent);

    [LibraryImport(Lib, EntryPoint = "ghostty_key_event_set_action")]
    public static partial void KeyEventSetAction(nint keyEvent, KeyAction action);

    [LibraryImport(Lib, EntryPoint = "ghostty_key_event_set_key")]
    public static partial void KeyEventSetKey(nint keyEvent, GhosttyKey key);

    [LibraryImport(Lib, EntryPoint = "ghostty_key_event_set_mods")]
    public static partial void KeyEventSetMods(nint keyEvent, GhosttyMods mods);

    [LibraryImport(Lib, EntryPoint = "ghostty_key_event_set_consumed_mods")]
    public static partial void KeyEventSetConsumedMods(nint keyEvent, GhosttyMods mods);

    [LibraryImport(Lib, EntryPoint = "ghostty_key_event_set_utf8")]
    public static partial void KeyEventSetUtf8(nint keyEvent, byte* utf8, nuint len);

    [LibraryImport(Lib, EntryPoint = "ghostty_key_event_set_unshifted_codepoint")]
    public static partial void KeyEventSetUnshiftedCodepoint(nint keyEvent, uint codepoint);

    // ---- mouse encoding ----
    [LibraryImport(Lib, EntryPoint = "ghostty_mouse_encoder_new")]
    public static partial GhosttyResult MouseEncoderNew(nint allocator, out nint encoder);

    [LibraryImport(Lib, EntryPoint = "ghostty_mouse_encoder_free")]
    public static partial void MouseEncoderFree(nint encoder);

    [LibraryImport(Lib, EntryPoint = "ghostty_mouse_encoder_setopt")]
    public static partial void MouseEncoderSetopt(nint encoder, MouseEncoderOption option, void* value);

    [LibraryImport(Lib, EntryPoint = "ghostty_mouse_encoder_setopt_from_terminal")]
    public static partial void MouseEncoderSetoptFromTerminal(nint encoder, nint terminal);

    [LibraryImport(Lib, EntryPoint = "ghostty_mouse_encoder_encode")]
    public static partial GhosttyResult MouseEncoderEncode(nint encoder, nint mouseEvent, byte* outBuf, nuint outBufSize, out nuint outLen);

    [LibraryImport(Lib, EntryPoint = "ghostty_mouse_event_new")]
    public static partial GhosttyResult MouseEventNew(nint allocator, out nint mouseEvent);

    [LibraryImport(Lib, EntryPoint = "ghostty_mouse_event_free")]
    public static partial void MouseEventFree(nint mouseEvent);

    [LibraryImport(Lib, EntryPoint = "ghostty_mouse_event_set_action")]
    public static partial void MouseEventSetAction(nint mouseEvent, MouseAction action);

    [LibraryImport(Lib, EntryPoint = "ghostty_mouse_event_set_button")]
    public static partial void MouseEventSetButton(nint mouseEvent, MouseButton button);

    [LibraryImport(Lib, EntryPoint = "ghostty_mouse_event_clear_button")]
    public static partial void MouseEventClearButton(nint mouseEvent);

    [LibraryImport(Lib, EntryPoint = "ghostty_mouse_event_set_mods")]
    public static partial void MouseEventSetMods(nint mouseEvent, GhosttyMods mods);

    [LibraryImport(Lib, EntryPoint = "ghostty_mouse_event_set_position")]
    public static partial void MouseEventSetPosition(nint mouseEvent, GhosttyMousePosition position);

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
}
