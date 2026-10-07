using System.Collections.Concurrent;
using System.Net;

namespace Zinc.Terminal;

/// <summary>
/// The host names that mean this PC in a <c>file://host/path</c> URI (OSC 7 folder reports, OSC 8 links): none,
/// <c>localhost</c>, its computer name (the NetBIOS one and the DNS one: Git Bash and WSL send the latter) and
/// any a WSL distro was given in its wsl.conf (<see cref="Add"/>). A shell on another machine (ssh) names its
/// own host, and its paths mean nothing here.
/// </summary>
public static class LocalHosts
{
    private static readonly ConcurrentDictionary<string, byte> Names = new(StringComparer.OrdinalIgnoreCase);

    static LocalHosts()
    {
        Add("localhost");
        Add(Environment.MachineName);
        try
        {
            Add(Dns.GetHostName());
        }
        catch (System.Net.Sockets.SocketException)
        {
            // The computer name above still counts.
        }
    }

    /// <summary>Counts <paramref name="name"/> as this PC from now on (a WSL distro's own host name).</summary>
    public static void Add(string? name)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            Names.TryAdd(name.Trim(), 0);
        }
    }

    /// <summary>
    /// Whether <paramref name="host"/> (a URI's host part, possibly empty) is this PC: one of the names exactly
    /// (case aside) or a loopback address. No domain is stripped: fresh RHEL/Rocky/CentOS servers are named
    /// <c>localhost.localdomain</c>, and our scripts send the bare name. A remote machine named plain
    /// <c>localhost</c> (some containers and cloud images) still passes; only knowing a tab is in ssh could tell.
    /// </summary>
    public static bool Is(string host) =>
        host.Length == 0 || Names.ContainsKey(host) ||
        IPAddress.TryParse(host.Trim('[', ']'), out var address) && IPAddress.IsLoopback(address);
}
