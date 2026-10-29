using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;
using Remnants.Core.Colony;
using Remnants.Core.Survivors;

namespace Remnants.Game.Colony.Ui;

/// <summary>Survivor list plus details of the selected one, with assign / unassign job controls.</summary>
public partial class RosterPanel : PanelContainer
{
    private readonly ColonySession _session;
    private readonly ItemList _list = new() { CustomMinimumSize = new Vector2(0, 170), FocusMode = FocusModeEnum.None };
    private readonly RichTextLabel _details = Ui.Rich(scroll: true);
    private readonly OptionButton _jobs = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill, FocusMode = FocusModeEnum.None };
    private readonly Button _assign;
    private readonly Button _unassign;
    private readonly List<int> _ids = new();
    private readonly List<int> _jobBuildingIds = new();

    public int? SelectedSurvivorId { get; private set; }

    public event Action<int?>? SelectionChanged;

    public RosterPanel(ColonySession session)
    {
        _session = session;
        CustomMinimumSize = new Vector2(310, 0);
        var box = Ui.Panel(this);
        box.AddChild(Ui.Heading("Survivors"));
        box.AddChild(_list);
        box.AddChild(_details);

        var jobRow = new HBoxContainer();
        jobRow.AddChild(_jobs);
        _assign = Ui.Button("Assign", Assign, 64);
        _unassign = Ui.Button("Idle", Unassign, 48);
        jobRow.AddChild(_assign);
        jobRow.AddChild(_unassign);
        box.AddChild(Ui.Heading("Job"));
        box.AddChild(jobRow);

        _list.ItemSelected += index => Select(_ids[(int)index]);
    }

    public void Select(int? survivorId)
    {
        SelectedSurvivorId = survivorId;
        _jobs.Select(-1); // default the job picker to the new survivor's current job
        SelectionChanged?.Invoke(survivorId);
        Refresh();
    }

    public void Refresh()
    {
        var sim = _session.Sim;
        var survivors = sim.State.Survivors.OrderBy(s => s.Id).ToList();
        if (SelectedSurvivorId is { } sel && survivors.All(s => s.Id != sel))
        {
            SelectedSurvivorId = null;
            SelectionChanged?.Invoke(null);
        }

        var ids = survivors.Select(s => s.Id).ToList();
        if (!ids.SequenceEqual(_ids))
        {
            _list.Clear();
            _ids.Clear();
            _ids.AddRange(ids);
            foreach (var _ in survivors) _list.AddItem("");
        }
        for (var i = 0; i < survivors.Count; i++)
        {
            var s = survivors[i];
            _list.SetItemText(i, $"{s.Name}  ·  {(s.IsChild ? "child" : "adult")}  ·  {ColonyText.Job(sim, s)}");
            _list.SetItemCustomFgColor(i, s.StarvingTicks > 0 || !s.HasBed ? Ui.Bad : Colors.White);
            if (s.Id == SelectedSurvivorId) _list.Select(i);
        }
        if (SelectedSurvivorId is null) _list.DeselectAll();

        var selected = SelectedSurvivorId is { } id ? sim.State.FindSurvivor(id) : null;
        _details.Text = selected is null ? "[color=#999]Select a survivor.[/color]" : Describe(sim, selected);
        RefreshJobs(sim, selected);
    }

    private void RefreshJobs(ColonySim sim, Survivor? s)
    {
        var buildings = sim.State.Buildings.Where(b => sim.Data.Buildings[b.DefId].Job is not null).OrderBy(b => b.Id).ToList();
        var previous = _jobs.Selected >= 0 && _jobs.Selected < _jobBuildingIds.Count ? _jobBuildingIds[_jobs.Selected] : (int?)null;
        _jobs.Clear();
        _jobBuildingIds.Clear();
        foreach (var b in buildings)
        {
            var slots = sim.Data.Buildings[b.DefId].Level(b.Level).WorkerSlots;
            _jobs.AddItem($"{ColonyText.BuildingName(sim, b)}  ({sim.State.WorkersAt(b.Id).Count()}/{slots})");
            _jobBuildingIds.Add(b.Id);
        }
        var want = previous ?? s?.AssignedBuildingId;
        var index = want is { } w ? _jobBuildingIds.IndexOf(w) : -1;
        _jobs.Select(index >= 0 ? index : _jobBuildingIds.Count > 0 ? 0 : -1);

        _jobs.Disabled = s is null || _jobBuildingIds.Count == 0;
        _assign.Disabled = _jobs.Disabled;
        _unassign.Disabled = s?.AssignedBuildingId is null;
    }

    private void Assign()
    {
        if (SelectedSurvivorId is not { } id || _jobs.Selected < 0) return;
        _session.Execute(new AssignJob(id, _jobBuildingIds[_jobs.Selected]));
    }

    private void Unassign()
    {
        if (SelectedSurvivorId is { } id) _session.Execute(new Unassign(id));
    }

    private static string Describe(ColonySim sim, Survivor s)
    {
        var rules = sim.Data.Rules;
        var text = new StringBuilder();
        text.AppendLine($"[b]{s.Name}[/b]  {s.Sex}, {s.LifeStage}, {ColonyText.AgeYears(s, rules)} y");
        text.AppendLine($"Job: {ColonyText.Job(sim, s)}");
        var hpColor = s.Health < 50 ? Ui.Hex(Ui.Bad) : Ui.Hex(Ui.Good);
        text.AppendLine($"Health: [color=#{hpColor}]{s.Health:0}/{Survivor.MaxHealth:0}[/color]   ({ColonyText.Needs(s, rules)})");
        text.AppendLine($"Body: {ColonyText.Body(s.Body).Replace("\n", ", ")}");
        text.AppendLine("[color=#aaa]Attributes[/color]");
        text.AppendLine(string.Join("  ", Enum.GetValues<AttributeKind>().Select(a => $"{a.ToString()[..3]} {s.Get(a):0}")));
        var skills = Enum.GetValues<Skill>().Where(k => s.Get(k) >= 0.5).OrderByDescending(k => s.Get(k)).ToList();
        text.AppendLine("[color=#aaa]Skills[/color]");
        text.Append(skills.Count == 0 ? "none yet" : string.Join("  ", skills.Select(k => $"{k} {s.Get(k):0}")));
        return text.ToString();
    }
}
