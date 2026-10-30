using Remnants.Core.Colony;
using Remnants.Core.Data;
using Remnants.Core.Survivors;

namespace Remnants.Core.Tests;

/// <summary>Shared fixtures: the real data/ folder, plus small hand-built defs for exact-math tests.</summary>
public static class TestData
{
    private static readonly Lazy<GameData> Shipped =
        new(() => GameDataLoader.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "data")));

    private static readonly Lazy<GameData> RealNights = new(() => With(Shipped.Value, nights: true, weather: false));

    private static readonly Lazy<GameData> RealData = new(() => With(Shipped.Value, nights: false, weather: false));

    private static GameData With(GameData d, bool nights, bool weather)
    {
        var rules = d.Rules with { DayNight = d.Rules.DayNight with { Enabled = nights && d.Rules.DayNight.Enabled } };
        return new GameData(d.Resources.Values, d.Jobs.Values, d.Buildings.Values, rules, d.Recipes.Values, d.Progression, d.Cybernetics?.Def, d.Schematics,
            d.Weather with { Enabled = weather && d.Weather.Enabled }, d.Food?.Def, d.Electronics, d.Medicine?.Def);
    }

    /// <summary>
    /// The real data/ folder with the day and night cycle and the weather off (everyone works every tick, nobody sleeps,
    /// always clear), so tests of other rules can count ticks without a clock. <see cref="RealWithNights"/> turns the
    /// cycle on; <see cref="RealWithWeather"/> is the data exactly as shipped.
    /// </summary>
    public static GameData Real => RealData.Value;

    public static GameData RealWithNights => RealNights.Value;

    public static GameData RealWithWeather => Shipped.Value;

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
        foreach (var (res, amount) in stock ?? new())
            if (res == Food) state.AddFood(amount);
            else state.Stockpile.Add(res, amount);
        return new ColonySim(Real, state);
    }

    /// <summary>Stock key the helpers read as "this much food value" (the old food pool): it becomes potatoes (1 food each).</summary>
    public const string Food = "food";
    public const string Potato = "potato";

    /// <summary>Exactly <paramref name="value"/> food (an opened potato with that much left), for tests of scarcity.</summary>
    public static void AddOpenFood(this ColonyState state, double value) =>
        state.Food.Open.Add(new OpenPortion { ItemId = Potato, Left = value });

    /// <summary>
    /// Adds whole potatoes worth at least <paramref name="value"/> food that never spoil (so long tests of other rules
    /// keep a steady pantry; spoilage has its own tests).
    /// </summary>
    public static void AddFood(this ColonyState state, double value) =>
        Pantry.Add(state, Real, Potato, (int)Math.Ceiling(value - 1e-9), shelfMultiplier: 0);

    public static void AddFood(this ColonySim sim, double value) => sim.State.AddFood(value);

    public static Survivor AddSurvivor(this ColonySim sim, LifeStage stage = LifeStage.Adult, double attributes = 50)
    {
        var s = Survivor(attributes, stage: stage);
        s.Id = sim.State.NextSurvivorId++;
        s.Name = $"S{s.Id}";
        var life = sim.Data.Rules.Life;
        s.GrowthTicks = stage switch { LifeStage.Infant => 0, LifeStage.Child => life.InfantTicks + life.ChildTicks / 2, _ => 0 };
        s.AgeTicks = stage == LifeStage.Adult ? 25 * sim.Data.Rules.TicksPerYear : Bonds.AgeTicksAt(s.GrowthTicks, sim.Data.Rules);
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
