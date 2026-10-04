// Zinc: crt. An old monitor: curved glass, scanlines, color fringes and a faint flicker.
// Rounded corners, a faint RGB mask, fringes that grow toward the edges, a vignette, and a barely visible flicker.
// Animated (the flicker uses time): for a still picture, drawn only when the terminal changes, make the line marked
// FLICKER below `float flicker = 1.0;`.
//
// To play with: every setting is a constant just below. Save the file (a copy in a "shaders" folder next to
// config.toml, named crt.glsl, takes the place of this one) and the terminal shows the change at once.

// The glass. CURVE bends the picture like a bulging tube: 0.0 is flat, 0.03 a gentle modern CRT, 0.06 (this) an
// old TV, 0.12+ a fishbowl. The bend grows with the square of the distance from the middle (see bulge() below), so
// the middle stays sharp and the edges curve away. CURVE_X/CURVE_Y weigh it per direction: an old TV curves both
// ways; a Trinitron was flat up and down (CURVE_Y = 0.0).
const float CURVE = 0.06;
const float CURVE_X = 1.0;
const float CURVE_Y = 1.0;
// How round the corners of the curved screen are, as a fraction of its size (0.0: square; 0.08: very round).
const float CORNER = 0.03;
const float SCANLINES = 0.25;       // scanline darkness (0-1)
const float LINE_SPACING = 3.0;     // pixels between scanlines
const float MASK = 0.08;            // the RGB stripes of the phosphor mask (0: none)
const float FRINGE = 1.2;           // color fringing at the edges, in pixels
const float VIGNETTE = 0.35;        // corner darkening
const float FLICKER = 0.015;        // brightness flicker (0: none)
const float BRIGHTNESS = 1.12;      // makes up for the light the lines and mask take away

// Where on the terminal's picture this pixel of the glass looks: uv from 0-1 across the window, back in 0-1 inside the
// picture (outside it: the bezel). Centered (-1..1), each point is pushed outward by CURVE times its squared distance
// from the middle, so the picture's edges move past the window's and the middle barely moves.
vec2 bulge(vec2 uv)
{
    vec2 c = uv * 2.0 - 1.0;
    c *= 1.0 + CURVE * vec2(CURVE_X, CURVE_Y) * dot(c, c);
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
