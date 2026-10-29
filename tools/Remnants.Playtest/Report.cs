using System.Text;

namespace Remnants.Playtest;

/// <summary>Seed-by-seed table plus medians, for docs/BALANCE.md.</summary>
public static class Report
{
    public static string Summaries(IReadOnlyList<PlaythroughSummary> runs)
    {
        static string D(double? d) => d is { } v ? $"{v,5:0.0}" : "    -";
        var sb = new StringBuilder();
        sb.AppendLine("seed  pop starv expDead other left births 1stPreg 1stBirth houseOK houseBuilt TH2   ws    armory minFoodDays exp expDeaths linkedEarly crafted recruits");
        foreach (var r in runs)
            sb.AppendLine($"{r.Seed,4} {r.FinalPopulation,4} {r.StarvationDeaths,5} {r.ExpeditionDeaths,7} {r.OtherDeaths,5} {r.Departures,4} {r.Births,6} " +
                          $"{D(r.FirstPregnancyDay)}   {D(r.FirstBirthDay)}    {D(r.HouseAffordableDay)}   {D(r.HouseBuiltDay)}  {D(r.TownHall2Day)} {D(r.WorkshopDay)} {D(r.ArmoryDay)}  {r.MinFoodDays,6:0.0}     {r.Expeditions,3} {r.ExpeditionsWithDeaths,5} {r.LinkedEncountersEarly,10} {r.CraftedItems,7} {r.Recruits,5}");
        double? Median(Func<PlaythroughSummary, double?> f)
        {
            var v = runs.Select(f).Where(x => x is not null).Select(x => x!.Value).OrderBy(x => x).ToList();
            return v.Count == 0 ? null : v[v.Count / 2];
        }
        sb.AppendLine($"median: pop {Median(r => r.FinalPopulation):0}, first pregnancy day {D(Median(r => r.FirstPregnancyDay))}, first birth day {D(Median(r => r.FirstBirthDay))}, " +
                      $"house affordable {D(Median(r => r.HouseAffordableDay))}, TH2 {D(Median(r => r.TownHall2Day))}, min food days {Median(r => r.MinFoodDays):0.0}");
        sb.AppendLine($"totals: starvation deaths {runs.Sum(r => r.StarvationDeaths)}, expedition deaths {runs.Sum(r => r.ExpeditionDeaths)} over {runs.Sum(r => r.Expeditions)} expeditions " +
                      $"({runs.Sum(r => r.ExpeditionsWithDeaths)} with deaths), departures {runs.Sum(r => r.Departures)}, births {runs.Sum(r => r.Births)}, " +
                      $"early Linked sightings {runs.Sum(r => r.LinkedEncountersEarly)}, recruits {runs.Sum(r => r.Recruits)}");
        return sb.ToString();
    }
}
