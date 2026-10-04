using System.Buffers.Binary;

namespace Zinc.Shaders;

/// <summary>
/// The uniform block of Ghostty's shader prefix (<see cref="ShaderSource.Prefix"/>) as the bytes of a constant buffer:
/// std140, the layout SPIRV-Cross keeps in its HLSL (packoffsets; ShaderTests checks every offset against it).
/// Positions are in pixels with y down (Ghostty's Metal convention, which Direct3D shares).
/// </summary>
public sealed class ShaderUniforms
{
    /// <summary>281 registers of 16 bytes.</summary>
    public const int Size = 281 * 16;

    /// <summary>The buffer, ready to copy into the GPU's.</summary>
    public byte[] Bytes { get; } = new byte[Size];

    /// <summary>Byte offsets by uniform name (arrays: their first element; each element takes a register).</summary>
    public static IReadOnlyDictionary<string, int> Offsets { get; } = new Dictionary<string, int>
    {
        ["iResolution"] = 0 * 16,
        ["iTime"] = 0 * 16 + 12,
        ["iTimeDelta"] = 1 * 16,
        ["iFrameRate"] = 1 * 16 + 4,
        ["iFrame"] = 1 * 16 + 8,
        ["iChannelTime"] = 2 * 16,
        ["iChannelResolution"] = 6 * 16,
        ["iMouse"] = 10 * 16,
        ["iDate"] = 11 * 16,
        ["iSampleRate"] = 12 * 16,
        ["iCurrentCursor"] = 13 * 16,
        ["iPreviousCursor"] = 14 * 16,
        ["iCurrentCursorColor"] = 15 * 16,
        ["iPreviousCursorColor"] = 16 * 16,
        ["iCurrentCursorStyle"] = 17 * 16,
        ["iPreviousCursorStyle"] = 17 * 16 + 4,
        ["iCursorVisible"] = 17 * 16 + 8,
        ["iTimeCursorChange"] = 17 * 16 + 12,
        ["iTimeFocus"] = 18 * 16,
        ["iFocus"] = 18 * 16 + 4,
        ["iPalette"] = 19 * 16,
        ["iBackgroundColor"] = 275 * 16,
        ["iForegroundColor"] = 276 * 16,
        ["iCursorColor"] = 277 * 16,
        ["iCursorText"] = 278 * 16,
        ["iSelectionForegroundColor"] = 279 * 16,
        ["iSelectionBackgroundColor"] = 280 * 16,
    };

    /// <summary>The picture's size in pixels: iResolution and iChannelResolution[0] (the terminal's picture).</summary>
    public void SetResolution(float width, float height)
    {
        Floats("iResolution", width, height, 1);
        Floats("iChannelResolution", width, height, 1);
    }

    /// <summary>Seconds since the shader started, since the last frame, and the frame's number.</summary>
    public void SetTime(float time, float delta, int frame)
    {
        Floats("iTime", time);
        Floats("iTimeDelta", delta);
        Floats("iFrameRate", delta > 0 ? 1 / delta : 0);
        Int("iFrame", frame);
        Floats("iChannelTime", time);
    }

    /// <summary>iDate: year, month (0-11, as Shadertoy), day, seconds since midnight.</summary>
    public void SetDate(DateTime now) =>
        Floats("iDate", now.Year, now.Month - 1, now.Day, (float)now.TimeOfDay.TotalSeconds);

    /// <summary>Whether the terminal has focus, and when that last changed (seconds, the iTime clock).</summary>
    public void SetFocus(bool focused, float since)
    {
        Int("iFocus", focused ? 1 : 0);
        Floats("iTimeFocus", since);
    }

    /// <summary>
    /// The cursor as drawn this frame, in pixels: its left edge, its bottom edge (Ghostty's "+Y edge" with y down: a
    /// shader's rectangle is x..x+w, y-h..y), its size, its color (0-1, alpha included). As in Ghostty, a change of
    /// place or color makes the old one iPreviousCursor and stamps iTimeCursorChange with <paramref name="time"/>
    /// (cursor trails animate from there). Not called for frames without a cursor drawn (blink, hidden): the last stays.
    /// </summary>
    public void SetCursor(float x, float y, float width, float height, float r, float g, float b, float a, float time)
    {
        Span<float> cursor = [x, y, width, height];
        Span<float> color = [r, g, b, a];
        if (Same("iCurrentCursor", cursor) && Same("iCurrentCursorColor", color))
        {
            return;
        }

        Copy("iCurrentCursor", "iPreviousCursor");
        Copy("iCurrentCursorColor", "iPreviousCursorColor");
        Floats("iCurrentCursor", cursor);
        Floats("iCurrentCursorColor", color);
        Floats("iTimeCursorChange", time);
    }

    /// <summary>Ghostty's cursor style codes (CURSORSTYLE_* in the prefix).</summary>
    public enum CursorStyle { Block = 0, BlockHollow = 1, Bar = 2, Underline = 3, Lock = 4 }

    /// <summary>The cursor's style (the previous one kept when it changes) and whether the terminal shows a cursor at all.</summary>
    public void SetCursorStyle(CursorStyle style, bool visible)
    {
        var current = BinaryPrimitives.ReadInt32LittleEndian(Bytes.AsSpan(Offsets["iCurrentCursorStyle"], 4));
        if (current != (int)style)
        {
            Int("iPreviousCursorStyle", current);
            Int("iCurrentCursorStyle", (int)style);
        }

        Int("iCursorVisible", visible ? 1 : 0);
    }

    /// <summary>The 256-color palette as the terminal has it now (programs may change it), 3 bytes a color.</summary>
    public void SetPalette(ReadOnlySpan<byte> rgb)
    {
        var at = Offsets["iPalette"];
        for (var i = 0; i < 256 && i * 3 + 2 < rgb.Length; i++)
        {
            for (var c = 0; c < 3; c++)
            {
                BinaryPrimitives.WriteSingleLittleEndian(Bytes.AsSpan(at + i * 16 + c * 4, 4), rgb[i * 3 + c] / 255f);
            }
        }
    }

    /// <summary>A vec3 color uniform from 0-255 components (iBackgroundColor, iCursorColor...).</summary>
    public void SetColor(string name, byte r, byte g, byte b) => Floats(name, r / 255f, g / 255f, b / 255f);

    /// <summary>
    /// The mouse as Shadertoy gives it, in pixels: xy = where it is while the left button is down (kept after), zw = where
    /// the button went down, negative once it's up.
    /// </summary>
    public void SetMouse(float x, float y, float downX, float downY, bool down) =>
        Floats("iMouse", x, y, down ? downX : -Math.Abs(downX), down ? downY : -Math.Abs(downY));

    private bool Same(string name, ReadOnlySpan<float> values)
    {
        var at = Offsets[name];
        for (var i = 0; i < values.Length; i++)
        {
            if (BinaryPrimitives.ReadSingleLittleEndian(Bytes.AsSpan(at + i * 4, 4)) != values[i])
            {
                return false;
            }
        }

        return true;
    }

    private void Copy(string from, string to) => Bytes.AsSpan(Offsets[from], 16).CopyTo(Bytes.AsSpan(Offsets[to], 16));

    /// <summary>A uniform's floats (vecN, or a float).</summary>
    public void Floats(string name, params ReadOnlySpan<float> values)
    {
        var at = Offsets[name];
        for (var i = 0; i < values.Length; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(Bytes.AsSpan(at + i * 4, 4), values[i]);
        }
    }

    /// <summary>An int uniform.</summary>
    public void Int(string name, int value) => BinaryPrimitives.WriteInt32LittleEndian(Bytes.AsSpan(Offsets[name], 4), value);

    /// <summary>A float read back (tests).</summary>
    public float Float(string name, int component = 0) =>
        BinaryPrimitives.ReadSingleLittleEndian(Bytes.AsSpan(Offsets[name] + component * 4, 4));
}
