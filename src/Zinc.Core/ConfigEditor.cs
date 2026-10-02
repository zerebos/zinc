using System.Collections;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Tomlyn.Parsing;
using Tomlyn.Syntax;

namespace Zinc;

/// <summary>
/// Changes one setting in config.toml text and leaves every other character as it was: comments,
/// blank lines, order, quoting, line endings. The syntax tree only locates things; edits are
/// spliced into the text. A setting is replaced where it is (under its table, as a dotted key,
/// or inside an inline table), else added at the end of the table it belongs to, else in a new
/// table at the end (root keys go above the first table).
/// </summary>
public static partial class ConfigEditor
{
    /// <summary>
    /// <paramref name="text"/> with <paramref name="path"/> (e.g. ["window", "workspace-tabs"] or
    /// ["profile", "wsl:Fedora", "colors", "theme"]) set to <paramref name="value"/>: a string,
    /// bool, integer, double, or a list of those.
    /// </summary>
    public static string Set(string text, IReadOnlyList<string> path, object value)
    {
        if (path.Count == 0)
        {
            throw new ArgumentException("A setting needs a key.", nameof(path));
        }

        var doc = Parse(text);
        var formatted = FormatValue(value);
        var nl = text.Contains("\r\n") ? "\r\n" : "\n";

        // 1. Already set: replace the value.
        if (Find(doc).FirstOrDefault(e => e.Path.SequenceEqual(path)) is { } existing)
        {
            var span = existing.KeyValue.Value!.Span;
            return text[..span.Offset] + formatted + text[(span.Offset + span.Length)..];
        }

        // 2. Inside an inline table that holds its siblings: font = { size = 12 } + font.family.
        foreach (var inline in Find(doc).Where(e => e.KeyValue.Value is InlineTableSyntax && IsPrefix(e.Path, path)))
        {
            var table = (InlineTableSyntax)inline.KeyValue.Value!;
            var key = FormatKey(path.Skip(inline.Path.Count));
            if (table.Items.Any())
            {
                var last = table.Items.Last().KeyValue!.Value!.Span;
                var at = last.Offset + last.Length;
                return text[..at] + $", {key} = {formatted}" + text[at..];
            }

            var brace = table.OpenBrace!.Span;
            var after = brace.Offset + brace.Length;
            return text[..after] + $" {key} = {formatted} " + text[after..].TrimStart();
        }

        // 3. Under the deepest existing table that holds it.
        var tables = doc.Tables.OfType<TableSyntax>().Where(t => t.Name != null).Select(t => (Table: t, Path: KeyParts(t.Name!))).ToList();
        if (tables.Where(t => t.Path.Count < path.Count && IsPrefix(t.Path, path)).OrderByDescending(t => t.Path.Count).FirstOrDefault() is { Table: not null } home)
        {
            var line = $"{FormatKey(path.Skip(home.Path.Count))} = {formatted}";
            // A key-value's span includes its line break: anchor on its value (or the header's name).
            var anchor = home.Table.Items.Any() ? (SyntaxNode)home.Table.Items.Last().Value! : home.Table.Name!;
            var at = EndOfLine(text, anchor.Span.Offset + anchor.Span.Length);
            return InsertLine(text, at, line, nl);
        }

        // 4. A root key: above the first table (after the file's opening comments).
        if (path.Count == 1)
        {
            var line = $"{FormatKey(path)} = {formatted}";
            if (doc.KeyValues.Any())
            {
                var last = doc.KeyValues.Last().Value!.Span;
                return InsertLine(text, EndOfLine(text, last.Offset + last.Length), line, nl);
            }

            var first = FirstContentLine(text);
            return text[..first] + line + nl + (first < text.Length ? nl : "") + text[first..];
        }

        // 5. A new table at the end. Profiles get their own [profile.<id>] section, after the
        //    last one there is (so it joins the others rather than follow unrelated sections).
        var headerLength = path[0] == "profile" && path.Count > 2 ? 2 : path.Count - 1;
        var header = FormatKey(path.Take(headerLength));
        var body = $"{FormatKey(path.Skip(headerLength))} = {formatted}";
        if (headerLength == 2 && tables.LastOrDefault(t => t.Path is ["profile", _, ..]) is { Table: not null } lastProfile)
        {
            var anchor = lastProfile.Table.Items.Any() ? (SyntaxNode)lastProfile.Table.Items.Last().Value! : lastProfile.Table.Name!;
            var at = EndOfLine(text, anchor.Span.Offset + anchor.Span.Length);
            return text[..at] + nl + nl + $"[{header}]" + nl + body + text[at..];
        }

        var end = text.Length == 0 || text.EndsWith('\n') ? text : text + nl;
        var gap = end.Length == 0 || end.EndsWith(nl + nl) ? "" : nl;
        return end + gap + $"[{header}]" + nl + body + nl;
    }

    /// <summary><paramref name="text"/> without the line setting <paramref name="path"/>; unchanged if it isn't set on a line of its own.</summary>
    public static string Remove(string text, IReadOnlyList<string> path)
    {
        var doc = Parse(text);
        if (Find(doc).FirstOrDefault(e => e.Path.SequenceEqual(path) && !e.Inline) is not { } existing)
        {
            return text;
        }

        var key = existing.KeyValue.Key!.Span;
        var value = existing.KeyValue.Value!.Span;
        var start = text.LastIndexOf('\n', Math.Max(0, key.Offset - 1)) + 1;
        var end = EndOfLine(text, value.Offset + value.Length);
        if (end < text.Length && text[end] == '\r')
        {
            end++;
        }

        if (end < text.Length && text[end] == '\n')
        {
            end++;
        }

        return text[..start] + text[end..];
    }

    /// <summary>
    /// <paramref name="text"/> without the table <paramref name="path"/> (e.g. <c>[profile.x]</c>)
    /// and its sub-tables (<c>[profile.x.font]</c>): each from its header to the next table's,
    /// less the comment lines just above that one (they introduce it). Keys set elsewhere as
    /// dotted keys (<c>profile.x.name = …</c> at the top) aren't touched.
    /// </summary>
    public static string RemoveTable(string text, IReadOnlyList<string> path)
    {
        var doc = Parse(text);
        var headers = doc.Tables.Where(t => t.Name != null)
            .Select(t => (Path: KeyParts(t.Name!), Start: text.LastIndexOf('\n', Math.Max(0, t.Span.Start.Offset - 1)) + 1))
            .OrderBy(t => t.Start)
            .ToList();
        for (var i = headers.Count - 1; i >= 0; i--)
        {
            var (tablePath, start) = headers[i];
            if (!(tablePath.SequenceEqual(path) || IsPrefix(path, tablePath)))
            {
                continue;
            }

            var end = i + 1 < headers.Count ? headers[i + 1].Start : text.Length;
            // Comment lines right above the next header belong to it.
            while (end > start)
            {
                var lineStart = text.LastIndexOf('\n', Math.Max(0, end - 2)) + 1;
                if (lineStart <= start || !text[lineStart..end].TrimStart().StartsWith('#'))
                {
                    break;
                }

                end = lineStart;
            }

            text = text[..start] + text[end..];
        }

        return text;
    }

    /// <summary>Sets a value in a config file, writing it atomically (a temp file moved over it).</summary>
    public static void SetInFile(string file, IReadOnlyList<string> path, object value)
    {
        var text = File.Exists(file) ? File.ReadAllText(file) : "";
        var updated = Set(text, path, value);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
        var temp = file + ".tmp";
        File.WriteAllText(temp, updated, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temp, file, overwrite: true);
    }

    // ------------------------------------------------------------------ helpers

    private static DocumentSyntax Parse(string text)
    {
        var doc = SyntaxParser.Parse(text, "config.toml");
        if (doc.HasErrors)
        {
            throw new FormatException($"config.toml isn't valid TOML: {doc.Diagnostics[0].Message}");
        }

        return doc;
    }

    private sealed record Located(IReadOnlyList<string> Path, KeyValueSyntax KeyValue, bool Inline);

    /// <summary>Every key-value with its full path, inline tables' items included.</summary>
    private static IEnumerable<Located> Find(DocumentSyntax doc)
    {
        var all = new List<Located>();
        foreach (var kv in doc.KeyValues)
        {
            Add([], kv, inline: false);
        }

        foreach (var table in doc.Tables.OfType<TableSyntax>().Where(t => t.Name != null))
        {
            foreach (var kv in table.Items)
            {
                Add(KeyParts(table.Name!), kv, inline: false);
            }
        }

        return all;

        void Add(IReadOnlyList<string> prefix, KeyValueSyntax kv, bool inline)
        {
            if (kv.Key == null)
            {
                return;
            }

            var path = prefix.Concat(KeyParts(kv.Key)).ToList();
            all.Add(new Located(path, kv, inline));
            if (kv.Value is InlineTableSyntax table)
            {
                foreach (var item in table.Items)
                {
                    if (item.KeyValue != null)
                    {
                        Add(path, item.KeyValue, inline: true);
                    }
                }
            }
        }
    }

    internal static List<string> KeyParts(KeySyntax key)
    {
        var parts = new List<string> { KeyText(key.Key) };
        foreach (var dot in key.DotKeys)
        {
            parts.Add(KeyText(dot.Key));
        }

        return parts;
    }

    private static string KeyText(BareKeyOrStringValueSyntax? key) => key switch
    {
        BareKeySyntax bare => bare.Key?.Text ?? "",
        StringValueSyntax s => s.Value ?? "",
        _ => "",
    };

    private static bool IsPrefix(IReadOnlyList<string> prefix, IReadOnlyList<string> path) =>
        prefix.Count < path.Count && prefix.SequenceEqual(path.Take(prefix.Count));

    /// <summary>Where the line containing <paramref name="offset"/> ends (before its line break).</summary>
    private static int EndOfLine(string text, int offset)
    {
        var i = Math.Min(offset, text.Length);
        while (i < text.Length && text[i] is not ('\n' or '\r'))
        {
            i++;
        }

        return i;
    }

    private static string InsertLine(string text, int endOfLine, string line, string nl) =>
        text[..endOfLine] + nl + line + text[endOfLine..];

    /// <summary>The start of the first line that isn't blank or a comment (the end if there's none).</summary>
    private static int FirstContentLine(string text)
    {
        var start = 0;
        while (start < text.Length)
        {
            var end = text.IndexOf('\n', start);
            var line = (end < 0 ? text[start..] : text[start..end]).Trim();
            if (line.Length > 0 && !line.StartsWith('#'))
            {
                return start;
            }

            if (end < 0)
            {
                return text.Length;
            }

            start = end + 1;
        }

        return text.Length;
    }

    [GeneratedRegex("^[A-Za-z0-9_-]+$")]
    private static partial Regex BareKey();

    private static string FormatKey(IEnumerable<string> parts) =>
        string.Join('.', parts.Select(p => BareKey().IsMatch(p) ? p : FormatString(p)));

    /// <summary>A value as TOML: paths with backslashes as 'literal strings', the rest as "basic" ones.</summary>
    public static string FormatValue(object value) => value switch
    {
        string s => s.Contains('\\') && !s.Contains('\'') && !s.Contains('\n') ? $"'{s}'" : FormatString(s),
        bool b => b ? "true" : "false",
        int or long or short or byte => Convert.ToString(value, CultureInfo.InvariantCulture)!,
        float or double or decimal => FormatNumber(Convert.ToDouble(value, CultureInfo.InvariantCulture)),
        IEnumerable list => "[" + string.Join(", ", list.Cast<object>().Select(FormatValue)) + "]",
        _ => throw new ArgumentException($"Can't write a {value.GetType().Name} to TOML.", nameof(value)),
    };

    private static string FormatNumber(double d)
    {
        var s = d.ToString("R", CultureInfo.InvariantCulture);
        return s.Contains('.') || s.Contains('E') || s.Contains("Infinity") || s.Contains("NaN") ? s : s + ".0";
    }

    private static string FormatString(string s)
    {
        var sb = new StringBuilder("\"");
        foreach (var c in s)
        {
            sb.Append(c switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                < ' ' => $"\\u{(int)c:X4}",
                _ => c.ToString(),
            });
        }

        return sb.Append('"').ToString();
    }
}
