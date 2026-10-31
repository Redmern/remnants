using System.Text;
using System.Text.RegularExpressions;

namespace Remnants.Core.Help;

public enum HelpBlockKind { Paragraph, Heading, Bullet }

public enum HelpSpanKind { Text, Bold, Emphasis, Link }

/// <summary>A run of text in a block; a link carries the topic id it points at.</summary>
public sealed record HelpSpan(HelpSpanKind Kind, string Text, string? Topic = null);

public sealed record HelpBlock(HelpBlockKind Kind, IReadOnlyList<HelpSpan> Spans);

/// <summary>
/// The help texts' small markup, simple enough for writers who never touch code:
/// <code>
/// ## Heading            a line starting with "## "
/// - bullet              a line starting with "- " or "* "
/// blank line            ends a paragraph (lines in between are joined with a space)
/// **bold**  *em*        inline emphasis
/// [[topic_id]]          a link to another topic, shown with its title
/// [[topic_id|words]]    a link shown as "words"
/// </code>
/// Anything else is plain text. Unclosed markers stay as they are.
/// </summary>
public static partial class HelpMarkup
{
    [GeneratedRegex(@"\[\[([a-z0-9_.\-]+)(?:\|([^\]]+))?\]\]|\*\*(.+?)\*\*|\*([^*\s][^*]*?)\*")]
    private static partial Regex Inline();

    [GeneratedRegex(@"\[\[([a-z0-9_.\-]+)(?:\|[^\]]+)?\]\]")]
    private static partial Regex LinkPattern();

    /// <summary>The topic ids <paramref name="text"/> links to.</summary>
    public static IEnumerable<string> Links(string text) => LinkPattern().Matches(text).Select(m => m.Groups[1].Value);

    public static IReadOnlyList<HelpBlock> Parse(string text, Func<string, string?>? titleOf = null)
    {
        var blocks = new List<HelpBlock>();
        var paragraph = new StringBuilder();
        void Flush()
        {
            if (paragraph.Length == 0) return;
            blocks.Add(new HelpBlock(HelpBlockKind.Paragraph, Spans(paragraph.ToString(), titleOf)));
            paragraph.Clear();
        }
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                Flush();
                continue;
            }
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                Flush();
                blocks.Add(new HelpBlock(HelpBlockKind.Heading, Spans(line[3..].Trim(), titleOf)));
            }
            else if (line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("* ", StringComparison.Ordinal))
            {
                Flush();
                blocks.Add(new HelpBlock(HelpBlockKind.Bullet, Spans(line[2..].Trim(), titleOf)));
            }
            else
            {
                if (paragraph.Length > 0) paragraph.Append(' ');
                paragraph.Append(line);
            }
        }
        Flush();
        return blocks;
    }

    /// <summary>One line of inline markup as spans. A link without a label shows the topic's title (or its id).</summary>
    public static IReadOnlyList<HelpSpan> Spans(string line, Func<string, string?>? titleOf = null)
    {
        var spans = new List<HelpSpan>();
        var at = 0;
        foreach (Match m in Inline().Matches(line))
        {
            if (m.Index > at) spans.Add(new HelpSpan(HelpSpanKind.Text, line[at..m.Index]));
            if (m.Groups[1].Success)
            {
                var id = m.Groups[1].Value;
                var label = m.Groups[2].Success ? m.Groups[2].Value : titleOf?.Invoke(id) ?? id;
                spans.Add(new HelpSpan(HelpSpanKind.Link, label, id));
            }
            else if (m.Groups[3].Success) spans.Add(new HelpSpan(HelpSpanKind.Bold, m.Groups[3].Value));
            else spans.Add(new HelpSpan(HelpSpanKind.Emphasis, m.Groups[4].Value));
            at = m.Index + m.Length;
        }
        if (at < line.Length) spans.Add(new HelpSpan(HelpSpanKind.Text, line[at..]));
        return spans;
    }

    /// <summary>The text with the markup taken out (for search snippets and tooltips).</summary>
    public static string Plain(string text, Func<string, string?>? titleOf = null) =>
        string.Join("\n", Parse(text, titleOf).Select(b => (b.Kind == HelpBlockKind.Bullet ? "• " : "") + string.Concat(b.Spans.Select(s => s.Text))));
}
