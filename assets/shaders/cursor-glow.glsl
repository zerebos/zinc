// Zinc: cursor-glow. A soft halo of the cursor's own color around it, easy to find on a busy screen.
// Still: drawn only when the terminal changes (the cursor moving is a change).

const float RADIUS = 18.0;          // how far the halo reaches, in pixels at 100% scale
const float STRENGTH = 0.35;        // how bright it is at the cursor's edge

void mainImage(out vec4 fragColor, in vec2 fragCoord)
{
    vec4 base = texture(iChannel0, fragCoord / iResolution.xy);
    if (iCursorVisible == 0 || iCurrentCursor.z <= 0.0)
    {
        fragColor = base;
        return;
    }

    // The cursor's rectangle (y goes down: from y - height to y), and the distance from this pixel to it.
    vec2 low = vec2(iCurrentCursor.x, iCurrentCursor.y - iCurrentCursor.w);
    vec2 high = vec2(iCurrentCursor.x + iCurrentCursor.z, iCurrentCursor.y);
    vec2 outside = max(max(low - fragCoord, fragCoord - high), 0.0);
    float distance = length(outside);

    float scale = max(1.0, iResolution.y / 1000.0);
    float halo = STRENGTH * exp(-distance / (RADIUS * scale * 0.35));
    halo *= step(0.5, distance); // the cursor itself is left as it is

    fragColor = vec4(base.rgb + iCurrentCursorColor.rgb * halo, max(base.a, halo));
}
