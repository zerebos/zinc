using System.Runtime.InteropServices;

namespace Zinc.Shaders;

/// <summary>
/// GLSL to HLSL (shader model 5) for custom shaders, through zinc-shader.dll (glslang + SPIRV-Cross, as Ghostty does
/// for Metal; scripts/build/shader.ps1). The DLL is loaded on the first call only, so terminals without shaders never
/// load it. Takes ~30 ms a shader: call it off the UI thread.
/// </summary>
public static partial class ShaderTranslator
{
    // The DLL keeps its last error in globals (and glslang its init state): one call at a time.
    private static readonly Lock Gate = new();

    /// <summary>
    /// Translates <paramref name="shader"/> (a Ghostty/Shadertoy shader, without the prefix) to HLSL. False: why not,
    /// in <paramref name="error"/>, in the shader's own line numbers and named by <paramref name="file"/>.
    /// </summary>
    public static bool TryTranslate(string shader, string file, out string hlsl, out string error)
    {
        (hlsl, error) = ("", "");
        int result;
        string text;
        lock (Gate)
        {
            result = zinc_shader_glsl_to_hlsl(ShaderSource.Complete(shader), out var output);
            text = Marshal.PtrToStringUTF8(output) ?? "";
            zinc_shader_free(output);
        }

        if (result != 0)
        {
            error = ShaderSource.Describe(text, file);
            return false;
        }

        hlsl = text;
        return true;
    }

    [LibraryImport("zinc-shader", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int zinc_shader_glsl_to_hlsl(string glsl, out nint text);

    [LibraryImport("zinc-shader")]
    private static partial void zinc_shader_free(nint text);
}
