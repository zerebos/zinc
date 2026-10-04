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
