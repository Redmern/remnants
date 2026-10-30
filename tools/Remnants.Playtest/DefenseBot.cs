using Remnants.Core.Colony;
using Remnants.Core.Raids;
using Remnants.Core.Survivors;

namespace Remnants.Playtest;

/// <summary>
/// The bot's defense: as the grace period ends it puts up a Lookout Tower with a guard, an Alarm Bell and spike strips on
/// every side; then a gate and wooden walls across the side raids came from, more strips and guards as the colony grows,
/// and tier upgrades it has schematics for. It re-arms traps and queues every repair, and on a warning shuts the gates,
/// shelters the children and recalls a party that can make it home. Raids always auto-resolve. Spends only what the
/// build order does not need.
/// </summary>
public sealed class DefenseBot(ColonySim sim, ColonyBot colony)
{
    private RaidData Raids => sim.Raids!;
    private ColonyState State => sim.State;
    private DefenseGrid Grid => DefenseGrid.Of(sim, Raids);
    private int _preppedRaid;

    public List<RaidReport> Reports { get; } = [];
    public double? FirstRaidDay { get; private set; }
    private int _seenReport;

    public void Act()
    {
        if (sim.Raids is null) return;
        Collect();
        Prep();
        Maintain();
        var day = State.Tick / (double)sim.Data.Rules.TicksPerDay;
        if (day < Raids.Def.Scheduler.GraceDays - 2) return;
        Build();
        Guard();
    }

    private void Collect()
    {
        if (State.Defense.LastReport is { } r && r.RaidId != _seenReport)
        {
            _seenReport = r.RaidId;
            Reports.Add(r);
            FirstRaidDay ??= r.Tick / (double)sim.Data.Rules.TicksPerDay;
        }
    }

    /// <summary>
    /// What we can spend: essentials (tower, bell, a strip a side) only keep a small reserve; the rest leave the next build
    /// goal's cost untouched too.
    /// </summary>
    private bool Spare(Dictionary<string, double> cost, bool essential = false)
    {
        var goal = essential ? new Dictionary<string, double>() : colony.GoalCost();
        return cost.All(kv => State.Stockpile.Get(kv.Key) - goal.GetValueOrDefault(kv.Key) - (kv.Key == "wood" ? 10 : 0) >= kv.Value);
    }

    private bool TryPlace(string id, (int X, int Y) at, Side facing = Side.South, bool essential = false)
    {
        if (!sim.Raids!.Defenses.TryGetValue(id, out var def) || !DefenseSchematics.Unlocked(sim, id)) return false;
        if (!Spare(DefenseHandlers.Cost(sim, def, def.Cost), essential)) return false;
        var cmd = new PlaceDefense(id, at.X, at.Y, facing);
        if (!DefenseHandlers.CanPlace(sim, Raids, cmd).Ok) return false;
        return sim.Execute(cmd).Ok;
    }

    private (int X, int Y) Edge(Side side, int inset, int offset)
    {
        var g = Grid;
        return side switch
        {
            Side.North => (g.Width / 2 + offset, inset),
            Side.South => (g.Width / 2 + offset, g.Height - 1 - inset),
            Side.West => (inset, g.Height / 2 + offset),
            _ => (g.Width - 1 - inset, g.Height / 2 + offset),
        };
    }

    private int Count(Func<DefenseDef, bool> what) => State.Defense.Defenses.Count(d => what(Raids.Defense(d.DefId)));

    /// <summary>The side the last raids came from most, else North (where Husks come from most).</summary>
    private Side Threatened() =>
        Reports.GroupBy(r => r.Approach).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).Select(g => g.Key).FirstOrDefault(Side.North);

    private void Build()
    {
        // 1. A tower on the threatened side, 2. a bell, 3. a strip on every side.
        if (Count(d => d.Kind == DefenseKind.Tower) == 0 && !TryPlace("watchtower_1", Edge(Side.North, 3, -2), essential: true)) return;
        if (Count(d => d.Kind == DefenseKind.Alarm) == 0 && !TryPlace("alarm_bell", Edge(Side.South, 3, 3), essential: true)) return;
        foreach (var side in Enum.GetValues<Side>())
            if (!State.Defense.Defenses.Any(d => Raids.Defense(d.DefId).IsTrap && Grid.SideOf(d.X, d.Y) == side)
                && !TryPlace("spike_strip", Edge(side, 1, 0), essential: true)) return;
        // 4. The gate on the road, then walls across the threatened side and the road.
        if (Count(d => d.Kind == DefenseKind.Gate) == 0 && !TryPlace("gate_wood", Edge(Side.South, 2, 0))) return;
        var front = Threatened();
        for (var o = 1; o <= 4; o++)
            foreach (var side in new[] { front, Side.South }.Distinct())
                foreach (var off in new[] { o, -o })
                    if (!State.Defense.Defenses.Any(d => (d.X, d.Y) == Edge(side, 2, off)) && !TryPlace("wall_wood", Edge(side, 2, off))) return;
        // 5. More strips on the threatened side; better ones when known.
        var trap = new[] { "scrap_mine", "fire_trap", "pit_trap", "spike_strip" }.First(id => DefenseSchematics.Unlocked(sim, id) || id == "spike_strip");
        for (var o = 1; o <= 3; o++)
            if (!State.Defense.Defenses.Any(d => (d.X, d.Y) == Edge(front, 1, o * 2)) && !TryPlace(trap, Edge(front, 1, o * 2))) return;
        // 6. Upgrades we have schematics for.
        foreach (var d in State.Defense.Defenses.OrderBy(d => d.Id).ToList())
            if (Raids.Defense(d.DefId).UpgradesTo is { } next && DefenseSchematics.Unlocked(sim, next)
                && Spare(DefenseHandlers.UpgradeCost(Raids.Defense(d.DefId), Raids.Defense(next))))
                sim.Execute(new UpgradeDefense(d.Id));
        // 7. A turret on the threatened side once the Town Hall allows it.
        if (Count(d => d.Kind == DefenseKind.Turret) == 0) TryPlace("defense_turret", Edge(front, 3, 2), front);
    }

    /// <summary>One guard per 6 adults at home from 7 (a guard is a worker lost), the best eyes and aim; posted at the tower.</summary>
    private void Guard()
    {
        var adults = State.Survivors.Where(s => !s.Away && !s.IsChild && s.PatientAtBuildingId is null).ToList();
        var want = adults.Count < 7 ? 0 : adults.Count / 6;
        var guards = State.Defense.Guards.Count;
        foreach (var extra in State.Defense.Guards.Keys.Take(Math.Max(0, guards - want)).ToList())
            sim.Execute(new AssignGuard(extra, null));
        if (guards >= want) return;
        var post = State.Defense.Defenses.Where(d => DefenseHandlers.FreeGuardSlots(sim, Raids, d) > 0).OrderBy(d => d.Id).FirstOrDefault();
        if (post is null)
        {
            TryPlace("sandbag_nest", Edge(Threatened(), 3, 3));
            return;
        }
        var pick = adults.Where(s => !State.Defense.Guards.ContainsKey(s.Id))
            .OrderByDescending(s => s.Get(AttributeKind.Perception) + s.Get(Skill.Firearms)).ThenBy(s => s.Id).FirstOrDefault();
        if (pick is not null) sim.Execute(new AssignGuard(pick.Id, post.Id));
    }

    private void Maintain()
    {
        foreach (var d in State.Defense.Defenses.Where(d => d.Sprung).ToList())
            if (Spare(DefenseHandlers.Cost(sim, Raids.Defense(d.DefId), Raids.Defense(d.DefId).RearmCost), essential: true)) sim.Execute(new RearmTrap(d.Id));
        foreach (var b in State.Buildings.Where(b => RaidSystem.Damage(State, b) > 0).ToList())
            sim.Execute(new QueueRepair(RepairTarget.Building, b.Id));
        foreach (var d in State.Defense.Defenses.Where(d => d.Hp < Raids.Defense(d.DefId).Hp).ToList())
            sim.Execute(new QueueRepair(RepairTarget.Defense, d.Id));
    }

    private void Prep()
    {
        if (State.Defense.Raid is not { Stage: RaidStage.Warned } raid || raid.Id == _preppedRaid) return;
        _preppedRaid = raid.Id;
        sim.Execute(new CloseGates(true));
        sim.Execute(new ShelterChildren(true));
        if (DefenseHandlers.CanRecall(State, raid)) sim.Execute(new RecallExpedition());
    }
}
