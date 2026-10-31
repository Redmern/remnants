using System.Linq;
using Godot;
using Remnants.Core.Colony;

namespace Remnants.Game.Colony;

/// <summary>
/// Debug scenes for construction sites (smoke run and screenshots). `--construction-demo=MODE`:
/// <list type="bullet">
/// <item>`0`, `50`, `100`: a Workshop site at that share of its work with two builders on it, selected, the camera close
/// (100 finishes on the first tick, so the hand-over and its dust play);</item>
/// <item>`upgrade`: a Woodcutter being upgraded to level 2 at 40%, scaffold round the old model, builders at work;</item>
/// <item>`assign`: place a Farm through the build command, then click "+" on its builder slots and the best candidate as a
/// player would; prints <c>[construction-demo]</c> lines with the result.</item>
/// </list>
/// Add `--site-tip` to show the hover tooltip over the site.
/// </summary>
public partial class ColonyScreen
{
    private void RunConstructionDemo(string mode)
    {
        var sim = _session.Sim;
        foreach (var res in sim.Data.Resources.Keys) sim.State.Stockpile.Add(res, 300);
        // Mid-morning, everyone on the day shift (before any site exists, so no work happens on the way).
        JumpToHour(10);
        var adults = sim.State.Survivors.Where(s => !s.IsChild).ToList();
        if (mode == "levels")
        {
            LevelSheet(DebugCapture.Value("--level-types") ?? "woodcutter,farm,quarry,scrapyard,shelter,workshop");
            return;
        }
        int? id = mode switch
        {
            "upgrade" => UpgradeSite(),
            "assign" => PlaceAndAssign(),
            _ => NewSite(int.TryParse(mode, out var p) ? p : 50),
        };
        if (id is not { } siteId || sim.Site(siteId) is not { } site)
        {
            if (mode != "100") GD.PrintErr($"[construction-demo] no site for '{mode}'");
            return;
        }
        GD.Print($"[construction-demo] {mode}: {sim.Data.Buildings[site.DefId].Name} L{site.Level} at {site.Progress * 100:0}%, " +
                 $"{site.Builders.Count} builders, delivered {site.DeliveredTotal:0}/{site.MaterialsTotal:0}");
        if (DebugCapture.HasFlag("--site-tip")) ShowSiteTip(site.Plot);

        int? NewSite(int percent)
        {
            if (_session.Execute(new Construct("workshop", 8)) is not { Ok: true, EntityId: { } built }) return null;
            Stage(built, percent / 100.0, adults.Take(2));
            return built;
        }

        int? UpgradeSite()
        {
            var cutter = sim.Instantly(() => _session.Execute(new Construct("woodcutter", 8))).EntityId;
            if (cutter is not { } c) return null;
            if (adults.Count > 2) _session.Execute(new AssignJob(adults[2].Id, c));
            var hall = sim.State.Buildings.First(b => b.DefId == sim.Data.Rules.TownHallBuildingId);
            if (hall.Level < 2) sim.Instantly(() => _session.Execute(new Upgrade(hall.Id)));
            if (!_session.Execute(new Upgrade(c)).Ok) return null;
            Stage(c, 0.4, adults.Take(2));
            return c;
        }

        int? PlaceAndAssign()
        {
            if (_session.Execute(new Construct("farm", 8)) is not { Ok: true, EntityId: { } farm }) return null;
            Focus(farm);
            _session.SetSpeed(0);
            // Wait for the panel to lay out, then click "+" on the builder slots and the first candidate.
            GetTree().CreateTimer(0.4).Timeout += () =>
            {
                var slots = _hud.Building.Site.Builders;
                if (!slots.OpenFirstFree() || slots.Picker is not { } picker)
                {
                    GD.PrintErr("[construction-demo] no free builder slot to click");
                    return;
                }
                GetTree().CreateTimer(0.3).Timeout += () =>
                {
                    if (picker.Rows.FirstOrDefault() is not { } row)
                    {
                        GD.PrintErr("[construction-demo] picker is empty");
                        return;
                    }
                    row.EmitSignal(BaseButton.SignalName.Pressed);
                    var builders = _session.Sim.Site(farm)?.Builders.Count ?? 0;
                    if (builders == 1) GD.Print("[construction-demo] assigned a builder to the Farm site");
                    else GD.PrintErr($"[construction-demo] assign failed: {builders} builders");
                    // Let it build: the Farm is a quick shed.
                    _session.FastForward(12, s => s.Site(farm) is null);
                    GD.Print(_session.Sim.State.FindBuilding(farm) is not null
                        ? "[construction-demo] the Farm is built"
                        : $"[construction-demo] the Farm is still at {(_session.Sim.Site(farm)?.Progress ?? 0) * 100:0}%");
                };
            };
            return farm;
        }
    }

    /// <summary>
    /// `--construction-demo=levels [--level-types=a,b,…]`: each type at levels 1, 2 and 3 in a row (columns 0–2 of the plot
    /// grid, a type per row, up to four), the Town Hall moved aside, for judging the level progression of the models.
    /// </summary>
    private void LevelSheet(string types)
    {
        var sim = _session.Sim;
        var cols = sim.Data.Rules.Layout.PlotColumns;
        foreach (var b in sim.State.Buildings.Where(b => b.DefId != sim.Data.Rules.TownHallBuildingId).ToList()) sim.State.Buildings.Remove(b);
        var hall = sim.State.Buildings.First(b => b.DefId == sim.Data.Rules.TownHallBuildingId);
        hall.Level = 3;
        hall.Plot = cols - 1;
        var row = 0;
        foreach (var type in types.Split(',').Take(sim.Data.Rules.Layout.PlotRows))
        {
            if (!sim.Data.Buildings.TryGetValue(type, out var def)) continue;
            for (var level = 1; level <= System.Math.Min(3, def.MaxLevel); level++)
                sim.State.Buildings.Add(new Building { Id = sim.State.NextBuildingId++, DefId = type, Level = level, Plot = row * cols + level - 1 });
            row++;
        }
        _town.Reset();
        _hud.Roster.Select(null);
        _town.Select(null);
        _camera.Focus(_layout.Center(cols + 1) + new Vector3(0, 0, TownLayout.PlotSize * 0.5f));
        _camera.ZoomTo(44);
        _session.SetSpeed(0);
        GD.Print($"[construction-demo] level sheet: {types}");
    }

    /// <summary>Debug staging: a site at <paramref name="share"/> of its work (materials hauled to match), these builders on it, in view.</summary>
    private void Stage(int siteId, double share, System.Collections.Generic.IEnumerable<Remnants.Core.Survivors.Survivor> builders)
    {
        var sim = _session.Sim;
        if (sim.Site(siteId) is not { } site) return;
        var delivered = share >= 1 ? 1 : System.Math.Min(1, share + 0.3);
        foreach (var (res, amount) in site.Materials) site.Delivered[res] = amount * delivered;
        site.Work = share >= 1 ? site.WorkNeeded - 1e-3 : site.WorkNeeded * share;
        foreach (var s in builders) _session.Execute(new AssignBuilder(s.Id, siteId));
        Focus(siteId);
        if (share >= 1) _session.Sim.Tick();
        RefreshAll();
        _walkers.Sync();
        _walkers.PlaceAtWork();
    }

    private void Focus(int buildingId)
    {
        var plot = _session.Sim.Site(buildingId)?.Plot ?? _session.Sim.State.FindBuilding(buildingId)?.Plot;
        if (plot is not { } p) return;
        _hud.Roster.Select(null);
        _town.Select(buildingId);
        _camera.Focus(_layout.Center(p) + new Vector3(0, 0, TownLayout.PlotSize * 0.15f));
        _camera.ZoomTo(26);
    }

    private void ShowSiteTip(int plot) => _siteTip.Pin(plot);
}
