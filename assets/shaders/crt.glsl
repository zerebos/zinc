// Zinc: crt. An old monitor: curved glass, scanlines, color fringes and a faint flicker.
// Rounded corners, a faint RGB mask, fringes that grow toward the edges, a vignette, and a barely visible flicker.
// Animated (the flicker uses time): for a
// still picture, drawn only when the terminal changes, make the line marked FLICKER below `float flicker = 1.0;`.

const float CURVE = 0.06;           // how much the glass bulges (0: flat)
const float CORNER = 0.03;          // corner rounding, as a fraction of the screen
const float SCANLINES = 0.25;       // scanline darkness (0-1)
const float LINE_SPACING = 3.0;     // pixels between scanlines
const float MASK = 0.08;            // the RGB stripes of the phosphor mask (0: none)
const float FRINGE = 1.2;           // color fringing at the edges, in pixels
const float VIGNETTE = 0.35;        // corner darkening
const float FLICKER = 0.015;        // brightness flicker (0: none)
const float BRIGHTNESS = 1.12;      // makes up for the light the lines and mask take away

// The screen position under this pixel, after the glass's curve (0-1 inside the screen).
vec2 bulge(vec2 uv)
{
    vec2 c = uv * 2.0 - 1.0;
    c *= 1.0 + CURVE * dot(c, c);
    return c * 0.5 + 0.5;
}

void mainImage(out vec4 fragColor, in vec2 fragCoord)
{
    vec2 uv = bulge(fragCoord / iResolution.xy);

    // Off the curved screen: the bezel.
    vec2 edge = min(uv, 1.0 - uv);
    float inside = smoothstep(0.0, 0.002, min(edge.x, edge.y));
    vec2 corner = max(vec2(CORNER) - edge, 0.0);
    inside *= 1.0 - smoothstep(CORNER * 0.9, CORNER, length(corner));

    // Each color a little apart, more so away from the middle.
    vec2 fromMiddle = uv - 0.5;
    vec2 fringe = fromMiddle * FRINGE / iResolution.xy * 2.0;
    vec3 color;
    color.r = texture(iChannel0, uv + fringe).r;
    color.g = texture(iChannel0, uv).g;
    color.b = texture(iChannel0, uv - fringe).b;

    float lines = 1.0 - SCANLINES * (0.5 + 0.5 * cos(6.2831853 * fragCoord.y / LINE_SPACING));
    // Every third pixel column red, green, blue: the other two colors a little dimmer.
    float stripe = mod(floor(fragCoord.x), 3.0);
    vec3 mask = vec3(1.0 - MASK) + MASK * vec3(stripe == 0.0, stripe == 1.0, stripe == 2.0);
    float vignette = 1.0 - VIGNETTE * pow(length(fromMiddle) * 1.25, 2.5);
    float flicker = 1.0 - FLICKER * (0.5 + 0.5 * sin(iTime * 37.0)); // FLICKER

    color *= lines * mask * vignette * flicker * BRIGHTNESS;
    fragColor = vec4(color * inside, inside);
}
