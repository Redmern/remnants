using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;
using Remnants.Core.Colony;
using Remnants.Core.Survivors;

namespace Remnants.Game.Colony.Ui;

/// <summary>
/// Survivor list (with partners and growth) plus details of the selected one: family, mood, growth, job controls and
/// a matchmaking row to encourage or discourage a pair.
/// </summary>
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
    private readonly OptionButton _pairWith = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill, FocusMode = FocusModeEnum.None };
    private readonly List<int> _pairIds = new();
    private readonly Button _encourage;
    private readonly Button _discourage;
    private readonly Button _neutral;

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

        var pairRow = new HBoxContainer();
        pairRow.AddChild(_pairWith);
        _encourage = Ui.Button("Match", () => Matchmake(PairStance.Encourage), 56);
        _discourage = Ui.Button("Apart", () => Matchmake(PairStance.Discourage), 52);
        _neutral = Ui.Button("Let be", () => Matchmake(PairStance.Neutral), 52);
        _encourage.TooltipText = "Encourage these two: they grow closer faster.";
        _discourage.TooltipText = "Keep these two apart (splits a couple).";
        _neutral.TooltipText = "Stop encouraging or discouraging.";
        pairRow.AddChild(_encourage);
        pairRow.AddChild(_discourage);
        pairRow.AddChild(_neutral);
        box.AddChild(Ui.Heading("Matchmaking"));
        box.AddChild(pairRow);

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
            _list.SetItemText(i, $"{s.Name}{Ties(sim, s)}  ·  {ColonyText.Job(sim, s)}");
            _list.SetItemCustomFgColor(i, s.StarvingTicks > 0 || !s.HasBed || sim.IsWorsening(s) ? Ui.Bad : s.Away ? Ui.Muted : Colors.White);
            if (s.Id == SelectedSurvivorId) _list.Select(i);
        }
        if (SelectedSurvivorId is null) _list.DeselectAll();

        var selected = SelectedSurvivorId is { } id ? sim.State.FindSurvivor(id) : null;
        _details.Text = selected is null ? "[color=#999]Select a survivor.[/color]" : Describe(sim, selected);
        RefreshJobs(sim, selected);
        RefreshPairs(sim, selected);
    }

    /// <summary>The roster's relationship line: partner and pregnancy for adults, stage and growth for minors.</summary>
    private static string Ties(ColonySim sim, Survivor s)
    {
        if (s.IsChild) return $"  ·  {ColonyText.Stage(s, sim.Data.Rules).ToLowerInvariant()}";
        var text = ColonyText.PersonName(sim.State, s.PartnerId) is { } partner ? $" ♥ {partner}" : "";
        return s.Pregnancy is not null ? text + "  ·  expecting" : text;
    }

    private void RefreshPairs(ColonySim sim, Survivor? s)
    {
        var others = s is null || s.IsChild
            ? new List<Survivor>()
            : sim.State.Survivors.Where(o => o.Id != s.Id && !o.IsChild).OrderBy(o => o.Id).ToList();
        var labels = others.Select(o =>
        {
            var stance = Bonds.Stance(sim.State, s!.Id, o.Id) switch
            {
                PairStance.Encourage => " +",
                PairStance.Discourage => " ×",
                _ => "",
            };
            var tie = s.PartnerId == o.Id ? "partner" : ColonyText.Percent(Bonds.Affinity(sim.State, s.Id, o.Id) / sim.Data.Rules.Relationships.CoupleThreshold);
            return $"{o.Name} ({tie}){stance}";
        }).ToList();

        var unchanged = _pairWith.ItemCount == labels.Count && labels.Select((l, i) => _pairWith.GetItemText(i) == l).All(same => same);
        if (!unchanged)
        {
            var previous = _pairWith.Selected >= 0 && _pairWith.Selected < _pairIds.Count ? _pairIds[_pairWith.Selected] : (int?)null;
            _pairWith.Clear();
            _pairIds.Clear();
            foreach (var label in labels) _pairWith.AddItem(label);
            _pairIds.AddRange(others.Select(o => o.Id));
            var index = previous is { } p ? _pairIds.IndexOf(p) : -1;
            _pairWith.Select(index >= 0 ? index : _pairIds.Count > 0 ? 0 : -1);
        }
        _pairWith.Disabled = _pairIds.Count == 0;
        _encourage.Disabled = _discourage.Disabled = _neutral.Disabled = _pairWith.Disabled;
    }

    private void Matchmake(PairStance stance)
    {
        if (SelectedSurvivorId is not { } id || _pairWith.Selected < 0 || _pairWith.Selected >= _pairIds.Count) return;
        _session.Execute(new Matchmake(id, _pairIds[_pairWith.Selected], stance));
    }

    private void RefreshJobs(ColonySim sim, Survivor? s)
    {
        var buildings = sim.State.Buildings.Where(b => sim.Data.Buildings[b.DefId].Job is not null).OrderBy(b => b.Id).ToList();
        var labels = buildings.Select(b =>
            $"{ColonyText.BuildingName(sim, b)}  ({sim.State.WorkersAt(b.Id).Count()}/{sim.Data.Buildings[b.DefId].Level(b.Level).WorkerSlots})").ToList();

        // Rebuild only on change: clearing every tick would close an open dropdown.
        var unchanged = _jobs.ItemCount == labels.Count && labels.Select((l, i) => _jobs.GetItemText(i) == l).All(same => same);
        if (!unchanged || _jobs.Selected < 0)
        {
            var previous = _jobs.Selected >= 0 && _jobs.Selected < _jobBuildingIds.Count ? _jobBuildingIds[_jobs.Selected] : (int?)null;
            _jobs.Clear();
            _jobBuildingIds.Clear();
            foreach (var label in labels) _jobs.AddItem(label);
            _jobBuildingIds.AddRange(buildings.Select(b => b.Id));
            var want = previous ?? s?.AssignedBuildingId;
            var index = want is { } w ? _jobBuildingIds.IndexOf(w) : -1;
            _jobs.Select(index >= 0 ? index : _jobBuildingIds.Count > 0 ? 0 : -1);
        }

        _jobs.Disabled = s is null || s.Away || s.IsInfant || s.PatientAtBuildingId is not null || _jobBuildingIds.Count == 0;
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
        var moodColor = s.Mood < 55 ? Ui.Hex(Ui.Bad) : s.Mood < 80 ? "dddddd" : Ui.Hex(Ui.Good);
        text.AppendLine($"Mood: [color=#{moodColor}]{s.Mood:0}/{Survivor.MaxMood:0} {ColonyText.Mood(s)}[/color]");
        Family(sim, s, text);
        text.AppendLine($"Body: {ColonyText.Body(s.Body, sim.Scavenge).Replace("\n", ", ")}");
        if (s.PatientAtBuildingId is not null)
            text.AppendLine($"In the Med Bay, healed in {(sim.HealEta(s) is { } eta ? ColonyText.Duration(eta, rules) : "—")}");
        else if (sim.IsWorsening(s))
            text.AppendLine($"[color=#{Ui.Hex(Ui.Bad)}]A severe wound is getting worse: admit to the Med Bay.[/color]");
        text.AppendLine("[color=#aaa]Attributes[/color]");
        text.AppendLine(string.Join("  ", Enum.GetValues<AttributeKind>().Select(a => $"{a.ToString()[..3]} {s.Get(a):0}")));
        var skills = Enum.GetValues<Skill>().Where(k => s.Get(k) >= 0.5).OrderByDescending(k => s.Get(k)).ToList();
        text.AppendLine("[color=#aaa]Skills[/color]");
        text.Append(skills.Count == 0 ? "none yet" : string.Join("  ", skills.Select(k => $"{k} {s.Get(k):0}")));
        return text.ToString();
    }

    /// <summary>Growth, pregnancy, partner (or closest bonds), parents and children.</summary>
    private static void Family(ColonySim sim, Survivor s, StringBuilder text)
    {
        var rules = sim.Data.Rules;
        var good = Ui.Hex(Ui.Good);
        if (s.IsChild)
        {
            var next = s.IsInfant ? "child" : "adult";
            var eta = Bonds.TicksToNextStage(s, rules.Life) is { } t ? ColonyText.Duration((int)t, rules) : "—";
            var hungry = s.MalnourishedTicks > 0 ? $"  [color=#{Ui.Hex(Ui.Bad)}](hungry {s.MalnourishedTicks}h)[/color]" : "";
            text.AppendLine($"Growing: {ColonyText.Stage(s, rules)} · {next} in {eta}{hungry}");
        }
        if (s.Pregnancy is { } p)
        {
            var father = ColonyText.PersonName(sim.State, p.FatherId);
            var due = ColonyText.Duration((int)(Bonds.TicksToBirth(sim.State, s) ?? 0), rules);
            text.AppendLine($"[color=#{good}]Expecting{(father is null ? "" : $" with {father}")}: due in {due}[/color]");
        }
        if (ColonyText.PersonName(sim.State, s.PartnerId) is { } partner) text.AppendLine($"Partner: [color=#{good}]{partner}[/color]");
        else if (!s.IsChild)
        {
            var close = Bonds.Closest(sim.State, s).Where(x => x.Bond.Affinity > 0).Take(2)
                .Select(x => $"{x.Other.Name} {ColonyText.Percent(x.Bond.Affinity / rules.Relationships.CoupleThreshold)}").ToList();
            text.AppendLine(close.Count == 0 ? "Single" : $"Single · close to {string.Join(", ", close)}");
        }
        var parents = new[] { s.MotherId, s.FatherId }.Select(id => ColonyText.PersonName(sim.State, id)).OfType<string>().ToList();
        if (parents.Count > 0) text.AppendLine($"Parents: {string.Join(", ", parents)}");
        var children = Bonds.Children(sim.State, s).Select(c => $"{c.Name} ({c.LifeStage.ToString().ToLowerInvariant()})").ToList();
        if (children.Count > 0) text.AppendLine($"Children: {string.Join(", ", children)}");
    }
}
