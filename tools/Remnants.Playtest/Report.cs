using System.Text;

namespace Remnants.Playtest;

/// <summary>Seed-by-seed table plus medians, for docs/BALANCE.md.</summary>
public static class Report
{
    public static string Summaries(IReadOnlyList<PlaythroughSummary> runs)
    {
        static string D(double? d) => d is { } v ? $"{v,5:0.0}" : "    -";
        var sb = new StringBuilder();
        sb.AppendLine("seed  pop starv expDead other left births 1stPreg 1stBirth houseOK houseBuilt TH2   ws    armory lab   minFoodDays exp expDeaths linkedEarly crafted recruits");
        foreach (var r in runs)
            sb.AppendLine($"{r.Seed,4} {r.FinalPopulation,4} {r.StarvationDeaths,5} {r.ExpeditionDeaths,7} {r.OtherDeaths,5} {r.Departures,4} {r.Births,6} " +
                          $"{D(r.FirstPregnancyDay)}   {D(r.FirstBirthDay)}    {D(r.HouseAffordableDay)}   {D(r.HouseBuiltDay)}  {D(r.TownHall2Day)} {D(r.WorkshopDay)} {D(r.ArmoryDay)} {D(r.LabDay)}  {r.MinFoodDays,6:0.0}     {r.Expeditions,3} {r.ExpeditionsWithDeaths,5} {r.LinkedEncountersEarly,10} {r.CraftedItems,7} {r.Recruits,5}");
        double? Median(Func<PlaythroughSummary, double?> f)
        {
            var v = runs.Select(f).Where(x => x is not null).Select(x => x!.Value).OrderBy(x => x).ToList();
            return v.Count == 0 ? null : v[v.Count / 2];
        }
        sb.AppendLine($"median: pop {Median(r => r.FinalPopulation):0}, first pregnancy day {D(Median(r => r.FirstPregnancyDay))}, first birth day {D(Median(r => r.FirstBirthDay))}, " +
                      $"house affordable {D(Median(r => r.HouseAffordableDay))}, TH2 {D(Median(r => r.TownHall2Day))}, " +
                      $"armory {D(Median(r => r.ArmoryDay))}, lab {D(Median(r => r.LabDay))}, min food days {Median(r => r.MinFoodDays):0.0}");
        sb.AppendLine($"perks: first perk point day {D(Median(r => r.FirstPerkPointDay))} (median), survivor-days per level-up " +
                      $"{runs.Sum(r => r.SurvivorDays) / Math.Max(1, runs.Sum(r => r.LevelUps)):0.0}, perks learned {runs.Sum(r => r.PerksLearned)} " +
                      $"({(double)runs.Sum(r => r.PerksLearned) / runs.Count:0.0}/colony), mean level at the end {Median(r => r.MeanLevel):0.0} (median), " +
                      $"max level {runs.Max(r => r.MaxLevel)}, unspent points {runs.Sum(r => r.UnspentPoints)}");
        if (runs.All(r => r.Cyber is not null))
        {
            var tiers = string.Join(", ", Enumerable.Range(0, 5).Select(t =>
            {
                var reached = runs.Count(r => r.Cyber!.TierDay[t] is not null);
                return $"T{t} {D(Median(r => r.Cyber!.TierDay[t])).Trim()} ({reached}/{runs.Count})";
            }));
            sb.AppendLine($"cybernetics: tier first craftable (median day, colonies) {tiers}; limbs lost {runs.Sum(r => r.Cyber!.LimbsLost)}, " +
                          $"fitted at the end by tier [{string.Join(" ", Enumerable.Range(0, 5).Select(t => runs.Sum(r => r.Cyber!.FittedByTier[t])))}], " +
                          $"surgeries {runs.Sum(r => r.Cyber!.Surgeries)} ({runs.Sum(r => r.Cyber!.Complications)} complications), " +
                          $"limbs still missing {runs.Sum(r => r.Cyber!.MissingAtEnd)}, lowest humanity {runs.Min(r => r.Cyber!.MinHumanity):0}");
        }
        if (runs.All(r => r.Food is not null))
            sb.AppendLine($"food: rotted {runs.Sum(r => r.Food!.Rotted)} units ({(double)runs.Sum(r => r.Food!.Rotted) / runs.Count:0}/colony), " +
                          $"food poisonings {runs.Sum(r => r.Food!.Poisonings)}, batches cooked/preserved {runs.Sum(r => r.Food!.Cooked)} " +
                          $"({(double)runs.Sum(r => r.Food!.Cooked) / runs.Count:0}/colony), mean nutrition at the end {Median(r => r.Food!.MeanNutrition):0.00} (median), " +
                          $"cooked share of the diet {Median(r => r.Food!.CookedShareEaten) * 100:0}% (median)");
        sb.AppendLine($"totals: starvation deaths {runs.Sum(r => r.StarvationDeaths)}, expedition deaths {runs.Sum(r => r.ExpeditionDeaths)} over {runs.Sum(r => r.Expeditions)} expeditions " +
                      $"({runs.Sum(r => r.ExpeditionsWithDeaths)} with deaths), departures {runs.Sum(r => r.Departures)}, births {runs.Sum(r => r.Births)}, " +
                      $"early Linked sightings {runs.Sum(r => r.LinkedEncountersEarly)}, recruits {runs.Sum(r => r.Recruits)}");
        return sb.ToString();
    }
}
