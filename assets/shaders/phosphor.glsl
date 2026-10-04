// Zinc: phosphor. A one-color screen in Lit Cell's green, glowing a little.
// Every color becomes a shade of it, bright text glows a little, and the background keeps a faint green cast.
// Still: drawn only when the terminal changes.
// For amber, set PHOSPHOR to vec3(1.0, 0.69, 0.25).

const vec3 PHOSPHOR = vec3(0.34, 1.0, 0.62);   // the screen's color at full brightness
const float BACKGROUND = 0.05;                 // how much of it the darkest parts keep
const float GLOW = 0.35;                       // how much bright text glows (0: none)

float luma(vec3 c) { return dot(c, vec3(0.2126, 0.7152, 0.0722)); }

// Brightness above the color scheme's background: the background itself is 0, so it stays dark green, not mid green.
float level(vec2 uv)
{
    float floorLevel = luma(iBackgroundColor);
    return clamp((luma(texture(iChannel0, uv).rgb) - floorLevel) / max(1.0 - floorLevel, 0.1), 0.0, 1.0);
}

void mainImage(out vec4 fragColor, in vec2 fragCoord)
{
    vec2 uv = fragCoord / iResolution.xy;
    float alpha = texture(iChannel0, uv).a;
    float light = level(uv);

    // A small cross of samples around the pixel: enough for a soft halo on text.
    vec2 px = max(1.0, iResolution.y / 1000.0) * 1.5 / iResolution.xy;
    float halo = (level(uv + vec2(px.x, 0.0)) + level(uv - vec2(px.x, 0.0)) +
                  level(uv + vec2(0.0, px.y)) + level(uv - vec2(0.0, px.y))) * 0.25;

    // Brighter text a little brighter (a phosphor's curve), the background never quite black.
    float shade = BACKGROUND + (1.0 - BACKGROUND) * pow(light, 0.8) + GLOW * halo * 0.5;
    fragColor = vec4(PHOSPHOR * shade, alpha);
}
