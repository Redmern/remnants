using Remnants.Core.Colony;
using Remnants.Core.Data;
using Remnants.Core.Scavenge;
using Remnants.Core.Survivors;

namespace Remnants.Core.Help;

/// <summary>One line of a breakdown: a term, its value as shown ("×0.92", "+15%", "2.0 days") and an optional note.</summary>
public sealed record BreakdownRow(string Label, string Value, string Note = "");

/// <summary>Live numbers for a help topic: the terms of a calculation, top to bottom, and the result.</summary>
public sealed record Breakdown(string Title, IReadOnlyList<BreakdownRow> Rows, string Total, string Formula = "");

/// <summary>
/// What a breakdown is about: the UI fills in what it has (the selected survivor, the hovered zone, the attack being
/// previewed). A provider that lacks what it needs returns null and the popover shows the text alone.
/// </summary>
public sealed record HelpContext
{
    public ColonySim? Colony { get; init; }
    public Survivor? Survivor { get; init; }
    public Building? Building { get; init; }

    /// <summary>A construction site (a new building's has no <see cref="Building"/> yet).</summary>
    public ConstructionSite? Site { get; init; }
    public ZoneDef? Zone { get; init; }
    public ScavengeSim? Scavenge { get; init; }
    public Unit? Attacker { get; init; }
    public ActionProfile? Action { get; init; }
    public Unit? Target { get; init; }

    /// <summary>The tile the attack is previewed from (a candidate move); null: where the attacker stands.</summary>
    public Scavenge.Pos? From { get; init; }

    /// <summary>What the attacker's side knows of the target (<see cref="Bestiary.Knowledge"/>): below killed, its defence is one row.</summary>
    public BestiaryTier TargetKnown { get; init; } = BestiaryTier.Killed;

    public static readonly HelpContext None = new();
}

/// <summary>
/// The live breakdowns help topics can name as their <c>provider</c>: each one a read-only query over Core, the same
/// factors the rules multiply, so the numbers in a help popover are the ones the game uses.
/// </summary>
public static class Breakdowns
{
    public const string JobOutput = "job_output";
    public const string ColonyOutput = "colony_output";
    public const string FoodDays = "food_days";
    public const string Travel = "travel";
    public const string HitChance = "hit_chance";
    public const string Humanity = "humanity";
    public const string ConstructionSite = "construction_site";

    private static readonly Dictionary<string, Func<HelpContext, Breakdown?>> Providers = new(StringComparer.Ordinal)
    {
        [JobOutput] = JobOutputOf,
        [ColonyOutput] = ColonyOutputOf,
        [FoodDays] = FoodDaysOf,
        [Travel] = TravelOf,
        [HitChance] = HitChanceOf,
        [Humanity] = HumanityOf,
        [ConstructionSite] = SiteOf,
    };

    public static IEnumerable<string> Ids => Providers.Keys;

    /// <summary>The breakdown <paramref name="id"/> for <paramref name="context"/>; null when unknown or it lacks what it needs.</summary>
    public static Breakdown? For(string id, HelpContext context) =>
        Providers.TryGetValue(id, out var provider) ? provider(context) : null;

    private static string Times(double f) => $"×{f:0.00}";
    private static string Days(double d) => $"{d:0.#} day{(Math.Abs(d - 1) < 0.05 ? "" : "s")}";
    private static string Pct(double f) => $"{f * 100:0}%";
    private static string Signed(double f) => f >= 0 ? $"+{f * 100:0}%" : $"−{-f * 100:0}%";

    /// <summary>A survivor's output at their job (or at <see cref="HelpContext.Building"/>): every factor of <see cref="Colony.JobOutput.PerTick"/>.</summary>
    private static Breakdown? JobOutputOf(HelpContext c)
    {
        if (c.Colony is not { } sim || c.Survivor is not { } s) return null;
        var building = c.Building ?? (s.AssignedBuildingId is { } bid ? sim.State.FindBuilding(bid) : null);
        if (building is null) return null;
        var def = sim.Data.Buildings[building.DefId];
        if (def.Job is null) return null;
        var job = sim.Data.Jobs[def.Job];
        var rules = sim.Data.Rules.Output;
        var data = sim.Data;
        var rows = new List<BreakdownRow>
        {
            new("Base output", $"{def.BaseOutput:0.##}/tick", def.Name),
            new("Building level", Times(def.Level(building.Level).OutputMultiplier), $"level {building.Level}"),
            new("Performance", Times(Colony.JobOutput.Performance(job, s, rules)), $"attributes and {job.Skill} skill: {Pct(Colony.JobOutput.Effectiveness(job, s))} effective"),
            new("Tool", Times(Colony.JobOutput.ToolFactor(job, s))),
            new("Health and body", Times(Colony.JobOutput.HealthFactor(job, s, rules)), $"{s.Health:0} HP, the parts the job uses"),
            new("Conditions and pain", Times(Colony.JobOutput.ConditionFactor(s, data))),
            new("Age", Times(Colony.JobOutput.LifeFactor(s, rules)), s.IsChild ? "a child" : ""),
            new("Mood", Times(Colony.JobOutput.MoodFactor(s, rules)), $"mood {s.Mood:0}"),
            new("Perks and cybernetics", Times(Colony.JobOutput.PerkFactor(job, s, data))),
            new("Food buffs", Times(FoodEffects.OutputFactor(s, data))),
        };
        var perTick = Colony.JobOutput.PerTick(def, building.Level, job, s, rules, data);
        var perDay = perTick * Schedule.ShiftHoursPerDay(s, building, def, sim.Data.Rules);
        return new Breakdown($"{s.Name} at the {def.Name}", rows, $"{perTick:0.###}/tick · {perDay:0.#}/day on shift",
            "base × level × performance × tool × health × conditions × age × mood × perks × food");
    }

    /// <summary>A construction site: work and materials so far, the crew's pace and the time left at it.</summary>
    private static Breakdown? SiteOf(HelpContext c)
    {
        if (c.Colony is not { } sim) return null;
        var site = c.Site ?? (c.Building is { } b ? sim.Site(b.Id) : null);
        if (site is null) return null;
        var def = sim.Data.Buildings[site.DefId];
        var f = sim.SiteForecast(site);
        var rows = new List<BreakdownRow>
        {
            new("Work", $"{site.Work:0.#} / {site.WorkNeeded:0.#}", $"level {site.Level} build work"),
            new("Materials hauled", $"{site.DeliveredTotal:0} / {site.MaterialsTotal:0}", "work never passes this share"),
            new("Builders", $"{f.Builders} / {sim.MaxBuilders(site)}", f.Helpers > 0 ? $"+ {f.Helpers} idle helping at {Pct(sim.Data.Rules.Construction.HelperShare)}" : ""),
            new("Crew work per hour", $"{f.WorkPerHour:0.##}", "averaged over the day's shifts"),
            new("Crew hauling per hour", $"{f.HaulPerHour:0}", "units"),
        };
        var total = f.BlockedBy ?? (f.EtaHours is { } h ? (h < sim.Data.Rules.TicksPerDay ? $"about {Math.Ceiling(h):0} h left" : $"about {Days(h / sim.Data.Rules.TicksPerDay)} left") : "no progress");
        return new Breakdown(site.IsUpgrade ? $"{def.Name} → level {site.Level}" : $"{def.Name} (site)", rows, total,
            "materials left ÷ hauling + work left ÷ work per hour");
    }

    /// <summary>What each staffed gathering building makes per day.</summary>
    private static Breakdown? ColonyOutputOf(HelpContext c)
    {
        if (c.Colony is not { } sim) return null;
        var rows = sim.State.Buildings
            .Select(b => (Building: b, Def: sim.Data.Buildings[b.DefId], PerDay: sim.OutputPerDay(b)))
            .Where(x => x.PerDay > 0)
            .OrderByDescending(x => x.PerDay)
            .Select(x => new BreakdownRow(x.Def.Name, $"{x.PerDay:0.#}/day", x.Def.Produces ?? "food"))
            .ToList();
        return rows.Count == 0 ? null : new Breakdown("Output per day", rows, $"{rows.Count} buildings at work");
    }

    private static Breakdown? FoodDaysOf(HelpContext c)
    {
        if (c.Colony is not { } sim) return null;
        var days = sim.FoodDays;
        return new Breakdown("Food",
        [
            new("In store", $"{sim.FoodStock:0.#}", "food value of every edible lot"),
            new("Eaten per day", $"{sim.FoodPerDay:0.#}", $"{sim.State.Survivors.Count} mouths"),
        ], double.IsInfinity(days) ? "nobody eats" : $"{days:0.#} days left", "in store ÷ eaten per day");
    }

    /// <summary>A trip to <see cref="HelpContext.Zone"/> leaving now: base days, each term (weather, ...), rations.</summary>
    private static Breakdown? TravelOf(HelpContext c)
    {
        if (c.Colony is not { } sim || c.Zone is not { } zone) return null;
        var plan = WorldMap.Travel(sim, zone, sim.State.Tick);
        var perDay = (double)sim.Data.Rules.TicksPerDay;
        var rows = new List<BreakdownRow> { new("On foot", Days(plan.BaseTicks / perDay), zone.Name) };
        rows.AddRange(plan.Modifiers.Select(m => new BreakdownRow(m.Source,
            m.Blocks ? "blocked" : m.ExtraTicks != 0 ? $"{(m.ExtraTicks > 0 ? "+" : "−")}{Days(Math.Abs(m.ExtraTicks) / perDay)}" : Times(m.Multiplier), m.Reason)));
        rows.Add(new("Rations per member", $"{WorldMap.RationsPerMember(sim.Data, plan.Ticks / perDay):0.#} food", "base ration + per day, out and back"));
        return new Breakdown($"To {zone.Name}", rows, plan.Blocked ? "cannot leave now" : $"{Days(plan.Ticks / perDay)} each way",
            "base days × multipliers + extra days");
    }

    /// <summary>The attack being previewed: accuracy and every term <see cref="Combat.HitChance"/> takes off or adds.</summary>
    private static Breakdown? HitChanceOf(HelpContext c)
    {
        if (c.Scavenge is not { } sim || c.Attacker is not { } attacker || c.Action is not { } action || c.Target is not { } target) return null;
        var data = sim.Data;
        var from = c.From ?? attacker.Pos;
        var profile = sim.Profile(target);
        var ambush = Combat.AmbushAgainst(data, sim.State, attacker, target, c.From);
        var bane = Combat.Bane(data, action, target);
        var mark = Combat.MarkBonus(sim, attacker, target);
        var weather = Combat.WeatherPenalty(sim, action);
        var radio = ElectronicWarfare.RadioAccuracy(sim, attacker);
        var range = Math.Max(0, Math.Sqrt(from.DistanceSquared(target.Pos)) - 1) * data.Rules.Combat.RangeFalloff;
        var dodge = ambush is { IgnoreDodge: true } ? Math.Min(0, profile.Dodge) : profile.Dodge;
        var rows = new List<BreakdownRow> { new("Accuracy", Pct(action.Accuracy), action.Name) };
        void Term(string label, double value, string note = "")
        {
            if (Math.Abs(value) > 1e-9) rows.Add(new(label, Signed(value), note));
        }
        Term("Drone mark", mark);
        Term("Weather", -weather, "dust and rain on ranged shots");
        Term("Radio", radio);
        Term("Dodged their attack", Combat.OutplayBonus(sim, attacker, target), "you stepped out of their telegraphed blow");
        Term("Card", attacker.CardHit, "for the next attack");
        var cover = Combat.CoverAgainst(data, sim.State.Map, target.Pos, from);
        if (c.TargetKnown >= BestiaryTier.Killed)
        {
            Term("Target's dodge", -dodge, ambush is { IgnoreDodge: true } ? "caught off guard" : "");
            Term("Cover", -cover, "the tile beside the target, on your side");
            Term("Brace", -profile.CoverBonus);
        }
        else Term("Target's defence", -(dodge + cover + profile.CoverBonus), "dodge, cover and bracing: learn it in the Bestiary");
        Term("Range", -range, $"{data.Rules.Combat.RangeFalloff * 100:0}% per tile past the first");
        Term("Ambush", ambush?.HitBonus ?? 0, ambush is { Backs: true } ? "an ally beside the target blunts it" : "they cannot see you");
        Term("Bane", bane?.HitBonus ?? 0);
        var chance = Combat.ChanceToHit(sim, attacker, action, target, c.From);
        return new Breakdown($"{attacker.Name} → {target.Name}", rows, Pct(chance),
            $"clamped to {Pct(data.Rules.Combat.MinHitChance)}–{Pct(data.Rules.Combat.MaxHitChance)}");
    }

    /// <summary>A survivor's humanity: what each prosthetic costs, what it caps their mood at.</summary>
    private static Breakdown? HumanityOf(HelpContext c)
    {
        if (c.Colony is not { } sim || c.Survivor is not { } s || sim.Data.Cybernetics is not { } cyber) return null;
        var body = s.Body;
        var rows = new List<BreakdownRow> { new("Start", $"{Body.MaxHumanity:0}") };
        rows.AddRange(body.Parts.Where(p => p.Value.Condition == PartCondition.Prosthetic && p.Value.HumanityCost > 0)
            .Select(p => new BreakdownRow($"Prosthetic {p.Key}", $"−{p.Value.HumanityCost:0}")));
        if (body.HumanityLoss > 0) rows.Add(new("Other losses", $"−{body.HumanityLoss:0}"));
        var rules = cyber.Humanity;
        rows.Add(new("Mood cap", $"{Cybernetics.MoodCap(rules, body.Humanity):0}", $"full mood from {rules.MoodCapFrom:0} humanity up"));
        return new Breakdown($"{s.Name}'s humanity", rows,
            $"{body.Humanity:0}" + (body.Humanity < rules.Detached ? " · detached" : ""), $"detached below {rules.Detached:0}: partners and children suffer");
    }
}
