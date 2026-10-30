using System.Text.RegularExpressions;
using Remnants.Core.Colony;
using Remnants.Core.Survivors;

namespace Remnants.Core.Story;

/// <summary>A line as the UI shows it: speaker id and display name, resolved text, and the choices open now.</summary>
public sealed record DialogueView(
    string DialogueId, string NodeId, DialogueMode Mode, string Speaker, string SpeakerName, int? SurvivorId, string Text,
    List<(int Index, string Text, string? Hint)> Choices, string Title);

/// <summary>
/// Plays dialogue graphs over the campaign's <see cref="CampaignState.Active"/> session: take a call, go on, choose.
/// Node effects apply when a line is shown, choice effects when it is picked; every line goes to the log. Text
/// placeholders are resolved here (Core), so tests can check them.
/// </summary>
public static class Dialogue
{
    internal static CommandResult Take(ColonySim sim, int callId)
    {
        var campaign = sim.State.Campaign!;
        var call = campaign.Inbox.Find(c => c.Id == callId && c.Tick <= sim.State.Tick);
        if (call is null) return CommandResult.Fail(CommandError.UnknownCall, $"No call {callId}.");
        if (sim.Story!.Dialogues.GetValueOrDefault(call.DialogueId) is not { } def) return CommandResult.Fail(CommandError.UnknownCall, $"Call {callId} has no dialogue.");
        call.Taken = true;
        Open(sim, def, call.Id);
        return CommandResult.Success($"Call from {Campaign.CallerName(sim, def)}.", call.Id);
    }

    /// <summary>Opens a dialogue at its first line (a call replayed from the log opens again from the start).</summary>
    public static void Open(ColonySim sim, DialogueDef def, int? callId = null)
    {
        sim.State.Campaign!.Active = new DialogueSession { DialogueId = def.Id, CallId = callId };
        Enter(sim, def, def.Nodes[0]);
    }

    internal static CommandResult Advance(ColonySim sim)
    {
        if (Current(sim) is not var (def, node)) return CommandResult.Fail(CommandError.NoDialogue, "No dialogue is open.");
        if (node.Choices.Count > 0) return CommandResult.Fail(CommandError.InvalidChoice, "This line needs a choice.");
        Go(sim, def, node.Next);
        return CommandResult.Success("Go on.");
    }

    internal static CommandResult Choose(ColonySim sim, string nodeId, int index)
    {
        if (Current(sim) is not var (def, node)) return CommandResult.Fail(CommandError.NoDialogue, "No dialogue is open.");
        if (node.Id != nodeId || index < 0 || index >= node.Choices.Count) return CommandResult.Fail(CommandError.InvalidChoice, "No such choice here.");
        var choice = node.Choices[index];
        if (!choice.Requires.All(c => Campaign.Holds(sim, c))) return CommandResult.Fail(CommandError.InvalidChoice, "That choice is not open.");
        var campaign = sim.State.Campaign!;
        campaign.AddLog(new(sim.State.Tick, def.Id, SpeakerName(sim, node.Speaker == "narration" ? "player" : node.Speaker), Resolve(sim, choice.Text), Choice: true));
        foreach (var e in choice.Effects) Campaign.Apply(sim, e, def.Title);
        Go(sim, def, choice.Next);
        return CommandResult.Success(choice.Text);
    }

    /// <summary>The open dialogue's line as the UI shows it, or null.</summary>
    public static DialogueView? View(ColonySim sim)
    {
        if (Current(sim) is not var (def, node)) return null;
        var choices = node.Choices.Select((c, i) => (i, c))
            .Where(t => t.c.Requires.All(r => Campaign.Holds(sim, r)))
            .Select(t => (t.i, Resolve(sim, t.c.Text), t.c.Hint)).ToList();
        return new DialogueView(def.Id, node.Id, def.Mode, node.Speaker, SpeakerName(sim, node.Speaker), SurvivorOf(sim, node.Speaker)?.Id,
            Resolve(sim, node.Text), choices, def.Title);
    }

    private static (DialogueDef, DialogueNode)? Current(ColonySim sim)
    {
        if (sim.State.Campaign?.Active is not { } session || sim.Story?.Dialogues.GetValueOrDefault(session.DialogueId) is not { } def) return null;
        return def.Node(session.NodeId) is { } node ? (def, node) : null;
    }

    private static void Go(ColonySim sim, DialogueDef def, string? next)
    {
        var campaign = sim.State.Campaign!;
        // Conditional lines that do not hold are passed over.
        for (var hops = 0; next is not null && def.Node(next) is { } node && hops <= def.Nodes.Count; hops++)
        {
            if (node.When is { } when && !Campaign.Holds(sim, when) && node.Choices.Count == 0)
            {
                next = node.Next;
                continue;
            }
            Enter(sim, def, node);
            return;
        }
        campaign.Active = null;
        campaign.Heard.Add(def.Id);
    }

    private static void Enter(ColonySim sim, DialogueDef def, DialogueNode node)
    {
        var campaign = sim.State.Campaign!;
        campaign.Active!.NodeId = node.Id;
        if (node.Text.Length > 0) campaign.AddLog(new(sim.State.Tick, def.Id, SpeakerName(sim, node.Speaker), Resolve(sim, node.Text)));
        foreach (var e in node.Effects) Campaign.Apply(sim, e, def.Title);
    }

    /// <summary>The colony survivor who speaks for the player: the highest level adult at home (ties: lowest id).</summary>
    public static Survivor? Leader(ColonySim sim) =>
        sim.State.Survivors.Where(s => !s.IsChild && !s.Away).OrderByDescending(s => s.Level).ThenBy(s => s.Id).FirstOrDefault()
        ?? sim.State.Survivors.Where(s => !s.IsChild).OrderBy(s => s.Id).FirstOrDefault();

    /// <summary>The colony survivor behind a speaker id: the leader for "player", the unique survivor (Pell) by id.</summary>
    public static Survivor? SurvivorOf(ColonySim sim, string speaker) => speaker switch
    {
        "player" => Leader(sim),
        "narration" => null,
        _ => sim.State.Survivors.Find(s => s.UniqueId == speaker),
    };

    public static string SpeakerName(ColonySim sim, string speaker) =>
        SurvivorOf(sim, speaker)?.Name
        ?? (speaker == "narration" ? "" : sim.Story?.Characters.GetValueOrDefault(speaker) is { } c ? c.CallSign ?? c.Name : speaker);

    private static readonly Regex Placeholder = new(@"\{([a-z_]+)[:.]([a-z_]+)\}", RegexOptions.Compiled);

    /// <summary>
    /// Resolves <c>{survivor:leader}</c>, <c>{survivor:best_medic}</c>, <c>{survivor:best_shot}</c>, <c>{name:pell}</c>,
    /// <c>{colony.population}</c>, <c>{colony.dead_last_month}</c>, <c>{colony.day}</c>; unknown ones stay as they are.
    /// </summary>
    public static string Resolve(ColonySim sim, string text) => Placeholder.Replace(text, m =>
    {
        var state = sim.State;
        var adults = state.Survivors.Where(s => !s.IsChild).OrderBy(s => s.Id).ToList();
        string Best(Skill skill) => adults.OrderByDescending(s => s.Get(skill)).FirstOrDefault()?.Name ?? "someone";
        return (m.Groups[1].Value, m.Groups[2].Value) switch
        {
            ("survivor", "leader") => Leader(sim)?.Name ?? "someone",
            ("survivor", "best_medic") => Best(Skill.Medicine),
            ("survivor", "best_shot") => Best(Skill.Firearms),
            ("name", var id) => state.Survivors.Find(s => s.UniqueId == id)?.Name ?? sim.Story?.Characters.GetValueOrDefault(id)?.Name ?? id,
            ("colony", "population") => state.Survivors.Count.ToString(),
            ("colony", "dead_last_month") => state.Memorial.Count(e => e.Tick >= state.Tick - 30 * sim.Data.Rules.TicksPerDay).ToString(),
            ("colony", "day") => Campaign.Day(sim).ToString(),
            _ => m.Value,
        };
    });
}
