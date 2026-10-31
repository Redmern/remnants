using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Core.Colony;
using Remnants.Core.Raids;
using Remnants.Core.Scavenge;
using Remnants.Game.Colony;

namespace Remnants.Game.Scavenge;

/// <summary>
/// The colony as it looks on a fought raid's map (a <c>colony_</c> map from Core's <see cref="RaidBattle.ColonyMap"/>, cell
/// for cell the defense grid): each building's own model (<c>{type}_l{level}</c>, as in the town) over its 3×3 block
/// instead of wall segments, and the placed defenses with the town's <see cref="DefenseModels"/> — walls and shut gates on
/// their wall tiles, barricades and nests on their cover tiles, towers on their raised tiles, open gates, lights, the
/// alarm and trap markers on the floor (turrets are the map's own turret devices). Floodlights burn at night (their lit
/// ground is the map's flare, see <see cref="Fog.FogOverlay"/>). Purely visual; null for any other map, or when the
/// colony that made it is not the session's.
/// </summary>
public sealed class ColonyBattleDressing
{
    private readonly ModelLibrary _library;
    private readonly ColonySim _sim;
    private readonly RaidData _raids;
    private readonly Dictionary<Pos, Building> _centres = new();
    private readonly HashSet<Pos> _blocks = new();
    private readonly Dictionary<Pos, PlacedDefense> _defenses = new();
    private readonly Dictionary<Pos, ConstructionSite> _sites = new();
    private readonly bool _night;

    private ColonyBattleDressing(ModelLibrary library, ColonySim sim, RaidData raids, DefenseGrid grid, bool night)
    {
        _library = library;
        _sim = sim;
        _raids = raids;
        _night = night;
        foreach (var b in sim.State.Buildings)
        {
            var cells = grid.PlotCells(b.Plot).ToList();
            int x0 = cells.Min(c => c.X), y0 = cells.Min(c => c.Y);
            for (var y = y0 + 1; y < y0 + grid.CellsPerPlot; y++)
                for (var x = x0 + 1; x < x0 + grid.CellsPerPlot; x++) _blocks.Add(new Pos(x, y));
            _centres[new Pos(x0 + 2, y0 + 2)] = b;
        }
        foreach (var d in sim.State.Defense.Defenses) _defenses[new Pos(d.X, d.Y)] = d;
        // New buildings still on the scaffold stand as far as they are built (Core makes them frames or walls).
        foreach (var s in sim.State.Construction.Sites.Where(s => !s.IsUpgrade && s.Progress >= RaidBattle.SiteFrameFrom))
        {
            var cells = grid.PlotCells(s.Plot).ToList();
            int x0 = cells.Min(c => c.X), y0 = cells.Min(c => c.Y);
            if (s.Progress >= RaidBattle.SiteWallsFrom)
                for (var y = y0 + 1; y < y0 + grid.CellsPerPlot; y++)
                    for (var x = x0 + 1; x < x0 + grid.CellsPerPlot; x++) _blocks.Add(new Pos(x, y));
            _sites[new Pos(x0 + 2, y0 + 2)] = s;
        }
    }

    /// <summary>Whether this run is a raid battle on a colony map (also tells the unit models to draw structures as markers).</summary>
    public static bool IsColonyMap(ScavengeState state) => state.Mission?.MapId?.StartsWith("colony_") == true;

    public static ColonyBattleDressing? For(ScavengeState state, ModelLibrary library)
    {
        if (!IsColonyMap(state) || ColonySession.Current?.Sim is not { Raids: { } raids } sim) return null;
        var grid = DefenseGrid.Of(sim, raids);
        if (grid.Width != state.Map.Width || grid.Height != state.Map.Height) return null;
        var phase = DayNight.PhaseAtHour(state.Environment.Hour, sim.Data.Rules.DayNight);
        return new ColonyBattleDressing(library, sim, raids, grid, phase is DayPhase.Night or DayPhase.Dusk);
    }

    /// <summary>
    /// Adds the colony's art for tile <paramref name="p"/> under <paramref name="tile"/> (at the tile's centre); true when the
    /// map's own prop for the tile (wall segment, cover) should be left out.
    /// </summary>
    public bool Dress(Node3D tile, Pos p, Terrain terrain)
    {
        if (_centres.TryGetValue(p, out var b) && terrain != Terrain.Rubble)
        {
            var model = _library.Building(_sim.Data.Buildings[b.DefId], b.Level);
            if (RaidSystem.Damage(_sim.State, b) > 0) DefenseModels.Tint(model, new Color(0.25f, 0.12f, 0.06f, 0.22f));
            tile.AddChild(model);
        }
        if (_sites.TryGetValue(p, out var site) && terrain != Terrain.Rubble)
        {
            var model = _library.Building(_sim.Data.Buildings[site.DefId], 1);
            var height = ConstructionSites.Bounds(model).End.Y;
            BuildReveal.Set(BuildReveal.Apply(model), tile.Position.Y - 0.05f + (height + 0.1f) * (float)site.Progress);
            tile.AddChild(model);
            tile.AddChild(ConstructionSites.Scaffold(ConstructionSites.Bounds(model), height + 0.6f));
        }
        if (_blocks.Contains(p)) return terrain == Terrain.Wall;
        if (!_defenses.TryGetValue(p, out var d) || !_raids.Defenses.TryGetValue(d.DefId, out var def)) return false;
        if (d.Hp <= 0 || def.Kind == DefenseKind.Turret) return false;  // rubble and the turret device draw themselves
        var node = DefenseModels.Build(_library, def, d.Facing);
        if (d.Sprung) DefenseModels.Tint(node, new Color(0.08f, 0.08f, 0.08f, 0.55f));
        if (def.Lights && _night && ThreatModel.Working(_sim, d)) Light(node);
        tile.AddChild(node);
        return terrain is Terrain.Wall or Terrain.Cover;
    }

    /// <summary>The lamp head glows and throws a cold cone of light down on the approach.</summary>
    private static void Light(Node3D node)
    {
        if (node.FindChild("Lamp", true, false) is MeshInstance3D { MaterialOverride: StandardMaterial3D lamp }) lamp.EmissionEnergyMultiplier = 6f;
        node.AddChild(new SpotLight3D
        {
            Position = new Vector3(0, 3.4f, 0.2f), RotationDegrees = new Vector3(-75, 180, 0), LightColor = new Color(0.95f, 0.95f, 0.85f),
            LightEnergy = 4f, SpotRange = 16f, SpotAngle = 50f, ShadowEnabled = false,
        });
    }
}

/// <summary>The structure tags of a colony map (Core's <see cref="RaidBattle"/> constants), for the unit models.</summary>
public static class RaidBattleTags
{
    public const string Hall = RaidBattle.HallTag;
    public const string Mast = RaidBattle.MastTag;
}
