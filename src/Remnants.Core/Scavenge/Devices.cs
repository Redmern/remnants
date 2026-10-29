using Remnants.Core.Items;

namespace Remnants.Core.Scavenge;

/// <summary>
/// Field effects of equipped devices (<see cref="ItemDef.Device"/>). A device works while its carrier is active and
/// meets the item's body requirement.
/// <list type="bullet">
/// <item>Jammer: a Linked unit within the radius (Chebyshev) of a hostile carrier drops off the <see cref="Network"/>: it
/// neither shares nor receives what the squad sees, does not coordinate (cover, flanking, focus fire, suppression) and
/// fights like a Husk. Pylons in the radius go offline, so they neither relay nor call reinforcements.</item>
/// <item>Scanner: its sight bonus applies through the profile; <see cref="ScannedBy"/> is the hook for showing caches and
/// units through walls, which the scavenge view does not use yet.</item>
/// </list>
/// </summary>
public static class Devices
{
    public static bool IsJammed(ScavengeSim sim, Unit unit) =>
        Network.IsLinked(sim.Data, unit) && JamsTile(sim, unit.Pos, unit.Side);

    /// <summary>Whether a jammer carried by someone hostile to <paramref name="side"/> covers the tile.</summary>
    public static bool JamsTile(ScavengeSim sim, Pos tile, string side) =>
        Carriers(sim, DeviceEffect.Jammer).Any(c =>
            c.Unit.Side != side && c.Unit.Side != Unit.NeutralSide && c.Unit.Pos.Chebyshev(tile) <= c.Device.Radius);

    /// <summary>Tiles within a scanner's radius of this unit (empty without a working scanner).</summary>
    public static IEnumerable<Pos> ScannedBy(ScavengeSim sim, Unit unit)
    {
        var radius = Working(sim, unit).Where(d => d.Effect == DeviceEffect.Scanner).Select(d => d.Radius).DefaultIfEmpty(0).Max();
        if (radius == 0) yield break;
        for (var dy = -radius; dy <= radius; dy++)
            for (var dx = -radius; dx <= radius; dx++)
            {
                var p = new Pos(unit.Pos.X + dx, unit.Pos.Y + dy);
                if (sim.State.Map.InBounds(p)) yield return p;
            }
    }

    private static IEnumerable<(Unit Unit, DeviceDef Device)> Carriers(ScavengeSim sim, DeviceEffect effect) =>
        sim.State.Units.Where(u => u.IsActive)
            .SelectMany(u => Working(sim, u).Where(d => d.Effect == effect).Select(d => (u, d)));

    private static IEnumerable<DeviceDef> Working(ScavengeSim sim, Unit unit) =>
        unit.Equipment.Values.Select(id => sim.Data.Items[id])
            .Where(i => i.Device is not null && ProfileDeriver.MeetsRequirement(unit.Body, i.Requires))
            .Select(i => i.Device!);
}
