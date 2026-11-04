using System.Linq;
using Remnants.Core.Scavenge;

namespace Remnants.Game.Scavenge;

/// <summary>
/// Debug (<c>--tactics-demo[=overlay|picker|reaction|enemy|preview|targeting|smoke]</c>, for screenshots): stages a Husk and a Linked in sight of the party
/// with readied strikes (one aimed at a survivor's tile), so the threat overlay, the telegraphs and the hand strip show;
/// <c>picker</c> also opens the character picker, <c>reaction</c> on a reaction card against the aimed strike; <c>targeting</c>
/// aims Shove at the Husk (brought next to the party) and <c>smoke</c> a smoke bomb, with a hand of ability cards.
/// </summary>
public static class TacticsDemo
{
    public static bool Requested => DebugCapture.HasFlag("--tactics-demo") || DebugCapture.Value("--tactics-demo") is not null;

    public static string Mode => DebugCapture.Value("--tactics-demo") is { Length: > 0 } m ? m : "overlay";

    public static void Stage(ScavengeSim sim, int playerId)
    {
        var state = sim.State;
        var data = sim.Data;
        var mine = state.Units.Where(u => u.PlayerId == playerId && u.OnMap).OrderBy(u => u.Id).ToList();
        if (mine.Count == 0) return;
        var anchor = mine[0].Pos;
        Pos? Free(int dx, int dy)
        {
            for (var r = 0; r < 4; r++)
                foreach (var d in Pos.Neighbours8.Prepend(new Pos(0, 0)))
                {
                    var p = new Pos(anchor.X + dx + d.X * r, anchor.Y + dy + d.Y * r);
                    if (state.Map.InBounds(p) && data.Terrain(state.Map[p]).Passable && state.UnitAt(p) is null && !state.Map.IsExit(p)) return p;
                }
            return null;
        }
        var dir = anchor.Y < state.Map.Height / 2 ? 1 : -1;
        if (Free(1, 4 * dir) is { } huskAt)
        {
            var husk = NewScavenge.EnemyUnit(data, state, data.Enemies["husk"], huskAt);
            state.Units.Add(husk);
            husk.Ai.Mode = AiMode.Hunting;
            state.Strikes.Add(new TileStrike { UnitId = husk.Id, ActionId = "claw", From = husk.Pos, Tile = mine[0].Pos, Direction = Combat.Face(husk.Pos, mine[0].Pos), Tiles = [mine[0].Pos] });
        }
        if (Free(-3, 5 * dir) is { } linkedAt)
        {
            var linked = NewScavenge.EnemyUnit(data, state, data.Enemies["linked"], linkedAt);
            state.Units.Add(linked);
            linked.Ai.Mode = AiMode.Hunting;
            var target = mine.Count > 1 ? mine[1].Pos : new Pos(anchor.X - 1, anchor.Y);
            state.Strikes.Add(new TileStrike { UnitId = linked.Id, ActionId = sim.Profile(linked).Actions.First().Id, From = linked.Pos, Tile = target, Direction = Combat.Face(linked.Pos, target), Tiles = [target] });
        }
        // The staged enemies act after everyone already in the order; the unit up now (whose turn has begun) stays up.
        foreach (var e in state.Units.Where(u => u.Kind == UnitKind.Enemy && u.IsActive && !state.TurnOrder.Contains(u.Id)))
            state.TurnOrder.Add(e.Id);
        if (SquadDecks.Of(state, playerId) is { } deck)
        {
            // A hand that shows every kind: a reaction, a hard card, a boost, a dead card.
            deck.Hand = Mode is "targeting" or "smoke"
                ? ["shove", "smoke_bomb", "switch_places", "flank_step", "sidestep"]
                : ["sidestep", "steady_aim", "dash", "hold_the_line", "take_cover"];
            deck.Points = deck.Budget;
        }
    }
}
