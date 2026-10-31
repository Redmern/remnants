using Remnants.Core.Colony;
using Remnants.Core.Items;
using Remnants.Core.Scavenge;
using Remnants.Core.Survivors;

namespace Remnants.Core.Raids;

/// <summary>
/// The fought raid (stage 2): a tactical map generated from the real colony layout, played with the scavenge engine and
/// the story framework's Defend / Survive objectives, and fed back through the same <see cref="Consequences"/> as
/// auto-resolve.
/// <list type="bullet">
/// <item><see cref="ColonyMap"/>: the defense grid 1:1 as a <see cref="HandmadeMapDef"/>. Built plots become a 3×3 block of
/// wall with a door tile holding the building as a structure (HP from <c>consequences.buildingHp</c>; the Town Hall tagged
/// <c>hall</c>, the Radio Mast <c>mast</c>); walls and shut gates are wall, barricades and nests cover, wrecked defenses
/// rubble, towers (and the Watchtower building) raised posts, turrets the colony's own turret devices; the approach band is
/// where attackers spawn; the party spawns by the Town Hall.</item>
/// <item><see cref="Create"/>: the raid's attackers (minus what the traps take out first) in waves toward the Town Hall,
/// "survive <c>battle.rounds</c> rounds and keep the Town Hall standing"; the defenders with their gear from the inventory;
/// guards at their posts; floodlights lit at night; the arrival's hour and phase.</item>
/// <item><see cref="Apply"/>: survivors as they came out (injuries, deaths, field skill), building damage from the
/// structures' HP, then <see cref="Consequences.Apply"/> with the battle's casualties and a pressure from how it went.</item>
/// <item><see cref="StoryMap"/>: the same map for a story perimeter mission (M5, M14), when it has every marker and tag the
/// mission's script names; otherwise the mission keeps its handmade perimeter map.</item>
/// </list>
/// </summary>
public static class RaidBattle
{
    public const string HallTag = "hall";
    public const string MastTag = "mast";
    public const string TowerMarker = "tower";
    public const string ApproachMarker = "approach";

    private const string Pool = "bcdfghjkmnoqrtuvwxyzABCDFGHIJKLMNOQRUVWXYZ0123456789!$&*+-:;<>?@^_|";

    /// <summary>A new building's site shows on the battle map from this share of its work (a frame: low cover)...</summary>
    public const double SiteFrameFrom = 0.15;

    /// <summary>...and stands as walls from this share.</summary>
    public const double SiteWallsFrom = 0.6;

    /// <summary>The tag of a construction site's structure in a battle map.</summary>
    public static string SiteTag(ConstructionSite site) => $"s{site.BuildingId}";

    /// <summary>A finished level-1 building's HP, which a site has its progress share of.</summary>
    public static double SiteLevelHp(RaidData raids) => raids.Def.Consequences.BuildingHp[0];

    /// <summary>The tag of a building's structure in a battle map.</summary>
    public static string TagOf(ColonySim sim, Building b) =>
        b.DefId == sim.Data.Rules.TownHallBuildingId ? HallTag : b.DefId == "radio_mast" ? MastTag : $"b{b.Id}";

    /// <summary>
    /// The colony as a tactical map, attackers spawning in the band <paramref name="approach"/> side (marker
    /// <paramref name="spawnMarker"/>). <paramref name="gatesClosed"/>: every gate is shut (else only gates shut by hand).
    /// </summary>
    public static HandmadeMapDef ColonyMap(ColonySim sim, RaidData raids, Side approach, string spawnMarker = ApproachMarker, bool gatesClosed = false)
    {
        var grid = DefenseGrid.Of(sim, raids);
        var rows = Enumerable.Range(0, grid.Height).Select(_ => Enumerable.Repeat('.', grid.Width).ToArray()).ToArray();
        var markers = new Dictionary<string, string>(StringComparer.Ordinal);
        var structures = new List<MapStructureDef>();
        var devices = new List<MapDeviceDef>();
        var pool = new Queue<char>(Pool);
        char Name(string name)
        {
            var c = pool.Dequeue();
            markers[c.ToString()] = name;
            return c;
        }

        // The approach band: the outer two rows / columns of that side.
        var approachChar = Name(spawnMarker);
        for (var y = 0; y < grid.Height; y++)
            for (var x = 0; x < grid.Width; x++)
                if (grid.DistanceToEdge(x, y, approach) < 2) rows[y][x] = approachChar;

        // Exits on the far side, in the middle, so a defender can run.
        var far = approach switch { Side.North => Side.South, Side.South => Side.North, Side.East => Side.West, _ => Side.East };
        for (var y = 0; y < grid.Height; y++)
            for (var x = 0; x < grid.Width; x++)
                if (grid.DistanceToEdge(x, y, far) == 0
                    && (far is Side.North or Side.South ? Math.Abs(x - grid.Width / 2) < 2 : Math.Abs(y - grid.Height / 2) < 2))
                    rows[y][x] = 'E';

        var towerChar = Name(TowerMarker);
        foreach (var b in sim.State.Buildings.OrderBy(b => b.Id))
        {
            var cells = grid.PlotCells(b.Plot).ToList();
            int x0 = cells.Min(c => c.X), y0 = cells.Min(c => c.Y);
            for (var y = y0 + 1; y < y0 + grid.CellsPerPlot; y++)
                for (var x = x0 + 1; x < x0 + grid.CellsPerPlot; x++)
                    rows[y][x] = RaidSystem.IsRubble(sim, b) ? '%' : '#';
            var door = (X: x0 + grid.CellsPerPlot / 2, Y: y0 + grid.CellsPerPlot - 1);
            if (b.DefId == "watchtower")
            {
                rows[door.Y][door.X] = towerChar;
                continue;
            }
            if (RaidSystem.IsRubble(sim, b)) continue;
            var tag = TagOf(sim, b);
            rows[door.Y][door.X] = Name(tag);
            var hp = RaidSystem.BuildingHp(raids, b) - RaidSystem.Damage(sim.State, b);
            structures.Add(new MapStructureDef(sim.Data.Buildings[b.DefId].Name, tag, Math.Max(1, Math.Round(hp)), tag));
        }

        // New buildings still on the scaffold: a frame that gives low cover, walls once it is mostly up, at its share of HP.
        foreach (var site in sim.State.Construction.Sites.Where(s => !s.IsUpgrade).OrderBy(s => s.BuildingId))
        {
            if (site.Progress < SiteFrameFrom) continue;
            var cells = grid.PlotCells(site.Plot).ToList();
            int x0 = cells.Min(c => c.X), y0 = cells.Min(c => c.Y);
            var cell = site.Progress >= SiteWallsFrom ? '#' : '=';
            for (var y = y0 + 1; y < y0 + grid.CellsPerPlot; y++)
                for (var x = x0 + 1; x < x0 + grid.CellsPerPlot; x++)
                    rows[y][x] = cell;
            var tag = SiteTag(site);
            rows[y0 + grid.CellsPerPlot - 1][x0 + grid.CellsPerPlot / 2] = Name(tag);
            var hp = ConstructionSystem.SiteHp(site, SiteLevelHp(raids));
            structures.Add(new MapStructureDef($"{sim.Data.Buildings[site.DefId].Name} (site)", tag, Math.Max(1, Math.Round(hp)), tag));
        }

        foreach (var d in sim.State.Defense.Defenses.OrderBy(d => d.Id))
        {
            if (!raids.Defenses.TryGetValue(d.DefId, out var def) || !grid.Inside(d.X, d.Y)) continue;
            ref var cell = ref rows[d.Y][d.X];
            if (d.Hp <= 0)
            {
                cell = '%';
                continue;
            }
            switch (def.Kind)
            {
                case DefenseKind.Wall:
                case DefenseKind.Gate when gatesClosed || d.Closed:
                    cell = '#';
                    break;
                case DefenseKind.Barricade:
                case DefenseKind.Post:
                    cell = '=';
                    break;
                case DefenseKind.Tower:
                    cell = towerChar;
                    break;
                case DefenseKind.Turret when !sim.State.Defense.Unpaid.Contains(d.Id):
                    var name = $"turret{d.Id}";
                    cell = Name(name);
                    devices.Add(new MapDeviceDef(DeviceKind.Turret, name, $"d{d.Id}", Party: true));
                    break;
            }
        }

        // The party gathers in the street by the Town Hall (the first gutter cells round its block).
        if (sim.State.Buildings.FirstOrDefault(b => b.DefId == sim.Data.Rules.TownHallBuildingId) is { } hall)
        {
            var cells = grid.PlotCells(hall.Plot).ToList();
            int x0 = cells.Min(c => c.X), y0 = cells.Min(c => c.Y);
            foreach (var (x, y) in cells.Where(c => c.X == x0 || c.Y == y0).Take(4))
                if (rows[y][x] == '.') rows[y][x] = 'S';
        }
        else rows[grid.Height / 2][grid.Width / 2] = 'S';

        return new HandmadeMapDef
        {
            Id = $"colony_{sim.State.Tick}", Zone = raids.Def.Battle.Zone, Rows = rows.Select(r => new string(r)).ToList(),
            Markers = markers, Structures = structures, Devices = devices, Towers = TowerMarker,
        };
    }

    /// <summary>Raised posts the map has (towers and Watchtower buildings).</summary>
    public static int TowerCount(HandmadeMapDef map) =>
        map.Markers.Where(kv => kv.Value == TowerMarker).Sum(kv => map.Rows.Sum(r => r.Count(c => c.ToString() == kv.Key)));

    /// <summary>
    /// The colony map for a story perimeter mission, or null when the colony lacks something its script needs (a marker
    /// a trigger spawns at or heads for, a structure a Defend objective names), so the handmade map stays the fallback.
    /// Attackers come from the north, as in the handmade perimeters.
    /// </summary>
    public static HandmadeMapDef? StoryMap(ColonySim sim, RaidData raids, FieldMission mission)
    {
        var spawns = mission.Triggers.SelectMany(t => t.Actions).Where(a => a.Kind == TriggerActionKind.Spawn).Select(a => a.Marker).OfType<string>().Distinct().ToList();
        if (spawns.Count != 1) return null;
        var map = ColonyMap(sim, raids, Side.North, spawns[0]);
        var names = map.Markers.Values.ToHashSet(StringComparer.Ordinal);
        var tags = map.Structures.Select(s => s.Tag).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var needs = mission.Triggers.SelectMany(t => t.Actions.Select(a => a.Goal).Append(t.When.Marker)).OfType<string>();
        if (needs.Any(n => !names.Contains(n))) return null;
        if (mission.Objectives.Any(o => o.Kind is ObjectiveKind.Defend && o.Tag is { } tag && !tags.Contains(tag))) return null;
        if (mission.Objectives.Any(o => o.Marker is { } m && !names.Contains(m))) return null;
        return map;
    }

    /// <summary>Attackers the traps take out before the fight (their damage over the threat's strength per attacker).</summary>
    public static int TrapKills(RaidData raids, Raid raid, RaidAssessment a) =>
        Math.Min(raid.Size - 1, (int)Math.Floor(a.TrapDamage / Math.Max(0.1, raids.Threat(raid.Kind).StrengthPerUnit)));

    /// <summary>The raid's field script: waves from the approach toward the Town Hall; survive the rounds and keep the hall standing.</summary>
    public static FieldMission Script(RaidData raids, Raid raid, int attackers)
    {
        var battle = raids.Def.Battle;
        var threat = raids.Threat(raid.Kind);
        var triggers = new List<TriggerDef>();
        var left = attackers;
        var spawned = 0;
        for (var w = 0; w < battle.Waves.Count && left > 0; w++)
        {
            var wave = battle.Waves[w];
            var n = w == battle.Waves.Count - 1 ? left : Math.Min(left, Math.Max(1, (int)Math.Round(attackers * wave.Share)));
            left -= n;
            var actions = new List<TriggerAction>();
            if (w == 0) actions.Add(new TriggerAction { Kind = TriggerActionKind.Say, Speaker = "Lookout", Text = $"{threat.Name} from the {raid.Approach.ToString().ToLowerInvariant()}! Hold the line!" });
            // Every third attacker is the threat's second kind (crawlers, drones), when it has one.
            var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
            for (var i = 0; i < n; i++, spawned++)
            {
                var id = threat.Enemies.Count > 1 && spawned % 3 == 2 ? threat.Enemies[1] : threat.Enemies[0];
                counts[id] = counts.GetValueOrDefault(id) + 1;
            }
            foreach (var (id, count) in counts)
                actions.Add(new TriggerAction { Kind = TriggerActionKind.Spawn, Enemy = id, Count = count, Marker = ApproachMarker, Tag = "raider", Goal = HallTag });
            triggers.Add(new TriggerDef { Id = $"wave{w + 1}", When = new TriggerWhen { Kind = TriggerKind.Round, Round = wave.Round }, Actions = actions });
        }
        return new FieldMission
        {
            Id = $"raid{raid.Id}", Name = $"{threat.Name} at the fence", AtHome = true, RescueDowned = true,
            Objectives =
            [
                new ObjectiveDef { Id = "survive", Kind = ObjectiveKind.Survive, Text = $"Hold out {battle.Rounds} rounds", Count = battle.Rounds },
                new ObjectiveDef { Id = HallTag, Kind = ObjectiveKind.Defend, Text = "Keep the Town Hall standing", Tag = HallTag, FailsMission = true },
            ],
            Triggers = triggers,
        };
    }

    /// <summary>Whether the warned raid can be fought: scavenge data, a Town Hall, attackers with field units, and someone to fight.</summary>
    public static string? CannotFight(ColonySim sim, Raid raid)
    {
        if (sim.Raids is not { } raids || sim.Scavenge is not { } data) return "No field data loaded.";
        if (!data.Zones.ContainsKey(raids.Def.Battle.Zone)) return "No battle zone.";
        if (raids.Threat(raid.Kind).Enemies.Count == 0) return "These raiders have no field units yet.";
        if (!sim.State.Buildings.Any(b => b.DefId == sim.Data.Rules.TownHallBuildingId)) return "There is no Town Hall to hold.";
        if (!AutoResolve.Defenders(sim, raid).Any()) return "Nobody is able to fight.";
        return null;
    }

    /// <summary>The battle in progress, resumed from the save, or a new one (the raid must have arrived and be fought).</summary>
    public static ScavengeSim Open(ColonySim sim)
    {
        var raids = sim.Raids ?? throw new InvalidOperationException("Raids are not loaded.");
        var data = sim.Scavenge ?? throw new InvalidOperationException("Scavenge data is not loaded.");
        var raid = sim.State.Defense.Raid ?? throw new InvalidOperationException("No raid is here.");
        if (!raid.Fight || raid.Stage != RaidStage.Battle) throw new InvalidOperationException("The raid is not being fought (yet).");
        if (raid.Battle is { } running) return new ScavengeSim(data, running);
        var battle = Create(sim, raids, raid);
        raid.Battle = battle.State;
        return battle;
    }

    /// <summary>Builds the battle (does not store it; <see cref="Open"/> does).</summary>
    public static ScavengeSim Create(ColonySim sim, RaidData raids, Raid raid)
    {
        var data = sim.Scavenge ?? throw new InvalidOperationException("Scavenge data is not loaded.");
        var a = AutoResolve.Assess(sim, raids, raid);
        var attackers = Math.Max(1, raid.Size - TrapKills(raids, raid, a));
        raid.BattleAttackers = attackers;
        var map = ColonyMap(sim, raids, raid.Approach, gatesClosed: raid.GatesClosed);
        var members = a.Defenders.Select(d => new PartyMember(d.Survivor,
            new Dictionary<EquipSlot, string>(new[] { (EquipSlot.Weapon, d.Weapon), (EquipSlot.Body, d.Armor), (EquipSlot.Head, d.Helmet) }
                .Where(g => g.Item2 is not null).Select(g => KeyValuePair.Create(g.Item1, g.Item2!))))).ToList();
        if (raid.BattleSeed == 0) raid.BattleSeed = sim.Rng.NextULong() | 1;
        var setup = new MissionSetup(Script(raids, raid, attackers), map, Towers: TowerCount(map));
        var battle = NewScavenge.Create(data, raids.Def.Battle.Zone, raid.BattleSeed, [new PartySetup(ColonySim.ExpeditionPlayerId, members, Name: "Colony")],
            new ZoneConditions(0, sim.MissionEnvironment(raid.ArrivalTick)), setup);
        Stage(sim, raids, battle.State);
        return battle;
    }

    /// <summary>Guards to their posts; floodlights lit at night.</summary>
    private static void Stage(ColonySim sim, RaidData raids, ScavengeState state)
    {
        foreach (var g in Guards.All(sim.State))
        {
            var unit = state.Units.FirstOrDefault(u => u.Kind == UnitKind.Survivor && u.Origin?.Id == g.Survivor.Id);
            if (unit is null) continue;
            var post = new Pos(g.Post.X, g.Post.Y);
            var spot = new[] { post }.Concat(Pos.Orthogonal.Select(d => post + d))
                .FirstOrDefault(p => state.Map.InBounds(p) && sim.Scavenge!.Terrain(state.Map[p]).Passable && state.UnitAt(p) is null, unit.Pos);
            unit.Pos = spot;
        }
        if (DayNight.PhaseAtHour(state.Environment.Hour, sim.Data.Rules.DayNight) is DayPhase.Night or DayPhase.Dusk)
            foreach (var d in sim.State.Defense.Defenses.Where(d => ThreatModel.Working(sim, d) && raids.Defenses.GetValueOrDefault(d.DefId)?.Lights == true))
                state.Flares.Add(new Flare(new Pos(d.X, d.Y), raids.Defense(d.DefId).Range * DefensePower.Multiplier(sim, d, raids.Defense(d.DefId)), int.MaxValue));
    }

    /// <summary>
    /// The battle is over: survivors come home as they are, buildings take the structures' damage, and the consequences
    /// follow from how it went (held: p = ½ · attackers left standing; lost or left: ½ + ½ · that).
    /// </summary>
    internal static CommandResult Apply(ColonySim sim, RaidData raids, ApplyRaidBattle c)
    {
        var state = sim.State;
        if (state.Defense.Raid is not { Battle: { } battle } raid) return CommandResult.Fail(CommandError.NoRaidWarning, "No raid battle is on.");
        var result = c.Result;
        if (!result.Complete) return CommandResult.Fail(CommandError.ResultNotFinal, "The fight is not over yet.");
        var defenders = AutoResolve.Defenders(sim, raid).Select(s => s.Id).ToHashSet();
        if (result.PlayerId != ColonySim.ExpeditionPlayerId
            || result.Returned.Select(r => r.Survivor.Id).Concat(result.Deaths.Select(d => d.Id)).Any(id => !defenders.Contains(id)))
            return CommandResult.Fail(CommandError.ResultMismatch, "That result is not from this raid.");

        var injured = new List<RaidInjury>();
        foreach (var r in result.Returned)
        {
            var s = state.FindSurvivor(r.Survivor.Id)!;
            foreach (var line in BodyDiff.Describe(s.Body, r.Survivor.Body)) injured.Add(new(s.Name, line));
            s.Body = r.Survivor.Body;
            foreach (var part in s.Body.Parts.Values.Where(p => p.Condition == PartCondition.Injured))
                part.Severity = Math.Min(part.Severity, raids.Def.Battle.WoundCap);
            s.Health = Math.Clamp(r.Survivor.Health, 1, Survivor.MaxHealth);
            foreach (var (attribute, value) in r.Survivor.Attributes) s.Set(attribute, value);
            foreach (var (skill, value) in r.Survivor.Skills) s.Set(skill, Math.Max(s.Get(skill), value));
        }
        var dead = new List<string>();
        var home = result.Returned.Select(r => r.Survivor.Id).ToHashSet();
        foreach (var id in defenders.Where(id => !home.Contains(id)).Order())
            if (state.FindSurvivor(id) is { } s)
            {
                dead.Add(s.Name);
                ColonyDeaths.Remove(state, sim.Data, s, $"died defending the colony from {raids.Threat(raid.Kind).Name.ToLowerInvariant()}",
                    $"Killed in a raid ({raids.Threat(raid.Kind).Name})");
            }

        // The buildings take what their structures took.
        foreach (var b in state.Buildings.ToList())
        {
            var tag = TagOf(sim, b);
            if (battle.Units.FirstOrDefault(u => u.Tag == tag && u.Kind == UnitKind.Npc) is not { } unit) continue;
            var lost = Math.Max(0, unit.MaxHp - Math.Max(0, unit.Hp));
            if (unit.Status == UnitStatus.Dead) lost = RaidSystem.BuildingHp(raids, b);
            if (lost <= 0) continue;
            state.Defense.BuildingDamage[b.Id] = Math.Min(RaidSystem.BuildingHp(raids, b), RaidSystem.Damage(state, b) + lost);
            if (RaidSystem.IsRubble(sim, b)) RaidSystem.Wreck(sim, b);
        }

        // Sites lose work for the HP their frames took.
        foreach (var site in state.Construction.Sites.Where(s => !s.IsUpgrade).ToList())
        {
            if (battle.Units.FirstOrDefault(u => u.Tag == SiteTag(site) && u.Kind == UnitKind.Npc) is not { } unit) continue;
            var lost = unit.Status == UnitStatus.Dead ? unit.MaxHp : Math.Max(0, unit.MaxHp - Math.Max(0, unit.Hp));
            if (lost > 0) ConstructionSystem.Hit(site, SiteLevelHp(raids), lost);
        }

        var enemies = battle.Units.Where(u => u.Kind == UnitKind.Enemy && u.Tag == "raider").ToList();
        var standing = enemies.Count(u => u.Status != UnitStatus.Dead);
        var share = (double)standing / Math.Max(1, raid.BattleAttackers);
        var holds = result.Mission?.Outcome == MissionOutcome.Done;
        var p = holds ? 0.5 * Math.Min(0.98, share) : 0.5 + 0.5 * share;
        var a = AutoResolve.Assess(sim, raids, raid);
        a = a with { Pressure = Math.Round(p, 3), Defenders = [] };
        var report = Consequences.Apply(sim, raids, raid, a, sim.Rng, new BattleOutcome(dead, injured, raid.Size - standing));
        raid.Battle = null;
        RaidSystem.Finish(sim, raids, raid, report);
        return CommandResult.Success(report.Repelled ? "The raid was beaten off." : "They broke through.");
    }
}

/// <summary>What a fought battle decided, in place of auto-resolve's dice: who died, who was hurt, how many attackers fell.</summary>
public sealed record BattleOutcome(List<string> Dead, List<RaidInjury> Injured, int AttackersKilled);

/// <summary>Bring a fought raid home (the scavenge result of <see cref="RaidBattle.Open"/>'s run).</summary>
public sealed record ApplyRaidBattle(ExtractionResult Result) : IRaidCommand;
