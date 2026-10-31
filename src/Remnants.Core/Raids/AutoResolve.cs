using Remnants.Core.Colony;
using Remnants.Core.Items;
using Remnants.Core.Random;
using Remnants.Core.Survivors;

namespace Remnants.Core.Raids;

/// <summary>A defender in the strength calculation, with the gear handed out from the inventory.</summary>
public sealed record DefenderStrength(Survivor Survivor, double Strength, string? Weapon, string? Armor, bool Guard, bool Asleep, string? Helmet = null);

/// <summary>
/// The strength calculation of a raid (<see cref="AutoResolve.Assess"/>), before any dice. Attack is what reaches the
/// defenders after traps and walls; pressure p = attack / (attack + defense); the colony holds when p &lt; 0.5.
/// </summary>
public sealed record RaidAssessment(
    double RawAttack, double TrapDamage, double Soak, double Attack, double Defense, double Pressure,
    IReadOnlyList<DefenderStrength> Defenders, IReadOnlyList<PlacedDefense> Traps, IReadOnlyList<StrengthLine> Breakdown)
{
    public bool Holds => Pressure < 0.5;
}

/// <summary>
/// Auto-resolve: the strength calculation and its deterministic consequences (colony RNG).
/// <code>
/// defender  = perDefender · (1 − skillWeight + skillWeight · combat/50) · health · arms + weapon·perWeaponPower + armor·perArmor
///             × tired × (guard: 1 + guardBonus + post bonus + Sentry) × (asleep without an alarm: asleepShare) × night/light × weather
/// defense   = Σ defenders + Σ structures' defense (far side × farSideShare; powered × (1 + poweredBonus))
/// attack₀   = size · strengthPerUnit · phase · weather;  attack₁ = attack₀ − Σ armed traps on the approach that hit this kind
/// soak      = Σ walls/gates/barricades' soak · HP share (open gates lose openGateLoss);  share = min(maxSoak, soak / (soak + attack₁))
/// attack    = attack₁ · (1 − share);  p = attack / (attack + defense)
/// </code>
/// </summary>
public static class AutoResolve
{
    public static RaidAssessment Assess(ColonySim sim, RaidData raids, Raid raid)
    {
        var state = sim.State;
        var def = raids.Def;
        var s = def.Strength;
        var grid = DefenseGrid.Of(sim, raids);
        var threat = raids.Threat(raid.Kind);
        var phase = DayNight.PhaseAt(raid.ArrivalTick, sim.Data.Rules);
        var mods = def.Phases.GetValueOrDefault(phase) ?? new PhaseModifiers();
        var weather = RaidWeather.Modifiers(sim, raids, raid.ArrivalTick);
        var lines = new List<StrengthLine>();

        var working = state.Defense.Defenses.Where(d => ThreatModel.Working(sim, d) && raids.Defenses.ContainsKey(d.DefId))
            .Select(d => (d, def: raids.Defense(d.DefId))).ToList();
        double SideShare(PlacedDefense d, DefenseDef dd) => grid.Faces(d, dd, raid.Approach) ? 1 : s.FarSideShare;

        // Light and alarm on the approach.
        var lit = working.Any(w => w.def.Lights && grid.Faces(w.d, w.def, raid.Approach))
                  || state.Buildings.Any(b => b.Floodlight && grid.SideOf(grid.PlotCenter(b.Plot).X, grid.PlotCenter(b.Plot).Y) == raid.Approach);
        var alarm = working.Any(w => w.def.Wakes);
        var night = 1 - (1 - mods.Defense) * (1 - (lit ? mods.LitRecovers : 0));

        // Defenders and gear.
        var defenders = Defenders(sim, raid).ToList();
        var weapons = GearPool(sim, EquipSlot.Weapon);
        var armors = GearPool(sim, EquipSlot.Body);
        var helmets = GearPool(sim, EquipSlot.Head);
        var list = new List<DefenderStrength>();
        foreach (var d in defenders.OrderByDescending(Stats.Combat).ThenBy(d => d.Id))
        {
            var post = Guards.PostOf(state, d.Id);
            var guard = post is not null && Guards.OnDutyAt(new GuardDuty(d, post, post.NightWatch),
                DayNight.Hour(raid.ArrivalTick, sim.Data.Rules), sim.Data.Rules.DayNight);
            var asleep = !guard && sim.Data.Rules.DayNight.Enabled
                         && DayNight.InWindow(DayNight.Hour(raid.ArrivalTick, sim.Data.Rules), Schedule.SleepWindow(d, sim.Data.Rules.DayNight));
            var weapon = Take(weapons);
            var armor = Take(armors);
            var helmet = Take(helmets);
            var v = s.PerDefender * (1 - s.SkillWeight + s.SkillWeight * Stats.Combat(d) / 50)
                    * (d.Health / Survivor.MaxHealth) * Math.Max(0.3, d.Body.RegionEfficiency(BodyRegion.Arms));
            v += (weapon?.Power ?? 0) * s.PerWeaponPower + ((armor?.Power ?? 0) + (helmet?.Power ?? 0)) * s.PerArmor;
            if (Schedule.IsTired(d, sim.Data.Rules.DayNight)) v *= sim.Data.Rules.DayNight.Tired.OutputMultiplier;
            if (guard)
            {
                var postDef = raids.Defenses.GetValueOrDefault(post!.DefId);
                v *= 1 + s.GuardBonus + (postDef?.PostBonus ?? 0) + PerkEffects.For(d, sim.Data).Hook(PerkHooks.PostDefenseBonus);
            }
            if (asleep && !alarm) v *= mods.AsleepShare;
            v *= night * weather.Defense;
            list.Add(new DefenderStrength(d, v, weapon?.Id, armor?.Id, guard, asleep && !alarm, helmet?.Id));
            lines.Add(new($"{d.Name}{(guard ? " (guard)" : "")}{(weapon is null ? "" : $", {weapon.Name}")}{(asleep && !alarm ? ", woken late" : "")}", v));
        }
        var defense = list.Sum(l => l.Strength);
        foreach (var (d, dd) in working.Where(w => w.def.Defense > 0))
        {
            var v = dd.Defense * (d.Hp / dd.Hp) * SideShare(d, dd) * DefensePower.Multiplier(sim, d, dd);
            defense += v;
            lines.Add(new(dd.Name, v));
        }

        // Attack, traps, walls.
        var raw = raid.Size * threat.StrengthPerUnit * mods.Attack.GetValueOrDefault(raid.Kind, 1) * weather.Attack.GetValueOrDefault(raid.Kind, 1);
        var traps = working.Where(w => w.def.IsTrap && grid.SideOf(w.d.X, w.d.Y) == raid.Approach
                                       && (w.def.Hits.Intersect(threat.Tags).Any() || w.def.Lure && raid.Kind == ThreatKind.Husk || w.def.TrapDamage == 0))
            .OrderBy(w => grid.DistanceToEdge(w.d.X, w.d.Y, raid.Approach)).ThenBy(w => w.d.Id).ToList();
        var trapDamage = traps.Sum(w => w.def.Hits.Intersect(threat.Tags).Any() ? w.def.TrapDamage : 0);
        var afterTraps = Math.Max(0, raw - trapDamage);
        var soak = working.Where(w => w.def.Soak > 0)
            .Sum(w => w.def.Soak * (w.d.Hp / w.def.Hp) * SideShare(w.d, w.def) * DefensePower.Multiplier(sim, w.d, w.def));
        var openGate = working.Any(w => w.def.Kind == DefenseKind.Gate && grid.Faces(w.d, w.def, raid.Approach) && !raid.GatesClosed && !w.d.Closed);
        if (openGate) soak *= 1 - s.OpenGateLoss;
        var share = afterTraps <= 0 ? 0 : Math.Min(s.MaxSoakShare, soak / (soak + afterTraps * s.SoakScale));
        var attack = afterTraps * (1 - share);
        var pressure = attack <= 0 ? 0 : attack / (attack + Math.Max(0.001, defense));
        lines.Add(new($"{threat.Name} ×{raid.Size}", -raw));
        if (trapDamage > 0) lines.Add(new("Traps", trapDamage));
        if (share > 0) lines.Add(new(openGate ? "Walls (a gate left open)" : "Walls", afterTraps * share));
        return new RaidAssessment(raw, trapDamage, soak, attack, defense, pressure, list, traps.Select(t => t.d).ToList(), lines);
    }

    /// <summary>Who stands: the chosen defenders, or every able adult at home; guards always.</summary>
    public static IEnumerable<Survivor> Defenders(ColonySim sim, Raid raid)
    {
        bool Able(Survivor s) => !s.Away && !s.IsChild && s.PatientAtBuildingId is null;
        var guards = sim.State.Defense.Guards.Keys.ToHashSet();
        return sim.State.Survivors.Where(s => Able(s) && (raid.Defenders.Count == 0 || raid.Defenders.Contains(s.Id) || guards.Contains(s.Id)));
    }

    private sealed record Gear(string Id, string Name, double Power);

    /// <summary>
    /// The inventory's gear for a slot, best first (weapon: average damage × accuracy; body and head: armor). Modded gear ids
    /// resolve through the item catalogue with their mods applied, so gear mods count as they do in the field.
    /// </summary>
    private static List<Gear> GearPool(ColonySim sim, EquipSlot slot)
    {
        var pool = new List<Gear>();
        if (sim.Scavenge is not { } data) return pool;
        foreach (var (id, count) in sim.State.Inventory.Counts)
        {
            if (!data.Items.TryGetValue(id, out var item) || item.Slot != slot || item.Kind is not (ItemKind.Weapon or ItemKind.Armor or ItemKind.Clothing)) continue;
            var power = slot == EquipSlot.Weapon
                ? item.Actions.Select(a => (a.MinDamage + a.MaxDamage) / 2.0 * a.Accuracy).DefaultIfEmpty(0).Max()
                : item.Armor;
            for (var i = 0; i < count; i++) pool.Add(new Gear(id, item.Name, power));
        }
        return pool.OrderByDescending(g => g.Power).ThenBy(g => g.Id, StringComparer.Ordinal).ToList();
    }

    private static Gear? Take(List<Gear> pool)
    {
        if (pool.Count == 0) return null;
        var g = pool[0];
        pool.RemoveAt(0);
        return g;
    }

    /// <summary>Resolves the raid now: applies every consequence, logs it and returns the report.</summary>
    public static RaidReport Resolve(ColonySim sim, RaidData raids, Raid raid)
    {
        var a = Assess(sim, raids, raid);
        return Consequences.Apply(sim, raids, raid, a, sim.Rng);
    }
}

/// <summary>
/// What a raid does to the colony, from its pressure p (see <see cref="AutoResolve"/>); shared by auto-resolve and, in
/// stage 2, the fought battle. Deterministic: draws only from the colony RNG.
/// </summary>
public static class Consequences
{
    /// <param name="battle">A fought battle's casualties and kills, used instead of the dice; its buildings already took
    /// their damage from the map's structures.</param>
    public static RaidReport Apply(ColonySim sim, RaidData raids, Raid raid, RaidAssessment a, DeterministicRng rng, BattleOutcome? battle = null)
    {
        var state = sim.State;
        var rules = raids.Def.Consequences;
        var threat = raids.Threat(raid.Kind);
        var grid = DefenseGrid.Of(sim, raids);
        var p = a.Pressure;
        var holds = a.Holds;

        // Traps spring.
        var sprung = new List<string>();
        foreach (var t in a.Traps)
        {
            var def = raids.Defense(t.DefId);
            sprung.Add(def.Name);
            if (def.Rearmable) t.Sprung = true;
            else RaidSystem.RemoveDefense(state, t);
        }

        // Damage: the walls on the approach first, then (if they got in) the buildings nearest it.
        var damaged = new List<RaidDamage>();
        var damage = a.Attack * rules.DamagePerAttack * (0.25 + p);
        var walls = state.Defense.Defenses
            .Where(d => d.Hp > 0 && raids.Defenses.GetValueOrDefault(d.DefId) is { Soak: > 0 } && grid.SideOf(d.X, d.Y) == raid.Approach)
            .OrderBy(d => grid.DistanceToEdge(d.X, d.Y, raid.Approach)).ThenBy(d => d.Id).ToList();
        foreach (var w in walls)
        {
            if (damage <= 0) break;
            var hit = Math.Min(w.Hp, Math.Min(damage, raids.Defense(w.DefId).Hp * 0.6));
            w.Hp -= hit;
            damage -= hit;
            damaged.Add(new(raids.Defense(w.DefId).Name, Math.Round(hit), w.Hp <= 0));
            if (w.Hp <= 0) state.Log(new(state.Tick, ColonyEventType.DefenseDestroyed, $"The {raids.Defense(w.DefId).Name} went down."));
        }
        if (!holds && damage > 0 && battle is null)
        {
            var targets = state.Buildings
                .OrderBy(b => grid.DistanceToEdge(grid.PlotCenter(b.Plot).X, grid.PlotCenter(b.Plot).Y, raid.Approach)).ThenBy(b => b.Id)
                .Where(b => !RaidSystem.IsRubble(sim, b)).Take(3).ToList();
            // New buildings' sites near the approach are in reach too: a hit knocks their work back (the materials stay).
            var sites = state.Construction.Sites.Where(s => !s.IsUpgrade && s.Work > 1e-9)
                .OrderBy(s => grid.DistanceToEdge(grid.PlotCenter(s.Plot).X, grid.PlotCenter(s.Plot).Y, raid.Approach)).ThenBy(s => s.BuildingId)
                .Take(2).ToList();
            var levelHp = RaidBattle.SiteLevelHp(raids);
            while (damage > 0.5 && targets.Count + sites.Count > 0)
            {
                var pick = rng.Next(0, targets.Count + sites.Count);
                if (pick >= targets.Count)
                {
                    var site = sites[pick - targets.Count];
                    var knocked = ConstructionSystem.Hit(site, levelHp, Math.Max(1, damage / 2));
                    damage -= Math.Max(knocked, 0.5);
                    var siteName = $"{sim.Data.Buildings[site.DefId].Name} (site)";
                    var flat = site.Work <= 1e-9;
                    if (damaged.FindIndex(x => x.Name == siteName) is var j and >= 0) damaged[j] = damaged[j] with { Damage = damaged[j].Damage + Math.Round(knocked), Destroyed = flat };
                    else damaged.Add(new(siteName, Math.Round(knocked), flat));
                    if (flat) sites.Remove(site);
                    continue;
                }
                var b = targets[pick];
                var hp = RaidSystem.BuildingHp(raids, b) - state.Defense.BuildingDamage.GetValueOrDefault(b.Id);
                var hit = Math.Min(hp, Math.Max(1, damage / 2));
                state.Defense.BuildingDamage[b.Id] = state.Defense.BuildingDamage.GetValueOrDefault(b.Id) + hit;
                damage -= hit;
                var name = sim.Data.Buildings[b.DefId].Name;
                var wrecked = RaidSystem.IsRubble(sim, b);
                if (damaged.FindIndex(x => x.Name == name) is var i and >= 0) damaged[i] = damaged[i] with { Damage = damaged[i].Damage + Math.Round(hit), Destroyed = wrecked };
                else damaged.Add(new(name, Math.Round(hit), wrecked));
                if (wrecked)
                {
                    targets.Remove(b);
                    RaidSystem.Wreck(sim, b);
                }
                // An upgrade under way loses work too: the scaffold comes down with the walls.
                if (state.Construction.Find(b.Id) is { } upgrade)
                    upgrade.Work = Math.Max(0, upgrade.Work - hit / RaidSystem.BuildingHp(raids, b) * upgrade.WorkNeeded);
            }
        }

        // Spoils.
        var spoil = Math.Clamp((p - rules.SpoilFrom) / (1 - rules.SpoilFrom), 0, 1) * threat.SpoilShare;
        var stolen = new Dictionary<string, double>();
        if (spoil > 0)
        {
            foreach (var r in threat.SpoilResources)
            {
                var take = r switch
                {
                    CropDamage.Crops => CropDamage.Trample(sim, spoil),
                    CropDamage.Food => CropDamage.Steal(sim, spoil),
                    _ => Math.Round(state.Stockpile.Get(r) * spoil, 1),
                };
                if (take < 0.5) continue;
                if (r is not (CropDamage.Crops or CropDamage.Food)) state.Stockpile.Spend(new Dictionary<string, double> { [r] = take });
                stolen[r] = take;
            }
        }

        // Casualties.
        var injured = new List<RaidInjury>(battle?.Injured ?? []);
        var dead = new List<string>(battle?.Dead ?? []);
        var taken = new List<string>();
        var toTake = raid.Kind == ThreatKind.Linked && !holds ? (int)Math.Round(threat.MaxTaken * Math.Clamp((p - 0.5) * 2, 0, 1)) : 0;
        foreach (var d in a.Defenders.OrderBy(d => d.Strength).ThenBy(d => d.Survivor.Id).ToList())
        {
            var s = d.Survivor;
            if (toTake > 0)
            {
                toTake--;
                taken.Add(s.Name);
                ColonyDeaths.Remove(state, sim.Data, s, "was taken by the Linked", "Taken by the Linked in a raid");
                continue;
            }
            if (rng.NextDouble() >= p * p * rules.InjuryChance) continue;
            var part = Hurt(sim.Data, s, 0.2 + p * 0.6 * rng.NextDouble(), rng);
            if (p > rules.DeathFrom && rng.NextDouble() < (p - rules.DeathFrom) * 2)
            {
                dead.Add(s.Name);
                ColonyDeaths.Remove(state, sim.Data, s, $"died defending the colony from {threat.Name.ToLowerInvariant()}", $"Killed in a raid ({threat.Name})");
                continue;
            }
            injured.Add(new(s.Name, part));
        }
        if (!holds && !raid.ChildrenSheltered)
            foreach (var c in state.Survivors.Where(c => c.IsChild && !c.Away).OrderBy(c => c.Id).ToList())
                if (rng.NextDouble() < p * p * raids.Def.Strength.ChildRisk * rules.InjuryChance)
                    injured.Add(new(c.Name, Hurt(sim.Data, c, 0.2 + p * 0.4 * rng.NextDouble(), rng)));

        // Mood, XP.
        var mood = holds ? rules.WinMood : -rules.LossMood * Math.Clamp(p * 2 - 1, 0.25, 1);
        if (raid.ChildrenSheltered && holds) mood -= 1;
        foreach (var s in state.Survivors.Where(s => !s.Away))
            s.Mood = Math.Clamp(s.Mood + mood, 0, Survivor.MaxMood);
        foreach (var d in a.Defenders.Where(d => state.Survivors.Contains(d.Survivor)))
            Progression.GrantXp(state, sim.Data, d.Survivor, sim.Data.Progression.Xp.PerMission * (holds ? 1 : 0.5), XpSource.Mission);

        // Loot and schematics from a beaten raid.
        var killed = battle?.AttackersKilled ?? (int)Math.Round(raid.Size * Math.Clamp(holds ? 1 - p : (1 - p) * 0.5, 0, 1));
        var loot = new Dictionary<string, int>();
        string? schematic = null;
        if (holds)
        {
            loot = RaidLoot.Roll(sim, raid.Kind, threat, killed, rng);
            var drops = DefenseSchematics.Droppable(sim).ToList();
            if (drops.Count > 0 && rng.NextDouble() < threat.SchematicChance)
            {
                var pick = drops[rng.Next(0, drops.Count)];
                Schematics.Schematics.Grant(sim, pick.Id, Schematics.SchematicSource.Kill);
                schematic = pick.Name;
            }
        }

        var report = new RaidReport
        {
            RaidId = raid.Id, Tick = state.Tick, Kind = raid.Kind, Size = raid.Size, Approach = raid.Approach, Repelled = holds, Fought = battle is not null,
            Attack = Math.Round(a.Attack, 1), Defense = Math.Round(a.Defense, 1), Pressure = Math.Round(p, 3), AttackersKilled = killed,
            Defenders = battle is null ? a.Defenders.Select(d => d.Survivor.Name).ToList() : AutoResolve.Defenders(sim, raid).Select(s => s.Name).Concat(battle.Dead).ToList(), TrapsSprung = sprung, Damaged = damaged, Stolen = stolen,
            Injured = injured, Dead = dead, Taken = taken, Loot = loot, Schematic = schematic, MoodChange = mood,
            Breakdown = a.Breakdown.Select(l => l with { Value = Math.Round(l.Value, 1) }).ToList(),
        };
        state.Log(new(state.Tick, holds ? ColonyEventType.RaidRepelled : ColonyEventType.RaidBreached, Summary(report, threat)));
        return report;
    }

    /// <summary>
    /// A raid wound: an open (undressed) injury that can get infected like any (medicine.json), and on an arm or leg a
    /// fracture by the same rule as a hit in the field (a side roll, so the raid's own dice do not shift).
    /// </summary>
    private static string Hurt(Data.GameData data, Survivor s, double severity, DeterministicRng rng)
    {
        BodyPart[] parts = [BodyPart.Torso, BodyPart.LeftArm, BodyPart.RightArm, BodyPart.LeftLeg, BodyPart.RightLeg, BodyPart.Head];
        var part = parts[rng.Next(0, parts.Length)];
        s.Body.Injure(part, Math.Clamp(severity, 0, 0.9));
        s.Health = Math.Max(1, s.Health - severity * 40);
        var broken = Treatment.MaybeFracture(data.Medicine, s.Body, part, SideRoll.Next(rng, s.Id, (long)part, (long)(severity * 1000)));
        return $"{MedicalSystem.Describe(part)} {(broken ? "broken" : severity >= 0.6 ? "badly hurt" : "hurt")}";
    }

    private static string Summary(RaidReport r, ThreatDef threat)
    {
        var head = r.Repelled ? $"Repelled {r.Size} {threat.Name.ToLowerInvariant()}" : $"{threat.Name} ({r.Size}) broke through";
        var parts = new List<string>();
        if (r.Dead.Count > 0) parts.Add($"{r.Dead.Count} dead");
        if (r.Taken.Count > 0) parts.Add($"{r.Taken.Count} taken");
        if (r.Injured.Count > 0) parts.Add($"{r.Injured.Count} hurt");
        if (r.Stolen.Count > 0) parts.Add($"lost {string.Join(", ", r.Stolen.Select(kv => $"{kv.Value:0} {kv.Key}"))}");
        if (r.Damaged.Any(d => d.Destroyed)) parts.Add($"{r.Damaged.Count(d => d.Destroyed)} wrecked");
        return parts.Count == 0 ? head + "." : $"{head}: {string.Join(", ", parts)}.";
    }
}

/// <summary>
/// What beaten attackers leave behind, into the colony (salvage to the stockpile, food to the pantry, the rest to the
/// inventory). Each attacker killed (the threat's enemies in turn) rolls its loot table through
/// <see cref="Scavenge.FieldLoot.RaidSpoils"/>; an enemy without one (or a colony without scavenge data) falls back to the
/// threat's per-kill <see cref="ThreatDef.Loot"/>.
/// </summary>
public static class RaidLoot
{
    public static Dictionary<string, int> Roll(ColonySim sim, ThreatKind kind, ThreatDef threat, int killed, DeterministicRng rng)
    {
        _ = kind;
        var loot = new Dictionary<string, int>();
        var tabled = sim.Scavenge is { } data && threat.Enemies.Count > 0
                     && threat.Enemies.All(e => data.Enemies.TryGetValue(e, out var def) && data.Loot.Find(def.Loot) is not null);
        if (tabled)
        {
            var attackers = Enumerable.Range(0, Math.Max(0, killed)).Select(i => threat.Enemies[i % threat.Enemies.Count]);
            foreach (var (item, n) in Scavenge.FieldLoot.RaidSpoils(sim, attackers, rng: rng)) loot[item] = n;
            return loot;
        }
        foreach (var (item, perKill) in threat.Loot)
        {
            var expected = perKill * killed;
            var n = (int)Math.Floor(expected) + (rng.NextDouble() < expected - Math.Floor(expected) ? 1 : 0);
            if (n <= 0) continue;
            sim.State.Inventory.Add(item, n);
            loot[item] = n;
        }
        return loot;
    }
}
