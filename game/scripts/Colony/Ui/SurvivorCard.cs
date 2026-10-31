using System;
using System.Linq;
using Godot;
using Remnants.Core.Colony;
using Remnants.Core.Survivors;
using Remnants.Game.Hud;

namespace Remnants.Game.Colony.Ui;

/// <summary>
/// One survivor in the roster: portrait, name and status badges; the job (icon and name) and age on one line that ends
/// in "…" rather than running together; health (H) and mood (M) bars, each with its number on hover; and a level pip
/// (the level, ringed by the XP toward the next). Badges: injured / worsening, patient, pregnant, away, homeless with its
/// timer, starving, in class, asleep / on the night shift / tired, food buffs, perk points to spend, each with a tooltip.
/// Left-click selects, right-click asks the roster for its menu, and a card can be dragged onto a building panel to
/// assign the survivor there. Compact mode shows only the portrait (the collapsed roster).
/// </summary>
public partial class SurvivorCard : PanelContainer
{
    private readonly Portrait _portrait = new(46);
    private readonly Label _name = Widgets.Label("", "", size: 15);
    private readonly TextureRect _jobIcon = Icons.Rect(Icons.Get("job_idle", 14), 14, Palette.TextMuted);
    private readonly Label _job = Widgets.Label("", "Small");
    private readonly StatBar _health = new(null, 0, false);
    private readonly StatBar _mood = new(null, 0, false);
    private readonly LevelPip _level = new();
    private readonly HBoxContainer _badges = Widgets.Row(2);
    private readonly VBoxContainer _text = Widgets.Column(2);
    private bool _hover;
    private string _badgeSignature = "-";
    private Label? _homeless;
    private bool _selected;
    private bool _compact;
    private readonly Unchanged _set = new();
    private (Texture2D, Color)? _look;

    public int SurvivorId { get; }

    public event Action<int>? Picked;

    /// <summary>Right-click: the roster opens its menu (assign to…, Med Bay, details) at this card.</summary>
    public event Action<int, Control>? MenuRequested;

    public SurvivorCard() : this(0) { }

    public SurvivorCard(int survivorId)
    {
        SurvivorId = survivorId;
        MouseFilter = MouseFilterEnum.Stop;
        MouseDefaultCursorShape = CursorShape.PointingHand;
        FocusMode = FocusModeEnum.All;
        AddThemeStyleboxOverride("panel", HudTheme.CardStyle);
        var row = Widgets.Row(Space.Sm);
        row.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(row);
        _portrait.MouseFilter = MouseFilterEnum.Ignore;
        row.AddChild(_portrait);

        _text.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _text.MouseFilter = MouseFilterEnum.Ignore;
        row.AddChild(_text);
        var top = Widgets.Row(Space.Xs);
        _name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _name.ClipText = true;
        _name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        _name.AddThemeFontOverride("font", Fonts.BodyBold);
        top.AddChild(_name);
        top.AddChild(_badges);
        _text.AddChild(top);
        var job = Widgets.Row(Space.Xs);
        job.AddChild(_jobIcon);
        _job.ClipText = true;
        _job.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        _job.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        job.AddChild(_job);
        _text.AddChild(job);
        var bars = Widgets.Row(Space.Xs);
        bars.AddChild(Letter("H"));
        bars.AddChild(_health);
        bars.AddChild(Letter("M"));
        bars.AddChild(_mood);
        _health.SizeFlagsHorizontal = _mood.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _health.SizeFlagsVertical = _mood.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        bars.AddChild(_level);
        _text.AddChild(bars);

        Restyle();
        MouseEntered += () => { _hover = true; Restyle(); };
        MouseExited += () => { _hover = false; Restyle(); };
        FocusEntered += Restyle;
        FocusExited += Restyle;
    }

    private static Label Letter(string text)
    {
        var label = Widgets.Label(text, "Small", Palette.TextDim);
        label.AddThemeFontOverride("font", Fonts.Display);
        return label;
    }

    public override void _GuiInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Left }:
                // On release: a drag (onto a building) ends elsewhere and so never toggles the selection.
                Picked?.Invoke(SurvivorId);
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right }:
                AcceptEvent();
                MenuRequested?.Invoke(SurvivorId, this);
                break;
            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.Enter or Key.KpEnter or Key.Space }:
                AcceptEvent();
                Picked?.Invoke(SurvivorId);
                break;
            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.Menu }:
                AcceptEvent();
                MenuRequested?.Invoke(SurvivorId, this);
                break;
        }
    }

    /// <summary>Drag a card onto a building panel to assign the survivor there: the payload is {"survivor": id}.</summary>
    public override Variant _GetDragData(Vector2 atPosition)
    {
        var preview = new Portrait(40) { Modulate = new Color(1, 1, 1, 0.85f) };
        if (_look is var (texture, backdrop)) preview.Set(texture, backdrop);
        SetDragPreview(preview);
        return new Godot.Collections.Dictionary { ["survivor"] = SurvivorId };
    }

    /// <summary>The survivor id a drag carries, or null for anything else.</summary>
    public static int? DraggedId(Variant data) =>
        data.VariantType == Variant.Type.Dictionary && data.AsGodotDictionary() is { } d && d.TryGetValue("survivor", out var id) ? id.AsInt32() : null;

    public bool Compact
    {
        set
        {
            _compact = value;
            _text.Visible = !value;
        }
    }

    public bool Selected
    {
        set
        {
            if (value == _selected) return;
            _selected = value;
            _portrait.Selected = value;
            Restyle();
        }
    }

    private void Restyle() => AddThemeStyleboxOverride("panel",
        _selected ? HudTheme.CardSelectedStyle : _hover || HasFocus() ? HudTheme.CardHoverStyle : HudTheme.CardStyle);

    public void Refresh(ColonySim sim, Survivor s)
    {
        var rules = sim.Data.Rules;
        _look = (PortraitStudio.For(GetTree(), s), PortraitStudio.Backdrop(s.Id));
        _portrait.Set(_look.Value.Item1, _look.Value.Item2);
        _portrait.Dim = s.Away;
        _portrait.Alert = s.StarvingTicks > 0 || sim.IsWorsening(s) || !s.HasBed;
        _set.Text(_name, s.Name + (s.PartnerId is not null ? " ♥" : ""));
        var job = ColonyText.Job(sim, s);
        var age = s.IsChild ? $"{s.LifeStage.ToString().ToLowerInvariant()}, {ColonyText.AgeYears(s, rules)}y" : $"{ColonyText.AgeYears(s, rules)}y";
        _set.Text(_job, $"{job} · {age}");
        _set.Tooltip(_job, $"{job}\n{s.LifeStage}, {ColonyText.AgeYears(s, rules)} years");
        _set.Texture(_jobIcon, JobIcon(sim, s));
        _health.Set(s.Health / Survivor.MaxHealth, $"{s.Health:0}");
        _mood.Set(s.Mood / Survivor.MaxMood, $"{s.Mood:0}");
        _set.Tooltip(_health, $"Health {s.Health:0}/{Survivor.MaxHealth:0}" + (s.Health < Survivor.MaxHealth ? ": below full it slows work" : ""));
        _set.Tooltip(_mood, $"Mood {s.Mood:0}/{Survivor.MaxMood:0}: {ColonyText.Mood(s)}");
        var xp = sim.Data.Progression.Xp;
        _set.Visible(_level, !s.IsInfant);
        _level.Set(s.Level, Progression.LevelProgress(s, xp), s.PerkPoints > 0);
        _set.Tooltip(_level, s.Level >= xp.MaxLevel ? $"Level {s.Level} (max)"
            : $"Level {s.Level}: {Fmt.Amount(s.Xp)}/{Fmt.Amount(Progression.XpForLevel(s.Level + 1, xp))} XP to the next" +
              (s.PerkPoints > 0 ? $"\n{s.PerkPoints} perk point{(s.PerkPoints == 1 ? "" : "s")} to spend" : ""));
        RefreshBadges(sim, s);
        _set.Tooltip(this, _compact
            ? $"{s.Name} · {job}\nHealth {s.Health:0} · Mood {s.Mood:0}\nClick for details, right-click for actions."
            : $"{s.Name}, {s.LifeStage.ToString().ToLowerInvariant()} · {job}\nClick for details, right-click for actions, drag onto a building to assign.");
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
        foreach (var (buff, active) in FoodEffects.Active(s, sim.Data).OrderBy(x => x.Def.Debuff).ThenBy(x => x.Def.Id))
            badges.Add((FoodText.BuffIcon(buff.Id), buff.Debuff ? (buff.Id == "sick" ? Palette.Danger : Palette.Warn) : Palette.Good,
                FoodText.BuffTip(buff) + (active.Until - sim.State.Tick > 1 ? $"\n{ColonyText.Duration((int)(active.Until - sim.State.Tick), rules)} left" : "")));
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
                _homeless = Widgets.Label("", "Number", Palette.Danger, 13);
                _homeless.TooltipText = "Time left before they leave without a bed";
                _homeless.MouseFilter = MouseFilterEnum.Stop;
                _badges.AddChild(_homeless);
            }
        }
        if (_homeless is not null) _set.Text(_homeless, ColonyText.Duration(left, rules));
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

/// <summary>A survivor's level as a small numbered pip ringed by the XP toward the next level; amber dot when perk points wait.</summary>
public partial class LevelPip : Control
{
    private int _level;
    private float _progress;
    private bool _points;

    public LevelPip()
    {
        CustomMinimumSize = new Vector2(20, 20);
        MouseFilter = MouseFilterEnum.Stop;
        SizeFlagsVertical = SizeFlags.ShrinkCenter;
    }

    public void Set(int level, double progress, bool points)
    {
        var p = Mathf.Clamp((float)progress, 0, 1);
        if (level == _level && Mathf.IsEqualApprox(p, _progress) && points == _points) return;
        _level = level;
        _progress = p;
        _points = points;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var c = Size / 2;
        var r = Mathf.Min(Size.X, Size.Y) / 2 - 1.5f;
        DrawCircle(c, r, Palette.Inset);
        DrawArc(c, r, 0, Mathf.Tau, 24, Palette.EdgeLight, 1.5f, true);
        if (_progress > 0) DrawArc(c, r, -Mathf.Pi / 2, -Mathf.Pi / 2 + Mathf.Tau * _progress, 24, Palette.Amber, 2, true);
        var text = _level.ToString();
        var font = Fonts.Numbers;
        const int size = 12;
        var w = font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X;
        DrawString(font, new Vector2(c.X - w / 2, c.Y + size * 0.36f), text, HorizontalAlignment.Left, -1, size, Palette.Text);
        if (_points) DrawCircle(new Vector2(Size.X - 3, 3), 3, Palette.Amber);
    }
}
