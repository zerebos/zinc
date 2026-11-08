//! Builds zinc-sprite.dll: Ghostty's sprite rasterizer behind zinc_sprite.zig's C API.
//! Run by scripts/build/sprites.ps1, which puts zinc_sprite.zig into the Ghostty checkout
//! first (-Dghostty=<checkout>).
const std = @import("std");

pub fn build(b: *std.Build) void {
    const target = b.standardTargetOptions(.{});
    const optimize = b.standardOptimizeOption(.{});
    const ghostty = b.option([]const u8, "ghostty", "The Ghostty checkout (with src/zinc_sprite.zig in place)") orelse
        @panic("-Dghostty=<path to the Ghostty checkout> is required");

    const module = b.createModule(.{
        .root_source_file = .{ .cwd_relative = b.pathJoin(&.{ ghostty, "src", "zinc_sprite.zig" }) },
        .target = target,
        .optimize = optimize,
        .link_libc = true,
    });
    if (b.lazyDependency("z2d", .{ .target = target, .optimize = optimize })) |z2d| {
        module.addImport("z2d", z2d.module("z2d"));
    }

    const lib = b.addLibrary(.{
        .name = "zinc-sprite",
        .linkage = .dynamic,
        .root_module = module,
    });
    b.installArtifact(lib);
}
