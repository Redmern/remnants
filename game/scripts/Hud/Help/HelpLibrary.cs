using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;
using Remnants.Core.Help;

namespace Remnants.Game.Hud.Help;

/// <summary>
/// The help topics (data/help, through <see cref="GameDataPaths"/>) and the context their live breakdowns read. A screen
/// sets <see cref="Ambient"/> to what it shows (the colony, the selected survivor, the scavenge run); an
/// <see cref="InfoTip"/> can add its own (the zone on that row, the attack being previewed) on top. Bad help data
/// never breaks a screen: it logs once and shows nothing.
/// </summary>
public static class HelpLibrary
{
    private static HelpCatalog? _catalog;
    private static readonly HashSet<string> Warned = new();

    public static HelpCatalog Catalog => _catalog ??= LoadCatalog();

    /// <summary>What the current screen is about; set by the colony and scavenge screens, cleared when they leave.</summary>
    public static Func<HelpContext>? Ambient { get; set; }

    private static HelpCatalog LoadCatalog()
    {
        try
        {
            var catalog = HelpCatalog.Load(GameDataPaths.Data.Sub("help"));
            foreach (var problem in catalog.Validate(Breakdowns.Ids)) GD.PushWarning($"[help] {problem}");
            return catalog;
        }
        catch (Exception e)
        {
            GD.PushWarning($"[help] could not load the help topics: {e.Message}");
            return HelpCatalog.Empty;
        }
    }

    public static HelpTopic? Find(string id)
    {
        var topic = Catalog.Find(id);
        if (topic is null && Warned.Add(id)) GD.PushWarning($"[help] no topic '{id}'");
        return topic;
    }

    public static string? TitleOf(string id) => Catalog.Find(id)?.Title;

    /// <summary>The ambient context with <paramref name="local"/>'s non-null fields on top.</summary>
    public static HelpContext Context(Func<HelpContext>? local = null)
    {
        var ambient = Safe(() => Ambient?.Invoke()) ?? HelpContext.None;
        if (local is null || Safe(local) is not { } mine) return ambient;
        return new HelpContext
        {
            Colony = mine.Colony ?? ambient.Colony,
            Survivor = mine.Survivor ?? ambient.Survivor,
            Building = mine.Building ?? ambient.Building,
            Zone = mine.Zone ?? ambient.Zone,
            Scavenge = mine.Scavenge ?? ambient.Scavenge,
            Attacker = mine.Attacker ?? ambient.Attacker,
            Action = mine.Action ?? ambient.Action,
            Target = mine.Target ?? ambient.Target,
        };
    }

    /// <summary>The topic's live breakdown for <paramref name="context"/>, or null (no provider, nothing to show, or it threw).</summary>
    public static Breakdown? Live(HelpTopic topic, HelpContext context) =>
        topic.Provider is { } provider ? Safe(() => Breakdowns.For(provider, context)) : null;

    private static T? Safe<T>(Func<T?> f) where T : class
    {
        try
        {
            return f();
        }
        catch (Exception e)
        {
            if (Warned.Add(e.GetType().Name + e.Message)) GD.PushWarning($"[help] {e.Message}");
            return null;
        }
    }

    /// <summary>The topic's long text as BBCode for a <see cref="RichTextLabel"/>; links are <c>[url=topic:ID]</c>.</summary>
    public static string BbCode(string markup)
    {
        var sb = new StringBuilder();
        foreach (var block in HelpMarkup.Parse(markup, TitleOf))
        {
            if (sb.Length > 0) sb.Append(block.Kind == HelpBlockKind.Bullet ? "\n" : "\n\n");
            var inline = Inline(block.Spans);
            switch (block.Kind)
            {
                case HelpBlockKind.Heading:
                    sb.Append($"[font_size=13][color={Palette.Hex(Palette.Rust)}]{inline.ToUpperInvariant()}[/color][/font_size]");
                    break;
                case HelpBlockKind.Bullet:
                    sb.Append($"[indent][color={Palette.Hex(Palette.Amber)}]•[/color] {inline}[/indent]");
                    break;
                default:
                    sb.Append(inline);
                    break;
            }
        }
        return sb.ToString();
    }

    private static string Inline(IEnumerable<HelpSpan> spans) => string.Concat(spans.Select(s => s.Kind switch
    {
        HelpSpanKind.Bold => $"[b]{Escape(s.Text)}[/b]",
        HelpSpanKind.Emphasis => $"[i]{Escape(s.Text)}[/i]",
        HelpSpanKind.Link => $"[url=topic:{s.Topic}][color={Palette.Hex(Palette.Amber)}]{Escape(s.Text)}[/color][/url]",
        _ => Escape(s.Text),
    }));

    // BBCode has no escape for '[': swap in the left-bracket tag.
    private static string Escape(string text) => text.Replace("[", "[lb]");
}
