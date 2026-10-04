using System.Diagnostics;
using Vortice.D3DCompiler;
using Zinc.Shaders;

namespace Zinc.Terminal;

/// <summary>A custom shader ready for the GPU: Direct3D bytecode (ps_5_0), and whether it animates on its own.</summary>
internal sealed record CompiledShader(string File, byte[] Bytecode, bool UsesTime);

/// <summary>A pane's shaders as loaded: those that compiled, in order, and what went wrong with the others.</summary>
internal sealed record LoadedShaders(IReadOnlyList<CompiledShader> Shaders, IReadOnlyList<string> Errors);

/// <summary>
/// Reads, translates (GLSL to HLSL: <see cref="ShaderTranslator"/>) and compiles (D3DCompile) custom shaders off the UI
/// thread, ~130 ms a shader. Each file is compiled once while it's unchanged (same size and time) and shared by every
/// pane: a new tab, or a config reload, reuses it.
/// </summary>
internal static class ShaderLoader
{
    private static readonly Lock Gate = new();
    private static readonly Dictionary<string, (DateTime Written, long Length, Task<(CompiledShader? Shader, string? Error)> Load)> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The full-screen triangle every shader pass draws (no vertex buffer).</summary>
    public static readonly Lazy<byte[]> VertexShader = new(() => Compile("""
        float4 main(uint id : SV_VertexID) : SV_Position
        {
            float2 uv = float2((id << 1) & 2, id & 2);
            return float4(uv * float2(2, -2) + float2(-1, 1), 0, 1);
        }
        """, "vs_5_0", out var error) ?? throw new InvalidOperationException(error));

    /// <summary>
    /// A shader file was saved, created, deleted or renamed (a folder holding a loaded or wanted shader is watched; 300 ms
    /// of quiet first: editors save in steps). On a thread-pool thread, with the file's full path.
    /// </summary>
    public static event Action<string>? FileChanged;

    private static readonly Dictionary<string, FileSystemWatcher> Watchers = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Timer> Settling = new(StringComparer.OrdinalIgnoreCase);

    private static void Watch(string file)
    {
        var folder = Path.GetDirectoryName(file);
        if (folder == null || !Directory.Exists(folder) || Watchers.ContainsKey(folder))
        {
            return;
        }

        try
        {
            var watcher = new FileSystemWatcher(folder)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            };
            watcher.Changed += (_, e) => Settle(e.FullPath);
            watcher.Created += (_, e) => Settle(e.FullPath);
            watcher.Deleted += (_, e) => Settle(e.FullPath);
            watcher.Renamed += (_, e) =>
            {
                Settle(e.OldFullPath);
                Settle(e.FullPath); // editors that save by renaming a temporary file over it
            };
            watcher.EnableRaisingEvents = true;
            Watchers[folder] = watcher;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            Log.Write($"shader: can't watch {folder}: {ex.Message}");
        }
    }

    private static void Settle(string path)
    {
        lock (Gate)
        {
            if (!Cache.ContainsKey(path))
            {
                return; // not a shader anyone uses
            }

            if (Settling.TryGetValue(path, out var timer))
            {
                timer.Change(300, Timeout.Infinite);
                return;
            }

            Settling[path] = new Timer(_ =>
            {
                lock (Gate)
                {
                    Settling.Remove(path, out var done);
                    done?.Dispose();
                }

                Log.Write($"shader: {Path.GetFileName(path)} changed");
                FileChanged?.Invoke(path);
            }, null, 300, Timeout.Infinite);
        }
    }

    public static async Task<LoadedShaders> Load(IReadOnlyList<string> files)
    {
        var results = await Task.WhenAll(files.Select(LoadOne));
        return new([.. results.Where(r => r.Shader != null).Select(r => r.Shader!)], [.. results.Where(r => r.Error != null).Select(r => r.Error!)]);
    }

    private static Task<(CompiledShader? Shader, string? Error)> LoadOne(string file)
    {
        var info = new FileInfo(file);
        var (written, length) = info.Exists ? (info.LastWriteTimeUtc, info.Length) : (DateTime.MinValue, -1L);
        lock (Gate)
        {
            Watch(file);
            if (Cache.TryGetValue(file, out var cached) && cached.Written == written && cached.Length == length)
            {
                return cached.Load;
            }

            var load = Task.Run(() => Build(file));
            Cache[file] = (written, length, load);
            return load;
        }
    }

    private static (CompiledShader? Shader, string? Error) Build(string file)
    {
        var name = Path.GetFileName(file);
        string glsl;
        try
        {
            glsl = Read(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null, ex is FileNotFoundException or DirectoryNotFoundException ? $"{name}: no such file ({file})" : $"{name}: couldn't read it ({ex.Message})");
        }

        try
        {
            var watch = Stopwatch.StartNew();
            if (!ShaderTranslator.TryTranslate(glsl, file, out var hlsl, out var problem))
            {
                Log.Write($"shader: {name} doesn't compile: {problem.Replace('\n', ' ')}");
                return (null, problem);
            }

            var translated = watch.Elapsed.TotalMilliseconds;
            if (Compile(hlsl, "ps_5_0", out var error) is not { } bytecode)
            {
                // The translation's HLSL, refused: not the shader's fault as written, so its lines mean nothing to its author.
                Log.Write($"shader: {name}: Direct3D refused the translation: {error}");
                return (null, $"{name}: couldn't be compiled for Direct3D ({FirstLine(error)})");
            }

            Log.Write($"shader: {name} ready (translated in {translated:0} ms, compiled in {watch.Elapsed.TotalMilliseconds - translated:0} ms)");
            return (new CompiledShader(file, bytecode, ShaderSource.UsesTime(glsl)), null);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            Log.Write($"shader: no translator: {ex.Message}");
            return (null, $"{name}: Zinc's shader translator (zinc-shader.dll) is missing or broken; reinstalling Zinc should bring it back");
        }
    }

    /// <summary>Shared access and a few tries: an editor may be saving it right now.</summary>
    private static string Read(string file)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                return new StreamReader(stream).ReadToEnd();
            }
            catch (IOException ex) when (attempt < 3 && ex is not FileNotFoundException and not DirectoryNotFoundException)
            {
                Thread.Sleep(50);
            }
        }
    }

    private static byte[]? Compile(string source, string profile, out string? error)
    {
        error = null;
        var result = Compiler.Compile(source, "main", "shader", profile, out var blob, out var errors);
        using var _ = blob;
        using var __ = errors;
        if (result.Failure || blob == null)
        {
            error = errors?.AsString() ?? result.ToString();
            return null;
        }

        return blob.AsBytes();
    }

    private static string FirstLine(string? text) => (text ?? "").Split('\n')[0].Trim();
}
