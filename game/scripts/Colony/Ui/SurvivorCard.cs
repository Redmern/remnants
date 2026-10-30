using System;
using System.Linq;
using Godot;
using Remnants.Core.Colony;
using Remnants.Core.Survivors;
using Remnants.Game.Hud;

namespace Remnants.Game.Colony.Ui;

/// <summary>
/// One survivor in the roster: portrait, name, life stage, job (icon and name), health and mood bars, an XP bar with the
/// level, and status badges (injured / worsening, patient, pregnant, away, homeless with its timer, starving, in class,
/// asleep / on the night shift / tired, perk points to spend), each with a tooltip.
/// Compact mode shows only the portrait (the collapsed roster).
/// </summary>
public partial class SurvivorCard : PanelContainer
{
    private readonly Portrait _portrait = new(46);
    private readonly Label _name = Widgets.Label("", "", size: 15);
    private readonly Label _stage = Widgets.Label("", "Small");
    private readonly TextureRect _jobIcon = Icons.Rect(Icons.Get("job_idle", 14), 14, Palette.TextMuted);
    private readonly Label _job = Widgets.Label("", "Small");
    private readonly StatBar _health = new("stat_health", 0, false);
    private readonly StatBar _mood = new("stat_mood", 0, false);
    private readonly StatBar _xp = new("stat_xp");
    private readonly HBoxContainer _badges = Widgets.Row(2);
    private readonly VBoxContainer _text = Widgets.Column(1);
    private bool _hover;
    private string _badgeSignature = "-";
    private Label? _homeless;
    private bool _selected;

    public int SurvivorId { get; }

    public event Action<int>? Picked;

    public SurvivorCard() : this(0) { }

    public SurvivorCard(int survivorId)
    {
        SurvivorId = survivorId;
        MouseFilter = MouseFilterEnum.Stop;
        MouseDefaultCursorShape = CursorShape.PointingHand;
        AddThemeStyleboxOverride("panel", HudTheme.CardStyle);
        var row = Widgets.Row(8);
        row.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(row);
        _portrait.MouseFilter = MouseFilterEnum.Ignore;
        row.AddChild(_portrait);

        _text.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _text.MouseFilter = MouseFilterEnum.Ignore;
        row.AddChild(_text);
        var top = Widgets.Row(4);
        _name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _name.ClipText = true;
        _name.AddThemeFontOverride("font", Fonts.BodyBold);
        top.AddChild(_name);
        top.AddChild(_badges);
        _text.AddChild(top);
        var job = Widgets.Row(4);
        job.AddChild(_jobIcon);
        _job.ClipText = true;
        _job.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        job.AddChild(_job);
        job.AddChild(_stage);
        _text.AddChild(job);
        var bars = Widgets.Row(6);
        _health.SizeFlagsHorizontal = _mood.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        bars.AddChild(_health);
        bars.AddChild(_mood);
        _text.AddChild(bars);
        _xp.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _text.AddChild(_xp);

        MouseEntered += () => { _hover = true; Restyle(); };
        MouseExited += () => { _hover = false; Restyle(); };
        GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) Picked?.Invoke(SurvivorId);
        };
    }

    public bool Compact
    {
        set
        {
            _text.Visible = !value;
            CustomMinimumSize = value ? Vector2.Zero : new Vector2(0, 0);
        }
    }

    public bool Selected
    {
        set
        {
            _selected = value;
            _portrait.Selected = value;
            Restyle();
        }
    }

    private void Restyle() => AddThemeStyleboxOverride("panel",
        _selected ? HudTheme.CardSelectedStyle : _hover ? HudTheme.CardHoverStyle : HudTheme.CardStyle);

    public void Refresh(ColonySim sim, Survivor s)
    {
        var rules = sim.Data.Rules;
        _portrait.Set(PortraitStudio.For(GetTree(), s), PortraitStudio.Backdrop(s.Id));
        _portrait.Dim = s.Away;
        _portrait.Alert = s.StarvingTicks > 0 || sim.IsWorsening(s) || !s.HasBed;
        _name.Text = s.Name + (s.PartnerId is not null ? " ♥" : "");
        _stage.Text = s.IsChild ? $"{s.LifeStage.ToString().ToLowerInvariant()} {ColonyText.AgeYears(s, rules)}y" : $"{ColonyText.AgeYears(s, rules)}y";
        _job.Text = ColonyText.Job(sim, s);
        _jobIcon.Texture = JobIcon(sim, s);
        _health.Set(s.Health / Survivor.MaxHealth, $"{s.Health:0}");
        _mood.Set(s.Mood / Survivor.MaxMood, $"{s.Mood:0}");
        _health.TooltipText = $"Health {s.Health:0}/{Survivor.MaxHealth:0}";
        _mood.TooltipText = $"Mood {s.Mood:0}/{Survivor.MaxMood:0}: {ColonyText.Mood(s)}";
        var xp = sim.Data.Progression.Xp;
        _xp.Visible = !s.IsInfant;
        _xp.Set(Progression.LevelProgress(s, xp), $"L{s.Level}", Palette.Amber);
        _xp.TooltipText = s.Level >= xp.MaxLevel ? $"Level {s.Level} (max)"
            : $"Level {s.Level}: {Fmt.Amount(s.Xp)}/{Fmt.Amount(Progression.XpForLevel(s.Level + 1, xp))} XP to the next";
        RefreshBadges(sim, s);
        TooltipText = $"{s.Name}, {s.LifeStage.ToString().ToLowerInvariant()} · {ColonyText.Job(sim, s)}\nClick for details.";
    }

    private void RefreshBadges(ColonySim sim, Survivor s)
    {
        var rules = sim.Data.Rules;
        var badges = new System.Collections.Generic.List<(string Icon, Color Color, string Tip)>();
        if (sim.IsWorsening(s)) badges.Add(("stat_injured", Palette.Danger, "A severe wound is getting worse: admit to the Med Bay."));
        else if (s.IsInjured) badges.Add(("stat_injured", Palette.Warn, "Injured"));
        if (s.PatientAtBuildingId is not null) badges.Add(("job_patient", Palette.Good, "In the Med Bay"));
        if (s.Pregnancy is not null) badges.Add(("stat_pregnant", Palette.Amber, "Expecting"));
        if (s.Away) badges.Add(("job_away", Palette.TextMuted, "Away on an expedition"));
        if (s.StarvingTicks > 0) badges.Add(("stat_starving", Palette.Danger, "Starving"));
        if (sim.ClassOf(s) is { } study) badges.Add(("ui_study", Palette.Amber, study.StudentId == s.Id ? "In class (studying)" : "Teaching a class"));
        if (Remnants.Core.Schematics.Research.IsResearching(sim.State, s.Id)) badges.Add(("ui_research", Palette.Amber, "Researching at the Town Hall"));
        if (sim.IsAsleep(s)) badges.Add(("stat_sleep", Palette.Night, "Asleep"));
        else if (sim.OnShift(s) && !Schedule.IsDayShiftHour(s, sim.Hour, rules.DayNight)
                 && s.AssignedBuildingId is { } at && sim.State.FindBuilding(at) is { } shift)
            badges.Add(("ui_night_shift", Palette.Night, $"On the night shift at {sim.Data.Buildings[shift.DefId].Name}: losing sleep"));
        if (sim.IsTired(s))
            badges.Add(("stat_tired", Palette.Warn, $"Tired: missed sleep ({s.SleepDebt:0} h to make up). −{(int)Math.Round((1 - rules.DayNight.Tired.OutputMultiplier) * 100)}% output and a slow mood drain until they sleep."));
        if (s.PerkPoints > 0)
            badges.Add(("stat_perk", Palette.Amber, $"{s.PerkPoints} perk point{(s.PerkPoints == 1 ? "" : "s")} to spend: open Perks"));
        var left = Math.Max(0, rules.Housing.HomelessGraceTicks - s.HomelessTicks);
        if (!s.HasBed) badges.Add(("stat_homeless", Palette.Danger, "No bed: leaves when the timer runs out unless a bed frees up"));
        var signature = string.Join(",", badges.Select(b => b.Icon + b.Color.ToHtml()));
        if (signature != _badgeSignature)
        {
            _badgeSignature = signature;
            Widgets.Clear(_badges);
            foreach (var (icon, color, tip) in badges)
            {
                var rect = Icons.Rect(Icons.Get(icon, 14), 14, color);
                rect.TooltipText = tip;
                rect.MouseFilter = MouseFilterEnum.Stop;
                _badges.AddChild(rect);
            }
            _homeless = null;
            if (!s.HasBed)
            {
                _homeless = Widgets.Label("", "Number", Palette.Danger, 12);
                _badges.AddChild(_homeless);
            }
        }
        if (_homeless is not null) _homeless.Text = ColonyText.Duration(left, rules);
    }

    public static Texture2D JobIcon(ColonySim sim, Survivor s)
    {
        if (s.Away) return Icons.Get("job_away", 14);
        if (s.IsInfant) return Icons.Get("job_baby", 14);
        if (s.PatientAtBuildingId is not null) return Icons.Get("job_patient", 14);
        if (sim.ClassOf(s) is not null) return Icons.Get("ui_study", 14);
        if (Remnants.Core.Schematics.Research.IsResearching(sim.State, s.Id)) return Icons.Get("ui_research", 14);
        if (s.AssignedBuildingId is { } id && sim.State.FindBuilding(id) is { } b && sim.Data.Buildings[b.DefId].Job is { } job)
            return Icons.Get($"skill_{job}", 14, $"job_{job}", "skill_crafting");
        return Icons.Get("job_idle", 14);
    }
}
