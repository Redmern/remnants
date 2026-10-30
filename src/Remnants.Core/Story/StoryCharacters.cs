using Remnants.Core.Colony;
using Remnants.Core.Survivors;

namespace Remnants.Core.Story;

/// <summary>
/// Story characters as colony survivors (docs/story/survivors.md): joining (a unique survivor with fixed stats, perks
/// and trait hooks), leaving, dying, recruits the story hands over, and the daily rules their traits bring — Seven's
/// humanity growing back, the unease around it, Cole keeping score, Pell's mood following the colony's humanity, and
/// Doc's oath against Network-grade limbs.
/// </summary>
public static class StoryCharacters
{
    public const string Seven = "seven";
    public const string Doc = "hale";

    /// <summary>Seven's humanity stops growing back here.</summary>
    public const double HumanityGrowthCap = 60;

    /// <summary>Others are uneasy near Seven until its humanity reaches this.</summary>
    public const double UneasyBelow = 30;

    /// <summary>The colony survivor a character became, if they are in the colony now.</summary>
    /// <summary>"key" names the key-carrier (Iris, Seven, or the survivor who took the port).</summary>
    public static Survivor? SurvivorOf(ColonySim sim, string characterId) =>
        characterId == Key ? Campaign.KeyCarrierSurvivor(sim) : sim.State.Survivors.Find(s => s.UniqueId == characterId);

    public const string Key = "key";

    /// <summary>
    /// The character joins: a new adult survivor built from their <see cref="UniqueSurvivorDef"/> (a character without
    /// one, or one already in the colony, only counts as joined). Returns the survivor.
    /// </summary>
    public static Survivor? Join(ColonySim sim, string characterId)
    {
        var state = sim.State;
        var campaign = state.Campaign!;
        var c = campaign.Character(characterId);
        c.Met = true;
        c.Joined = true;
        c.Left = false;
        if (SurvivorOf(sim, characterId) is { } already) return already;
        if (sim.Story?.Characters.GetValueOrDefault(characterId) is not { Survivor: { } def } character) return null;
        var rng = sim.Rng;
        var s = new Survivor
        {
            Id = state.NextSurvivorId++,
            Name = def.Name ?? character.Name,
            UniqueId = characterId,
            Sex = Enum.Parse<Sex>(def.Sex),
            LifeStage = LifeStage.Adult,
            AgeTicks = def.AgeYears * sim.Data.Rules.TicksPerYear,
            Health = Survivor.MaxHealth,
            Mood = 70,
        };
        var range = sim.Data.Rules.NewGame.AttributeRange;
        foreach (var a in Enum.GetValues<AttributeKind>())
            s.Set(a, def.Attributes.TryGetValue(a.ToString(), out var v) ? v : rng.NextInclusive(range[0], range[1]));
        foreach (var (skill, value) in def.Skills) s.Set(Enum.Parse<Skill>(skill), value);
        foreach (var perk in def.Perks.Where(sim.Data.Perks.ContainsKey)) s.Perks.Add(perk);
        foreach (var traitId in def.Traits)
        {
            if (sim.Story!.Traits.GetValueOrDefault(traitId) is not { } trait) continue;
            s.Traits.Add(traitId);
            foreach (var (k, v) in trait.Hooks) s.TraitHooks[k] = s.TraitHooks.GetValueOrDefault(k) + v;
        }
        s.Body.HumanityLoss = Body.MaxHumanity - def.Humanity;
        state.Survivors.Add(s);
        c.SurvivorId = s.Id;
        state.Log(new(state.Tick, ColonyEventType.Recruited, $"{s.Name} joined the colony.", s.Id));
        return s;
    }

    /// <summary>The character leaves the colony alive (they stay in the story: Doc's clinic, the radio).</summary>
    public static void Leave(ColonySim sim, string characterId, string? line)
    {
        var state = sim.State;
        if (characterId == Key)
        {
            if (Campaign.KeyCarrierSurvivor(sim) is not { } carrier) return;
            if (carrier.UniqueId is { } unique) characterId = unique;
            else
            {
                state.Survivors.Remove(carrier);
                state.Memorial.Add(new(carrier.Id, carrier.Name, state.Tick, line ?? "In the Spire"));
                state.Log(new(state.Tick, ColonyEventType.StoryMission, $"{carrier.Name} stayed in the Spire.", carrier.Id));
                Bonds.OnGone(state, sim.Data, carrier, died: false);
                return;
            }
        }
        var c = state.Campaign!.Character(characterId);
        if (!c.Joined) return;
        c.Joined = false;
        c.Left = true;
        if (SurvivorOf(sim, characterId) is not { } s) return;
        state.Survivors.Remove(s);
        state.Log(new(state.Tick, ColonyEventType.StoryMission, line ?? $"{s.Name} left the colony.", s.Id));
        Bonds.OnGone(state, sim.Data, s, died: false);
    }

    /// <summary>The character is dead; a joined one leaves the roster for the memorial.</summary>
    public static void Die(ColonySim sim, string characterId, string? cause)
    {
        var state = sim.State;
        var c = state.Campaign!.Character(characterId);
        c.Dead = true;
        c.Joined = false;
        if (SurvivorOf(sim, characterId) is { } s) ColonyDeaths.Remove(state, sim.Data, s, "died", cause ?? "Died for the story");
    }

    /// <summary>A plain recruit from the story (the M7 patient, Cole's band, the Choir children).</summary>
    public static Survivor Recruit(ColonySim sim, string name, bool child, double humanity)
    {
        var state = sim.State;
        var rng = sim.Rng;
        var rules = sim.Data.Rules;
        var range = child ? rules.NewGame.ChildAttributeRange : rules.NewGame.AttributeRange;
        var s = new Survivor
        {
            Id = state.NextSurvivorId++,
            Name = name,
            Sex = rng.Next(0, 2) == 0 ? Sex.Male : Sex.Female,
            LifeStage = LifeStage.Adult,
            Mood = 60,
        };
        foreach (var a in Enum.GetValues<AttributeKind>()) s.Set(a, rng.NextInclusive(range[0], range[1]));
        if (child)
        {
            var ages = rules.NewGame.ChildAgeRange;
            s.AgeTicks = rng.NextInclusive(ages[0], ages[1]) * rules.TicksPerYear;
            s.GrowthTicks = Bonds.GrowthAtAge(s.AgeTicks, rules);
            s.LifeStage = Bonds.StageAt(s.GrowthTicks, rules.Life);
            s.AgeTicks = Bonds.AgeTicksAt(s.GrowthTicks, rules);
            foreach (var a in Enum.GetValues<AttributeKind>()) s.Aptitude[a] = Math.Max(s.Get(a), rng.NextInclusive(rules.NewGame.AttributeRange[0], rules.NewGame.AttributeRange[1]));
        }
        else
        {
            var ages = rules.Expedition.RecruitAgeRange;
            s.AgeTicks = rng.NextInclusive(ages[0], ages[1]) * rules.TicksPerYear;
        }
        s.Body.HumanityLoss = Body.MaxHumanity - Math.Clamp(humanity, 0, Body.MaxHumanity);
        state.Survivors.Add(s);
        state.Log(new(state.Tick, ColonyEventType.Recruited, $"{s.Name} joined the colony.", s.Id));
        return s;
    }

    /// <summary>The average humanity of the colony's adults (100 with none).</summary>
    public static double ColonyHumanity(ColonySim sim)
    {
        var adults = sim.State.Survivors.Where(s => !s.IsChild).ToList();
        return adults.Count == 0 ? Body.MaxHumanity : adults.Average(s => s.Body.Humanity);
    }

    /// <summary>
    /// Once per colony tick: joined characters who died at home or in the field are marked dead (their fallbacks start
    /// from the flags), and once per day the trait rules run.
    /// </summary>
    public static void Tick(ColonySim sim)
    {
        var state = sim.State;
        var campaign = state.Campaign!;
        foreach (var (id, c) in campaign.Characters.Where(kv => kv.Value is { Joined: true, Dead: false }))
        {
            if (c.SurvivorId is not { } sid || state.FindSurvivor(sid) is not null) continue;
            if (campaign.Taken.Any(t => t.Survivor.Id == sid) || state.Expedition?.Members.Any(m => m.SurvivorId == sid) == true) continue;
            c.Joined = false;
            // In the memorial: dead. Otherwise they walked away (no bed, no hope) and are still out there.
            if (state.Memorial.Any(m => m.SurvivorId == sid))
            {
                c.Dead = true;
                campaign.Flags[$"{id}_dead"] = 1;
                Campaign.Log(sim, $"{sim.Story?.Characters.GetValueOrDefault(id)?.Name ?? id} is gone.");
            }
            else
            {
                c.Left = true;
                campaign.Flags[$"{id}_left"] = 1;
            }
        }
        DocsOath(sim);
        if (state.Tick % sim.Data.Rules.TicksPerDay != 0) return;
        Daily(sim);
    }

    private static void Daily(ColonySim sim)
    {
        var state = sim.State;
        var home = state.Survivors.Where(s => !s.Away).ToList();
        var humanity = ColonyHumanity(sim);
        var food = state.Stockpile.Get("food");
        foreach (var s in home)
        {
            if (s.TraitHooks.GetValueOrDefault(Colony.PerkHooks.HumanityGrowthPerDay) is var grow and > 0 && s.Body.Humanity < HumanityGrowthCap)
                s.Body.HumanityLoss = Math.Max(Body.MaxHumanity - HumanityGrowthCap, s.Body.HumanityLoss - grow);
            if (s.TraitHooks.GetValueOrDefault(Colony.PerkHooks.MoodFoodSurplus) is var score and > 0)
                s.Mood = Math.Clamp(s.Mood + (food >= state.Survivors.Count ? score : -score) / 10, 0, Survivor.MaxMood);
            if (s.TraitHooks.GetValueOrDefault(Colony.PerkHooks.MoodFollowsHumanity) > 0 && !s.IsChild)
                s.Mood = Math.Min(s.Mood, humanity + 30);
        }
        // The unease around Seven: everyone else a little lower while it is still mostly machine.
        foreach (var uneasy in home.Where(s => s.TraitHooks.GetValueOrDefault(Colony.PerkHooks.NearbyMood) < 0 && s.Body.Humanity < UneasyBelow).ToList())
            foreach (var other in home.Where(o => o.Id != uneasy.Id))
                other.Mood = Math.Clamp(other.Mood + uneasy.TraitHooks[Colony.PerkHooks.NearbyMood], 0, Survivor.MaxMood);
    }

    /// <summary>
    /// Doc's oath: every Network-grade (tier 4) part newly fitted in the colony is counted once
    /// (<c>doc_t4_count</c>). The first, while Doc is in the colony, brings her warning; the second, she leaves.
    /// </summary>
    private static void DocsOath(ColonySim sim)
    {
        if (sim.Data.Cybernetics is not { } cyber) return;
        var campaign = sim.State.Campaign!;
        foreach (var s in sim.State.Survivors)
            foreach (var (part, p) in s.Body.Parts.OrderBy(kv => kv.Key))
            {
                if (p.Condition != PartCondition.Prosthetic || cyber.Node(p.ProstheticItemId) is not { Tier: >= 4 }) continue;
                if (!campaign.T4Seen.Add($"{s.Id}:{part}")) continue;
                var count = campaign.Flags[DocT4Count] = campaign.Flag(DocT4Count) + 1;
                if (!campaign.Character(Doc).Joined) continue;
                if (count == 1 && sim.Story!.Dialogues.ContainsKey("doc_oath_warn")) Campaign.QueueCall(sim, "doc_oath_warn");
                if (count >= 2)
                {
                    if (sim.Story!.Dialogues.ContainsKey("doc_oath_leave")) Campaign.QueueCall(sim, "doc_oath_leave");
                    campaign.Flags["doc_state"] = 3;
                    Leave(sim, Doc, "Doc Hale packed her bag and left. She did not say goodbye. She said: \"I told you.\"");
                }
            }
    }

    public const string DocT4Count = "doc_t4_count";
}
