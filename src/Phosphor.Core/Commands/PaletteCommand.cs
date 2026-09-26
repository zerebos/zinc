namespace Phosphor.Commands;

/// <summary>An entry in the command palette.</summary>
/// <param name="Title">What the user searches and sees, e.g. "Split right: Git Bash".</param>
/// <param name="Shortcut">Key chord shown on the right, if the command has one.</param>
/// <param name="Run">What happens when it's chosen.</param>
public sealed record PaletteCommand(string Title, string? Shortcut, Action Run);

/// <summary>A command that matched a query, with the matched character positions.</summary>
public sealed record PaletteMatch(PaletteCommand Command, int Score, int[] Positions);

public static class CommandFilter
{
    /// <summary>
    /// Matching commands, best first. An empty query keeps the original order (so the
    /// caller controls how the full list reads); ties keep it too.
    /// </summary>
    public static IReadOnlyList<PaletteMatch> Filter(IEnumerable<PaletteCommand> commands, string query)
    {
        var matches = new List<(PaletteMatch Match, int Order)>();
        var order = 0;
        foreach (var command in commands)
        {
            if (FuzzyMatch.Score(query, command.Title, out var positions) is { } score)
            {
                matches.Add((new PaletteMatch(command, score, positions), order));
            }

            order++;
        }

        return string.IsNullOrWhiteSpace(query)
            ? matches.Select(m => m.Match).ToList()
            : matches.OrderByDescending(m => m.Match.Score).ThenBy(m => m.Order).Select(m => m.Match).ToList();
    }
}
