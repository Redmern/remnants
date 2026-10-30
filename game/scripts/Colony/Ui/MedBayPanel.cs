using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Core.Colony;
using Remnants.Core.Survivors;
using Remnants.Game.Hud;

namespace Remnants.Game.Colony.Ui;

/// <summary>
/// Care at the selected Med Bay: patients with their heal ETA (discharge), admitting the injured (worsening wounds
/// marked), the surgery queue (progress, odds, ETA or what blocks it, cancel), and fitting a prosthetic from the
/// inventory onto a limb: crude and mechanical parts strap on, the rest is booked as surgery, and a healthy part is an
/// augmentation behind a confirm. The "Tree" button opens the cybernetics tree. Core decides what is allowed.
/// </summary>
public partial class MedBayPanel : PanelContainer
{
    private readonly ColonySession _session;
    private readonly RichTextLabel _info = Ui.Rich();
    private readonly VBoxContainer _patients = new();
    private readonly OptionButton _admitPick = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill, FocusMode = FocusModeEnum.None, ClipText = true, FitToLongestItem = false };
    private readonly OptionButton _limbPick = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill, FocusMode = FocusModeEnum.None, ClipText = true, FitToLongestItem = false };
    private readonly OptionButton _prostheticPick = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill, FocusMode = FocusModeEnum.None, ClipText = true, FitToLongestItem = false };
    private readonly VBoxContainer _surgeries = Widgets.Column(4);
    private readonly Button _fit;
    private readonly List<int> _admitIds = new();
    private readonly List<(int SurvivorId, BodyPart Part)> _limbs = new();
    private readonly List<string> _prosthetics = new();
    private string _signature = "";
    private string _surgerySignature = "";

    public int? BuildingId { get; private set; }

    /// <summary>"Tree" pressed: open the cybernetics tree on the picked survivor's part (when one is picked).</summary>
    public event Action<int?, BodyPart?>? CyberneticsPressed;

    public MedBayPanel(ColonySession session)
    {
        _session = session;
        CustomMinimumSize = new Vector2(300, 0);
        Visible = false;
        var box = Ui.Panel(this);
        box.AddChild(Ui.Heading("Patients", "job_patient"));
        box.AddChild(_info);
        box.AddChild(_patients);

        box.AddChild(Ui.Heading("Admit", "ui_plus"));
        var admit = new HBoxContainer();
        admit.AddChild(_admitPick);
        admit.AddChild(Widgets.IconButton("job_patient", "Admit", "Give them a Med Bay bed", Admit, variation: "Primary"));
        box.AddChild(admit);

        var tree = Widgets.IconButton("body_prosthetic", "Tree", "The cybernetics tree: designs, components and a comparison with the picked limb",
            () => CyberneticsPressed?.Invoke(PickedLimb()?.SurvivorId, PickedLimb()?.Part), variation: "Flat");
        tree.CustomMinimumSize = new Vector2(0, 26);
        box.AddChild(Widgets.SectionHeader("Surgery", "body_prosthetic", tree));
        box.AddChild(_surgeries);
        box.AddChild(_limbPick);
        var fit = new HBoxContainer();
        fit.AddChild(_prostheticPick);
        _fit = Widgets.IconButton("body_prosthetic", "Fit", "Crude and mechanical parts strap on; powered parts and up are surgery; a healthy limb is an augmentation", Fit, variation: "Primary");
        fit.AddChild(_fit);
        box.AddChild(fit);
        _limbPick.ItemSelected += _ =>
        {
            _signature = "";
            Refresh();
        };
        _prostheticPick.ItemSelected += _ => UpdateFitButton();
    }

    public void Show(int? buildingId)
    {
        BuildingId = buildingId;
        _signature = _surgerySignature = "";
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
                     $"[color=#{medColor}]Medicine {medicine:0.#}[/color] ({rules.Medical.MedicinePerPatientTick:0.###}/patient/h; without it only natural healing)";

        var data = sim.Scavenge!;
        var injured = sim.State.Present.Where(s => s.IsInjured && s.PatientAtBuildingId is null).OrderBy(s => s.Id).ToList();
        // Missing limbs first, then prosthetics (upgrades), then healthy parts (augmentation); never a part already booked.
        var limbs = sim.State.Present.Where(s => !s.IsInfant).OrderBy(s => s.Id)
            .SelectMany(s => s.Body.Parts
                .Where(kv => kv.Key is not (BodyPart.Head or BodyPart.Torso) && kv.Value.Condition != PartCondition.Injured
                             && sim.State.Surgeries.All(o => o.SurvivorId != s.Id || o.Part != kv.Key))
                .Select(kv => (Limb: (s.Id, kv.Key), Rank: kv.Value.Condition switch { PartCondition.Missing => 0, PartCondition.Prosthetic => 1, _ => 2 })))
            .OrderBy(x => x.Rank).Select(x => x.Limb).ToList();
        var picked = PickedLimb();
        if (picked is { } was && !limbs.Contains(was)) picked = null;
        picked ??= limbs.Count > 0 ? limbs[0] : null;
        var region = picked is { } pl ? Body.RegionOf(pl.Part) : (BodyRegion?)null;
        var prosthetics = sim.State.Inventory.Counts.Keys
            .Where(k => data.Items[k].Prosthetic is { } def && (region is null || def.Region == region))
            .OrderBy(k => sim.Data.Cybernetics?.Node(data.Items[k].DefinedId)?.Tier ?? 0).ThenBy(k => k, StringComparer.Ordinal).ToList();
        var signature = string.Join(",", patients.Select(p => p.Id)) + "|" + string.Join(",", injured.Select(s => s.Id)) + "|" +
                        string.Join(",", limbs.Select(l => LimbLabel(sim, sim.State.FindSurvivor(l.Id)!, l.Key))) + "|" + picked + "|" +
                        string.Join(",", prosthetics.Select(p => $"{p}:{sim.State.Inventory.Count(p)}"));
        if (signature != _signature)
        {
            _signature = signature;
            RebuildPatients(sim, patients);
            Fill(_admitPick, _admitIds, injured.Select(s => s.Id),
                injured.Select(s => (sim.IsWorsening(s) ? "! " : "") + $"{s.Name}: {WoundSummary(s)}"));
            _limbs.Clear();
            _limbs.AddRange(limbs);
            _limbPick.Clear();
            foreach (var (survivorId, part) in limbs) _limbPick.AddItem(LimbLabel(sim, sim.State.FindSurvivor(survivorId)!, part));
            _limbPick.Select(picked is { } k ? limbs.IndexOf(k) : -1);
            Fill(_prostheticPick, _prosthetics, prosthetics, prosthetics.Select(p =>
            {
                var def = data.Items[p].Prosthetic!;
                var tier = sim.Data.Cybernetics?.Node(data.Items[p].DefinedId) is { } node ? $"T{node.Tier}, " : "";
                return $"{data.Items[p].Name} ×{sim.State.Inventory.Count(p)} ({tier}{ColonyText.Percent(def.Quality)}, Med Bay L{def.MinMedBayLevel})";
            }));
            UpdateFitButton();
        }
        foreach (var row in _patients.GetChildren().OfType<HBoxContainer>())
            if (row.GetMeta("survivor").AsInt32() is var sid && sim.State.FindSurvivor(sid) is { } p)
                row.GetChild<Label>(1).Text = PatientLine(sim, p);
        RefreshSurgeries(sim, bay);
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
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            row.AddChild(label);
            var portrait = new Portrait(34);
            portrait.Set(PortraitStudio.For(GetTree(), p), PortraitStudio.Backdrop(p.Id));
            row.AddChild(portrait);
            row.MoveChild(portrait, 0);
            var id = p.Id;
            row.AddChild(Widgets.IconButton("ui_quit", "", "Discharge", () => _session.Execute(new Discharge(id)), variation: "Flat"));
            _patients.AddChild(row);
        }
    }

    private static string PatientLine(ColonySim sim, Survivor s)
    {
        if (!s.IsInjured && sim.State.Surgeries.Any(o => o.SurvivorId == s.Id)) return $"{s.Name}: waiting for surgery, HP {s.Health:0}";
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

    private static string LimbLabel(ColonySim sim, Survivor s, BodyPart part)
    {
        var p = s.Body[part];
        var what = p.Condition switch
        {
            PartCondition.Missing => "missing",
            PartCondition.Prosthetic => sim.Scavenge?.Items.GetValueOrDefault(p.ProstheticItemId ?? "")?.Name ?? "prosthetic",
            _ => "healthy",
        };
        return $"{s.Name}: {CyberText.Part(part).ToLowerInvariant()} ({what})";
    }

    private static void Fill<T>(OptionButton picker, List<T> ids, IEnumerable<T> newIds, IEnumerable<string> labels)
    {
        ids.Clear();
        ids.AddRange(newIds);
        picker.Clear();
        foreach (var label in labels) picker.AddItem(label);
        picker.Select(ids.Count > 0 ? 0 : -1);
    }

    private (int SurvivorId, BodyPart Part)? PickedLimb() =>
        _limbPick.Selected >= 0 && _limbPick.Selected < _limbs.Count ? _limbs[_limbPick.Selected] : null;

    private void UpdateFitButton()
    {
        var limb = PickedLimb();
        var ok = limb is not null && _prostheticPick.Selected >= 0 && _prostheticPick.Selected < _prosthetics.Count;
        _fit.Disabled = !ok;
        _fit.Text = ok ? CyberBooking.Verb(_session.Sim, limb!.Value.SurvivorId, limb.Value.Part, _prosthetics[_prostheticPick.Selected]) : "Fit";
    }

    /// <summary>The bay's booked surgeries: who gets what, progress, odds and ETA or what blocks it; cancel each.</summary>
    private void RefreshSurgeries(ColonySim sim, Building bay)
    {
        if (sim.Data.Cybernetics is null) return;
        var ops = sim.State.Surgeries.Where(o => o.BuildingId == bay.Id).ToList();
        var signature = string.Join(",", ops.Select(o => o.Id));
        if (signature != _surgerySignature)
        {
            _surgerySignature = signature;
            Widgets.Clear(_surgeries);
            if (ops.Count == 0) _surgeries.AddChild(Ui.Label("No surgery booked.", color: Ui.Muted));
            foreach (var op in ops)
            {
                var row = Widgets.Row(6);
                row.SetMeta("surgery", op.Id);
                var col = Widgets.Column(2);
                col.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                var text = Ui.Label("", 13);
                text.AutowrapMode = TextServer.AutowrapMode.WordSmart;
                col.AddChild(text);
                col.AddChild(new StatBar(null));
                row.AddChild(col);
                var id = op.Id;
                var cancel = Widgets.IconButton("ui_close", "", "Call it off: the part goes back to the stores", () => _session.Execute(new CancelSurgery(id)), variation: "Flat");
                cancel.SizeFlagsVertical = SizeFlags.ShrinkBegin;
                row.AddChild(cancel);
                _surgeries.AddChild(row);
            }
        }
        var first = true;
        foreach (var row in _surgeries.GetChildren().OfType<HBoxContainer>())
        {
            if (sim.State.Surgeries.Find(o => o.Id == row.GetMeta("surgery").AsInt32()) is not { } op) continue;
            var col = row.GetChild<VBoxContainer>(0);
            var (line, fraction, color) = SurgeryLine(sim, bay, op, first);
            col.GetChild<Label>(0).Text = line;
            col.GetChild<StatBar>(1).Set(fraction, ColonyText.Percent(fraction), color);
            first = false;
        }
    }

    private static (string Line, double Fraction, Color Color) SurgeryLine(ColonySim sim, Building bay, Surgery op, bool first)
    {
        var cyber = sim.Data.Cybernetics!;
        var item = sim.Scavenge!.Items[op.ItemId];
        var tier = cyber.Node(item.DefinedId)?.Tier ?? 0;
        var work = Cybernetics.Work(cyber, tier);
        var who = sim.State.FindSurvivor(op.SurvivorId)?.Name ?? "?";
        var head = $"{who}: {item.Name} → {CyberText.Part(op.Part).ToLowerInvariant()}{(op.Elective ? " (augmentation)" : "")}";
        var odds = $"odds {ColonyText.Percent(Cybernetics.SuccessChance(sim, bay, tier, op.Elective))}";
        var fraction = work <= 0 ? 1 : Math.Clamp(op.Progress / work, 0, 1);
        if (Cybernetics.Blocked(sim, bay, tier) is { } why) return ($"{head}\n⚠ {why} · {odds}", fraction, Palette.Danger);
        if (!first) return ($"{head}\nnext on the table · {odds}", fraction, Palette.TextDim);
        var effort = Cybernetics.EffortPerTick(sim, bay);
        var eta = effort > 0 ? ColonyText.Duration((int)Math.Ceiling((work - op.Progress) / effort), sim.Data.Rules) : "—";
        var noMedicine = sim.State.Stockpile.Get(sim.Data.Rules.Medical.MedicineResourceId) < cyber.Surgery.MedicinePerTick ? " · ⚠ no medicine" : "";
        return ($"{head}\n{odds} · done in {eta}{noMedicine}", fraction, Palette.Amber);
    }

    private void Admit()
    {
        if (BuildingId is { } bay && _admitPick.Selected >= 0) _session.Execute(new AdmitPatient(_admitIds[_admitPick.Selected], bay));
    }

    private void Fit()
    {
        if (PickedLimb() is not { } limb || _prostheticPick.Selected < 0 || _prostheticPick.Selected >= _prosthetics.Count) return;
        CyberBooking.Book(_session, this, limb.SurvivorId, limb.Part, _prosthetics[_prostheticPick.Selected]);
        _signature = "";
    }
}
