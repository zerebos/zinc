// Zinc: scanlines. Soft dark lines across the picture, like a CRT's, and a light vignette.
// Still: drawn only when the terminal changes. Settings to taste below.

const float LINE_STRENGTH = 0.22;   // how dark the lines get (0: none, 1: black)
const float LINE_SPACING = 3.0;     // pixels from one line to the next
const float VIGNETTE = 0.18;        // how much the corners darken (0: not at all)

void mainImage(out vec4 fragColor, in vec2 fragCoord)
{
    vec2 uv = fragCoord / iResolution.xy;
    vec4 color = texture(iChannel0, uv);

    // A smooth wave down the screen: darkest between lines, full brightness on them.
    float wave = 0.5 + 0.5 * cos(6.2831853 * fragCoord.y / LINE_SPACING);
    float lines = 1.0 - LINE_STRENGTH * wave;

    // Darker toward the corners, never in the middle.
    vec2 centered = uv - 0.5;
    float vignette = 1.0 - VIGNETTE * dot(centered, centered) * 2.5;

    fragColor = vec4(color.rgb * lines * vignette, color.a);
}
