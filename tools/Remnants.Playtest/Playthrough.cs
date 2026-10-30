using System.Text;
using Remnants.Core.Colony;
using Remnants.Core.Data;
using Remnants.Core.Scavenge;
using Remnants.Core.Survivors;

namespace Remnants.Playtest;

/// <summary>The colony at the end of one in-game day.</summary>
public sealed record DaySnapshot(
    int Day, int Adults, int Children, int Infants, int Beds, double Food, double FoodPerDay,
    IReadOnlyDictionary<string, double> Stock, string Buildings, string Jobs, List<string> Events);

/// <summary>Headline numbers of one playthrough, for comparing seeds and balance passes.</summary>
public sealed record PlaythroughSummary(
    ulong Seed, int Days, int FinalPopulation, int StarvationDeaths, int ExpeditionDeaths, int OtherDeaths,
    int Departures, int Births, double? FirstBirthDay, double? FirstPregnancyDay, double? HouseAffordableDay, double? HouseBuiltDay,
    double? TownHall2Day, double? FirstStarvingDay, double MinFoodDays, int Expeditions, int ExpeditionsWithDeaths,
    int LinkedEncountersEarly, double? WorkshopDay, double? ArmoryDay, double? LabDay, int CraftedItems, int Recruits,
    double? FirstPerkPointDay, int LevelUps, double SurvivorDays, int PerksLearned, double MeanLevel, int MaxLevel, int UnspentPoints,
    CyberSummary? Cyber = null, FoodSummary? Food = null);

/// <summary>Food and cooking: units that rotted, food poisonings, batches cooked or preserved, mean nutrition at the end.</summary>
public sealed record FoodSummary(int Rotted, int Poisonings, int Cooked, double MeanNutrition, double CookedShareEaten);

/// <summary>
/// Cybernetics: the first day a design of each tier could be queued (requirements met, building there), limbs lost,
/// parts fitted by tier, surgeries and complications, and the lowest humanity at the end.
/// </summary>
public sealed record CyberSummary(double?[] TierDay, int LimbsLost, int[] FittedByTier, int Surgeries, int Complications,
    int MissingAtEnd, double MinHumanity);

/// <summary>Runs <see cref="ColonyBot"/> on a fresh New Game and records a daily timeline.</summary>
public sealed class Playthrough
{
    public Playthrough(GameData data, ScavengeData scavenge, ulong seed, BotOptions? options = null)
    {
        Seed = seed;
        Sim = NewGame.Create(data, seed, scavenge);
        Bot = new ColonyBot(Sim, options);
    }

    public ulong Seed { get; }
    public ColonySim Sim { get; }
    public ColonyBot Bot { get; }
    public List<DaySnapshot> Timeline { get; } = [];

    private readonly List<ColonyEvent> _events = [];
    private int _starvationDeaths, _expeditionDeaths, _otherDeaths, _departures, _births, _crafted, _recruits, _levelUps, _perks;
    private double? _firstPerkPoint;
    private double _survivorTicks;
    private double? _firstBirth, _firstPregnancy, _houseAffordable, _houseBuilt, _townHall2, _firstStarving, _workshop, _armory, _lab;
    private double _minFoodDays = double.MaxValue;
    private readonly double?[] _tierDay = new double?[5];
    private int _limbsLost, _surgeries, _complications;
    private int _rotted, _poisonings, _cooked;
    private double _cookedEaten, _eaten;

    public PlaythroughSummary Run(int days, int actEveryTicks = 4)
    {
        var tpd = Sim.Data.Rules.TicksPerDay;
        for (var day = 0; day < days; day++)
        {
            var dayStart = _events.Count;
            var foodStart = Sim.FoodStock;
            for (var t = 0; t < tpd; t++)
            {
                if (t % actEveryTicks == 0)
                {
                    Bot.Act();
                    Collect();
                }
                Sim.Tick();
                Collect();
                Track();
            }
            TrackCyber();
            var s = Sim.State;
            Timeline.Add(new DaySnapshot(day + 1,
                s.Survivors.Count(x => !x.IsChild), s.Survivors.Count(x => x.LifeStage == LifeStage.Child), s.Survivors.Count(x => x.IsInfant),
                Sim.BedCapacity, Sim.FoodStock, Sim.FoodStock - foodStart,
                new SortedDictionary<string, double>(s.Stockpile.Amounts.Where(kv => kv.Value > 0.05).ToDictionary(kv => kv.Key, kv => kv.Value)),
                string.Join(" ", s.Buildings.OrderBy(b => b.Id).Select(b => $"{Short(b.DefId)}{b.Level}")),
                Jobs(),
                _events.Skip(dayStart).Where(Notable).Select(e => e.Message).ToList()));
        }
        var linkedEarly = Bot.Expeditions.Where(e => Sim.Scavenge!.Zones[e.Zone].Threat <= 1).Sum(e => e.Stats.LinkedSeen);
        var grown = Sim.State.Survivors.Where(x => !x.IsInfant).ToList();
        return new PlaythroughSummary(Seed, days, Sim.State.Survivors.Count, _starvationDeaths, _expeditionDeaths, _otherDeaths,
            _departures, _births, _firstBirth, _firstPregnancy, _houseAffordable, _houseBuilt, _townHall2, _firstStarving,
            _minFoodDays, Bot.Expeditions.Count, Bot.Expeditions.Count(e => e.Dead > 0), linkedEarly, _workshop, _armory, _lab, _crafted, _recruits,
            _firstPerkPoint, _levelUps, _survivorTicks / tpd, _perks, grown.Count == 0 ? 0 : grown.Average(x => x.Level),
            grown.Select(x => x.Level).DefaultIfEmpty(1).Max(), grown.Sum(x => x.PerkPoints), Cyber(),
            new FoodSummary(_rotted, _poisonings, _cooked, Sim.State.Survivors.Select(x => x.Nutrition).DefaultIfEmpty(0).Average(),
                _eaten <= 0 ? 0 : _cookedEaten / _eaten));
    }

    /// <summary>Where people are right now: workers per building, patients, away, idle.</summary>
    private string Jobs()
    {
        var s = Sim.State;
        var parts = s.Buildings.Select(b => (b, n: s.WorkersAt(b.Id).Count())).Where(x => x.n > 0).Select(x => $"{Short(x.b.DefId)}:{x.n}").ToList();
        var patients = s.Survivors.Count(x => x.PatientAtBuildingId is not null);
        var away = s.Survivors.Count(x => x.Away);
        var idle = s.Survivors.Count(x => !x.Away && !x.IsInfant && x.PatientAtBuildingId is null && x.AssignedBuildingId is null);
        if (patients > 0) parts.Add($"pat:{patients}");
        if (away > 0) parts.Add($"away:{away}");
        if (idle > 0) parts.Add($"idle:{idle}");
        return string.Join(" ", parts);
    }

    private double Day => Sim.State.Tick / (double)Sim.Data.Rules.TicksPerDay;

    /// <summary>Copies colony events logged since the last call (the log is capped, so the last one is found by identity).</summary>
    private void Collect()
    {
        var log = Sim.State.Events;
        var start = 0;
        if (_last is not null)
            for (var i = log.Count - 1; i >= 0; i--)
                if (ReferenceEquals(log[i], _last)) { start = i + 1; break; }
        foreach (var e in log.Skip(start))
        {
            _events.Add(e);
            switch (e.Type)
            {
                case ColonyEventType.Died when e.Message.Contains("starved"): _starvationDeaths++; break;
                case ColonyEventType.Died when e.Message.Contains(" died in "): _expeditionDeaths++; break;
                case ColonyEventType.Died: _otherDeaths++; break;
                case ColonyEventType.Left: _departures++; break;
                case ColonyEventType.Born: _births++; _firstBirth ??= Day; break;
                case ColonyEventType.Pregnant: _firstPregnancy ??= Day; break;
                case ColonyEventType.Starving: _firstStarving ??= Day; break;
                case ColonyEventType.Crafted:
                    _crafted++;
                    if (e.BuildingId is { } b && Sim.State.FindBuilding(b) is { } at && Sim.Data.Buildings[at.DefId].Job == "cooking") _cooked++;
                    break;
                case ColonyEventType.FoodRotted:
                    _rotted += System.Text.RegularExpressions.Regex.Matches(e.Message, @"(\d+) ").Sum(m => int.Parse(m.Groups[1].Value));
                    break;
                case ColonyEventType.FoodPoisoning: _poisonings++; break;
                case ColonyEventType.Recruited: _recruits++; break;
                case ColonyEventType.LevelUp: _levelUps++; _firstPerkPoint ??= Day; break;
                case ColonyEventType.PerkLearned: _perks++; break;
                case ColonyEventType.LimbLost: _limbsLost++; break;
                case ColonyEventType.SurgeryDone: _surgeries++; break;
                case ColonyEventType.SurgeryComplication: _surgeries++; _complications++; break;
            }
        }
        if (log.Count > 0) _last = log[^1];
    }

    private ColonyEvent? _last;

    private void Track()
    {
        var s = Sim.State;
        var house = Sim.Data.Buildings["house"];
        if (_houseAffordable is null && Sim.TownHallLevel >= house.MinTownHallLevel && s.Stockpile.CanAfford(house.Level(1).Cost))
            _houseAffordable = Day;
        if (_houseAffordable is null && s.Buildings.Any(b => b.DefId == "house")) _houseAffordable = Day;
        if (_houseBuilt is null && s.Buildings.Any(b => b.DefId == "house")) _houseBuilt = Day;
        if (_townHall2 is null && Sim.TownHallLevel >= 2) _townHall2 = Day;
        if (_workshop is null && s.Buildings.Any(b => b.DefId == "workshop")) _workshop = Day;
        if (_armory is null && s.Buildings.Any(b => b.DefId == "armory")) _armory = Day;
        if (_lab is null && s.Buildings.Any(b => b.DefId == "electronics_lab")) _lab = Day;
        _survivorTicks += s.Survivors.Count(x => !x.IsInfant);
        var food = Sim.Data.Rules.Food;
        var need = s.Survivors.Where(x => !x.Away).Sum(x => x.IsInfant ? food.InfantPerTick : x.IsChild ? food.ChildPerTick : food.AdultPerTick)
                   * Sim.Data.Rules.TicksPerDay;
        if (Day >= 1 && need > 0) _minFoodDays = Math.Min(_minFoodDays, Sim.FoodStock / need);
        // What people ate lately, by cooked vs raw (the diets' newest additions are this tick's meals).
        foreach (var x in s.Survivors.Where(x => !x.Away))
            foreach (var (id, v) in x.Diet)
            {
                var cooked = Sim.Data.Food?.Items.GetValueOrDefault(id)?.Meal is not null;
                _eaten += v;
                if (cooked) _cookedEaten += v;
            }
    }

    /// <summary>Once a day: which tiers of the tree the colony could make now.</summary>
    private void TrackCyber()
    {
        if (Sim.Data.Cybernetics is not { } cyber) return;
        foreach (var node in cyber.Nodes.Values.Where(n => _tierDay[n.Tier] is null))
            if (Cybernetics.Status(Sim, node).State != CyberNodeState.Locked) _tierDay[node.Tier] = Day;
    }

    private CyberSummary? Cyber()
    {
        if (Sim.Data.Cybernetics is not { } cyber) return null;
        var parts = Sim.State.Survivors.SelectMany(x => x.Body.Parts.Values).ToList();
        var fitted = new int[5];
        foreach (var p in parts.Where(p => p.Condition == PartCondition.Prosthetic)) fitted[cyber.TierOf(p)]++;
        return new CyberSummary(_tierDay, _limbsLost, fitted, _surgeries, _complications,
            parts.Count(p => p.Condition == PartCondition.Missing),
            Sim.State.Survivors.Select(x => x.Body.Humanity).DefaultIfEmpty(100).Min());
    }

    private static bool Notable(ColonyEvent e) => e.Type is ColonyEventType.Constructed or ColonyEventType.Upgraded or ColonyEventType.Died
        or ColonyEventType.Left or ColonyEventType.Born or ColonyEventType.Pregnant or ColonyEventType.NewCouple or ColonyEventType.Starving
        or ColonyEventType.ExpeditionLaunched or ColonyEventType.ExpeditionReturned or ColonyEventType.Recruited or ColonyEventType.BecameAdult
        or ColonyEventType.LimbLost or ColonyEventType.ProstheticFitted or ColonyEventType.SurgeryBooked or ColonyEventType.SurgeryDone
        or ColonyEventType.SurgeryComplication or ColonyEventType.ProstheticNeedsService or ColonyEventType.BlueprintStudied or ColonyEventType.PerkLearned or ColonyEventType.ZoneRevealed or ColonyEventType.Homeless
        or ColonyEventType.FoodRotted or ColonyEventType.FoodPoisoning;

    public static string Short(string defId) => defId switch
    {
        "town_hall" => "TH", "shelter" => "Sh", "woodcutter" => "Wc", "quarry" => "Qy", "scrapyard" => "Sy", "farm" => "Fm",
        "med_bay" => "Md", "workshop" => "Ws", "tailor" => "Tl", "kitchen" => "Kt", "armory" => "Ar", "electronics_lab" => "El",
        "house" => "Ho", "campfire" => "Cf", "hunting_lodge" => "Hl", "smokehouse" => "Sm", "cold_cellar" => "Cc", "greenhouse" => "Gh",
        _ => defId,
    };

    /// <summary>The day-by-day timeline plus expedition outcomes, as text.</summary>
    public string Describe(bool events = true)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"== seed {Seed} ==");
        sb.AppendLine("day  pop(A/C/I) beds  food(Δ/day)   wood  stone scrap cloth  med  elec  buildings");
        foreach (var d in Timeline)
        {
            double R(string id) => d.Stock.GetValueOrDefault(id);
            sb.AppendLine($"{d.Day,3}  {d.Adults,2}/{d.Children}/{d.Infants}     {d.Beds,3}  {d.Food,6:0.0}({d.FoodPerDay,5:+0.0;-0.0}) {R("wood"),6:0} {R("stone"),5:0} {R("scrap"),5:0} {R("cloth"),5:0} {R("medicine"),4:0.0} {R("electronics"),5:0}  {d.Buildings}  [{d.Jobs}]");
            if (events)
                foreach (var e in d.Events) sb.AppendLine($"       · {e}");
        }
        sb.AppendLine("expeditions:");
        foreach (var e in Bot.Expeditions)
            sb.AppendLine($"  day {e.Day,5:0.0} {e.Zone,-16} party {e.Party.Count} → back {e.Returned} (evac {e.Evacuated}), dead {e.Dead}, " +
                          $"new injuries {e.NewInjuries}, recruits {e.Recruits}, items {e.ItemsBack}, rounds {e.Stats.Rounds}, kills {e.Stats.Kills}, " +
                          $"linked seen {e.Stats.LinkedSeen}{(e.Error is null ? "" : $" ERROR {e.Error}")}");
        if (Sim.Data.Cybernetics is { } cyber)
            foreach (var tier in Enumerable.Range(1, 4).Where(t => _tierDay[t] is null))
            {
                var closest = cyber.Nodes.Values.Where(n => n.Tier == tier).Select(n => Cybernetics.Status(Sim, n)).MinBy(st => st.Missing.Count)!;
                sb.AppendLine($"cybernetics T{tier} not reached; closest {closest.Node.Id} misses: {string.Join("; ", closest.Missing)}");
            }
        if (Bot.Problems.Count > 0)
        {
            sb.AppendLine("bot problems:");
            foreach (var p in Bot.Problems.Distinct().Take(20)) sb.AppendLine($"  {p}");
        }
        return sb.ToString();
    }
}
