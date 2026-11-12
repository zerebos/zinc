namespace Zinc;

/// <summary>Where an installed Zinc looks for updates, as the address built into a release says it.</summary>
public static class UpdateFeed
{
    /// <summary>
    /// The GitHub repository an update address names ("https://github.com/owner/name"), whose releases hold the
    /// updates. Null for any other address, such as a folder or a web address that serves the release files
    /// themselves.
    /// </summary>
    public static string? GitHubRepository(string address)
    {
        if (!Uri.TryCreate(address.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // Exactly an owner and a name. A longer path is a page or a file on GitHub, not a repository.
        var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
        {
            return null;
        }

        var name = parts[1].EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? parts[1][..^4] : parts[1];
        return name.Length == 0 ? null : $"https://github.com/{parts[0]}/{name}";
    }
}
