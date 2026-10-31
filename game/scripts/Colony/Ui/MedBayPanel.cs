using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Core.Colony;
using Remnants.Core.Data;
using Remnants.Core.Survivors;
using Remnants.Game.Hud;
using Remnants.Game.Hud.Help;

namespace Remnants.Game.Colony.Ui;

/// <summary>
/// Care at the selected Med Bay. A pinned header (beds used, heal rate, medicine and how many days it lasts) over three
/// tabs, one task each: <b>Patients</b> (who is in a bed with their heal ETA, discharge, admitting the injured with
/// worsening wounds marked), <b>Surgery</b> (the queue with progress, odds, ETA or what blocks it, cancel; fitting a
/// prosthetic from the inventory onto a limb: crude and mechanical parts strap on, the rest is booked as surgery, and a
/// healthy part is an augmentation behind a confirm; "Tree" opens the cybernetics tree) and <b>Dispensary</b>
/// (MedBayPanel.Dispensary.cs: the medical stock and the auto-treatment order). Rows are kept by id and updated in place
/// every tick; only a changed set of patients, operations or supplies adds or removes rows. Core decides what is allowed.
/// </summary>
public partial class MedBayPanel : PanelContainer
{
    private readonly ColonySession _session;
    private readonly Unchanged _set = new();
    private readonly PanelTabs _tabs = new();
    private readonly Label _beds = Widgets.Label("", "Number");
    private readonly Label _heal = Widgets.Label("", "Small", Palette.TextMuted);
    private readonly Label _medicine = Widgets.Label("", "Small");
    private readonly VBoxContainer _patients = Widgets.Column(4);
    private readonly KeyedRows<int> _patientRows;
    private readonly Label _noPatients = Widgets.Label("No patients. Admit someone injured below.", "Muted");
    private readonly OptionButton _admitPick = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill, FocusMode = FocusModeEnum.All, ClipText = true, FitToLongestItem = false };
    private readonly Button _admit;
    private readonly OptionButton _limbPick = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill, FocusMode = FocusModeEnum.All, ClipText = true, FitToLongestItem = false };
    private readonly OptionButton _prostheticPick = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill, FocusMode = FocusModeEnum.All, ClipText = true, FitToLongestItem = false };
    private readonly VBoxContainer _surgeries = Widgets.Column(4);
    private readonly KeyedRows<int> _surgeryRows;
    private readonly Label _noSurgery = Widgets.Label("No surgery booked.", "Muted");
    private readonly Button _fit;
    private readonly List<int> _admitIds = new();
    private readonly List<(int SurvivorId, BodyPart Part)> _limbs = new();
    private readonly List<string> _prosthetics = new();
    private string _signature = "";

    public int? BuildingId { get; private set; }

    /// <summary>"Tree" pressed: open the cybernetics tree on the picked survivor's part (when one is picked).</summary>
    public event Action<int?, BodyPart?>? CyberneticsPressed;

    public MedBayPanel(ColonySession session)
    {
        _session = session;
        _patientRows = new KeyedRows<int>(_patients, 1);
        _surgeryRows = new KeyedRows<int>(_surgeries, 1);
        _stockRows = new KeyedRows<string>(_stock, 1);
        _priorityRows = new KeyedRows<TreatPriority>(_priorities);
        CustomMinimumSize = new Vector2(300, 0);
        Visible = false;
        var box = Ui.Panel(this);

        // Pinned header: what this bay holds and what keeps it going, whatever the tab.
        var head = Widgets.Row(6);
        head.AddChild(PanelKit.Tip("BEDS", "med_bay", "Heading"));
        head.AddChild(_beds);
        head.AddChild(Widgets.Spacer());
        _heal.HorizontalAlignment = HorizontalAlignment.Right;
        head.AddChild(_heal);
        box.AddChild(head);
        var med = Widgets.Row(4);
        med.AddChild(Icons.Rect(Icons.Get("res_medicine", 14), 14, Palette.TextMuted));
        med.AddChild(InfoTip.Beside(_medicine, "medicine"));
        box.AddChild(med);
        box.AddChild(_tabs);

        // Patients: who is in a bed, and admitting the next one.
        var patients = Widgets.Column(8);
        _patients.AddChild(_noPatients);
        patients.AddChild(_patients);
        patients.AddChild(PanelKit.Header("Admit", "ui_plus", "injuries"));
        var admit = Widgets.Row(4);
        admit.AddChild(_admitPick);
        _admit = PanelKit.Focusable(Widgets.IconButton("job_patient", "Admit", "Give them a Med Bay bed", Admit, variation: "Primary"));
        admit.AddChild(_admit);
        patients.AddChild(admit);
        _tabs.Add("patients", "Patients", patients, "Who is in a bed, how long until they heal; admit the injured");

        // Surgery: the queue and booking a prosthetic.
        var surgery = Widgets.Column(8);
        var tree = PanelKit.Focusable(Widgets.IconButton("body_prosthetic", "Tree", "The cybernetics tree: designs, components and a comparison with the picked limb",
            () => CyberneticsPressed?.Invoke(PickedLimb()?.SurvivorId, PickedLimb()?.Part), variation: "Flat"));
        tree.CustomMinimumSize = new Vector2(0, 26);
        surgery.AddChild(PanelKit.Header("Booked", "body_prosthetic", "prosthetic_tiers", tree));
        _surgeries.AddChild(_noSurgery);
        surgery.AddChild(_surgeries);
        surgery.AddChild(PanelKit.Header("Fit a prosthetic", "ui_plus", "humanity"));
        surgery.AddChild(_limbPick);
        var fit = Widgets.Row(4);
        fit.AddChild(_prostheticPick);
        _fit = PanelKit.Focusable(Widgets.IconButton("body_prosthetic", "Fit", "Crude and mechanical parts strap on; powered parts and up are surgery; a healthy limb is an augmentation", Fit, variation: "Primary"));
        fit.AddChild(_fit);
        surgery.AddChild(fit);
        _tabs.Add("surgery", "Surgery", surgery, "Booked operations and fitting prosthetics");

        _tabs.Add("dispensary", "Dispensary", BuildDispensary(), "Medical supplies in stock and what the bay treats on its own");

        _limbPick.ItemSelected += _ =>
        {
            _signature = "";
            Refresh();
        };
        _prostheticPick.ItemSelected += _ => UpdateFitButton();
    }

    /// <summary>Opens a tab ("patients", "surgery", "dispensary"), e.g. for debug captures.</summary>
    public void ShowTab(string tab) => _tabs.Select(tab);

    public void Show(int? buildingId)
    {
        if (buildingId != BuildingId)
        {
            _patientRows.Reset();
            _surgeryRows.Reset();
        }
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
        RefreshHeader(sim, bay, patients.Count, level.Patients);

        var data = sim.Scavenge!;
        var injured = sim.State.Present.Where(s => Medicine.NeedsCare(s, sim.Data) && s.PatientAtBuildingId is null).OrderBy(s => s.Id).ToList();
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
        var signature = string.Join(",", injured.Select(s => s.Id)) + "|" +
                        string.Join(",", limbs.Select(l => LimbLabel(sim, sim.State.FindSurvivor(l.Id)!, l.Key))) + "|" + picked + "|" +
                        string.Join(",", prosthetics.Select(p => $"{p}:{sim.State.Inventory.Count(p)}"));
        if (signature != _signature)
        {
            _signature = signature;
            var keep = _admitPick.Selected >= 0 && _admitPick.Selected < _admitIds.Count ? _admitIds[_admitPick.Selected] : (int?)null;
            Fill(_admitPick, _admitIds, injured.Select(s => s.Id),
                injured.Select(s => (sim.IsWorsening(s) ? "! " : "") + $"{s.Name}: {WoundSummary(s)}{ConditionTags(sim, s)}"),
                "No one needs a bed");
            if (keep is { } k && _admitIds.IndexOf(k) is >= 0 and var at) _admitPick.Select(at);
            _limbs.Clear();
            _limbs.AddRange(limbs);
            _limbPick.Clear();
            foreach (var (survivorId, part) in limbs) _limbPick.AddItem(LimbLabel(sim, sim.State.FindSurvivor(survivorId)!, part));
            if (limbs.Count == 0) _limbPick.AddItem("No limb to fit");
            _limbPick.Select(picked is { } l ? limbs.IndexOf(l) : 0);
            Fill(_prostheticPick, _prosthetics, prosthetics, prosthetics.Select(p =>
            {
                var def = data.Items[p].Prosthetic!;
                var tier = sim.Data.Cybernetics?.Node(data.Items[p].DefinedId) is { } node ? $"T{node.Tier}, " : "";
                return $"{data.Items[p].Name} ×{sim.State.Inventory.Count(p)} ({tier}{ColonyText.Percent(def.Quality)}, Med Bay L{def.MinMedBayLevel})";
            }), "No prosthetic for this limb in the stores");
            UpdateFitButton();
        }
        RefreshPatients(sim, patients);
        RefreshSurgeries(sim, bay);
        RefreshDispensary(sim);
        _admitPick.Disabled = _admitIds.Count == 0;
        _admit.Disabled = _admitIds.Count == 0 || patients.Count >= level.Patients;
        _set.Tooltip(_admit, patients.Count >= level.Patients ? "Every bed is taken: discharge someone or upgrade the Med Bay" : "Give them a Med Bay bed");
        _limbPick.Disabled = _limbs.Count == 0;
        _prostheticPick.Disabled = _prosthetics.Count == 0;
        _tabs.SetLabel("patients", $"Patients {patients.Count}/{level.Patients}");
        var ops = sim.State.Surgeries.Count(o => o.BuildingId == bay.Id);
        _tabs.SetLabel("surgery", ops > 0 ? $"Surgery {ops}" : "Surgery");
        _tabs.SetTabVisible("surgery", sim.Data.Cybernetics is not null || _prosthetics.Count > 0);
    }

    /// <summary>"Beds 1/3 · heals 2%/h", and the medicine with how long it lasts at today's patients.</summary>
    private void RefreshHeader(ColonySim sim, Building bay, int patients, int beds)
    {
        var rules = sim.Data.Rules;
        _set.Text(_beds, $"{patients}/{beds}");
        _set.FontColor(_beds, patients >= beds ? Palette.Warn : Palette.Text);
        _set.Text(_heal, $"heals {sim.HealPerTick(bay) * 100:0.##}% severity/h per wound");
        var medicine = sim.State.Stockpile.Get(rules.Medical.MedicineResourceId);
        var perDay = rules.Medical.MedicinePerPatientTick * rules.TicksPerDay * patients;
        var lasts = patients == 0 ? "used only while someone is in a bed"
            : medicine < rules.Medical.MedicinePerPatientTick ? "out: patients heal only naturally"
            : Lasts((int)Math.Min(medicine / Math.Max(rules.Medical.MedicinePerPatientTick * patients, 1e-9), 999 * rules.TicksPerDay), rules);
        _set.Text(_medicine, $"Medicine {Fmt.Amount(medicine)} ({lasts})");
        _set.FontColor(_medicine, patients > 0 && medicine < rules.Medical.MedicinePerPatientTick ? Palette.Danger
            : patients > 0 && medicine < perDay * 2 ? Palette.Warn : Palette.TextMuted);
        _set.Tooltip(_medicine, $"Each patient uses {rules.Medical.MedicinePerPatientTick:0.###} an hour ({perDay:0.##} a day for {patients}). Without it, wounds heal only naturally.");
    }

    private static string Lasts(int ticks, RulesDef rules) =>
        ticks >= 999 * rules.TicksPerDay ? "enough for 999+ days"
        : ticks >= 2 * rules.TicksPerDay ? $"enough for ~{ticks / rules.TicksPerDay} days" : $"enough for ~{ColonyText.Duration(ticks, rules)}";

    private void RefreshPatients(ColonySim sim, List<Survivor> patients)
    {
        _set.Visible(_noPatients, patients.Count == 0);
        _patientRows.Sync(patients.Select(p => p.Id).ToList(), PatientRow, (id, row) =>
        {
            if (sim.State.FindSurvivor(id) is not { } p) return;
            var label = row.GetChild<Label>(1);
            _set.Text(label, PatientLine(sim, p));
            _set.Tooltip(label, PatientTip(sim, p));
        });
    }

    private HBoxContainer PatientRow(int id)
    {
        var row = Widgets.Row(6);
        var portrait = new Portrait(34);
        if (_session.Sim.State.FindSurvivor(id) is { } p) portrait.Set(PortraitStudio.For(GetTree(), p), PortraitStudio.Backdrop(p.Id));
        row.AddChild(portrait);
        var label = Widgets.Wrapped("", "Small", Palette.Text);
        label.MouseFilter = MouseFilterEnum.Stop;
        row.AddChild(label);
        var discharge = PanelKit.Focusable(Widgets.TextButton("Discharge", () => _session.Execute(new Discharge(id)), "Send them home from the bed (healing slows to natural)", "Flat"));
        discharge.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        row.AddChild(discharge);
        return row;
    }

    private static string PatientLine(ColonySim sim, Survivor s)
    {
        if (!s.IsInjured && sim.State.Surgeries.Any(o => o.SurvivorId == s.Id)) return $"{s.Name}: waiting for surgery · HP {s.Health:0}";
        var eta = !s.IsInjured ? (Medicine.NeedsCare(s, sim.Data) ? "being treated" : "healed, discharge them")
            : sim.HealEta(s) is { } ticks ? $"healed in {ColonyText.Duration(ticks, sim.Data.Rules)}"
            : "not healing yet (worsening or untreated)";
        var worst = s.Body.Parts.Where(kv => kv.Value.Condition == PartCondition.Injured).OrderByDescending(kv => kv.Value.Severity).FirstOrDefault();
        var wound = worst.Value is null ? "no wounds" : $"{CyberText.Part(worst.Key).ToLowerInvariant()} {ColonyText.Percent(worst.Value.Severity)}";
        return $"{s.Name}: {wound} · HP {s.Health:0} · {eta}{ConditionTags(sim, s)}";
    }

    private static string PatientTip(ColonySim sim, Survivor s)
    {
        var wounds = s.Body.Parts.Where(kv => kv.Value.Condition == PartCondition.Injured).OrderByDescending(kv => kv.Value.Severity)
            .Select(kv => $"{CyberText.Part(kv.Key)}: {ColonyText.Percent(kv.Value.Severity)} severity").ToList();
        if (sim.IsWorsening(s)) wounds.Add("Worsening: a wound gets worse faster than the bay heals it. Treat the condition first.");
        return wounds.Count == 0 ? s.Name : string.Join("\n", wounds.Prepend(s.Name));
    }

    private static string WoundSummary(Survivor s)
    {
        var wounds = s.Body.Parts.Values.Where(p => p.Condition == PartCondition.Injured).Select(p => p.Severity).ToList();
        if (wounds.Count == 0) return "no wounds";
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

    /// <summary>Fills a picker; with nothing to pick it shows <paramref name="placeholder"/> instead of an empty box.</summary>
    private static void Fill<T>(OptionButton picker, List<T> ids, IEnumerable<T> newIds, IEnumerable<string> labels, string placeholder)
    {
        ids.Clear();
        ids.AddRange(newIds);
        picker.Clear();
        foreach (var label in labels) picker.AddItem(label);
        if (ids.Count == 0) picker.AddItem(placeholder);
        picker.Select(0);
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

    private string _surgerySignature = "";

    /// <summary>The bay's booked surgeries: who gets what, progress, odds and ETA or what blocks it; cancel each.</summary>
    private void RefreshSurgeries(ColonySim sim, Building bay)
    {
        if (sim.Data.Cybernetics is null) return;
        var ops = sim.State.Surgeries.Where(o => o.BuildingId == bay.Id).ToList();
        _set.Visible(_noSurgery, ops.Count == 0);
        var first = true;
        _surgeryRows.Sync(ops.Select(o => o.Id).ToList(), SurgeryRow, (id, row) =>
        {
            if (ops.Find(o => o.Id == id) is not { } op) return;
            var col = row.GetChild<VBoxContainer>(0);
            var (line, fraction, color) = SurgeryLine(sim, bay, op, first);
            _set.Text(col.GetChild<Label>(0), line);
            col.GetChild<StatBar>(1).Set(fraction, ColonyText.Percent(fraction), color);
            first = false;
        });
    }

    private HBoxContainer SurgeryRow(int id)
    {
        var row = Widgets.Row(6);
        var col = Widgets.Column(2);
        col.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        col.AddChild(Widgets.Wrapped("", "Small", Palette.Text));
        col.AddChild(new StatBar(null));
        row.AddChild(col);
        var cancel = PanelKit.Focusable(Widgets.IconButton("ui_close", "", "Call it off: the part goes back to the stores", () => _session.Execute(new CancelSurgery(id)), variation: "Flat"));
        cancel.SizeFlagsVertical = SizeFlags.ShrinkBegin;
        row.AddChild(cancel);
        return row;
    }

    private static (string Line, double Fraction, Color Color) SurgeryLine(ColonySim sim, Building bay, Surgery op, bool first)
    {
        var cyber = sim.Data.Cybernetics!;
        var item = sim.Scavenge!.Items[op.ItemId];
        var tier = cyber.Node(item.DefinedId)?.Tier ?? 0;
        var work = Cybernetics.Work(cyber, tier);
        var who = sim.State.FindSurvivor(op.SurvivorId)?.Name ?? "?";
        var head = $"{who}: {item.Name} → {CyberText.Part(op.Part).ToLowerInvariant()}{(op.Elective ? " (augmentation)" : "")}";
        var odds = $"success {ColonyText.Percent(Cybernetics.SuccessChance(sim, bay, tier, op.Elective))}";
        var fraction = work <= 0 ? 1 : Math.Clamp(op.Progress / work, 0, 1);
        if (Cybernetics.Blocked(sim, bay, tier) is { } why) return ($"{head}\n⚠ {why} · {odds}", fraction, Palette.Danger);
        if (!first) return ($"{head}\nnext on the table · {odds}", fraction, Palette.TextDim);
        var effort = Cybernetics.EffortPerTick(sim, bay);
        var eta = effort > 0 ? $"done in {ColonyText.Duration((int)Math.Ceiling((work - op.Progress) / effort), sim.Data.Rules)}" : "no surgeon on shift";
        var noMedicine = sim.State.Stockpile.Get(sim.Data.Rules.Medical.MedicineResourceId) < cyber.Surgery.MedicinePerTick ? " · ⚠ no medicine" : "";
        return ($"{head}\n{odds} · {eta}{noMedicine}", fraction, Palette.Amber);
    }

    private void Admit()
    {
        if (BuildingId is { } bay && _admitPick.Selected >= 0 && _admitPick.Selected < _admitIds.Count)
            _session.Execute(new AdmitPatient(_admitIds[_admitPick.Selected], bay));
    }

    private void Fit()
    {
        if (PickedLimb() is not { } limb || _prostheticPick.Selected < 0 || _prostheticPick.Selected >= _prosthetics.Count) return;
        CyberBooking.Book(_session, this, limb.SurvivorId, limb.Part, _prosthetics[_prostheticPick.Selected]);
        _signature = "";
    }
}
