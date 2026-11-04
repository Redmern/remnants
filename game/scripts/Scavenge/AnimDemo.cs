using System.Linq;
using Remnants.Core.Scavenge;
using Remnants.Core.Survivors;

namespace Remnants.Game.Scavenge;

/// <summary>
/// Debug: <c>--anim-demo=survivor|enemy</c> stages the debug mission for the animation captures (with
/// <see cref="Anim.FrameRecorder"/>'s <c>--record</c>). Everything hostile goes; then <c>survivor</c>: the first survivor up
/// carries a hunting rifle and walks on a peg leg, a sturdy Husk waits a few tiles off for a butt strike and an aimed
/// shot; <c>cyborg</c>: the same Husk, faced by a survivor on jumper legs with a gun arm and a thermal eye (one mesh per
/// part from the cybernetics data); <c>enemy</c>: a Husk and a Linked hunt the party from just inside its sight, so ending the turn shows them
/// walk up and attack.
/// </summary>
public static class AnimDemo
{
    public static string? Mode => DebugCapture.Value("--anim-demo");
    public static bool Requested => Mode is { Length: > 0 };

    public static void Stage(ScavengeSim sim, int playerId)
    {
        var state = sim.State;
        var map = state.Map;
        foreach (var u in state.Units.Where(u => u.PlayerId is null && u.Kind is UnitKind.Enemy or UnitKind.Device)) u.Status = UnitStatus.Dead;
        state.Devices.Clear();
        var acting = state.CurrentUnit!;
        bool Free(Pos p) => map.InBounds(p) && map[p] == Terrain.Floor && state.UnitAt(p) is null && !map.IsExit(p) && state.Caches.All(c => c.Pos != p);
        Pos Near(Pos from, int min, int max) => map.AllPositions.Where(p => Free(p) && p.Chebyshev(from) >= min && p.Chebyshev(from) <= max)
            .OrderBy(p => p.Chebyshev(from)).ThenBy(map.Index).First();

        switch (Mode)
        {
            case "survivor":
                acting.Equipment[Remnants.Core.Items.EquipSlot.Weapon] = "rifle";
                acting.Body.Lose(BodyPart.LeftLeg);
                acting.Body.FitProsthetic(BodyPart.LeftLeg, 0.6);
                var husk = NewScavenge.EnemyUnit(sim.Data, state, sim.Data.Enemies["husk"], Near(acting.Pos, 4, 4));
                husk.Hp = husk.MaxHp = 999;
                husk.CanBeDowned = false;
                state.Units.Add(husk);
                break;
            case "cyborg":
                acting.Equipment.Remove(Remnants.Core.Items.EquipSlot.Weapon);
                foreach (var (part, item) in new[] { (BodyPart.RightArm, "gun_arm"), (BodyPart.LeftLeg, "jumper_leg"), (BodyPart.RightLeg, "jumper_leg"), (BodyPart.Eyes, "thermal_eye") })
                    acting.Body.Fit(part, 1.2, item);
                var target = NewScavenge.EnemyUnit(sim.Data, state, sim.Data.Enemies["husk"], Near(acting.Pos, 3, 3));
                target.Hp = target.MaxHp = 999;
                target.CanBeDowned = false;
                state.Units.Add(target);
                break;
            case "enemy":
                var sight = (int)sim.Profile(acting).Sight;
                foreach (var kind in new[] { "husk", "linked" })
                {
                    var enemy = NewScavenge.EnemyUnit(sim.Data, state, sim.Data.Enemies[kind], Near(acting.Pos, 3, sight - 1));
                    enemy.Ai.Mode = AiMode.Hunting;
                    enemy.Ai.LastKnown = acting.Pos;
                    state.Units.Add(enemy);
                    state.TurnOrder.Add(enemy.Id);
                    enemy.Ap = sim.Profile(enemy).MaxAp;
                    enemy.Mp = sim.Profile(enemy).MaxMp;
                }
                break;
        }
        Visibility.UpdateExplored(sim.Data, state);
    }
}
