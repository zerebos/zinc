//! Zinc's C API for Ghostty's sprite rasterizer (src/font/sprite): the box drawing, block
//! elements, Symbols for Legacy Computing, Powerline and Braille shapes Ghostty draws itself,
//! pixel-exact for a given cell size, instead of taking them from a font.
//!
//! scripts/build/sprites.ps1 copies this file into the pinned Ghostty checkout (as
//! src/zinc_sprite.zig, so its relative imports resolve) and builds it with
//! native-src/sprite/build.zig into zinc-sprite.dll. Ghostty is MIT-licensed.
const std = @import("std");
const font = @import("font/main.zig");

/// The I/O instance the sprite canvas uses (Ghostty's own comes from its app-wide state, which
/// this library doesn't have; the build points the canvas here).
var threaded: std.Io.Threaded = .init_single_threaded;

pub fn io() std.Io {
    return threaded.io();
}

/// Whether Ghostty draws this codepoint itself.
export fn zinc_sprite_has(cp: u32) bool {
    const face: font.sprite.Face = .{ .metrics = undefined };
    return face.hasCodepoint(cp, null);
}

/// Draws `cp` for a cell of `cell_width` x (`ascent` + `descent`) pixels, lines `thickness`
/// pixels thick, into `out` (8-bit coverage, `out_width` x `out_height`, row-major). The
/// drawing can extend past the cell (diagonals meeting across cells): `out_x`/`out_y` receive
/// where its top-left sits relative to the cell's top-left, and `drawn_width`/`drawn_height` its
/// size. Returns 0, 1 if Ghostty doesn't draw this codepoint, 2 if `out` is too small, 3 on failure.
export fn zinc_sprite_draw(
    cp: u32,
    cell_width: u32,
    ascent: u32,
    descent: u32,
    thickness: u32,
    out: [*]u8,
    out_width: u32,
    out_height: u32,
    out_x: *i32,
    out_y: *i32,
    drawn_width: *u32,
    drawn_height: *u32,
) i32 {
    if (!zinc_sprite_has(cp)) return 1;

    const alloc = std.heap.c_allocator;
    const metrics: font.Metrics = .calc(.{
        .px_per_em = @floatFromInt(ascent + descent),
        .cell_width = @floatFromInt(cell_width),
        .ascent = @floatFromInt(ascent),
        .descent = -@as(f64, @floatFromInt(descent)),
        .line_gap = 0.0,
        .underline_thickness = @floatFromInt(thickness),
        .strikethrough_thickness = @floatFromInt(thickness),
    });
    const face: font.sprite.Face = .{ .metrics = metrics };

    // Room for the cell plus the canvas's quarter-cell margin on every side, and the atlas's
    // own border.
    const side = 2 * @max(metrics.cell_width, metrics.cell_height) + 8;
    var atlas = font.Atlas.init(alloc, side, .grayscale) catch return 3;
    defer atlas.deinit(alloc);

    const glyph = face.renderGlyph(alloc, &atlas, cp, .{ .grid_metrics = metrics }) catch return 3;
    if (glyph.width > out_width or glyph.height > out_height) return 2;

    for (0..glyph.height) |row| {
        const src = atlas.data[(glyph.atlas_y + row) * atlas.size + glyph.atlas_x ..][0..glyph.width];
        @memcpy(out[row * out_width ..][0..glyph.width], src);
    }

    // Ghostty's offsets: x from the cell's left, y from the cell's bottom (baseline-style) to the
    // glyph's top. Converted to "from the cell's top-left".
    out_x.* = glyph.offset_x;
    out_y.* = @as(i32, @intCast(metrics.cell_height)) - glyph.offset_y;
    drawn_width.* = glyph.width;
    drawn_height.* = glyph.height;
    return 0;
}
