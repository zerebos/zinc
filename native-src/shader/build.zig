//! Builds zinc-shader.dll, which translates GLSL to HLSL for custom shaders (zinc_shader.zig). glslang comes from
//! Ghostty's own package in the pinned checkout (external/ghostty/pkg/glslang). SPIRV-Cross is built here from the
//! same upstream source Ghostty pins, because Ghostty's package leaves out the HLSL backend (Ghostty only needs Metal
//! and GLSL).
const std = @import("std");

pub fn build(b: *std.Build) void {
    const target = b.standardTargetOptions(.{});
    const optimize = b.standardOptimizeOption(.{});

    const module = b.createModule(.{
        .root_source_file = b.path("zinc_shader.zig"),
        .target = target,
        .optimize = optimize,
        .link_libc = true,
    });
    if (b.lazyDependency("glslang", .{ .target = target, .optimize = optimize })) |dep| {
        module.addImport("glslang", dep.module("glslang"));
        module.linkLibrary(dep.artifact("glslang"));
    }
    if (b.lazyDependency("spirv_cross", .{})) |upstream| {
        const cross = b.addLibrary(.{
            .name = "spirv_cross",
            .root_module = b.createModule(.{
                .target = target,
                .optimize = optimize,
                .link_libc = true,
                // Zig's own libc++, linked statically, so the windows-gnu build has no C++ runtime to ship.
                .link_libcpp = true,
            }),
            .linkage = .static,
        });
        cross.root_module.addCSourceFiles(.{
            .root = upstream.path(""),
            .flags = &.{ "-DSPIRV_CROSS_C_API_GLSL=1", "-DSPIRV_CROSS_C_API_HLSL=1", "-fno-sanitize=undefined" },
            .files = &.{
                "spirv_cross.cpp",       "spirv_parser.cpp", "spirv_cross_parsed_ir.cpp", "spirv_cfg.cpp",
                "spirv_cross_c.cpp",     "spirv_glsl.cpp",   "spirv_hlsl.cpp",
            },
        });
        module.linkLibrary(cross);
    }

    const lib = b.addLibrary(.{
        .name = "zinc-shader",
        .linkage = .dynamic,
        .root_module = module,
    });
    b.installArtifact(lib);
}
