using System.Text;
using System.Text.RegularExpressions;

namespace Zinc.Shaders;

/// <summary>
/// A custom shader's GLSL as Ghostty runs it: its Shadertoy-style prefix (the uniforms, iChannel0 = the terminal's
/// picture, main() calling the shader's mainImage) goes first, so Ghostty's and Shadertoy's shaders compile unchanged;
/// glslang's messages are given back in the shader file's own line numbers.
/// </summary>
public static partial class ShaderSource
{
    /// <summary>Ghostty's src/renderer/shaders/shadertoy_prefix.glsl (the uniform block layout is also ShaderUniforms').</summary>
    public const string Prefix = """
        #version 430 core
        
        layout(binding = 1, std140) uniform Globals {
            uniform vec3  iResolution;
            uniform float iTime;
            uniform float iTimeDelta;
            uniform float iFrameRate;
            uniform int   iFrame;
            uniform float iChannelTime[4];
            uniform vec3  iChannelResolution[4];
            uniform vec4  iMouse;
            uniform vec4  iDate;
            uniform float iSampleRate;
            uniform vec4  iCurrentCursor;
            uniform vec4  iPreviousCursor;
            uniform vec4  iCurrentCursorColor;
            uniform vec4  iPreviousCursorColor;
            uniform int   iCurrentCursorStyle;
            uniform int   iPreviousCursorStyle;
            uniform int   iCursorVisible;
            uniform float iTimeCursorChange;
            uniform float iTimeFocus;
            uniform int iFocus;
            uniform vec3  iPalette[256];
            uniform vec3  iBackgroundColor;
            uniform vec3  iForegroundColor;
            uniform vec3  iCursorColor;
            uniform vec3  iCursorText;
            uniform vec3  iSelectionForegroundColor;
            uniform vec3  iSelectionBackgroundColor;
        };
        
        #define CURSORSTYLE_BLOCK        0
        #define CURSORSTYLE_BLOCK_HOLLOW 1
        #define CURSORSTYLE_BAR          2
        #define CURSORSTYLE_UNDERLINE    3
        #define CURSORSTYLE_LOCK         4
        
        layout(binding = 0) uniform sampler2D iChannel0;
        
        // These are unused currently by Ghostty:
        // layout(binding = 1) uniform sampler2D iChannel1;
        // layout(binding = 2) uniform sampler2D iChannel2;
        // layout(binding = 3) uniform sampler2D iChannel3;
        
        layout(location = 0) in vec4 gl_FragCoord;
        layout(location = 0) out vec4 _fragColor;
        
        #define texture2D texture
        
        void mainImage( out vec4 fragColor, in vec2 fragCoord );
        void main() { mainImage (_fragColor, gl_FragCoord.xy); }
        """;

    // The prefix, then a line break, then the shader: its line 1 is the prefix's line count + 1.
    private static readonly int LineOffset = Prefix.Count(c => c == '\n') + 1;

    /// <summary>The complete fragment shader glslang compiles.</summary>
    public static string Complete(string shader) => Prefix + "\n" + shader;

    /// <summary>
    /// Whether the shader changes over time on its own (it reads the clock or the frame number), so it has to be
    /// redrawn every refresh while it animates; other shaders are drawn only when the terminal changes.
    /// </summary>
    public static bool UsesTime(string shader) => TimeUniforms().IsMatch(WithoutComments(shader));

    [GeneratedRegex(@"\bi(Time|TimeDelta|Frame|FrameRate|Date|ChannelTime)\b")]
    private static partial Regex TimeUniforms();

    private static string WithoutComments(string glsl) => Comments().Replace(glsl, " ");

    [GeneratedRegex(@"//[^\n]*|/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex Comments();

    /// <summary>
    /// glslang's log made readable: "crt.glsl, line 12: 'bar': undeclared identifier", one problem a line, its summary
    /// lines ("3 compilation errors") left out.
    /// </summary>
    public static string Describe(string log, string file)
    {
        var name = Path.GetFileName(file);
        var lines = new List<string>();
        foreach (var raw in log.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || Summary().IsMatch(line) || line.Contains("compilation terminated"))
            {
                continue;
            }

            if (line.Contains("No function definition (body) found") && log.Contains("mainImage"))
            {
                lines.Add($"{name}: no mainImage function (a shader defines void mainImage(out vec4 fragColor, in vec2 fragCoord))");
                break;
            }

            var located = Located().Match(line);
            if (!located.Success)
            {
                lines.Add($"{name}: {Strip(line)}");
                continue;
            }

            var number = int.Parse(located.Groups[2].Value) - LineOffset;
            var message = Tidy(located.Groups[3].Value);
            lines.Add(number >= 1 ? $"{name}, line {number}: {message}" : $"{name}: {message}");
        }

        return lines.Count == 0 ? $"{name}: {log.Trim()}" : string.Join('\n', lines.Distinct());
    }

    [GeneratedRegex(@"^ERROR:\s*\d+ compilation errors")]
    private static partial Regex Summary();

    [GeneratedRegex(@"^(ERROR|WARNING):\s*\d+:(\d+):\s*(.*)$")]
    private static partial Regex Located();

    private static string Strip(string line) => Regex.Replace(line, @"^(ERROR|WARNING):\s*", "");

    /// <summary>"'bar' : undeclared identifier" → "'bar': undeclared identifier"; "'' :  syntax error" → "syntax error".</summary>
    private static string Tidy(string message)
    {
        message = Regex.Replace(message, @"^''\s*:\s*", "");
        message = Regex.Replace(message, @"^('[^']*')\s*:\s*", "$1: ");
        return Regex.Replace(message, @"\s{2,}", " ").Trim();
    }
}
