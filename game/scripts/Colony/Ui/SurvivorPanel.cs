using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;
using Remnants.Core.Colony;
using Remnants.Core.Help;
using Remnants.Core.Survivors;
using Remnants.Game.Hud;
using Remnants.Game.Hud.Help;

namespace Remnants.Game.Colony.Ui;

/// <summary>
/// The selected survivor in detail, three tabs (UX audit 5): <b>Overview</b> (health and mood as labelled bars, needs and
/// status, nutrition, the job picker that assigns on pick, the output line; then <b>Body</b> and <b>Family</b> as
/// folding sections: only the parts that are not healthy, conditions with their treatments, humanity; family ties and
/// matchmaking with the current stance shown), <b>Skills</b> (attributes as a 2×3 grid, trained skills by value, the
/// untrained folded away) and <b>Perks</b> (<see cref="PerksTab"/>). Every non-obvious number has an (i). Reads Core; job
/// and matchmaking go through the session as commands. Rows are built once and updated in place.
/// </summary>
public partial class SurvivorPanel : PanelContainer
{
    private const int OverviewTab = 0, SkillsTab = 1, PerksTabIndex = 2;

    private readonly ColonySession _session;
    private readonly Unchanged _set = new();
    private readonly Portrait _portrait = new(64);
    private readonly Label _name = Widgets.Label("", "Display");
    private readonly Label _sub = Widgets.Label("", "Muted");
    private readonly TabStrip _tabs = new();
    private readonly ScrollContainer _scroll = new() { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
    private readonly List<Control> _pages = new();

    // Overview
    private readonly StatBar _health = new(null);
    private readonly StatBar _mood = new(null);
    private readonly RichTextLabel _overview = Ui.Rich();
    private readonly HBoxContainer _nutritionRow;
    private readonly Label _nutrition = Widgets.Label("", "");
    private readonly Label _diet = Widgets.Wrapped("", "Small");
    private readonly OptionButton _jobs = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill, FocusMode = FocusModeEnum.All, ClipText = true, FitToLongestItem = false };
    private readonly Label _jobNote = Widgets.Wrapped("", "Small");
    private readonly List<int?> _jobBuildingIds = new();
    private readonly Label _output = Widgets.Wrapped("", "");
    private readonly HBoxContainer _outputRow;
    private readonly Expander _body;
    private readonly Expander _familyBox;

    // Skills
    private readonly Dictionary<AttributeKind, Label> _attributes = new();
    private readonly Dictionary<Skill, (Control Row, StatBar Bar)> _skills = new();
    private readonly VBoxContainer _trained = Widgets.Column(Space.Xs);
    private readonly VBoxContainer _untrained = Widgets.Column(Space.Xs);
    private readonly Expander _untrainedBox;
    private string _skillOrder = "";

    // Body
    private readonly Dictionary<BodyPart, (Control Row, TextureRect Icon, Label Text, HBoxContainer Chips, StatBar Bar, Label Detail)> _parts = new();
    private readonly Label _allHealthy = Widgets.Label("All healthy.", "Muted");
    private readonly HBoxContainer _humanityRow;
    private readonly StatBar _humanity = new("body_prosthetic");
    private readonly Label _humanityText = Widgets.Wrapped("", "Small");

    // Family
    private readonly RichTextLabel _family = Ui.Rich();
    private readonly OptionButton _pairWith = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill, FocusMode = FocusModeEnum.All, ClipText = true, FitToLongestItem = false };
    private readonly List<int> _pairIds = new();
    private readonly Dictionary<PairStance, Button> _stances = new();

    // Perks
    private readonly PerksTab _perks;

    public int? SurvivorId { get; private set; }

    public event Action? Closed;

    public SurvivorPanel(ColonySession session)
    {
        _session = session;
        CustomMinimumSize = new Vector2(PanelWidth.Side, 0);
        SizeFlagsVertical = SizeFlags.ShrinkBegin;
        Visible = false;
        var box = Ui.Panel(this, Space.Sm);

        var head = Widgets.Row(Space.Md);
        head.AddChild(_portrait);
        var names = Widgets.Column(0);
        names.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _name.ClipText = true;
        _name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        names.AddChild(_name);
        names.AddChild(_sub);
        head.AddChild(names);
        var close = Widgets.IconButton("ui_close", "", "Close", () => Closed?.Invoke(), "Esc", "Flat");
        close.SizeFlagsVertical = SizeFlags.ShrinkBegin;
        head.AddChild(close);
        box.AddChild(head);

        _tabs.Add("Overview", "Health, mood, job, body and family");
        _tabs.Add("Skills", "Attributes and skills");
        _tabs.Add("Perks", "Level, XP and the perk tree");
        _tabs.Changed += OnTab;
        box.AddChild(_tabs);
        var pages = Widgets.Column(0);
        pages.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _scroll.AddChild(pages);
        box.AddChild(_scroll);

        _nutritionRow = InfoTip.Beside(_nutrition, "food_buffs", Context);
        _outputRow = InfoTip.Beside(_output, "jobs_output", Context);
        _humanityRow = Widgets.Row(Space.Sm);
        _body = new Expander("Body", BodySection(), extra: new InfoTip("injuries", Context));
        _familyBox = new Expander("Family", FamilySection(), extra: new InfoTip("family", Context));
        _untrainedBox = new Expander("Untrained", _untrained);
        _body.Toggled += _ => Refresh();
        _familyBox.Toggled += _ => Refresh();
        _untrainedBox.Toggled += _ => FitHeight();
        AddPage(pages, Overview());
        AddPage(pages, Skills());
        _perks = new PerksTab(session);
        AddPage(pages, _perks);
        Fit.OnParentResized(this, FitHeight);
    }

    private void AddPage(VBoxContainer pages, Control page)
    {
        page.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        page.Visible = _pages.Count == 0;
        _pages.Add(page);
        pages.AddChild(page);
    }

    private HelpContext Context() => new()
    {
        Colony = _session.Sim,
        Survivor = SurvivorId is { } id ? _session.Sim.State.FindSurvivor(id) : null,
    };

    private Control Overview()
    {
        var box = Widgets.Column(Space.Sm);
        _health.TooltipText = "Health. Below full it slows work; at zero they die.";
        _mood.TooltipText = "Mood. Low mood slows work; grief and heartbreak lower it, it recovers over time.";
        box.AddChild(Gauge("Health", _health, "health"));
        box.AddChild(Gauge("Mood", _mood, "mood"));
        box.AddChild(_overview);
        _nutrition.MouseFilter = MouseFilterEnum.Stop;
        box.AddChild(_nutritionRow);
        box.AddChild(_diet);

        box.AddChild(Widgets.SectionHeader("Job", "ui_build", new InfoTip("shifts", Context)));
        _jobs.ItemSelected += PickJob;
        _jobs.TooltipText = "Pick a workplace: they start there at once. \"Idle\" takes them off work.";
        box.AddChild(_jobs);
        _jobNote.AddThemeColorOverride("font_color", Palette.TextMuted);
        box.AddChild(_jobNote);
        _output.MouseFilter = MouseFilterEnum.Stop;
        _output.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        box.AddChild(_outputRow);
        box.AddChild(_body);
        box.AddChild(_familyBox);
        return box;
    }

    /// <summary>"Health ▬▬▬▬ 100 (i)": a labelled bar with its number and the topic behind it.</summary>
    private Control Gauge(string name, StatBar bar, string topic)
    {
        var row = Widgets.Row(Space.Sm);
        var label = Widgets.Label(name, "Muted");
        label.CustomMinimumSize = new Vector2(64, 0);
        row.AddChild(label);
        bar.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(bar);
        row.AddChild(new InfoTip(topic, Context));
        return row;
    }

    private Control Skills()
    {
        var box = Widgets.Column(Space.Sm);
        box.AddChild(Widgets.SectionHeader("Attributes", "attr_strength", new InfoTip("attributes", Context)));
        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", Space.Md);
        grid.AddThemeConstantOverride("v_separation", Space.Xs);
        foreach (var a in Enum.GetValues<AttributeKind>())
        {
            var cell = Widgets.Row(Space.Xs);
            cell.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            cell.AddChild(Icons.Rect(Icons.Attribute(a.ToString(), 16), 16, Palette.TextMuted));
            var name = Widgets.Label(a.ToString(), "Muted");
            name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            cell.AddChild(name);
            var value = Widgets.Label("", "Number");
            value.HorizontalAlignment = HorizontalAlignment.Right;
            value.CustomMinimumSize = new Vector2(28, 0);
            cell.AddChild(value);
            _attributes[a] = value;
            grid.AddChild(cell);
        }
        box.AddChild(grid);
        box.AddChild(Widgets.SectionHeader("Skills", "skill_crafting", new InfoTip("skills", Context)));
        box.AddChild(Widgets.Label("0–100 · best first · grow by working and studying", "Small"));
        box.AddChild(_trained);
        foreach (var k in Enum.GetValues<Skill>())
        {
            var bar = new StatBar(null);
            var row = Labelled(_session.Sim.Data.SkillInfo(k).Name, Icons.Skill(k.ToString(), 16), bar);
            row.TooltipText = SkillTip(k);
            row.MouseFilter = MouseFilterEnum.Stop;
            _skills[k] = (row, bar);
            _trained.AddChild(row);
        }
        box.AddChild(_untrainedBox);
        return box;
    }

    private Control BodySection()
    {
        var box = Widgets.Column(Space.Sm);
        _humanityRow.AddChild(Widgets.Label("Humanity", "Muted"));
        _humanity.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _humanityRow.AddChild(_humanity);
        _humanityRow.AddChild(new InfoTip("humanity", Context));
        box.AddChild(_humanityRow);
        _humanityText.AddThemeColorOverride("font_color", Palette.TextMuted);
        box.AddChild(_humanityText);
        box.AddChild(_allHealthy);
        foreach (var part in Enum.GetValues<BodyPart>())
        {
            var row = Widgets.Row(Space.Sm);
            var icon = Icons.Rect(Icons.BodyPart(part.ToString(), 22), 22, Palette.TextMuted);
            icon.SizeFlagsVertical = SizeFlags.ShrinkBegin;
            row.AddChild(icon);
            // "Left arm  [Infection 54%] ▬▬▬ hurt 45%", the item / tier / upkeep line under it.
            var col = Widgets.Column(0);
            col.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            var line = Widgets.Row(Space.Xs);
            var text = Widgets.Label(CyberText.Part(part), "");
            text.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            text.ClipText = true;
            text.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            var chips = Widgets.Row(2);
            var bar = new StatBar(null, 70);
            bar.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            bar.CustomMinimumSize = new Vector2(132, 0);
            var detail = Widgets.Wrapped("", "Small", Palette.TextMuted);
            line.AddChild(text);
            line.AddChild(chips);
            line.AddChild(bar);
            col.AddChild(line);
            col.AddChild(detail);
            row.AddChild(col);
            _parts[part] = (row, icon, text, chips, bar, detail);
            box.AddChild(row);
        }
        BuildConditions(box);
        return box;
    }

    private Control FamilySection()
    {
        var box = Widgets.Column(Space.Sm);
        box.AddChild(_family);
        box.AddChild(Widgets.SectionHeader("Matchmaking", "stat_health"));
        _pairWith.ItemSelected += _ => RefreshStance();
        box.AddChild(_pairWith);
        // A segmented control: the pair's current stance is the pressed segment.
        var segments = Widgets.Row(2);
        var group = new ButtonGroup();
        foreach (var (stance, label, tip) in new[]
                 {
                     (PairStance.Encourage, "Match", "Encourage these two: they grow closer faster."),
                     (PairStance.Neutral, "Let be", "Stop encouraging or discouraging."),
                     (PairStance.Discourage, "Apart", "Keep these two apart (splits a couple)."),
                 })
        {
            var button = Motion.OnPress(new Button
            {
                Text = label, TooltipText = tip, ThemeTypeVariation = "Tab", ToggleMode = true, ButtonGroup = group,
                FocusMode = FocusModeEnum.All, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 28),
                MouseDefaultCursorShape = CursorShape.PointingHand,
            });
            button.Pressed += () => Matchmake(stance);
            _stances[stance] = button;
            segments.AddChild(button);
        }
        box.AddChild(segments);
        return box;
    }

    private string SkillTip(Skill k)
    {
        var info = _session.Sim.Data.SkillInfo(k);
        var learn = string.Join(" + ", info.Attributes.Keys);
        var at = _session.Sim.Data.Progression.Study.Buildings.Where(kv => kv.Value.Skills.Contains(k))
            .Select(kv => _session.Sim.Data.Buildings[kv.Key].Name).FirstOrDefault();
        return $"{info.Name}{(info.Description.Length > 0 ? "\n" + info.Description : "")}" +
               (learn.Length > 0 ? $"\nLearned faster with {learn}." : "") + (at is null ? "" : $"\nStudied at the {at}.");
    }

    private static Control Labelled(string name, Texture2D icon, StatBar bar)
    {
        var row = Widgets.Row(Space.Sm);
        row.AddChild(Icons.Rect(icon, 16, Palette.TextMuted));
        var label = Widgets.Label(name, "Muted");
        label.CustomMinimumSize = new Vector2(104, 0);
        row.AddChild(label);
        bar.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(bar);
        return row;
    }

    /// <summary>
    /// Opens a tab by its old index (0 Overview, 1 Skills, 2 Body, 3 Family, 4 Perks): Body and Family are now Overview
    /// sections, opened and scrolled to.
    /// </summary>
    public void ShowTab(int index)
    {
        switch (index)
        {
            case 2 or 3:
                _tabs.Select(OverviewTab);
                var section = index == 2 ? _body : _familyBox;
                section.SetOpen(true);
                _scroll.CallDeferred(ScrollContainer.MethodName.EnsureControlVisible, section);
                break;
            case 4: _tabs.Select(PerksTabIndex); break;
            default: _tabs.Select(Mathf.Clamp(index, 0, 1)); break;
        }
    }

    private void OnTab(int tab)
    {
        for (var i = 0; i < _pages.Count; i++)
        {
            if (i == tab) Motion.Reveal(_pages[i]);
            else _pages[i].Visible = false;
        }
        _scroll.ScrollVertical = 0;
        Refresh();
    }

    public void Show(int? survivorId)
    {
        var changed = survivorId != SurvivorId;
        SurvivorId = survivorId;
        if (changed)
        {
            _jobs.Clear(); // rebuilt for the new survivor, picking their current job
            _jobBuildingIds.Clear();
            _skillOrder = "";
        }
        if (survivorId is null) Visible = false;
        else Motion.Reveal(this, new Vector2(-12, 0));
        Refresh();
    }

    public void Refresh()
    {
        var sim = _session.Sim;
        var s = SurvivorId is { } id ? sim.State.FindSurvivor(id) : null;
        if (s is null)
        {
            Visible = false;
            return;
        }
        if (!Visible) return;
        var rules = sim.Data.Rules;
        _portrait.Set(PortraitStudio.For(GetTree(), s), PortraitStudio.Backdrop(s.Id));
        _portrait.Dim = s.Away;
        _set.Text(_name, s.Name);
        _set.Text(_sub, $"{s.Sex} · {s.LifeStage} · {ColonyText.AgeYears(s, rules)} years · {ColonyText.Job(sim, s)}");
        _tabs.SetTitle(PerksTabIndex, s.PerkPoints > 0 ? $"Perks ({s.PerkPoints})" : "Perks");

        // Only the open tab is kept current: the others catch up when opened.
        switch (_tabs.Selected)
        {
            case OverviewTab:
                _health.Set(s.Health / Survivor.MaxHealth, $"{s.Health:0}");
                _mood.Set(s.Mood / Survivor.MaxMood, $"{s.Mood:0} {ColonyText.Mood(s)}");
                var overview = OverviewText(sim, s);
                if (_overview.Text != overview) _overview.Text = overview;
                RefreshNutrition(sim, s);
                RefreshJobs(sim, s);
                _set.Text(_output, OutputLine(sim, s, out var breakdown));
                _set.Tooltip(_output, breakdown);
                _set.Visible(_outputRow, _output.Text.Length > 0);
                RefreshBody(sim, s);
                if (_body.Open) RefreshConditions(sim, s);
                RefreshFamily(sim, s);
                break;
            case SkillsTab:
                RefreshSkills(sim, s);
                break;
            default:
                _perks.Refresh(s.Id);
                break;
        }
        FitHeight();
    }

    /// <summary>As tall as the open tab up to the room the HUD gives; past that the tab scrolls and the header stays.</summary>
    private void FitHeight()
    {
        var page = _pages[_tabs.Selected];
        var want = page.GetCombinedMinimumSize().Y;
        var chrome = GetCombinedMinimumSize().Y - _scroll.CustomMinimumSize.Y;
        var room = GetParentAreaSize().Y - chrome;
        var height = Mathf.Round(room > 0 ? Math.Min(want, room) : want);
        if (Math.Abs(_scroll.CustomMinimumSize.Y - height) >= 1) _scroll.CustomMinimumSize = new Vector2(0, height);
    }

    private static string OverviewText(ColonySim sim, Survivor s)
    {
        var rules = sim.Data.Rules;
        var lines = new List<string>();
        var needs = ColonyText.Needs(s, rules);
        lines.Add(needs == "fed, housed" ? Palette.Paint("Fed and housed", Palette.Good) : Palette.Paint("⚠ " + needs, Palette.Danger));
        if (s.Away) lines.Add("Away on an expedition.");
        if (s.PatientAtBuildingId is not null)
            lines.Add($"In the Med Bay, healed in {(sim.HealEta(s) is { } eta ? ColonyText.Duration(eta, rules) : "—")}.");
        else if (sim.IsWorsening(s)) lines.Add(Palette.Paint("⚠ A severe wound is getting worse: admit to the Med Bay.", Palette.Danger));
        if (s.Pregnancy is not null) lines.Add(Palette.Paint("Expecting: see Family below.", Palette.Amber));
        if (sim.ClassOf(s) is { } study) lines.Add(ClassLine(sim, s, study));
        var xp = sim.Data.Progression.Xp;
        lines.Add($"Level {s.Level} · {Fmt.Amount(s.Xp)} XP" + (s.Level < xp.MaxLevel ? Palette.Paint($" (next at {Fmt.Amount(Progression.XpForLevel(s.Level + 1, xp))})", Palette.TextMuted) : "")
                  + (s.PerkPoints > 0 ? "  " + Palette.Paint($"★ {s.PerkPoints} perk point{(s.PerkPoints == 1 ? "" : "s")}: see Perks", Palette.Amber) : ""));
        if (s.IsChild) lines.Add($"Growing: {ColonyText.Stage(s, rules)}");
        return string.Join("\n", lines);
    }

    private void RefreshNutrition(ColonySim sim, Survivor s)
    {
        var food = sim.Data.Food;
        _set.Visible(_nutritionRow, food is not null);
        _set.Visible(_diet, food is not null);
        if (food is null) return;
        var low = s.Nutrition < food.Rules.Nutrition.MalnourishedBelow;
        _set.Text(_nutrition, $"Nutrition {s.Nutrition * 100:0}%");
        _set.FontColor(_nutrition, low ? Palette.Warn : Palette.Text);
        _set.Tooltip(_nutrition, low ? "Malnourished: a varied, filling diet brings it back up." : "How well fed they are lately; variety and good meals keep it high.");
        var lines = new List<string>();
        if (s.Diet.Count > 0) lines.Add($"Lately: {string.Join(", ", s.Diet.OrderByDescending(kv => kv.Value).Take(3).Select(kv => FoodText.ItemName(sim, kv.Key)))}");
        foreach (var (buff, active) in FoodEffects.Active(s, sim.Data))
            lines.Add($"{(buff.Debuff ? "▼" : "▲")} {buff.Name}: {buff.Description}" + (active.Until - sim.State.Tick > 1 ? $" ({ColonyText.Duration((int)(active.Until - sim.State.Tick), sim.Data.Rules)} left)" : ""));
        _set.Text(_diet, string.Join("\n", lines));
        _set.Visible(_diet, lines.Count > 0);
    }

    /// <summary>"In class: studying Hacking with Bram at the Electronics Lab, done in 2d 3h" (or teaching, for a mentor).</summary>
    internal static string ClassLine(ColonySim sim, Survivor s, StudyOrder study)
    {
        var subject = study.Skill is { } k ? sim.Data.SkillInfo(k).Name : sim.Data.Perks[study.PerkId!].Name;
        var at = sim.State.FindBuilding(study.BuildingId) is { } b ? sim.Data.Buildings[b.DefId].Name : "class";
        var eta = sim.StudyEta(study) is { } t ? ColonyText.Duration(t, sim.Data.Rules) : "—";
        if (study.StudentId != s.Id)
            return Palette.Paint($"Teaching {ColonyText.PersonName(sim.State, study.StudentId)} {subject} at the {at} (done in {eta}).", Palette.Amber);
        var mentor = ColonyText.PersonName(sim.State, study.MentorId);
        return Palette.Paint($"In class: studying {subject}{(mentor is null ? "" : $" with {mentor}")} at the {at} (done in {eta}).", Palette.Amber);
    }

    /// <summary>
    /// "Makes 0.62 Wood/h on shift · 6.4/day" and, as the tooltip, the job output formula with this survivor's numbers
    /// (<see cref="JobOutput"/>): base × level × performance × tool × health × life stage × mood. The (i) beside it opens
    /// the same breakdown as a popover.
    /// </summary>
    private static string OutputLine(ColonySim sim, Survivor s, out string breakdown)
    {
        breakdown = "";
        if (sim.ClassOf(s) is not null) return "";
        if (s.AssignedBuildingId is not { } id || sim.State.FindBuilding(id) is not { } b) return "";
        var def = sim.Data.Buildings[b.DefId];
        if (def.Job is null) return "";
        var job = sim.Data.Jobs[def.Job];
        var rules = sim.Data.Rules.Output;
        var perf = JobOutput.Performance(job, s, rules);
        var tool = JobOutput.ToolFactor(job, s);
        var health = JobOutput.HealthFactor(job, s, rules);
        var life = JobOutput.LifeFactor(s, rules);
        var mood = JobOutput.MoodFactor(s, rules);
        var perks = JobOutput.PerkFactor(job, s, sim.Data);
        var level = def.Level(b.Level).OutputMultiplier;
        var perTick = JobOutput.PerTick(def, b.Level, job, s, rules, sim.Data);
        var unit = def.Produces is { } res ? sim.Data.Resources[res].Name : "work";
        var hours = Schedule.ShiftHoursPerDay(s, b, def, sim.Data.Rules);
        var tired = sim.IsTired(s) ? $"\n  tired: {Fmt.Factor(sim.Data.Rules.DayNight.Tired.OutputMultiplier)} until they sleep" : "";
        var attrs = string.Join(", ", job.Attributes.Select(kv => $"{kv.Key} {s.Get(kv.Key):0}"));
        breakdown = $"Output per hour = base × level × performance × tool × health × life × mood × perks\n" +
                    $"  base {def.BaseOutput:0.##}\n" +
                    $"  level {b.Level}: {Fmt.Factor(level)}\n" +
                    $"  performance {Fmt.Factor(perf)}  ({attrs}; {job.Skill} skill {s.Get(job.Skill):0})\n" +
                    $"  tool {Fmt.Factor(tool)}\n" +
                    $"  health {Fmt.Factor(health)}  (HP {s.Health:0}, body)\n" +
                    $"  life stage {Fmt.Factor(life)}\n" +
                    $"  mood {Fmt.Factor(mood)}  (mood {s.Mood:0})\n" +
                    $"  perks {Fmt.Factor(perks)}\n" +
                    $"= {perTick:0.###} {unit}/h on shift · {hours:0.#} shift hours a day = {perTick * hours:0.#}/day{tired}\nWorking trains {job.Skill}.";
        return $"Makes {perTick:0.##} {unit}/h on shift · {perTick * hours:0.#}/day";
    }

    private void RefreshSkills(ColonySim sim, Survivor s)
    {
        foreach (var (a, label) in _attributes) _set.Text(label, $"{s.Get(a):0}");
        foreach (var (k, (_, bar)) in _skills) bar.Set(s.Get(k) / Survivor.MaxStat, $"{s.Get(k):0}", Palette.Amber);
        // Trained skills best first; the untrained (under 1) fold away. Reordered only when the order changes.
        var order = _skills.Keys.OrderByDescending(k => s.Get(k)).ThenBy(k => k).ToList();
        var signature = string.Join(",", order.Select(k => $"{k}:{s.Get(k) >= 1}"));
        if (signature == _skillOrder) return;
        _skillOrder = signature;
        var untrained = 0;
        foreach (var k in order)
        {
            var row = _skills[k].Row;
            var target = s.Get(k) >= 1 ? _trained : _untrained;
            if (row.GetParent() != target) row.Reparent(target, false);
            target.MoveChild(row, -1);
            if (target == _untrained) untrained++;
        }
        _untrainedBox.Visible = untrained > 0;
        _untrainedBox.SetSummary($"{untrained} at 0");
    }

    private void RefreshBody(ColonySim sim, Survivor s)
    {
        var cyber = sim.Data.Cybernetics;
        var humanity = s.Body.Humanity;
        if (cyber is not null)
        {
            var (band, color) = CyberText.Humanity(cyber.Humanity, humanity);
            _humanity.Set(humanity / Body.MaxHumanity, $"{humanity:0}", color);
            _set.Text(_humanityText, band switch
            {
                "whole" => "Whole: their prosthetics have not changed who they are.",
                "strained" => $"Strained: mood cannot rise above {Cybernetics.MoodCap(cyber.Humanity, humanity):0}.",
                _ => $"Detached: mood at most {Cybernetics.MoodCap(cyber.Humanity, humanity):0}; their partner drifts away and their children suffer.",
            });
            _set.Tooltip(_humanity, $"Humanity {humanity:0}/100: 100 minus what each prosthetic costs (more for an elective augmentation). " +
                                    $"Below {cyber.Humanity.MoodCapFrom:0} mood is capped; below {cyber.Humanity.Detached:0} partners and children suffer.");
        }
        // Humanity only matters once someone carries a prosthetic.
        var prosthetic = s.Body.Parts.Values.Any(p => p.Condition == PartCondition.Prosthetic);
        _set.Visible(_humanityRow, cyber is not null && (prosthetic || humanity < Body.MaxHumanity));
        _set.Visible(_humanityText, _humanityRow.Visible);
        var med = sim.Data.Medicine;
        var hurt = 0;
        var notes = new List<string>();
        foreach (var (part, (row, icon, text, chips, bar, detail)) in _parts)
        {
            var p = s.Body[part];
            var name = CyberText.Part(part);
            var shown = p.Condition != PartCondition.Healthy;
            _set.Visible(row, shown);
            if (!shown) continue;
            hurt++;
            var tags = new List<(string Text, Color Color, string Topic, string Tip)>();
            switch (p.Condition)
            {
                case PartCondition.Injured:
                    var worsening = p.Severity >= sim.Data.Rules.Medical.WorsenThreshold;
                    _set.Text(text, name);
                    _set.Tooltip(text, worsening ? "Worsening: admit to the Med Bay." : p.Dressing switch
                    {
                        Dressing.Clean => "Injured · clean dressing",
                        Dressing.Bandaged => "Injured · bandaged",
                        _ => "Injured · undressed: it can get infected",
                    });
                    if (med is not null && s.Body.Conditions.FirstOrDefault(c => c.Part == part && med.Condition(c.Id)?.Kind == Remnants.Core.Data.ConditionKind.Infection) is { } infection)
                        tags.Add(($"Infection {ColonyText.Percent(infection.Severity)}", Palette.Danger, "infection", "Infected: needs antibiotics, or it spreads."));
                    if (med is not null && s.Body.Find(med.Fracture.Condition, part) is { } f)
                        tags.Add((f.Treated ? "Splinted" : "Fractured", f.Treated ? Palette.TextMuted : Palette.Warn, "injuries", f.Treated ? "Splinted: heals in time." : "Fractured: a splint lets it heal."));
                    bar.Set(1 - p.Severity, $"hurt {ColonyText.Percent(p.Severity)}", worsening ? Palette.Danger : Palette.Warn);
                    _set.Modulate(icon, Palette.Danger);
                    _set.Visible(detail, worsening);
                    if (worsening)
                    {
                        _set.Text(detail, "Worsening: admit to the Med Bay.");
                        _set.FontColor(detail, Palette.Danger);
                    }
                    break;
                case PartCondition.Missing:
                    _set.Text(text, name);
                    _set.Tooltip(text, "");
                    bar.Set(0, "lost", Palette.Danger);
                    _set.Modulate(icon, Palette.TextDim);
                    _set.Visible(detail, true);
                    _set.Text(detail, "Missing: fit a prosthetic at the Med Bay.");
                    _set.FontColor(detail, Palette.TextMuted);
                    break;
                default:
                    var item = p.ProstheticItemId is { } pid && sim.Scavenge?.Items.GetValueOrDefault(pid) is { } def ? def.Name : "prosthetic";
                    _set.Text(text, name);
                    bar.Set(Math.Min(1, p.Efficiency), $"{ColonyText.Percent(p.Efficiency)} eff.", p.Efficiency >= 1 ? Palette.Good : Palette.Warn);
                    _set.Modulate(icon, Palette.Amber);
                    var lines = new List<string> { item };
                    if (cyber is not null && CyberText.Node(cyber, p) is { } node)
                    {
                        lines.Add(CyberText.TierAndSpec(cyber, node) + (p.Elective ? " · augmentation" : "") + $" · −{p.HumanityCost:0} humanity");
                        if (CyberText.Upkeep(cyber, p, sim.Data.Rules.TicksPerDay) is { } upkeep) lines.Add(upkeep);
                        _set.Modulate(icon, CyberText.Tint(cyber, node.Tier).Lerp(Palette.Amber, 0.3f));
                        _set.Tooltip(text, string.Join(" · ", CyberText.ItemStats(sim.Scavenge!.Items[node.Id]).Concat(CyberText.Effects(node.Effects, sim.Data))));
                        text.MouseFilter = MouseFilterEnum.Stop;
                    }
                    _set.Visible(detail, true);
                    _set.Text(detail, string.Join(" · ", lines));
                    _set.FontColor(detail, p.Upkeep < 1 - 1e-9 ? Palette.Warn : Palette.TextMuted);
                    hurt--; // a working prosthetic is not hurt
                    break;
            }
            SetTags(chips, tags);
            notes.AddRange(tags.Where(t => t.Topic == "infection").Select(t => t.Text.ToLowerInvariant()));
        }
        _set.Visible(_allHealthy, _parts.Values.All(x => !x.Row.Visible));
        var conditions = s.Body.Conditions.Count;
        _body.SetSummary(hurt == 0 && conditions == 0 ? "All healthy" : string.Join(" · ", new[]
            {
                hurt > 0 ? $"{hurt} part{(hurt == 1 ? "" : "s")} hurt" : "",
                conditions > 0 ? $"{conditions} condition{(conditions == 1 ? "" : "s")}" : "",
            }.Where(x => x.Length > 0)), hurt > 0 || conditions > 0 ? Palette.Warn : Palette.Good);
    }

    /// <summary>Small chips on a body row ("Infection 54%" with its (i)); reuses the chips it has.</summary>
    private void SetTags(HBoxContainer chips, List<(string Text, Color Color, string Topic, string Tip)> tags)
    {
        while (chips.GetChildCount() < tags.Count)
        {
            var chip = Widgets.Chip(null, "");
            var tip = new InfoTip("", Context);
            chip.GetChild(0).AddChild(tip);
            chips.AddChild(chip);
        }
        for (var i = 0; i < chips.GetChildCount(); i++)
        {
            var chip = chips.GetChild<PanelContainer>(i);
            _set.Visible(chip, i < tags.Count);
            if (i >= tags.Count) continue;
            var row = chip.GetChild(0);
            var label = row.GetChild<Label>(0);
            label.AddThemeFontSizeOverride("font_size", 13);
            _set.Text(label, tags[i].Text);
            _set.FontColor(label, tags[i].Color);
            _set.Tooltip(chip, tags[i].Tip);
            row.GetChild<InfoTip>(1).Topic = tags[i].Topic;
        }
    }

    private static string FamilyText(ColonySim sim, Survivor s)
    {
        var rules = sim.Data.Rules;
        var text = new StringBuilder();
        if (s.IsChild)
        {
            var next = s.IsInfant ? "child" : "adult";
            var eta = Bonds.TicksToNextStage(s, rules.Life) is { } t ? ColonyText.Duration((int)t, rules) : "—";
            var hungry = s.MalnourishedTicks > 0 ? "  " + Palette.Paint($"(hungry {s.MalnourishedTicks}h: grows up weaker)", Palette.Danger) : "";
            text.AppendLine($"[b]Growing[/b]  {ColonyText.Stage(s, rules)} · {next} in {eta}{hungry}");
        }
        if (s.Pregnancy is { } p)
        {
            var father = ColonyText.PersonName(sim.State, p.FatherId);
            var due = ColonyText.Duration((int)(Bonds.TicksToBirth(sim.State, s) ?? 0), rules);
            text.AppendLine(Palette.Paint($"[b]Expecting[/b]{(father is null ? "" : $" with {father}")}: due in {due}", Palette.Amber));
        }
        if (ColonyText.PersonName(sim.State, s.PartnerId) is { } partner) text.AppendLine($"[b]Partner[/b]  ♥ {partner}");
        else if (!s.IsChild)
        {
            var close = Bonds.Closest(sim.State, s).Where(x => x.Bond.Affinity > 0).Take(3)
                .Select(x => $"{x.Other.Name} {ColonyText.Percent(x.Bond.Affinity / rules.Relationships.CoupleThreshold)}").ToList();
            text.AppendLine(close.Count == 0 ? "[b]Single[/b]" : $"[b]Single[/b] · close to {string.Join(", ", close)}");
        }
        var parents = new[] { s.MotherId, s.FatherId }.Select(id => ColonyText.PersonName(sim.State, id)).OfType<string>().ToList();
        if (parents.Count > 0) text.AppendLine($"[b]Parents[/b]  {string.Join(", ", parents)}");
        var children = Bonds.Children(sim.State, s).Select(c => $"{c.Name} ({c.LifeStage.ToString().ToLowerInvariant()})").ToList();
        if (children.Count > 0) text.AppendLine($"[b]Children[/b]  {string.Join(", ", children)}");
        if (text.Length == 0) text.Append(Palette.Paint("No family ties yet.", Palette.TextMuted));
        return text.ToString().TrimEnd();
    }

    private void RefreshFamily(ColonySim sim, Survivor s)
    {
        var partner = ColonyText.PersonName(sim.State, s.PartnerId);
        _familyBox.SetSummary(s.Pregnancy is not null ? "Expecting" : partner is not null ? $"♥ {partner}" : s.IsChild ? ColonyText.Stage(s, sim.Data.Rules) : "Single",
            s.Pregnancy is not null ? Palette.Amber : null);
        if (!_familyBox.Open) return;
        var family = FamilyText(sim, s);
        if (_family.Text != family) _family.Text = family;
        RefreshPairs(sim, s);
    }

    /// <summary>The job picker: "Idle" then every workplace with its head count; picking one assigns at once.</summary>
    private void RefreshJobs(ColonySim sim, Survivor s)
    {
        var buildings = sim.State.Buildings.Where(b => sim.Data.Buildings[b.DefId].Job is not null).OrderBy(b => b.Id).ToList();
        var labels = buildings.Select(b =>
            $"{ColonyText.BuildingName(sim, b)}  ({sim.State.WorkersAt(b.Id).Count()}/{sim.Data.Buildings[b.DefId].Level(b.Level).WorkerSlots})").Prepend("Idle (no job)").ToList();

        // Rebuild only on change, and never under an open dropdown.
        var unchanged = _jobs.ItemCount == labels.Count && labels.Select((l, i) => _jobs.GetItemText(i) == l).All(same => same);
        if (!unchanged && !_jobs.GetPopup().Visible)
        {
            _jobs.Clear();
            _jobBuildingIds.Clear();
            _jobs.AddIconItem(Icons.Get("job_idle", 16), labels[0]);
            _jobBuildingIds.Add(null);
            for (var i = 0; i < buildings.Count; i++)
            {
                var def = sim.Data.Buildings[buildings[i].DefId];
                _jobs.AddIconItem(Icons.Building(def.Id, def.Category.ToString(), 16), labels[i + 1]);
                _jobBuildingIds.Add(buildings[i].Id);
            }
        }
        if (!_jobs.GetPopup().Visible)
        {
            var index = _jobBuildingIds.IndexOf(s.AssignedBuildingId);
            if (index >= 0 && _jobs.Selected != index) _jobs.Select(index);
        }

        var why = s.IsInfant ? "Too young to work."
            : s.Away ? "Away on an expedition: the job waits."
            : s.PatientAtBuildingId is not null ? "A patient in the Med Bay: the job waits."
            : sim.ClassOf(s) is not null ? "In class: the job waits until it ends."
            : buildings.Count == 0 ? "No workplaces yet: build one (B)." : "";
        _jobs.Disabled = why.Length > 0;
        _set.Text(_jobNote, why);
        _set.Visible(_jobNote, why.Length > 0);
    }

    private void PickJob(long index)
    {
        if (SurvivorId is not { } id || index < 0 || index >= _jobBuildingIds.Count) return;
        var current = _session.Sim.State.FindSurvivor(id)?.AssignedBuildingId;
        var target = _jobBuildingIds[(int)index];
        if (target == current) return;
        _session.Execute(target is { } b ? new AssignJob(id, b) : new Unassign(id));
        Refresh();
    }

    private void RefreshPairs(ColonySim sim, Survivor s)
    {
        var others = s.IsChild ? new List<Survivor>() : sim.State.Survivors.Where(o => o.Id != s.Id && !o.IsChild).OrderBy(o => o.Id).ToList();
        var labels = others.Select(o =>
        {
            var tie = s.PartnerId == o.Id ? "partner" : "bond " + ColonyText.Percent(Bonds.Affinity(sim.State, s.Id, o.Id) / sim.Data.Rules.Relationships.CoupleThreshold);
            return $"{o.Name} · {tie}";
        }).ToList();

        var unchanged = _pairWith.ItemCount == labels.Count && labels.Select((l, i) => _pairWith.GetItemText(i) == l).All(same => same);
        if (!unchanged && !_pairWith.GetPopup().Visible)
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
        foreach (var b in _stances.Values) b.Disabled = _pairWith.Disabled;
        RefreshStance();
    }

    /// <summary>Presses the segment of the picked pair's current stance.</summary>
    private void RefreshStance()
    {
        if (SurvivorId is not { } id || _pairWith.Selected < 0 || _pairWith.Selected >= _pairIds.Count) return;
        var stance = Bonds.Stance(_session.Sim.State, id, _pairIds[_pairWith.Selected]);
        if (_stances.TryGetValue(stance, out var button) && !button.ButtonPressed) button.SetPressedNoSignal(true);
    }

    private void Matchmake(PairStance stance)
    {
        if (SurvivorId is not { } id || _pairWith.Selected < 0 || _pairWith.Selected >= _pairIds.Count) return;
        _session.Execute(new Matchmake(id, _pairIds[_pairWith.Selected], stance));
        RefreshStance();
    }
}
