namespace Zinc.Commands;

/// <summary>An entry in the command palette.</summary>
/// <param name="Title">What the user searches and sees, e.g. "Split right: Git Bash".</param>
/// <param name="Shortcut">Key chord shown on the right, if the command has one.</param>
/// <param name="Run">What happens when it's chosen.</param>
/// <param name="Remember">Whether choosing it makes it a recent command (not for entries that come and go, like tabs).</param>
/// <param name="Icon">What the palette shows before the title (the app's own type; e.g. a profile).</param>
/// <param name="IsTab">A tab in the tab switcher ("@").</param>
/// <param name="AsksArgument">Choosing it keeps the palette open to ask for its argument (its title ends in "…").</param>
/// <param name="SearchWords">Other words that find it, not shown ("font size" for "Zoom in"); a match there ranks below title matches.</param>
public sealed record PaletteCommand(string Title, string? Shortcut, Action Run, bool Remember = true, object? Icon = null,
    bool IsTab = false, bool AsksArgument = false, string? SearchWords = null);

/// <summary>A command that matched a query, with the matched character positions.</summary>
public sealed record PaletteMatch(PaletteCommand Command, int Score, int[] Positions, bool Recent = false);

public static class CommandFilter
{
    /// <summary>How many recent commands lead the list when nothing is typed.</summary>
    public const int RecentShown = 5;

    /// <summary>
    /// Matching commands, best first. An empty query keeps the original order (so the
    /// caller controls how the full list reads); ties keep it too. With <paramref name="recent"/>,
    /// an empty query starts with the recently used commands, and a typed one ranks them a
    /// little higher.
    /// </summary>
    public static IReadOnlyList<PaletteMatch> Filter(IEnumerable<PaletteCommand> commands, string query, RecentCommands? recent = null)
    {
        var matches = new List<(PaletteMatch Match, int Order)>();
        var order = 0;
        foreach (var command in commands)
        {
            var score = FuzzyMatch.Score(query, command.Title, out var positions);
            if (score == null && command.SearchWords != null && FuzzyMatch.Score(query, command.SearchWords, out _) is { } other)
            {
                // Found by its other words: nothing in the title to highlight, and below title matches.
                score = other - 1_000_000;
                positions = [];
            }

            if (score is { } found)
            {
                var rank = command.Remember ? recent?.Rank(command.Title) : null;
                matches.Add((new PaletteMatch(command, found + Boost(rank), positions, rank != null), order));
            }

            order++;
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            return matches.OrderByDescending(m => m.Match.Score).ThenBy(m => m.Order).Select(m => m.Match).ToList();
        }

        // Nothing typed: the few most recent first (most recent on top), then everything in order.
        var leading = matches.Where(m => m.Match.Recent)
            .OrderBy(m => recent!.Rank(m.Match.Command.Title))
            .Take(RecentShown)
            .Select(m => m.Match)
            .ToList();
        return leading.Concat(matches.Select(m => m.Match with { Recent = false }).Where(m => !leading.Any(l => l.Command == m.Command))).ToList();
    }

    /// <summary>
    /// Ranking help for a recent command: about a letter's worth of match quality for the most
    /// recent, fading down the list, so a clearly better match still wins.
    /// </summary>
    private static int Boost(int? rank) => rank is { } r ? Math.Max(100, 600 - r * 25) : 0;
}

/// <summary>
/// Recently run palette commands, most recent first, by title. Shared by every window; saved
/// with the session.
/// </summary>
public sealed class RecentCommands
{
    public const int Capacity = 30;

    private readonly List<string> _titles = [];

    public RecentCommands(IEnumerable<string>? titles = null)
    {
        foreach (var title in titles ?? [])
        {
            if (!_titles.Contains(title) && _titles.Count < Capacity)
            {
                _titles.Add(title);
            }
        }
    }

    /// <summary>Most recent first.</summary>
    public IReadOnlyList<string> Titles => _titles;

    /// <summary>0 for the most recent; null if it isn't recent.</summary>
    public int? Rank(string title)
    {
        var i = _titles.IndexOf(title);
        return i < 0 ? null : i;
    }

    public void Note(string title)
    {
        _titles.Remove(title);
        _titles.Insert(0, title);
        if (_titles.Count > Capacity)
        {
            _titles.RemoveAt(_titles.Count - 1);
        }
    }
}
