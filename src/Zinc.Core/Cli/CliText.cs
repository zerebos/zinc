using System.Text;

namespace Zinc.Cli;

/// <summary>How zn prints what it's told: aligned columns for people (<c>(*)</c> = focused).</summary>
public static class CliText
{
    /// <summary>
    /// <paramref name="tree"/> narrowed to the targets <paramref name="request"/> names (<c>zn pane list --workspace 2</c>):
    /// only what's inside them.
    /// </summary>
    public static CliTree Filter(CliTree tree, CliRequest request) => new()
    {
        Windows = tree.Windows
            .Where(w => request.Window is not { } id || w.Id == id)
            .Select(w => new CliWindow
            {
                Id = w.Id, Focused = w.Focused, Quick = w.Quick,
                Workspaces = w.Workspaces
                    .Where(s => request.Workspace is not { } id || s.Id == id)
                    .Select(s => new CliWorkspace
                    {
                        Id = s.Id, Name = s.Name, Focused = s.Focused, Active = s.Active,
                        Panes = s.Panes.Where(p => request.Pane is not { } id || p.Id == id).ToList(),
                    })
                    .Where(s => s.Panes.Count > 0 || request.Pane == null)
                    .ToList(),
            })
            .Where(w => w.Workspaces.Count > 0 || (request.Workspace == null && request.Pane == null))
            .ToList(),
    };

    /// <summary>What <c>zn list</c>, <c>zn &lt;thing&gt; list</c> print: one row per thing at that level.</summary>
    /// <param name="level">"" (zn list), "window", "workspace", "pane" or "terminal".</param>
    /// <param name="cap">
    /// The longest a name, title or folder may be (zn passes 40 when printing to a terminal: one long title made the
    /// whole table too wide to read); null: whole, for output that goes on to another program.
    /// </param>
    public static string List(CliTree tree, string level, int? cap = null)
    {
        string Name(string text) => Cut(text, cap);
        string Folder(string? text) => CutFront(text ?? "", cap);

        List<string[]> rows = level switch
        {
            "window" =>
            [
                ["WINDOW", "WORKSPACES", "TERMINALS", "SHOWING"],
                .. tree.Windows.Select(w => new[]
                {
                    Mark(w.Id, w.Focused) + (w.Quick ? " quick" : ""), w.Workspaces.Count.ToString(),
                    w.Workspaces.Sum(s => s.Panes.Sum(p => p.Terminals.Count)).ToString(),
                    Name(w.Workspaces.FirstOrDefault(s => s.Active)?.Name ?? ""),
                }),
            ],
            "workspace" =>
            [
                ["WINDOW", "WORKSPACE", "NAME", "PANES", "TERMINALS"],
                .. tree.Windows.SelectMany(w => w.Workspaces.Select(s => new[]
                {
                    w.Id.ToString(), Mark(s.Id, s.Focused), Name(s.Name), s.Panes.Count.ToString(),
                    s.Panes.Sum(p => p.Terminals.Count).ToString(),
                })),
            ],
            "pane" =>
            [
                ["WINDOW", "WORKSPACE", "PANE", "TERMINALS", "SHOWING"],
                .. tree.Windows.SelectMany(w => w.Workspaces.SelectMany(s => s.Panes.Select(p => new[]
                {
                    w.Id.ToString(), $"{s.Id} {Name(s.Name)}", Mark(p.Id, p.Focused), p.Terminals.Count.ToString(),
                    Name(p.Terminals.FirstOrDefault(t => t.Active)?.Title ?? ""),
                }))),
            ],
            _ =>
            [
                level == "terminal"
                    ? ["WINDOW", "WORKSPACE", "PANE", "TERMINAL", "TITLE", "FOLDER", "PROFILE"]
                    : ["WINDOW", "WORKSPACE", "PANE", "TERMINAL", "TITLE", "FOLDER"],
                .. tree.Windows.SelectMany(w => w.Workspaces.SelectMany(s => s.Panes.SelectMany(p => p.Terminals.Select(t =>
                {
                    string[] row =
                    [
                        Mark(w.Id, w.Focused), Mark(s.Id, s.Focused) + " " + Name(s.Name), Mark(p.Id, p.Focused), Mark(t.Id, t.Focused),
                        Name(t.Title), Folder(t.Folder),
                    ];
                    return level == "terminal" ? [.. row, t.Profile] : row;
                })))),
            ],
        };
        return Table(rows);
    }

    /// <summary>Text cut to <paramref name="cap"/> characters, the end replaced by an ellipsis.</summary>
    public static string Cut(string text, int? cap) =>
        cap is { } n && text.Length > n ? text[..(n - 1)].TrimEnd() + "…" : text;

    /// <summary>A path cut to <paramref name="cap"/> characters from the front: its end (the folder itself) is what matters.</summary>
    public static string CutFront(string text, int? cap) =>
        cap is { } n && text.Length > n ? "…" + text[^(n - 1)..] : text;

    private static string Mark(int id, bool focused) => focused ? $"{id} (*)" : id.ToString();

    /// <summary>Rows as columns two spaces apart; the last column isn't padded.</summary>
    public static string Table(IReadOnlyList<string[]> rows)
    {
        var columns = rows.Max(r => r.Length);
        var widths = Enumerable.Range(0, columns).Select(c => rows.Max(r => c < r.Length ? r[c].Length : 0)).ToArray();
        var sb = new StringBuilder();
        foreach (var row in rows)
        {
            var line = string.Concat(row.Select((cell, c) => c == row.Length - 1 ? cell : cell.PadRight(widths[c] + 2)));
            sb.AppendLine(line.TrimEnd());
        }

        return sb.ToString();
    }
}
