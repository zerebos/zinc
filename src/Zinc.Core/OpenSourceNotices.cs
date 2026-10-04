namespace Zinc;

/// <summary>A piece of open-source software that ships inside Zinc, as Settings > About lists it.</summary>
/// <param name="Name">What it's called.</param>
/// <param name="Version">The version shipped (a commit for the Ghostty checkout).</param>
/// <param name="Use">What Zinc uses it for, in a few words.</param>
/// <param name="License">The license's short name (SPDX where there is one).</param>
/// <param name="Url">Its home page or source.</param>
/// <param name="LicenseFile">The license text, relative to the exe's folder.</param>
/// <param name="Packages">The NuGet packages it comes in (tests check their versions against the project files).</param>
public sealed record OpenSourceComponent(
    string Name, string Version, string Use, string License, string Url, string LicenseFile, string[] Packages);

/// <summary>
/// Everything open source that Zinc ships, with its license text next to the exe
/// (<c>Licenses\</c>, from assets/licenses; the font's and the color schemes' own licenses sit
/// with them). Tests keep it complete: every NuGet package the app ships, the pinned Ghostty
/// commit, and every license file must be here.
/// </summary>
public static class OpenSourceNotices
{
    public static IReadOnlyList<OpenSourceComponent> Components { get; } =
    [
        new("Ghostty (libghostty-vt)", "6301810", "Terminal emulation, the built-in glyphs, Kitty image placeholders",
            "MIT", "https://github.com/ghostty-org/ghostty", @"Licenses\Ghostty.txt", []),
        new("z2d", "0.12.1", "Draws the built-in glyphs (in Ghostty's sprite code)",
            "MPL-2.0", "https://github.com/vancluever/z2d", @"Licenses\z2d.txt", []),
        new("simdutf", "5.2.8", "Fast UTF-8 decoding (in libghostty-vt)",
            "MIT (or Apache-2.0)", "https://github.com/simdutf/simdutf", @"Licenses\simdutf.txt", []),
        new("Highway", "66486a1", "SIMD routines (in libghostty-vt)",
            "BSD-3-Clause (or Apache-2.0)", "https://github.com/google/highway", @"Licenses\Highway.txt", []),
        new("uucode", "0.2.0", "Unicode tables (in libghostty-vt), with Unicode data",
            "MIT, Unicode-3.0", "https://github.com/jacobsandlund/uucode", @"Licenses\uucode.txt", []),
        new("Zig standard library", "0.16.0", "Compiled into libghostty-vt, the glyph library and the shader translator",
            "MIT", "https://ziglang.org", @"Licenses\Zig.txt", []),
        new("Cascadia Mono NF", "2407.024", "The default font",
            "OFL-1.1", "https://github.com/microsoft/cascadia-code", @"Fonts\LICENSE-CascadiaCode.txt", []),
        new("iTerm2-Color-Schemes", "99d9701", "The bundled color schemes",
            "MIT", "https://github.com/mbadolato/iTerm2-Color-Schemes", @"Themes\LICENSE.txt", []),
        new("Windows App SDK", "2.5", "WinUI: windows, controls, the title bar",
            "Microsoft Software License Terms", "https://github.com/microsoft/windowsappsdk", @"Licenses\WindowsAppSDK.txt",
            ["Microsoft.WindowsAppSDK.WinUI", "Microsoft.WindowsAppSDK.Foundation", "Microsoft.WindowsAppSDK.InteractiveExperiences"]),
        new("Win2D", "1.4.0", "Draws the terminal",
            "MIT", "https://github.com/microsoft/Win2D", @"Licenses\Win2D.txt", ["Microsoft.Graphics.Win2D"]),
        new("ConPTY and OpenConsole", "1.24.260710001", "Runs console programs (from Windows Terminal)",
            "MIT", "https://github.com/microsoft/terminal", @"Licenses\ConPTY.txt", ["Microsoft.Windows.Console.ConPTY"]),
        new(".NET", Environment.Version.ToString(), "The runtime Zinc is written for, shipped with it",
            "MIT", "https://github.com/dotnet/runtime", @"Licenses\dotnet.txt", ["System.Drawing.Common"]),
        new("Tomlyn", "2.10.1", "Reads and edits config.toml",
            "BSD-2-Clause", "https://github.com/xoofx/Tomlyn", @"Licenses\Tomlyn.txt", ["Tomlyn"]),
        new("Velopack", "1.2.161", "Installs Zinc and keeps it up to date",
            "MIT", "https://github.com/velopack/velopack", @"Licenses\Velopack.txt", ["Velopack"]),
        new("glslang", "14.2.0", "Compiles custom shaders' GLSL (zinc-shader.dll, Ghostty's package)",
            "BSD-3-Clause and others (see the text)", "https://github.com/KhronosGroup/glslang", @"Licenses\glslang.txt", []),
        new("SPIRV-Cross", "13.1.1", "Translates custom shaders to HLSL for Direct3D (zinc-shader.dll)",
            "Apache-2.0", "https://github.com/KhronosGroup/SPIRV-Cross", @"Licenses\SPIRV-Cross.txt", []),
        new("Vortice.Windows", "3.8.3", "Direct3D 11 for custom shaders, with SharpGen.Runtime",
            "MIT", "https://github.com/amerkoleci/Vortice.Windows", @"Licenses\Vortice.txt", ["Vortice.Direct3D11", "Vortice.D3DCompiler"]),
        new("Heroicons", "", "The reset arrow in Settings",
            "MIT", "https://heroicons.com", @"Licenses\Heroicons.txt", []),
    ];
}
