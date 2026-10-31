using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Core.Colony;
using Remnants.Core.Help;
using Remnants.Game.Hud;
using Remnants.Game.Hud.Help;

namespace Remnants.Game.Colony.Ui;

/// <summary>
/// A construction site, hosted by the building panel as its <b>Construction</b> tab (shown while the building is built or
/// upgraded): what is built (or the level an upgrade reaches), the work done as a bar, each material hauled / needed, the
/// builders as worker slots ("+" picks someone; idle helpers are named), the time left at the crew's pace (or why it
/// stands still), and Pause, Priority, Night shift and Cancel. Built once and updated in place; InfoTips on the work,
/// builders, hauling and time. Core decides every command.
/// </summary>
public partial class ConstructionSitePanel : PanelContainer
{
    private readonly ColonySession _session;
    private readonly Unchanged _set = new();
    private readonly Label _title = Widgets.Label("", "Heading");
    private readonly StatBar _work = new("ui_build");
    private readonly HFlowContainer _materials = new();
    private readonly Dictionary<string, (PanelContainer Chip, Label Amount)> _chips = new();
    private string _chipIds = "";
    private readonly Label _buildersLabel = Widgets.Label("", "Heading");
    private readonly WorkerSlots _builders;
    private readonly Label _helpers = Widgets.Wrapped("", "Small", Palette.TextMuted);
    private readonly Label _eta = Widgets.Wrapped("", "");
    private readonly Button _pause;
    private readonly Button _priority;
    private readonly Button _night;
    private readonly Button _cancel;

    public int? BuildingId { get; private set; }

    /// <summary>The builder slots (debug: the smoke run assigns through them).</summary>
    public WorkerSlots Builders => _builders;

    /// <summary>"Open survivor" on a builder's portrait.</summary>
    public event Action<int>? SurvivorRequested;

    public ConstructionSitePanel(ColonySession session)
    {
        _session = session;
        var box = Widgets.Column(Space.Sm);
        AddChild(box);
        box.AddChild(InfoTip.Beside(_title, "construction", Context));
        box.AddChild(_work);
        var materialsHead = Widgets.Label("HAULED", "Heading");
        box.AddChild(InfoTip.Beside(materialsHead, "hauling", Context));
        _materials.AddThemeConstantOverride("h_separation", Space.Xs);
        _materials.AddThemeConstantOverride("v_separation", Space.Xs);
        box.AddChild(_materials);
        box.AddChild(InfoTip.Beside(_buildersLabel, "builders", Context));
        _builders = new WorkerSlots(session)
        {
            Title = sim => BuildingId is { } b && sim.Site(b) is { } site ? $"Build the {sim.Data.Buildings[site.DefId].Name}" : "Build",
            Candidate = (sim, s) => !s.IsInfant && (BuildingId is not { } b || sim.Site(b) is not { } site || !site.Builders.Contains(s.Id)),
            Fit = (sim, s) => sim.Data.Jobs.GetValueOrDefault(sim.Data.Rules.Construction.Job) is { } job ? BuildingPanel.JobFit(job, s) : null,
            Assign = s => new AssignBuilder(s.Id, BuildingId ?? -1),
            Unassign = s => new Unassign(s.Id),
            FreeTip = "Free builder slot: click to pick who builds here (they leave their job).",
        };
        _builders.SurvivorRequested += id => SurvivorRequested?.Invoke(id);
        box.AddChild(_builders);
        box.AddChild(_helpers);
        _eta.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        box.AddChild(InfoTip.Beside(_eta, "upgrade_time", Context));

        var row = Widgets.Row(Space.Sm);
        _pause = Motion.OnPress(Widgets.TextButton("Pause", TogglePause, "Stop work here (builders stand by)"));
        _pause.FocusMode = FocusModeEnum.All;
        _priority = Widgets.TextButton("Priority: Normal", CyclePriority, "Which site idle hands help first (Low: assigned builders only)");
        _priority.FocusMode = FocusModeEnum.All;
        row.AddChild(_pause);
        row.AddChild(_priority);
        box.AddChild(row);
        var row2 = Widgets.Row(Space.Sm);
        _night = Motion.OnPress(Widgets.IconButton("ui_night_shift", "Night shift", "", ToggleNight));
        _night.ToggleMode = true;
        _night.FocusMode = FocusModeEnum.All;
        _cancel = Widgets.IconButton("ui_close", "Cancel site", "", Cancel, variation: "Flat");
        _cancel.FocusMode = FocusModeEnum.All;
        row2.AddChild(_night);
        row2.AddChild(_cancel);
        box.AddChild(row2);
        Visible = false;
    }

    private HelpContext Context()
    {
        var sim = _session.Sim;
        var site = BuildingId is { } id ? sim.Site(id) : null;
        return new HelpContext
        {
            Colony = sim, Site = site, Building = site is null ? null : sim.State.FindBuilding(site.BuildingId),
            Survivor = site?.Builders.Select(b => sim.State.FindSurvivor(b)).FirstOrDefault(s => s is not null),
        };
    }

    public void Show(int? buildingId)
    {
        if (BuildingId != buildingId) _builders.Reset();
        BuildingId = buildingId;
        Refresh();
    }

    public void Refresh()
    {
        var sim = _session.Sim;
        var site = BuildingId is { } id ? sim.Site(id) : null;
        _set.Visible(this, site is not null);
        if (site is null) return;

        var def = sim.Data.Buildings[site.DefId];
        _set.Text(_title, site.IsUpgrade ? $"UPGRADING TO LEVEL {site.Level}" : $"BUILDING {def.Name.ToUpperInvariant()}");
        _work.Set(site.Progress, $"{Fmt.Percent(site.Progress)} built", site.Paused ? Palette.TextMuted : Palette.Amber);
        _set.Tooltip(_work, $"Work {site.Work:0.#} of {site.WorkNeeded:0.#}. Work never runs ahead of the materials on site.");
        RefreshMaterials(sim, site);

        var builders = site.Builders.Select(b => sim.State.FindSurvivor(b)).OfType<Remnants.Core.Survivors.Survivor>().ToList();
        var slots = sim.MaxBuilders(site);
        _set.Text(_buildersLabel, $"BUILDERS {builders.Count}/{slots}");
        _builders.WorkerTip = s => $"{s.Name}: {ConstructionSystem.WorkRate(sim, s):0.##} work an hour · Construction {s.Get(Remnants.Core.Survivors.Skill.Construction):0}";
        _builders.Set(builders, slots);
        var helpers = site.Helpers.Select(h => sim.State.FindSurvivor(h)?.Name).OfType<string>().ToList();
        _set.Text(_helpers, helpers.Count > 0 ? $"Lending a hand (idle): {string.Join(", ", helpers)}"
            : site.Priority == SitePriority.Low ? "Low priority: only assigned builders work here." : "Idle people help here when they have nothing else to do.");

        _set.Text(_eta, SiteText.Eta(sim, site));
        _set.FontColor(_eta, sim.SiteForecast(site).Blocked ? Palette.Danger : Palette.Text);
        SetText(_pause, site.Paused ? "Resume" : "Pause");
        SetText(_priority, $"Priority: {site.Priority}");
        var lit = ConstructionSystem.IsLit(sim, site);
        _set.Visible(_night, sim.Data.Rules.DayNight.Enabled);
        _night.Disabled = !lit && !site.NightShift;
        if (_night.ButtonPressed != site.NightShift) _night.SetPressedNoSignal(site.NightShift);
        _set.Tooltip(_night, lit
            ? $"Builders keep going after dusk at {Fmt.Percent(sim.Data.Rules.DayNight.NightShift.OutputMultiplier)} output (costs mood, makes them tired)."
            : "A new building's site is dark at night; only an upgrade of a lit building can be built at night.");
        _set.Tooltip(_cancel, $"Call it off: materials not yet hauled come back in full, hauled ones at {Fmt.Percent(sim.Data.Rules.Construction.RefundShare)}.");
    }

    private void SetText(Button button, string text)
    {
        if (button.Text != text) button.Text = text;
    }

    /// <summary>One chip per material (hauled / needed), kept by id so a tick changes a number, not the row.</summary>
    private void RefreshMaterials(ColonySim sim, ConstructionSite site)
    {
        var ids = string.Join(",", site.Materials.Keys);
        if (ids != _chipIds)
        {
            _chipIds = ids;
            foreach (var (chip, _) in _chips.Values) chip.QueueFree();
            _chips.Clear();
            foreach (var res in site.Materials.Keys)
            {
                var chip = Widgets.Chip(Icons.Resource(res, 14), "");
                var amount = chip.GetChild(0).GetChild<Label>(1);
                _materials.AddChild(chip);
                _chips[res] = (chip, amount);
            }
        }
        foreach (var (res, need) in site.Materials)
        {
            var (chip, amount) = _chips[res];
            var have = Math.Floor(site.Delivered.GetValueOrDefault(res) + 1e-6);
            _set.Text(amount, $"{have:0}/{need:0}");
            _set.FontColor(amount, have >= need ? Palette.Good : Palette.Text);
            _set.Tooltip(chip, $"{sim.Data.Resources[res].Name}: {have:0} of {need:0} hauled to the site");
        }
    }

    private void TogglePause()
    {
        if (BuildingId is { } id && _session.Sim.Site(id) is { } site) _session.Execute(new PauseConstruction(id, !site.Paused));
    }

    private void CyclePriority()
    {
        if (BuildingId is not { } id || _session.Sim.Site(id) is not { } site) return;
        var next = site.Priority switch { SitePriority.Low => SitePriority.Normal, SitePriority.Normal => SitePriority.High, _ => SitePriority.Low };
        _session.Execute(new SetConstructionPriority(id, next));
    }

    private void ToggleNight()
    {
        if (BuildingId is { } id && _session.Sim.Site(id) is { } site) _session.Execute(new SetSiteNightShift(id, !site.NightShift));
    }

    private void Cancel()
    {
        if (BuildingId is { } id) _session.Execute(new CancelConstruction(id));
    }
}

public static class SiteText
{
    /// <summary>"About 5h left at this pace", or why nothing happens.</summary>
    public static string Eta(ColonySim sim, ConstructionSite site)
    {
        var f = sim.SiteForecast(site);
        if (f.BlockedBy == "Paused") return "Paused: nobody works here until you resume.";
        if (f.Blocked) return "Nobody builds here: assign builders, or leave someone idle to help.";
        return f.EtaHours is { } h ? $"About {ColonyText.Duration(Math.Max(1, (int)Math.Ceiling(h)), sim.Data.Rules)} left at this pace (on the work schedule)."
            : "No progress at this pace.";
    }

    /// <summary>The tooltip over a site in the town: name, work left, builders, ETA and materials.</summary>
    public static string Tooltip(ColonySim sim, ConstructionSite site)
    {
        var def = sim.Data.Buildings[site.DefId];
        var head = site.IsUpgrade ? $"{def.Name}: upgrading to level {site.Level}" : $"{def.Name} (under construction)";
        var f = sim.SiteForecast(site);
        var materials = string.Join(", ", site.Materials.Select(kv =>
            $"{sim.Data.Resources[kv.Key].Name} {Math.Floor(site.Delivered.GetValueOrDefault(kv.Key) + 1e-6):0}/{kv.Value:0}"));
        return $"{head}\n{Fmt.Percent(site.Progress)} built · {Math.Max(0, site.WorkNeeded - site.Work):0.#} work left\n"
               + $"Builders {f.Builders}/{sim.MaxBuilders(site)}" + (f.Helpers > 0 ? $" + {f.Helpers} helping" : "") + "\n"
               + Eta(sim, site) + "\nMaterials on site: " + materials;
    }

    /// <summary>"~2h" / "~1.5 days" for the build menu.</summary>
    public static string Estimate(double hours, Remnants.Core.Data.RulesDef rules) =>
        hours < rules.TicksPerDay ? $"~{Math.Max(1, Math.Round(hours)):0}h" : $"~{hours / rules.TicksPerDay:0.#} days";
}
