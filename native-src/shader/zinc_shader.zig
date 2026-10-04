//! Zinc's C API for translating GLSL shaders to HLSL (custom shaders, Ghostty-style): glslang compiles the GLSL to
//! SPIR-V, SPIRV-Cross writes it out as HLSL (shader model 5), which Windows compiles at run time (D3DCompile).
//! Uses Ghostty's own glslang and spirv-cross packages (its pinned checkout); Ghostty does the same for Metal.
//! The Shadertoy-style prefix (uniforms, iChannel0) is Zinc's, prepended before this is called.
//! Not thread-safe: the caller serializes calls.
const std = @import("std");
const glslang = @import("glslang");

// SPIRV-Cross's C API (spirv_cross_c.h): just what's used here.
const spvc = struct {
    const Context = ?*opaque {};
    const Ir = ?*opaque {};
    const Compiler = ?*opaque {};
    const Options = ?*opaque {};
    const success: c_int = 0;
    const backend_hlsl: c_int = 2;
    const capture_take_ownership: c_int = 1;
    const option_hlsl_shader_model: c_int = 13 | 0x4000000;
    const option_force_zero_initialized_variables: c_int = 54 | 0x1000000;
    const ErrorCallback = *const fn (?*anyopaque, [*:0]const u8) callconv(.c) void;
    extern fn spvc_context_create(ctx: *Context) c_int;
    extern fn spvc_context_destroy(ctx: Context) void;
    extern fn spvc_context_set_error_callback(ctx: Context, cb: ErrorCallback, user: ?*anyopaque) void;
    extern fn spvc_context_parse_spirv(ctx: Context, spirv: [*]const u32, words: usize, ir: *Ir) c_int;
    extern fn spvc_context_create_compiler(ctx: Context, backend: c_int, ir: Ir, mode: c_int, compiler: *Compiler) c_int;
    extern fn spvc_compiler_create_compiler_options(compiler: Compiler, options: *Options) c_int;
    extern fn spvc_compiler_options_set_uint(options: Options, option: c_int, value: c_uint) c_int;
    extern fn spvc_compiler_options_set_bool(options: Options, option: c_int, value: u8) c_int;
    extern fn spvc_compiler_install_compiler_options(compiler: Compiler, options: Options) c_int;
    extern fn spvc_compiler_compile(compiler: Compiler, source: *[*:0]const u8) c_int;
};

const alloc = std.heap.c_allocator;
var initialized = false;

/// The last error SPIRV-Cross reported (its callback has nowhere else to put it).
var cross_error: ?[:0]u8 = null;

/// Translates `src`, a complete GLSL fragment shader, to HLSL. Returns 0 with the HLSL in `out`, or 1 with the
/// compiler's messages in `out`; either way `out` is freed with zinc_shader_free.
export fn zinc_shader_glsl_to_hlsl(src: [*:0]const u8, out: *?[*:0]u8) i32 {
    out.* = null;
    const hlsl = translate(std.mem.sliceTo(src, 0)) catch |err| {
        out.* = errorText(err);
        return 1;
    };
    out.* = hlsl.ptr;
    return 0;
}

export fn zinc_shader_free(text: ?[*:0]u8) void {
    if (text) |t| alloc.free(std.mem.sliceTo(t, 0));
}

var last_log: ?[:0]u8 = null;

fn errorText(err: anyerror) ?[*:0]u8 {
    if (last_log) |log| {
        last_log = null;
        return log.ptr;
    }
    if (cross_error) |msg| {
        cross_error = null;
        return msg.ptr;
    }
    const text = alloc.dupeZ(u8, @errorName(err)) catch return null;
    return text.ptr;
}

fn keepLog(a: [:0]const u8, b: [:0]const u8) void {
    if (last_log) |old| alloc.free(old);
    last_log = std.mem.concatWithSentinel(alloc, u8, &.{ a, if (a.len > 0 and b.len > 0) "\n" else "", b }, 0) catch null;
}

/// Declared so Zig doesn't export its own DLL entry point: the static C runtime's (libcmt) is the entry instead, and
/// it initializes the runtime (heap, locks, C++ statics). With Zig's, glslang's first allocation crashed on an
/// uninitialized heap.
pub const _DllMainCRTStartup = {};

fn translate(src: []const u8) ![:0]u8 {
    if (!initialized) {
        try glslang.init();
        initialized = true;
    }

    const code = try alloc.dupeZ(u8, src);
    defer alloc.free(code);
    const spirv = try spirvFromGlsl(code);
    defer alloc.free(spirv);
    return try hlslFromSpirv(spirv);
}

fn spirvFromGlsl(src: [:0]const u8) ![]u32 {
    const c = glslang.c;
    const input: c.glslang_input_t = .{
        .language = c.GLSLANG_SOURCE_GLSL,
        .stage = c.GLSLANG_STAGE_FRAGMENT,
        .client = c.GLSLANG_CLIENT_VULKAN,
        .client_version = c.GLSLANG_TARGET_VULKAN_1_2,
        .target_language = c.GLSLANG_TARGET_SPV,
        .target_language_version = c.GLSLANG_TARGET_SPV_1_5,
        .code = src.ptr,
        .default_version = 100,
        .default_profile = c.GLSLANG_NO_PROFILE,
        .force_default_version_and_profile = 0,
        .forward_compatible = 0,
        .messages = c.GLSLANG_MSG_DEFAULT_BIT,
        .resource = c.glslang_default_resource(),
    };

    const shader = try glslang.Shader.create(&input);
    defer shader.delete();
    shader.preprocess(&input) catch |err| {
        keepLog(shader.getInfoLog() catch "", shader.getDebugInfoLog() catch "");
        return err;
    };
    shader.parse(&input) catch |err| {
        keepLog(shader.getInfoLog() catch "", shader.getDebugInfoLog() catch "");
        return err;
    };

    const program = try glslang.Program.create();
    defer program.delete();
    program.addShader(shader);
    program.link(c.GLSLANG_MSG_SPV_RULES_BIT | c.GLSLANG_MSG_VULKAN_RULES_BIT) catch |err| {
        keepLog(program.getInfoLog() catch "", program.getDebugInfoLog() catch "");
        return err;
    };
    program.spirvGenerate(c.GLSLANG_STAGE_FRAGMENT);
    const words = try alloc.alloc(u32, program.spirvGetSize());
    program.spirvGet(words);
    return words;
}

fn hlslFromSpirv(spirv: []const u32) ![:0]u8 {
    var ctx: spvc.Context = null;
    if (spvc.spvc_context_create(&ctx) != spvc.success) return error.SpvcFailed;
    defer spvc.spvc_context_destroy(ctx);
    spvc.spvc_context_set_error_callback(ctx, &(struct {
        fn callback(_: ?*anyopaque, msg: [*:0]const u8) callconv(.c) void {
            if (cross_error) |old| alloc.free(old);
            cross_error = alloc.dupeZ(u8, std.mem.sliceTo(msg, 0)) catch null;
        }
    }).callback, null);

    var ir: spvc.Ir = null;
    if (spvc.spvc_context_parse_spirv(ctx, spirv.ptr, spirv.len, &ir) != spvc.success) return error.SpvcFailed;
    var compiler: spvc.Compiler = null;
    if (spvc.spvc_context_create_compiler(ctx, spvc.backend_hlsl, ir, spvc.capture_take_ownership, &compiler) != spvc.success)
        return error.SpvcFailed;

    var options: spvc.Options = null;
    if (spvc.spvc_compiler_create_compiler_options(compiler, &options) != spvc.success) return error.SpvcFailed;
    // Shader model 5.0: what D3D11 feature level 11 runs, and what D3DCompile's ps_5_0 takes.
    if (spvc.spvc_compiler_options_set_uint(options, spvc.option_hlsl_shader_model, 50) != spvc.success) return error.SpvcFailed;
    // Every variable starts at zero: Direct3D's compiler refuses one that's assigned on only some paths (error X4000,
    // "used without having been initialized"), which GLSL allows (matrix-hallway.glsl in a Ghostty collection).
    if (spvc.spvc_compiler_options_set_bool(options, spvc.option_force_zero_initialized_variables, 1) != spvc.success) return error.SpvcFailed;
    if (spvc.spvc_compiler_install_compiler_options(compiler, options) != spvc.success) return error.SpvcFailed;

    var result: [*:0]const u8 = undefined;
    if (spvc.spvc_compiler_compile(compiler, &result) != spvc.success) return error.SpvcFailed;
    return try alloc.dupeZ(u8, std.mem.sliceTo(result, 0));
}
