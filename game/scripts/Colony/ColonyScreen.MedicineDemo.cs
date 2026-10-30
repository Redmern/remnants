using System.Linq;
using Remnants.Core.Colony;
using Remnants.Core.Data;
using Remnants.Core.Survivors;

namespace Remnants.Game.Colony;

/// <summary>
/// `--medicine-demo` (fresh game only): a Med Bay level 2 with a medic, the dispensary at work (antibiotics, dressings, a
/// blood bag) and three patients: an infected arm with a broken leg, food sickness, and a severe wound in pain. The Med
/// Bay and the infected survivor are selected (`--survivor-tab=2` for their Body tab). Debug only.
/// </summary>
public partial class ColonyScreen
{
    private void RunMedicineDemo()
    {
        var sim = _session.Sim;
        if (sim.Data.Medicine is not { } med || sim.Scavenge is null) return;
        foreach (var res in sim.Data.Resources.Keys) sim.State.Stockpile.Add(res, 200);
        var hall = sim.State.Buildings.First(b => b.DefId == sim.Data.Rules.TownHallBuildingId).Id;
        _session.Execute(new Upgrade(hall));
        int? Build(string defId, int plot, int level)
        {
            if (_session.Execute(new Construct(defId, plot)).EntityId is not { } id) return null;
            for (var l = 1; l < level; l++) _session.Execute(new Upgrade(id));
            return id;
        }
        var bay = Build("med_bay", 8, 2);
        Build("kitchen", 9, 1);
        Build("house", 14, 1);
        Build("farm", 15, 1);
        foreach (var id in new[] { "sch_field_dressings", "sch_painkillers", "sch_antibiotics", "sch_blood_transfusion", "sch_combat_stims" })
            Remnants.Core.Schematics.Schematics.Grant(sim, id, Remnants.Core.Schematics.SchematicSource.Debug);
        foreach (var (id, n) in new[] { ("bandage@fine", 3), ("bandage", 2), ("antiseptic", 2), ("splint@crude", 1), ("painkiller", 2), ("blood_bag", 1), ("stim", 2) })
            sim.State.Inventory.Add(id, n);

        var adults = sim.State.Survivors.Where(s => !s.IsChild).ToList();
        if (adults.Count < 3 || bay is not { } medBay) return;
        // Debug only: a seasoned medic, and three people who had a bad week.
        var medic = adults[0];
        medic.Set(Skill.Medicine, 40);
        medic.Perks.UnionWith(["field_medic", "pharmacist"]);
        _session.Execute(new AssignJob(medic.Id, medBay));

        var infected = adults[1];
        infected.Body.Injure(BodyPart.LeftArm, 0.45);
        Treatment.Afflict(infected.Body, med.Infection.Condition, 0.55, BodyPart.LeftArm);
        infected.Body.Injure(BodyPart.LeftLeg, 0.5);
        Treatment.Fracture(med, infected.Body, BodyPart.LeftLeg);
        infected.Health = 78;
        var sick = adults[2];
        Sickness.Apply(sim.State, sim.Data, sick, "food", 0.5);
        if (adults.Count > 3) adults[3].Body.Injure(BodyPart.Torso, 0.65);

        _session.Execute(new QueueRecipe(medBay, "antibiotics", 2));
        _session.Execute(new QueueRecipe(medBay, "clean_dressing", Repeat: true));
        _session.Execute(new QueueRecipe(medBay, "blood_bag"));
        _session.Execute(new SetCarePriorities([TreatPriority.Bleeding, TreatPriority.Infection, TreatPriority.Fracture, TreatPriority.Sickness]));
        _session.Execute(new AdmitPatient(infected.Id, medBay));
        _session.Execute(new AdmitPatient(sick.Id, medBay));
        _session.FastForward(3, _ => false);
        // Stocked after the bay's first treatments, so the Body tab offers it as an override.
        sim.State.Inventory.Add("antibiotics@crude");
        _hud.Roster.Select(infected.Id);
        _town.Select(medBay);
        _session.SetSpeed(0);
        // `--dispensary`: the supplies, the auto-treat order and the recipe queue instead of the building card.
        if (DebugCapture.HasFlag("--dispensary"))
        {
            _hud.Roster.Select(null);
            _hud.Log.ToggleCollapsed();
            GetTree().CreateTimer(0.5).Timeout += () => _hud.ScrollRightTo(_hud.MedBay.Dispensary);
        }
    }
}
