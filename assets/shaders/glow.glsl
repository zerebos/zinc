// Zinc: glow. Bright text bleeds a soft light into its surroundings, as on a phosphor screen.
// Still: drawn only when the terminal changes.

const float INTENSITY = 0.45;       // how strong the glow is
const float THRESHOLD = 0.45;       // how bright a pixel must be to glow (0-1)
const float RADIUS = 6.0;           // how far the glow reaches, in pixels at 100% scale

vec3 bright(vec2 uv)
{
    vec3 c = texture(iChannel0, uv).rgb;
    float level = max(c.r, max(c.g, c.b));
    return c * smoothstep(THRESHOLD, 1.0, level);
}

void mainImage(out vec4 fragColor, in vec2 fragCoord)
{
    vec2 uv = fragCoord / iResolution.xy;
    vec4 base = texture(iChannel0, uv);

    // 24 samples on a spiral out to the radius, nearer ones weighing more (a cheap, soft blur).
    float scale = max(1.0, iResolution.y / 1000.0);
    vec2 pixel = scale * RADIUS / iResolution.xy;
    vec3 glow = vec3(0.0);
    float total = 0.0;
    for (int i = 0; i < 24; i++)
    {
        float t = (float(i) + 0.5) / 24.0;
        float angle = float(i) * 2.39996323; // the golden angle: even coverage
        vec2 offset = vec2(cos(angle), sin(angle)) * (0.2 + 0.8 * sqrt(t)); // from a little away: not the pixel itself
        float weight = 1.0 - t;
        glow += bright(uv + offset * pixel) * weight;
        total += weight;
    }

    glow = glow / total * INTENSITY;
    // "Screen" blending: light added where it's dark, little where it's already bright (bright text doesn't wash out).
    fragColor = vec4(1.0 - (1.0 - base.rgb) * (1.0 - glow), max(base.a, max(glow.r, max(glow.g, glow.b))));
}
