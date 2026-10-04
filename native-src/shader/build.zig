//! Builds zinc-shader.dll: GLSL -> HLSL for custom shaders (zinc_shader.zig). glslang comes from Ghostty's own
//! package in the pinned checkout (external/ghostty/pkg/glslang); SPIRV-Cross is built here from the same upstream
//! source Ghostty pins, because Ghostty's package leaves out the HLSL backend (it only needs Metal and GLSL).
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
                // MSVC: its own C++ headers come with libc (Ghostty's packages note why not linkLibCpp).
                .link_libcpp = target.result.abi != .msvc,
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

    // glslang and SPIRV-Cross are C++: Microsoft's C and C++ runtimes, static (/MT), as Ghostty's packages compile
    // them, so there's no runtime DLL for Zinc to carry. Zig's search paths miss these two folders: the build script
    // passes them (found with vswhere).
    if (target.result.abi == .msvc) {
        if (b.option([]const u8, "ucrt-lib", "Windows SDK's ucrt lib folder")) |dir| module.addLibraryPath(.{ .cwd_relative = dir });
        if (b.option([]const u8, "msvc-lib", "MSVC's lib folder")) |dir| module.addLibraryPath(.{ .cwd_relative = dir });
        module.linkSystemLibrary("libucrt", .{});
        module.linkSystemLibrary("libvcruntime", .{});
        module.linkSystemLibrary("libcpmt", .{});
        module.linkSystemLibrary("libcmt", .{});
    }

    const lib = b.addLibrary(.{
        .name = "zinc-shader",
        .linkage = .dynamic,
        .root_module = module,
    });
    b.installArtifact(lib);
}
