using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Core.Colony;
using Remnants.Core.Schematics;
using Remnants.Core.Survivors;
using Remnants.Game.Hud;

namespace Remnants.Game.Colony.Ui;

/// <summary>
/// Research at the Town Hall (shown while it is selected): the running research with its researcher, progress, ETA
/// (<see cref="Research.Eta"/>) and Cancel (refunds the Research Data); a researcher picker (adults at home, best
/// <see cref="Research.Factor"/> first, idle ones marked); and every heard-of schematic with its cost, the Town Hall
/// level it needs and a Start button, or Core's reason why not (<see cref="Research.Check"/>).
/// </summary>
public partial class ResearchPanel : PanelContainer
{
    private readonly ColonySession _session;
    private readonly MarginContainer _data = new();
    private readonly VBoxContainer _running = Widgets.Column(4);
    private readonly ProgressBar _bar = new() { ShowPercentage = false, CustomMinimumSize = new Vector2(0, 8), MaxValue = 1 };
    private readonly Label _runningText = Widgets.Wrapped("", "Small");
    private readonly Label _runningTitle = Widgets.Label("", "", Palette.Text, 15);
    private readonly Portrait _portrait = new(38);
    private readonly OptionButton _researcher = new()
    {
        SizeFlagsHorizontal = SizeFlags.ExpandFill, FocusMode = FocusModeEnum.All, ClipText = true, FitToLongestItem = false,
    };
    private readonly List<int> _researcherIds = new();
    private readonly VBoxContainer _options = Widgets.Column(4);
    private readonly Label _pickerLabel = Widgets.Label("Researcher", "Small");
    private readonly KeyedRows<string> _rows;
    private readonly Label _empty = Widgets.Wrapped("Nothing to research yet. A schematic becomes researchable once the colony has heard of it: "
        + "in the trader's stock, on a terminal, dropped by an enemy you left behind.", "Muted");
    private readonly Label _busy = Widgets.Wrapped("One research at a time: the rest can start when this one is done, or cancel it.", "Small", Palette.Warn);
    private readonly Unchanged _ui = new();
    private string _dataSignature = "";
    private int? _portraitFor;

    public int? BuildingId { get; private set; }

    public ResearchPanel(ColonySession session)
    {
        _session = session;
        CustomMinimumSize = new Vector2(300, 0);
        Visible = false;
        var box = Ui.Panel(this);
        var dataRow = Widgets.Row(4);
        dataRow.AddChild(_data);
        dataRow.AddChild(new Hud.Help.InfoTip("research"));
        box.AddChild(Widgets.SectionHeader("Research", "ui_research", dataRow));

        var card = new PanelContainer { ThemeTypeVariation = "CardSelected" };
        var row = Widgets.Row(8);
        card.AddChild(row);
        row.AddChild(_portrait);
        var col = Widgets.Column(2);
        col.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _runningTitle.AddThemeFontOverride("font", Fonts.BodyBold);
        _runningTitle.ClipText = true;
        col.AddChild(_runningTitle);
        _bar.AddThemeStyleboxOverride("fill", HudTheme.Box(Palette.Amber, radius: 2, padX: 0, padY: 0));
        col.AddChild(_bar);
        col.AddChild(_runningText);
        row.AddChild(col);
        var cancel = PanelKit.Focusable(Widgets.IconButton("ui_close", "", "Stop the research: the Research Data comes back.", () => _session.Execute(new CancelResearch()), variation: "Flat"));
        cancel.SizeFlagsVertical = SizeFlags.ShrinkBegin;
        row.AddChild(cancel);
        _running.AddChild(card);
        box.AddChild(_running);

        box.AddChild(PanelKit.Header("Heard of", "item_schematic", "schematics"));
        var picker = Widgets.Row(6);
        picker.AddChild(Hud.Help.InfoTip.Beside(_pickerLabel, "research"));
        picker.AddChild(_researcher);
        box.AddChild(picker);
        _researcher.ItemSelected += _ => Refresh();
        _options.AddChild(_empty);
        _options.AddChild(_busy);
        _rows = new KeyedRows<string>(_options, offset: 2);
        box.AddChild(_options);
    }

    public void Show(int? buildingId)
    {
        BuildingId = buildingId;
        _dataSignature = "";
        Refresh();
    }

    public void Refresh()
    {
        var sim = _session.Sim;
        var building = BuildingId is { } id ? sim.State.FindBuilding(id) : null;
        Visible = building is not null && sim.Data.Schematics.All.Count > 0 && building.DefId == sim.Data.Schematics.Rules.Research.Building;
        if (!Visible) return;

        var data = Math.Floor(SchematicText.ResearchData(sim)).ToString();
        if (data != _dataSignature)
        {
            _dataSignature = data;
            Widgets.Clear(_data);
            _data.AddChild(SchematicText.DataChip(sim));
        }
        RefreshRunning(sim);
        RefreshResearchers(sim);
        RefreshOptions(sim);
    }

    private void RefreshRunning(ColonySim sim)
    {
        var order = sim.State.Schematics.Research;
        _running.Visible = order is not null;
        if (order is null) return;
        var def = sim.Data.Schematics.Find(order.SchematicId);
        var who = sim.State.FindSurvivor(order.ResearcherId);
        _runningTitle.Text = def?.Name ?? order.SchematicId;
        _runningTitle.TooltipText = def is null ? "" : SchematicText.Tooltip(sim, def);
        var done = order.Work > 0 ? order.Progress / order.Work : 0;
        Ui.Glide(_bar, done, _session);
        var eta = Research.Eta(sim) is { } t ? ColonyText.Duration(t, sim.Data.Rules) : "—";
        var where = who is null ? "" : who.Away ? " (away: paused)" : who.PatientAtBuildingId is not null ? " (a patient: paused)" : "";
        _runningText.Text = $"{who?.Name ?? "Nobody"}{where} · speed ×{(who is null ? 0 : Research.Factor(sim, who)):0.00} · {Fmt.Percent(done)} · ETA {eta}";
        _runningText.TooltipText = "Speed: the researcher's factor (base + Intellect + Electronics); higher is faster. Works on their day shift only.";
        if (who is not null && _portraitFor != who.Id)
        {
            _portraitFor = who.Id;
            _portrait.Set(PortraitStudio.For(GetTree(), who), PortraitStudio.Backdrop(who.Id));
        }
    }

    /// <summary>Adults at home who could research now, best researchers first; idle ones say so, workers leave their job.</summary>
    private void RefreshResearchers(ColonySim sim)
    {
        var candidates = sim.State.Present
            .Where(s => !s.IsChild && !s.IsInfant && s.PatientAtBuildingId is null && sim.ClassOf(s) is null && !Research.IsResearching(sim.State, s.Id))
            .OrderByDescending(s => Research.Factor(sim, s)).ThenBy(s => s.AssignedBuildingId is null ? 0 : 1).ThenBy(s => s.Id).ToList();
        var labels = candidates.Select(s =>
        {
            var job = s.AssignedBuildingId is { } b && sim.State.FindBuilding(b) is { } at ? $"leaves the {sim.Data.Buildings[at.DefId].Name}" : "idle";
            return $"{s.Name}  speed ×{Research.Factor(sim, s):0.00}, {job}";
        }).ToList();
        var ids = candidates.Select(s => s.Id).ToList();
        if (!_researcherIds.SequenceEqual(ids))
        {
            var previous = _researcher.Selected >= 0 && _researcher.Selected < _researcherIds.Count ? _researcherIds[_researcher.Selected] : (int?)null;
            _researcherIds.Clear();
            _researcherIds.AddRange(ids);
            _researcher.Clear();
            foreach (var label in labels) _researcher.AddItem(label);
            var index = previous is { } p ? ids.IndexOf(p) : -1;
            _researcher.Select(index >= 0 ? index : ids.Count > 0 ? 0 : -1);
        }
        else
            for (var i = 0; i < labels.Count; i++)
                if (_researcher.GetItemText(i) != labels[i]) _researcher.SetItemText(i, labels[i]);
        _researcher.Disabled = ids.Count == 0;
        _researcher.TooltipText = "Speed ×N: the research factor (base + Intellect + Electronics), best first. The researcher leaves their job while researching.";
    }

    private Survivor? SelectedResearcher(ColonySim sim) =>
        _researcher.Selected >= 0 && _researcher.Selected < _researcherIds.Count ? sim.State.FindSurvivor(_researcherIds[_researcher.Selected]) : null;

    private void RefreshOptions(ColonySim sim)
    {
        var running = sim.State.Schematics.Research?.SchematicId;
        var heard = sim.State.Schematics.HeardOf.Keys.Where(id => id != running).Select(sim.Data.Schematics.Find).OfType<SchematicDef>()
            .OrderBy(d => d.Tier).ThenBy(d => d.Name, StringComparer.Ordinal).ToList();
        var researcher = SelectedResearcher(sim);
        _pickerLabel.Visible = _researcher.Visible = heard.Count > 0;
        _ui.Visible(_empty, heard.Count == 0);
        _ui.Visible(_busy, running is not null && heard.Count > 0);
        var rules = sim.Data.Schematics.Rules.Research;
        _rows.Sync(heard.Select(d => d.Id).ToList(), id => new ResearchOptionRow(sim, sim.Data.Schematics.Find(id)!, () => Start(id)), (id, row) =>
        {
            var def = sim.Data.Schematics.Find(id)!;
            var check = Research.Check(sim, id);
            var researchable = def.Has(SchematicSource.Research) && SchematicCatalog.At(rules.Cost, def.Tier) > 0;
            var cost = SchematicCatalog.At(rules.Cost, def.Tier);
            var hall = SchematicCatalog.At(rules.TownHallLevel, def.Tier);
            var needs = rules.Needs.Count > 0 ? SchematicCatalog.At(rules.Needs, def.Tier) : new Dictionary<string, int>();
            var needText = string.Concat(needs.Select(kv => $", {sim.Data.Buildings.GetValueOrDefault(kv.Key)?.Name ?? kv.Key} L{kv.Value}"));
            _ui.Text(row.Line, researchable
                ? $"Tier {def.Tier} · Town Hall L{hall}{needText} · {Estimate(sim, def, researcher)}"
                : $"Tier {def.Tier}: cannot be researched");
            var why = !check.Ok && check.Error != CommandError.ResearchBusy && check.Error != CommandError.CannotAfford ? "⚠ " + check.Message : "";
            _ui.Text(row.Why, why);
            _ui.Visible(row.Why, why.Length > 0);
            _ui.Visible(row.Cost, researchable);
            _ui.Visible(row.Start, researchable);
            if (!researchable) return;
            var costKey = $"{cost}|{Math.Floor(SchematicText.ResearchData(sim))}";
            if (row.CostShown != costKey)
            {
                row.CostShown = costKey;
                Widgets.Clear(row.Cost);
                row.Cost.AddChild(SchematicText.DataChip(sim, cost));
            }
            var busy = check.Error == CommandError.ResearchBusy;
            if (row.Start.Text != (busy ? "Busy" : "Start")) row.Start.Text = busy ? "Busy" : "Start";
            var disabled = !check.Ok || researcher is null;
            if (row.Start.Disabled != disabled) row.Start.Disabled = disabled;
            _ui.Tooltip(row.Start, busy ? "One research at a time: this can start when the running one is done (or cancelled)."
                : researcher is null ? "Pick a researcher first."
                : check.Ok ? $"{researcher.Name} starts on it ({cost:0} Research Data)." : check.Message);
        });
    }

    /// <summary>"about 4d with Juno" at the researcher's factor, on their day shift.</summary>
    private static string Estimate(ColonySim sim, SchematicDef def, Survivor? researcher)
    {
        var work = SchematicCatalog.At(sim.Data.Schematics.Rules.Research.Work, def.Tier);
        var factor = researcher is null ? sim.Data.Schematics.Rules.Research.BaseFactor : Research.Factor(sim, researcher);
        var rules = sim.Data.Rules;
        var shift = rules.DayNight.Enabled ? DayNight.WindowLength(rules.DayNight.WorkHours, rules.TicksPerDay) : rules.TicksPerDay;
        var ticks = (int)Math.Ceiling(work / Math.Max(0.01, factor) * rules.TicksPerDay / Math.Max(1, shift));
        return $"about {ColonyText.Duration(ticks, rules)}{(researcher is null ? "" : $" with {researcher.Name}")}";
    }

    private void Start(string schematicId)
    {
        if (SelectedResearcher(_session.Sim) is { } who) _session.Execute(new StartResearch(schematicId, who.Id));
    }
}

/// <summary>One heard-of schematic's row, built once and updated in place.</summary>
public partial class ResearchOptionRow : PanelContainer
{
    public readonly Label Line = Widgets.Wrapped("", "Small");
    public readonly Label Why = Widgets.Wrapped("", "Small", Palette.Danger);
    public readonly MarginContainer Cost = new();
    public readonly Button Start;
    public string CostShown = "";

    public ResearchOptionRow() : this(null!, null!, null!) { }

    public ResearchOptionRow(ColonySim sim, SchematicDef def, System.Action start)
    {
        ThemeTypeVariation = "Card";
        if (def is null) { Start = new Button(); return; }
        TooltipText = SchematicText.Tooltip(sim, def);
        var row = Widgets.Row(8);
        AddChild(row);
        row.AddChild(Icons.Rect(Icons.Get(SchematicText.Icon(def), 20), 20, SchematicText.Accent(def)));
        var col = Widgets.Column(2);
        col.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var name = Widgets.Label(def.Name, "", Palette.Text, 14);
        name.AddThemeFontOverride("font", Fonts.BodyBold);
        name.ClipText = true;
        col.AddChild(name);
        col.AddChild(Line);
        col.AddChild(Why);
        row.AddChild(col);
        var right = Widgets.Column(4);
        right.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        right.AddChild(Cost);
        Start = PanelKit.Focusable(Widgets.IconButton("ui_research", "Start", "", start, variation: "Primary"));
        Start.CustomMinimumSize = new Vector2(84, 28);
        right.AddChild(Start);
        row.AddChild(right);
    }
}
