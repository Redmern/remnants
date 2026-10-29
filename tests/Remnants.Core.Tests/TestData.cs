using Remnants.Core.Colony;
using Remnants.Core.Data;
using Remnants.Core.Survivors;

namespace Remnants.Core.Tests;

/// <summary>Shared fixtures: the real data/ folder, plus small hand-built defs for exact-math tests.</summary>
public static class TestData
{
    private static readonly Lazy<GameData> RealData =
        new(() => GameDataLoader.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "data")));

    public static GameData Real => RealData.Value;

    public static JobDef Job(
        Dictionary<AttributeKind, double>? attributes = null,
        double skillWeight = 1.0,
        double toolBonus = 0.5,
        Dictionary<BodyRegion, double>? body = null) => new()
        {
            Id = "test_job",
            Name = "Test Job",
            Skill = Skill.Woodcutting,
            SkillWeight = skillWeight,
            Attributes = attributes ?? new() { [AttributeKind.Strength] = 1.0 },
            BodyParts = body ?? new() { [BodyRegion.Arms] = 1.0 },
            ToolBonus = toolBonus,
        };

    public static Survivor Survivor(double allAttributes = 50, double skill = 0, LifeStage stage = LifeStage.Adult)
    {
        var s = new Survivor { Id = 1, Name = "Test", LifeStage = stage };
        foreach (var a in Enum.GetValues<AttributeKind>()) s.Set(a, allAttributes);
        s.Set(Skill.Woodcutting, skill);
        return s;
    }

    /// <summary>An empty colony on real data with a Town Hall at the given level and nothing else.</summary>
    public static ColonySim EmptyColony(int townHallLevel = 1, Dictionary<string, double>? stock = null)
    {
        var state = new ColonyState();
        state.Buildings.Add(new Building { Id = state.NextBuildingId++, DefId = "town_hall", Level = townHallLevel });
        foreach (var (res, amount) in stock ?? new()) state.Stockpile.Add(res, amount);
        return new ColonySim(Real, state);
    }

    public static Survivor AddSurvivor(this ColonySim sim, LifeStage stage = LifeStage.Adult, double attributes = 50)
    {
        var s = Survivor(attributes, stage: stage);
        s.Id = sim.State.NextSurvivorId++;
        s.Name = $"S{s.Id}";
        s.AgeTicks = (stage == LifeStage.Adult ? 25 : 8) * sim.Data.Rules.TicksPerYear;
        sim.State.Survivors.Add(s);
        return s;
    }

    public static Building Build(this ColonySim sim, string defId)
    {
        var def = sim.Data.Buildings[defId];
        foreach (var (res, amount) in def.Level(1).Cost) sim.State.Stockpile.Add(res, amount);
        var result = sim.Execute(new Construct(defId));
        Assert.True(result.Ok, result.Message);
        return sim.State.FindBuilding(result.EntityId!.Value)!;
    }
}
