using System.Text.Json;
using Remnants.Core.Data;
using Remnants.Core.Serialization;

namespace Remnants.Core.Help;

/// <summary>
/// One help topic (data/help/*.json): a short line for the hover tooltip, a long text in <see cref="HelpMarkup"/> for the
/// detail popover and the help index, and optionally the id of a live breakdown (<see cref="Breakdowns"/>) the UI shows
/// under the text with numbers from the running game.
/// </summary>
public sealed record HelpTopic
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";

    /// <summary>One or two sentences: what the hover tooltip shows.</summary>
    public string Short { get; init; } = "";

    /// <summary>The full explanation in <see cref="HelpMarkup"/>: paragraphs, "- " bullets, "## " headings, **bold**, *em*, [[topic]] links.</summary>
    public string Long { get; init; } = "";

    /// <summary>A <see cref="Breakdowns"/> id: the live numbers shown under the text (null: text only).</summary>
    public string? Provider { get; init; }

    /// <summary>Topics listed as "Related" under the text.</summary>
    public List<string> Related { get; init; } = new();

    /// <summary>Extra words the help index search matches (the title, short and long text always count).</summary>
    public List<string> Keywords { get; init; } = new();

    /// <summary>The help index group; the file's category when left out.</summary>
    public string Category { get; init; } = "";
}

/// <summary>One data/help/*.json file: a category and its topics.</summary>
public sealed record HelpFile
{
    public string Category { get; init; } = "";
    public List<HelpTopic> Topics { get; init; } = new();
}

/// <summary>
/// Every help topic, by id and in file order. Writers edit data/help/*.json; <see cref="Validate"/> checks that ids are
/// unique, that links and related topics exist and that short texts stay short, so a typo fails a test instead of
/// showing a dead link.
/// </summary>
public sealed class HelpCatalog
{
    /// <summary>The longest a hover line may be (characters).</summary>
    public const int MaxShort = 220;

    private readonly Dictionary<string, HelpTopic> _byId = new(StringComparer.Ordinal);

    public IReadOnlyList<HelpTopic> Topics { get; }

    /// <summary>Category names in the order their files load.</summary>
    public IReadOnlyList<string> Categories { get; }

    public HelpCatalog(IEnumerable<HelpTopic> topics)
    {
        Topics = topics.ToList();
        foreach (var t in Topics) _byId.TryAdd(t.Id, t);
        Categories = Topics.Select(t => t.Category).Distinct().ToList();
    }

    public static readonly HelpCatalog Empty = new([]);

    public HelpTopic? Find(string id) => _byId.GetValueOrDefault(id);

    public bool Contains(string id) => _byId.ContainsKey(id);

    /// <summary>Reads every *.json in <paramref name="folder"/> (the data's help/ folder), files in name order.</summary>
    public static HelpCatalog Load(DataSource folder)
    {
        var topics = new List<HelpTopic>();
        foreach (var path in folder.List(""))
        {
            HelpFile file;
            try
            {
                file = JsonSerializer.Deserialize<HelpFile>(folder.Read(path), Json.Options) ?? new HelpFile();
            }
            catch (JsonException e)
            {
                throw new GameDataException($"help/{path} is invalid: {e.Message}");
            }
            topics.AddRange(file.Topics.Select(t => t.Category.Length > 0 ? t : t with { Category = file.Category }));
        }
        return new HelpCatalog(topics);
    }

    /// <summary>What is wrong with the catalogue (empty when it is fine); <paramref name="providers"/> are the known breakdown ids.</summary>
    public IReadOnlyList<string> Validate(IEnumerable<string> providers)
    {
        var known = providers.ToHashSet(StringComparer.Ordinal);
        var problems = new List<string>();
        foreach (var dup in Topics.GroupBy(t => t.Id).Where(g => g.Count() > 1)) problems.Add($"duplicate topic id '{dup.Key}'");
        foreach (var t in Topics)
        {
            if (t.Id.Length == 0) problems.Add($"a topic titled '{t.Title}' has no id");
            if (t.Title.Length == 0) problems.Add($"'{t.Id}' has no title");
            if (t.Short.Length == 0) problems.Add($"'{t.Id}' has no short text");
            if (t.Short.Length > MaxShort) problems.Add($"'{t.Id}' short text is {t.Short.Length} characters (max {MaxShort})");
            if (t.Long.Length == 0) problems.Add($"'{t.Id}' has no long text");
            if (t.Provider is { } p && !known.Contains(p)) problems.Add($"'{t.Id}' names unknown provider '{p}'");
            foreach (var r in t.Related.Where(r => !Contains(r))) problems.Add($"'{t.Id}' relates to unknown topic '{r}'");
            foreach (var link in HelpMarkup.Links(t.Long).Where(l => !Contains(l))) problems.Add($"'{t.Id}' links to unknown topic '{link}'");
        }
        return problems;
    }

    /// <summary>
    /// Topics matching every word of <paramref name="query"/> (case-insensitive), best first: title hits, then keyword,
    /// then short, then long text. An empty query lists every topic in catalogue order.
    /// </summary>
    public IReadOnlyList<HelpTopic> Search(string query)
    {
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(w => w.ToLowerInvariant()).ToArray();
        if (words.Length == 0) return Topics;
        var scored = new List<(HelpTopic Topic, int Score, int Order)>();
        for (var i = 0; i < Topics.Count; i++)
        {
            var t = Topics[i];
            var title = t.Title.ToLowerInvariant();
            var keys = string.Join(' ', t.Keywords).ToLowerInvariant() + " " + t.Id.Replace('_', ' ');
            var shortText = t.Short.ToLowerInvariant();
            var longText = t.Long.ToLowerInvariant();
            var score = 0;
            foreach (var w in words)
            {
                var s = title.Contains(w) ? 8 : keys.Contains(w) ? 4 : shortText.Contains(w) ? 2 : longText.Contains(w) ? 1 : 0;
                if (s == 0)
                {
                    score = 0;
                    break;
                }
                score += s + (title.StartsWith(w, StringComparison.Ordinal) ? 2 : 0);
            }
            if (score > 0) scored.Add((t, score, i));
        }
        return scored.OrderByDescending(s => s.Score).ThenBy(s => s.Order).Select(s => s.Topic).ToList();
    }
}
