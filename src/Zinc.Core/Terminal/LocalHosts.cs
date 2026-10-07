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

    /// <summary>How many names count (it grows when one is <see cref="Add"/>ed).</summary>
    public static int Count => Names.Count;

    /// <summary>Counts <paramref name="name"/> as this PC from now on (a WSL distro's own host name).</summary>
    public static void Add(string? name)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            Names.TryAdd(name.Trim(), 0);
        }
    }

    /// <summary>
    /// Whether <paramref name="host"/> (a URI's host part, possibly empty) is this PC. A domain after the name
    /// (<c>pc.localdomain</c>, <c>pc.lan</c>) doesn't change that; a loopback address is local too.
    /// </summary>
    public static bool Is(string host)
    {
        if (host.Length == 0 || Names.ContainsKey(host))
        {
            return true;
        }

        if (IPAddress.TryParse(host.Trim('[', ']'), out var address))
        {
            return IPAddress.IsLoopback(address);
        }

        var dot = host.IndexOf('.');
        return dot > 0 && Names.ContainsKey(host[..dot]);
    }
}
