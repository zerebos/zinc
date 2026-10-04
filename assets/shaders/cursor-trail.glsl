// Zinc: cursor-trail. A short streak follows the cursor when it jumps.
// When the cursor jumps (a new line, a click, an editor's motion), a streak in its color follows it there and fades
// in a quarter of a second. Typing (moving one cell) leaves none.
// Animated: it uses time, so it redraws every refresh while the terminal may animate.

const float DURATION = 0.25;        // seconds the streak lasts
const float MIN_JUMP = 2.5;         // cells the cursor must move for a streak
const float OPACITY = 0.55;         // how strong the streak is where it starts

// Distance from p to the segment a-b, and how far along it (0 at a, 1 at b) the nearest point is.
vec2 segment(vec2 p, vec2 a, vec2 b)
{
    vec2 ab = b - a;
    float t = clamp(dot(p - a, ab) / max(dot(ab, ab), 1e-4), 0.0, 1.0);
    return vec2(length(p - (a + ab * t)), t);
}

void mainImage(out vec4 fragColor, in vec2 fragCoord)
{
    vec4 base = texture(iChannel0, fragCoord / iResolution.xy);
    float age = (iTime - iTimeCursorChange) / DURATION;
    vec2 cell = max(iCurrentCursor.zw, vec2(1.0));
    // Each cursor's middle (y goes down: a rectangle runs from y - height to y).
    vec2 to = iCurrentCursor.xy + vec2(cell.x, -cell.y) * 0.5;
    vec2 from = iPreviousCursor.xy + vec2(max(iPreviousCursor.z, 1.0), -max(iPreviousCursor.w, 1.0)) * 0.5;
    float jump = length((to - from) / vec2(max(cell.x, cell.y * 0.5), cell.y));
    if (age >= 1.0 || iCursorVisible == 0 || jump < MIN_JUMP)
    {
        fragColor = base;
        return;
    }

    // The streak's tail catches up with its head as it fades.
    float fade = 1.0 - age;
    vec2 tail = mix(from, to, age * age);
    vec2 d = segment(fragCoord, tail, to);
    float width = cell.y * 0.5 * (0.35 + 0.65 * d.y); // thin at the tail, the cursor's height at the head
    float streak = smoothstep(width, width * 0.4, d.x) * fade * OPACITY * (0.3 + 0.7 * d.y);

    fragColor = vec4(mix(base.rgb, iCurrentCursorColor.rgb, streak), max(base.a, streak));
}
