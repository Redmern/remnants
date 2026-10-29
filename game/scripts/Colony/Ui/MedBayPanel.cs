using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Core.Colony;
using Remnants.Core.Items;
using Remnants.Core.Survivors;

namespace Remnants.Game.Colony.Ui;

/// <summary>
/// Care at the selected Med Bay: patients with their heal ETA (discharge), admitting the injured (worsening wounds
/// marked), and fitting a prosthetic from the inventory onto a missing limb. Core decides what is allowed.
/// </summary>
public partial class MedBayPanel : PanelContainer
{
    private readonly ColonySession _session;
    private readonly RichTextLabel _info = Ui.Rich();
    private readonly VBoxContainer _patients = new();
    private readonly OptionButton _admitPick = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill, FocusMode = FocusModeEnum.None, ClipText = true };
    private readonly OptionButton _limbPick = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill, FocusMode = FocusModeEnum.None, ClipText = true };
    private readonly OptionButton _prostheticPick = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill, FocusMode = FocusModeEnum.None, ClipText = true };
    private readonly List<int> _admitIds = new();
    private readonly List<(int SurvivorId, BodyPart Part)> _limbs = new();
    private readonly List<string> _prosthetics = new();
    private string _signature = "";

    public int? BuildingId { get; private set; }

    public MedBayPanel(ColonySession session)
    {
        _session = session;
        CustomMinimumSize = new Vector2(300, 0);
        Visible = false;
        var box = Ui.Panel(this);
        box.AddChild(Ui.Heading("Patients"));
        box.AddChild(_info);
        box.AddChild(_patients);

        box.AddChild(Ui.Heading("Admit"));
        var admit = new HBoxContainer();
        admit.AddChild(_admitPick);
        admit.AddChild(Ui.Button("Admit", Admit, 64));
        box.AddChild(admit);

        box.AddChild(Ui.Heading("Fit prosthetic"));
        box.AddChild(_limbPick);
        var fit = new HBoxContainer();
        fit.AddChild(_prostheticPick);
        fit.AddChild(Ui.Button("Fit", Fit, 48));
        box.AddChild(fit);
    }

    public void Show(int? buildingId)
    {
        BuildingId = buildingId;
        _signature = "";
        Refresh();
    }

    public void Refresh()
    {
        var sim = _session.Sim;
        var bay = BuildingId is { } id ? sim.State.FindBuilding(id) : null;
        Visible = bay is not null && bay.DefId == sim.Data.Rules.Medical.BuildingId && sim.Scavenge is not null;
        if (!Visible) return;

        var rules = sim.Data.Rules;
        var level = sim.Data.Buildings[bay!.DefId].Level(bay.Level);
        var patients = sim.State.PatientsAt(bay.Id).OrderBy(s => s.Id).ToList();
        var medicine = sim.State.Stockpile.Get(rules.Medical.MedicineResourceId);
        var medColor = Ui.Hex(medicine >= rules.Medical.MedicinePerPatientTick ? Ui.Good : Ui.Bad);
        _info.Text = $"Beds {patients.Count}/{level.Patients} · heals {sim.HealPerTick(bay) * 100:0.##}% severity/h per wound\n" +
                     $"[color=#{medColor}]Medicine {medicine:0.#}[/color] ({rules.Medical.MedicinePerPatientTick:0.##}/patient/h; without it only natural healing)";

        var data = sim.Scavenge!;
        var injured = sim.State.Present.Where(s => s.IsInjured && s.PatientAtBuildingId is null).OrderBy(s => s.Id).ToList();
        var limbs = sim.State.Present.OrderBy(s => s.Id)
            .SelectMany(s => s.Body.Parts.Where(kv => kv.Value.Condition == PartCondition.Missing).Select(kv => (s.Id, kv.Key)))
            .ToList();
        var prosthetics = sim.State.Inventory.Counts.Keys.Where(k => data.Items[k].Prosthetic is not null).ToList();
        var signature = string.Join(",", patients.Select(p => p.Id)) + "|" + string.Join(",", injured.Select(s => s.Id)) + "|" +
                        string.Join(",", limbs) + "|" + string.Join(",", prosthetics.Select(p => $"{p}:{sim.State.Inventory.Count(p)}"));
        if (signature != _signature)
        {
            _signature = signature;
            RebuildPatients(sim, patients);
            Fill(_admitPick, _admitIds, injured.Select(s => s.Id),
                injured.Select(s => (sim.IsWorsening(s) ? "! " : "") + $"{s.Name}: {WoundSummary(s)}"));
            _limbs.Clear();
            _limbs.AddRange(limbs);
            _limbPick.Clear();
            foreach (var (survivorId, part) in limbs) _limbPick.AddItem($"{sim.State.FindSurvivor(survivorId)!.Name}: missing {part}");
            _limbPick.Select(limbs.Count > 0 ? 0 : -1);
            Fill(_prostheticPick, _prosthetics, prosthetics, prosthetics.Select(p =>
            {
                var def = data.Items[p].Prosthetic!;
                return $"{data.Items[p].Name} ×{sim.State.Inventory.Count(p)} ({def.Region}, {ColonyText.Percent(def.Quality)}, Med Bay L{def.MinMedBayLevel})";
            }));
        }
        foreach (var row in _patients.GetChildren().OfType<HBoxContainer>())
            if (row.GetMeta("survivor").AsInt32() is var sid && sim.State.FindSurvivor(sid) is { } p)
                row.GetChild<Label>(0).Text = PatientLine(sim, p);
        _admitPick.Disabled = _admitIds.Count == 0;
        _limbPick.Disabled = _limbs.Count == 0;
        _prostheticPick.Disabled = _prosthetics.Count == 0;
    }

    private void RebuildPatients(ColonySim sim, List<Survivor> patients)
    {
        foreach (var child in _patients.GetChildren()) child.QueueFree();
        if (patients.Count == 0) _patients.AddChild(Ui.Label("No patients.", color: Ui.Muted));
        foreach (var p in patients)
        {
            var row = new HBoxContainer();
            row.SetMeta("survivor", p.Id);
            var label = Ui.Label(PatientLine(sim, p), 13);
            label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            row.AddChild(label);
            var id = p.Id;
            row.AddChild(Ui.Button("Discharge", () => _session.Execute(new Discharge(id)), 80));
            _patients.AddChild(row);
        }
    }

    private static string PatientLine(ColonySim sim, Survivor s)
    {
        var eta = sim.HealEta(s) is { } ticks ? ColonyText.Duration(ticks, sim.Data.Rules) : "—";
        var worst = s.Body.Parts.Where(kv => kv.Value.Condition == PartCondition.Injured).OrderByDescending(kv => kv.Value.Severity).FirstOrDefault();
        var wound = worst.Value is null ? "healed" : $"{worst.Key} {ColonyText.Percent(worst.Value.Severity)}";
        return $"{s.Name}: {wound}, HP {s.Health:0}, ETA {eta}";
    }

    private static string WoundSummary(Survivor s)
    {
        var wounds = s.Body.Parts.Values.Where(p => p.Condition == PartCondition.Injured).Select(p => p.Severity).ToList();
        return wounds.Count == 1 ? $"1 wound ({ColonyText.Percent(wounds[0])})" : $"{wounds.Count} wounds, worst {ColonyText.Percent(wounds.Max())}";
    }

    private static void Fill<T>(OptionButton picker, List<T> ids, IEnumerable<T> newIds, IEnumerable<string> labels)
    {
        ids.Clear();
        ids.AddRange(newIds);
        picker.Clear();
        foreach (var label in labels) picker.AddItem(label);
        picker.Select(ids.Count > 0 ? 0 : -1);
    }

    private void Admit()
    {
        if (BuildingId is { } bay && _admitPick.Selected >= 0) _session.Execute(new AdmitPatient(_admitIds[_admitPick.Selected], bay));
    }

    private void Fit()
    {
        if (_limbPick.Selected < 0 || _prostheticPick.Selected < 0) return;
        var (survivorId, part) = _limbs[_limbPick.Selected];
        _session.Execute(new FitProsthetic(survivorId, part, _prosthetics[_prostheticPick.Selected]));
    }
}
