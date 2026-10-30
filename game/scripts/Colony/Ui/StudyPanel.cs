using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Core.Colony;
using Remnants.Core.Survivors;
using Remnants.Game.Hud;

namespace Remnants.Game.Colony.Ui;

/// <summary>
/// Classes at the selected study building (Town Hall, Electronics Lab): who studies what with whom and when they are
/// done (cancel), and a new class: student, subject (a skill taught here, or a perk a mentor here can teach), mentor
/// (none = study alone up to the self-study cap) and an optional manual, with Core's plan (cap, rate, ETA, or why
/// not) before Start. Core decides (<see cref="ColonySim.PlanStudy"/>, <see cref="StartStudy"/>).
/// </summary>
public partial class StudyPanel : PanelContainer
{
    private readonly ColonySession _session;
    private readonly Label _slots = Widgets.Label("", "Number", Palette.TextMuted, 14);
    private readonly VBoxContainer _classes = Widgets.Column(4);
    private readonly OptionButton _student = Picker();
    private readonly OptionButton _subject = Picker();
    private readonly OptionButton _mentor = Picker();
    private readonly OptionButton _manual = Picker();
    private readonly Label _manualLabel = Widgets.Label("Manual", "Small");
    private readonly Label _plan = Widgets.Wrapped("", "Small");
    private readonly Button _start;
    private readonly List<int> _studentIds = new();
    private readonly List<string> _subjectKeys = new();
    private readonly List<int?> _mentorIds = new();
    private readonly List<string?> _manualIds = new();
    private string _classSignature = "";

    public int? BuildingId { get; private set; }

    public StudyPanel(ColonySession session)
    {
        _session = session;
        CustomMinimumSize = new Vector2(300, 0);
        Visible = false;
        var box = Ui.Panel(this);
        box.AddChild(Widgets.SectionHeader("Study", "ui_study", _slots));
        box.AddChild(_classes);

        box.AddChild(Widgets.SectionHeader("New class", "ui_plus"));
        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 6);
        grid.AddThemeConstantOverride("v_separation", 4);
        foreach (var (label, picker) in new (Control, OptionButton)[]
                 {
                     (Widgets.Label("Student", "Small"), _student), (Widgets.Label("Subject", "Small"), _subject),
                     (Widgets.Label("Mentor", "Small"), _mentor), (_manualLabel, _manual),
                 })
        {
            grid.AddChild(label);
            grid.AddChild(picker);
            picker.ItemSelected += _ => Refresh();
        }
        box.AddChild(grid);
        box.AddChild(_plan);
        _start = Widgets.IconButton("ui_study", "Start class", "Student and mentor leave their jobs while in class.", Start, variation: "Primary");
        box.AddChild(_start);
    }

    private static OptionButton Picker() => new()
    {
        SizeFlagsHorizontal = SizeFlags.ExpandFill, FocusMode = FocusModeEnum.None, ClipText = true, FitToLongestItem = false,
    };

    public void Show(int? buildingId)
    {
        BuildingId = buildingId;
        _classSignature = "";
        Refresh();
    }

    public void Refresh()
    {
        var sim = _session.Sim;
        var building = BuildingId is { } id ? sim.State.FindBuilding(id) : null;
        Visible = building is not null && sim.Data.Progression.Study.Buildings.ContainsKey(building.DefId);
        if (!Visible) return;
        var teaches = sim.Data.Progression.Study.Buildings[building!.DefId];
        var classes = sim.State.Studies.Where(o => o.BuildingId == building.Id).OrderBy(o => o.Id).ToList();
        var slots = sim.StudySlots(building);
        _slots.Text = $"{classes.Count}/{slots}";
        _slots.TooltipText = $"Students at once at level {building.Level}. Upgrade for more.";
        RefreshClasses(sim, classes);

        // Student: anyone at home, not a baby, not a patient, not in class already.
        var students = sim.State.Present
            .Where(s => !s.IsInfant && s.PatientAtBuildingId is null && sim.ClassOf(s) is null)
            .OrderBy(s => s.IsChild ? 0 : 1).ThenBy(s => s.Id).ToList();
        Fill(_student, _studentIds, students.Select(s => s.Id),
            students.Select(s => $"{s.Name}  (L{s.Level}{(s.IsChild ? ", child: learns fast" : "")}{(s.AssignedBuildingId is null ? "" : ", working")})"));
        var student = sim.State.FindSurvivor(Selected(_student, _studentIds));

        // Subject: the skills taught here, then the perks of the disciplines studied here that some mentor at home has.
        var subjects = new List<(string Key, string Label)>();
        foreach (var k in teaches.Skills)
            subjects.Add(($"skill:{k}", $"{sim.Data.SkillInfo(k).Name}  ({(student is null ? 0 : student.Get(k)):0})"));
        var mentorsAtHome = sim.State.Present.Where(m => !m.IsChild && m.PatientAtBuildingId is null).ToList();
        foreach (var d in sim.Data.Progression.Disciplines.Where(d => d.StudyAt == building.DefId))
            foreach (var p in sim.Data.PerksOf(d.Id))
                if (student is not null && !student.HasPerk(p.Id) && mentorsAtHome.Any(m => m.Id != student.Id && m.HasPerk(p.Id)))
                    subjects.Add(($"perk:{p.Id}", $"Perk: {p.Name}"));
        Fill(_subject, _subjectKeys, subjects.Select(x => x.Key), subjects.Select(x => x.Label));
        var subject = Selected(_subject, _subjectKeys);
        Skill? skill = subject is { } key && key.StartsWith("skill:") ? System.Enum.Parse<Skill>(key[6..]) : null;
        var perkId = subject is { } pk && pk.StartsWith("perk:") ? pk[5..] : null;

        // Mentor: adults ahead in the skill (or who have the perk); a skill can also be studied alone.
        var mentors = mentorsAtHome.Where(m => student is null || m.Id != student.Id)
            .Where(m => skill is { } k ? student is null || m.Get(k) >= student.Get(k) + sim.Data.Progression.Study.MentorLead
                : perkId is not null && m.HasPerk(perkId))
            .OrderByDescending(m => skill is { } k ? m.Get(k) : 0).ThenBy(m => m.Id).ToList();
        var mentorIds = new List<int?>();
        var mentorLabels = new List<string>();
        if (skill is not null)
        {
            mentorIds.Add(null);
            mentorLabels.Add($"Nobody: alone, up to {sim.Data.Progression.Study.SelfStudyCap:0}");
        }
        foreach (var m in mentors)
        {
            mentorIds.Add(m.Id);
            var busy = sim.ClassOf(m) is not null ? ", teaching" : m.AssignedBuildingId is not null ? ", working" : "";
            mentorLabels.Add(skill is { } k ? $"{m.Name}  ({sim.Data.SkillInfo(k).Name} {m.Get(k):0}{busy})" : $"{m.Name}{busy}");
        }
        Fill(_mentor, _mentorIds, mentorIds, mentorLabels);
        var mentorId = Selected(_mentor, _mentorIds);

        // Manual: any in the inventory for this skill.
        var manuals = skill is { } ms && sim.Scavenge is { } items
            ? sim.State.Inventory.Counts.Keys.Where(i => items.Items.TryGetValue(i, out var d) && d.Manual?.Skill == ms).ToList()
            : new List<string>();
        _manual.Visible = _manualLabel.Visible = manuals.Count > 0;
        Fill(_manual, _manualIds, new string?[] { null }.Concat(manuals),
            new[] { "None" }.Concat(manuals.Select(m => $"{sim.Scavenge!.Items[m].Name} ×{sim.State.Inventory.Count(m)}")));
        var manual = _manual.Visible ? Selected(_manual, _manualIds) : null;

        _student.Disabled = _studentIds.Count == 0;
        _subject.Disabled = _subjectKeys.Count == 0;
        _mentor.Disabled = _mentorIds.Count == 0;
        if (student is null || subject is null)
        {
            _plan.Text = students.Count == 0 ? "Nobody is free to study." : "Pick what to study.";
            _plan.AddThemeColorOverride("font_color", Palette.TextMuted);
            _start.Disabled = true;
            return;
        }
        var plan = sim.PlanStudy(new StartStudy(student.Id, building.Id, skill, perkId, mentorId, manual));
        var eta = plan.EtaTicks is { } t ? ColonyText.Duration(t, sim.Data.Rules) : "—";
        _plan.Text = !plan.Ok ? "⚠ " + plan.Check.Message
            : skill is { } sk ? $"{sim.Data.SkillInfo(sk).Name} {student.Get(sk):0} → {plan.Cap:0}, done in {eta}" + (mentorId is null ? " (alone)" : " (mentor ×2 speed)")
            : $"Learns {sim.Data.Perks[perkId!].Name} in {eta}, without a perk point.";
        _plan.AddThemeColorOverride("font_color", plan.Ok ? Palette.Text : Palette.Danger);
        _start.Disabled = !plan.Ok;
    }

    private void RefreshClasses(ColonySim sim, List<StudyOrder> classes)
    {
        var signature = string.Join(",", classes.Select(o => $"{o.Id}:{o.MentorId}"));
        if (signature != _classSignature)
        {
            _classSignature = signature;
            Widgets.Clear(_classes);
            if (classes.Count == 0) _classes.AddChild(Widgets.Label("No classes. Pick a student below.", "Muted"));
            foreach (var o in classes)
            {
                var row = Widgets.Row(6);
                row.SetMeta("study", o.Id);
                var portrait = new Portrait(34);
                if (sim.State.FindSurvivor(o.StudentId) is { } s) portrait.Set(PortraitStudio.For(GetTree(), s), PortraitStudio.Backdrop(s.Id));
                row.AddChild(portrait);
                var label = Widgets.Wrapped("", "Small");
                label.AddThemeColorOverride("font_color", Palette.Text);
                row.AddChild(label);
                var id = o.Id;
                row.AddChild(Widgets.IconButton("ui_close", "", "End this class", () => _session.Execute(new CancelStudy(id)), variation: "Flat"));
                _classes.AddChild(row);
            }
        }
        foreach (var row in _classes.GetChildren().OfType<HBoxContainer>())
            if (classes.Find(o => o.Id == row.GetMeta("study").AsInt32()) is { } o)
                row.GetChild<Label>(1).Text = ClassText(sim, o);
    }

    private static string ClassText(ColonySim sim, StudyOrder o)
    {
        var student = sim.State.FindSurvivor(o.StudentId);
        var mentor = ColonyText.PersonName(sim.State, o.MentorId);
        var eta = sim.StudyEta(o) is { } t ? ColonyText.Duration(t, sim.Data.Rules) : "—";
        var subject = o.Skill is { } k
            ? $"{sim.Data.SkillInfo(k).Name} {student?.Get(k) ?? 0:0}"
            : $"{sim.Data.Perks[o.PerkId!].Name} {ColonyText.Percent(o.Progress / (sim.Data.Progression.Study.PerkWorkPerTier * sim.Data.Perks[o.PerkId!].Tier))}";
        return $"{student?.Name}: {subject}{(mentor is null ? " (alone)" : $" with {mentor}")} · ETA {eta}";
    }

    /// <summary>Refills a picker only when its items change (so an open dropdown stays open) and keeps the selection by id.</summary>
    private static void Fill<T>(OptionButton picker, List<T> ids, IEnumerable<T> newIds, IEnumerable<string> labels)
    {
        var idList = newIds.ToList();
        var labelList = labels.ToList();
        var same = picker.ItemCount == labelList.Count && labelList.Select((l, i) => picker.GetItemText(i) == l).All(x => x)
                   && ids.SequenceEqual(idList);
        if (same && picker.Selected >= 0) return;
        var previous = picker.Selected >= 0 && picker.Selected < ids.Count ? ids[picker.Selected] : default;
        var hadPrevious = picker.Selected >= 0 && picker.Selected < ids.Count;
        ids.Clear();
        ids.AddRange(idList);
        picker.Clear();
        foreach (var label in labelList) picker.AddItem(label);
        var index = hadPrevious ? ids.IndexOf(previous!) : -1;
        picker.Select(index >= 0 ? index : ids.Count > 0 ? 0 : -1);
    }

    private static T? Selected<T>(OptionButton picker, List<T> ids) =>
        picker.Selected >= 0 && picker.Selected < ids.Count ? ids[picker.Selected] : default;

    private void Start()
    {
        var student = Selected(_student, _studentIds);
        if (BuildingId is not { } building || student == 0) return;
        var subject = Selected(_subject, _subjectKeys);
        if (subject is null) return;
        Skill? skill = subject.StartsWith("skill:") ? System.Enum.Parse<Skill>(subject[6..]) : null;
        var perk = subject.StartsWith("perk:") ? subject[5..] : null;
        _session.Execute(new StartStudy(student, building, skill, perk, Selected(_mentor, _mentorIds),
            _manual.Visible ? Selected(_manual, _manualIds) : null));
    }
}
