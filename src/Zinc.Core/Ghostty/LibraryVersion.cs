using System.Text;

namespace Zinc.Ghostty;

/// <summary>
/// Checks that the loaded ghostty-vt.dll can serve the bindings in <see cref="Native"/>, as libghostty-vt's stability
/// guide asks. A later minor or patch release of the same major version works, because 1.x only adds to the C ABI.
/// </summary>
public static unsafe class LibraryVersion
{
    /// <summary>The release the bindings are written for.</summary>
    public const int Major = 1, Minor = 0;

    private static readonly Lazy<(string? Version, string? Problem)> Loaded = new(Read);

    /// <summary>The loaded library's version, such as "1.0.1", or null when it doesn't report one.</summary>
    public static string? Version => Loaded.Value.Version;

    /// <summary>Throws when the loaded library is from another major release, or older than the bindings.</summary>
    public static void EnsureCompatible()
    {
        if (Loaded.Value.Problem is { } problem)
        {
            throw new InvalidOperationException(problem);
        }
    }

    private static (string?, string?) Read()
    {
        ulong major = 0, minor = 0;
        GhosttyString text = default;
        try
        {
            if (Native.BuildInfo(BuildInfoData.VersionMajor, &major) != GhosttyResult.Success ||
                Native.BuildInfo(BuildInfoData.VersionMinor, &minor) != GhosttyResult.Success ||
                Native.BuildInfo(BuildInfoData.VersionString, &text) != GhosttyResult.Success)
            {
                return (null, NeedsMessage("doesn't report its version"));
            }
        }
        catch (EntryPointNotFoundException)
        {
            // ghostty_build_info arrived in 1.0, so a DLL without it is from before.
            return (null, NeedsMessage("is older than 1.0"));
        }

        var version = Encoding.UTF8.GetString((byte*)text.Ptr, (int)text.Len);
        Log.Write($"libghostty-vt {version}");
        return major == Major && minor >= Minor ? (version, null) : (version, NeedsMessage($"is {version}"));
    }

    private static string NeedsMessage(string what) =>
        $"Zinc needs libghostty-vt {Major}.{Minor} or a later {Major}.x, but its ghostty-vt.dll {what}.";
}
