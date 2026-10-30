using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;
using Remnants.Core.Colony;
using Remnants.Core.Survivors;
using Remnants.Game.Hud;

namespace Remnants.Game.Colony.Ui;

/// <summary>
/// The selected survivor in detail, in tabs: <b>Overview</b> (health, mood, needs, job with an output breakdown and
/// the job picker), <b>Skills</b> (attributes and skills as bars), <b>Body</b> (each part: healthy, injured with
/// severity, missing, prosthetic), <b>Family</b> (growth, pregnancy, partner, parents, children, closest bonds and
/// matchmaking) and <b>Perks</b> (level, XP, the perk tree; see <see cref="PerksTab"/>). Reads Core; job and matchmaking go through the session as commands.
/// </summary>
public partial class SurvivorPanel : PanelContainer
{
    private readonly ColonySession _session;
    private readonly Portrait _portrait = new(72);
    private readonly Label _name = Widgets.Label("", "Display");
    private readonly Label _sub = Widgets.Label("", "Muted");
    private readonly TabContainer _tabs = new() { CustomMinimumSize = new Vector2(0, 420) };

    // Overview
    private readonly StatBar _health = new("stat_health");
    private readonly StatBar _mood = new("stat_mood");
    private readonly RichTextLabel _overview = Ui.Rich();
    private readonly Label _output = Widgets.Label("", "Muted");
    private readonly OptionButton _jobs = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill, FocusMode = FocusModeEnum.None, ClipText = true, FitToLongestItem = false };
    private readonly Button _assign;
    private readonly Button _unassign;
    private readonly List<int> _jobBuildingIds = new();

    // Skills
    private readonly Dictionary<AttributeKind, StatBar> _attributes = new();
    private readonly Dictionary<Skill, StatBar> _skills = new();

    // Body
    private readonly Dictionary<BodyPart, (TextureRect Icon, Label Text, StatBar Bar, Label Detail)> _parts = new();
    private readonly StatBar _humanity = new("body_prosthetic");
    private readonly Label _humanityText = Widgets.Wrapped("", "Small");

    // Family
    private readonly RichTextLabel _family = Ui.Rich();
    private readonly OptionButton _pairWith = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill, FocusMode = FocusModeEnum.None, ClipText = true, FitToLongestItem = false };
    private readonly List<int> _pairIds = new();
    private readonly Button _encourage;
    private readonly Button _discourage;
    private readonly Button _neutral;

    // Perks
    private readonly PerksTab _perks;

    public int? SurvivorId { get; private set; }

    public event Action? Closed;

    public SurvivorPanel(ColonySession session)
    {
        _session = session;
        CustomMinimumSize = new Vector2(390, 0);
        SizeFlagsVertical = SizeFlags.ShrinkBegin;
        Visible = false;
        var box = Ui.Panel(this, 8);

        var head = Widgets.Row(10);
        head.AddChild(_portrait);
        var names = Widgets.Column(0);
        names.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _name.AddThemeFontSizeOverride("font_size", 24);
        names.AddChild(_name);
        names.AddChild(_sub);
        head.AddChild(names);
        var close = Widgets.IconButton("ui_close", "", "Close", () => Closed?.Invoke(), variation: "Flat");
        close.SizeFlagsVertical = SizeFlags.ShrinkBegin;
        head.AddChild(close);
        box.AddChild(head);
        box.AddChild(_tabs);

        _assign = Widgets.IconButton("stat_check", "", "Assign to the picked building", Assign);
        _unassign = Widgets.IconButton("job_idle", "", "Make idle (unassign)", Unassign);
        _tabs.AddChild(Overview());
        _tabs.AddChild(Skills());
        _tabs.AddChild(BodyTab());
        _encourage = Ui.Button("Match", () => Matchmake(PairStance.Encourage), 60);
        _discourage = Ui.Button("Apart", () => Matchmake(PairStance.Discourage), 60);
        _neutral = Ui.Button("Let be", () => Matchmake(PairStance.Neutral), 60);
        _tabs.AddChild(FamilyTab());
        _perks = new PerksTab(session);
        _tabs.AddChild(Scroll(_perks, "Perks"));
    }

    private Control Overview()
    {
        var box = Widgets.Column(8);
        box.Name = "Overview";
        _health.TooltipText = "Health. Below full it slows work; at zero they die.";
        _mood.TooltipText = "Mood. Low mood slows work; grief and heartbreak lower it, it recovers over time.";
        box.AddChild(_health);
        box.AddChild(_mood);
        box.AddChild(_overview);
        box.AddChild(Widgets.SectionHeader("Job", "ui_build"));
        var row = Widgets.Row(4);
        row.AddChild(_jobs);
        row.AddChild(_assign);
        row.AddChild(_unassign);
        box.AddChild(row);
        _output.MouseFilter = MouseFilterEnum.Stop;
        _output.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        box.AddChild(_output);
        return Scroll(box, "Overview");
    }

    private Control Skills()
    {
        var box = Widgets.Column(6);
        box.AddChild(Widgets.SectionHeader("Attributes", "attr_strength"));
        foreach (var a in Enum.GetValues<AttributeKind>()) box.AddChild(Labelled(a.ToString(), Icons.Attribute(a.ToString(), 16), _attributes[a] = new StatBar(null)));
        box.AddChild(Widgets.SectionHeader("Skills", "skill_crafting"));
        foreach (var k in Enum.GetValues<Skill>())
        {
            var row = Labelled(_session.Sim.Data.SkillInfo(k).Name, Icons.Skill(k.ToString(), 16), _skills[k] = new StatBar(null));
            row.TooltipText = SkillTip(k);
            box.AddChild(row);
        }
        return Scroll(box, "Skills");
    }

    private Control BodyTab()
    {
        var box = Widgets.Column(6);
        box.AddChild(Labelled("Humanity", Icons.Get("stat_mood", 16), _humanity));
        _humanityText.AddThemeColorOverride("font_color", Palette.TextMuted);
        box.AddChild(_humanityText);
        foreach (var part in Enum.GetValues<BodyPart>())
        {
            var row = Widgets.Row(8);
            var icon = Icons.Rect(Icons.BodyPart(part.ToString(), 22), 22, Palette.TextMuted);
            row.AddChild(icon);
            // "Left leg: Cyborg Runner Leg  ▬▬▬ 135%" on one line, the tier / upkeep line under it for prosthetics.
            var col = Widgets.Column(0);
            col.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            var line = Widgets.Row(6);
            var text = Widgets.Label("", "");
            text.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            text.ClipText = true;
            var bar = new StatBar(null, 90);
            bar.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            var detail = Widgets.Label("", "Small", Palette.TextMuted);
            detail.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            line.AddChild(text);
            line.AddChild(bar);
            col.AddChild(line);
            col.AddChild(detail);
            row.AddChild(col);
            _parts[part] = (icon, text, bar, detail);
            box.AddChild(row);
        }
        return Scroll(box, "Body");
    }

    private Control FamilyTab()
    {
        var box = Widgets.Column(8);
        box.AddChild(_family);
        box.AddChild(Widgets.SectionHeader("Matchmaking", "stat_health"));
        box.AddChild(_pairWith);
        var buttons = Widgets.Row(4);
        _encourage.TooltipText = "Encourage these two: they grow closer faster.";
        _discourage.TooltipText = "Keep these two apart (splits a couple).";
        _neutral.TooltipText = "Stop encouraging or discouraging.";
        foreach (var b in new[] { _encourage, _discourage, _neutral })
        {
            b.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            buttons.AddChild(b);
        }
        box.AddChild(buttons);
        return Scroll(box, "Family");
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
        var row = Widgets.Row(6);
        row.AddChild(Icons.Rect(icon, 16, Palette.TextMuted));
        var label = Widgets.Label(name, "", size: 14);
        label.CustomMinimumSize = new Vector2(96, 0);
        row.AddChild(label);
        bar.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(bar);
        return row;
    }

    private static ScrollContainer Scroll(Control content, string name)
    {
        var scroll = new ScrollContainer { Name = name, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        content.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(content);
        return scroll;
    }

    /// <summary>Opens a tab by index (0 Overview, 1 Skills, 2 Body, 3 Family, 4 Perks).</summary>
    public void ShowTab(int index) => _tabs.CurrentTab = Mathf.Clamp(index, 0, _tabs.GetTabCount() - 1);

    public void Show(int? survivorId)
    {
        var changed = survivorId != SurvivorId;
        SurvivorId = survivorId;
        if (changed) _jobs.Select(-1); // default the picker to the new survivor's current job
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
        _name.Text = s.Name;
        _sub.Text = $"{s.Sex} · {s.LifeStage} · {ColonyText.AgeYears(s, rules)} years";

        _health.Set(s.Health / Survivor.MaxHealth, $"{s.Health:0}");
        _mood.Set(s.Mood / Survivor.MaxMood, $"{s.Mood:0} {ColonyText.Mood(s)}");
        _overview.Text = OverviewText(sim, s);
        RefreshJobs(sim, s);
        _output.Text = OutputLine(sim, s, out var breakdown);
        _output.TooltipText = breakdown;

        foreach (var (a, bar) in _attributes) bar.Set(s.Get(a) / Survivor.MaxStat, $"{s.Get(a):0}", Palette.Rust);
        foreach (var (k, bar) in _skills) bar.Set(s.Get(k) / Survivor.MaxStat, $"{s.Get(k):0}", Palette.Amber);
        RefreshBody(sim, s);
        _family.Text = FamilyText(sim, s);
        RefreshPairs(sim, s);
        _perks.Refresh(s.Id);
        _tabs.SetTabTitle(4, s.PerkPoints > 0 ? $"Perks ({s.PerkPoints})" : "Perks");
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
        if (s.Pregnancy is not null) lines.Add(Palette.Paint("Expecting — see Family.", Palette.Amber));
        if (sim.ClassOf(s) is { } study) lines.Add(ClassLine(sim, s, study));
        var xp = sim.Data.Progression.Xp;
        lines.Add($"Level {s.Level} · {Fmt.Amount(s.Xp)} XP" + (s.PerkPoints > 0 ? "  " + Palette.Paint($"★ {s.PerkPoints} perk point{(s.PerkPoints == 1 ? "" : "s")} — see Perks", Palette.Amber) : "")
                  + (s.Level < xp.MaxLevel ? Palette.Paint($"  (next at {Fmt.Amount(Progression.XpForLevel(s.Level + 1, xp))})", Palette.TextMuted) : ""));
        if (s.IsChild) lines.Add($"Growing: {ColonyText.Stage(s, rules)}");
        if (sim.Data.Food is { } food)
        {
            var low = s.Nutrition < food.Rules.Nutrition.MalnourishedBelow;
            lines.Add($"Nutrition {Palette.Paint($"{s.Nutrition * 100:0}%", low ? Palette.Warn : Palette.Good)}"
                      + (s.Diet.Count > 0 ? Palette.Paint($"  · lately {string.Join(", ", s.Diet.OrderByDescending(kv => kv.Value).Take(3).Select(kv => FoodText.ItemName(sim, kv.Key)))}", Palette.TextMuted) : ""));
            foreach (var (buff, active) in FoodEffects.Active(s, sim.Data))
                lines.Add(Palette.Paint(buff.Name, buff.Debuff ? Palette.Warn : Palette.Good)
                          + Palette.Paint($" — {buff.Description}" + (active.Until - sim.State.Tick > 1 ? $" ({ColonyText.Duration((int)(active.Until - sim.State.Tick), rules)} left)" : ""), Palette.TextMuted));
        }
        return string.Join("\n", lines);
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
    /// "Makes 0.62 Wood/h" and, as the tooltip, the job output formula with this survivor's numbers
    /// (<see cref="JobOutput"/>): base × level × performance × tool × health × life stage × mood.
    /// </summary>
    private static string OutputLine(ColonySim sim, Survivor s, out string breakdown)
    {
        breakdown = "";
        if (sim.ClassOf(s) is not null) return "";
        if (s.AssignedBuildingId is not { } id || sim.State.FindBuilding(id) is not { } b) return s.IsInfant ? "" : "No job. Pick a building and Assign.";
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
        return $"Makes {perTick:0.##} {unit}/h on shift ({perTick * hours:0.#}/day). Hover for the breakdown.";
    }

    private void RefreshBody(ColonySim sim, Survivor s)
    {
        var cyber = sim.Data.Cybernetics;
        var humanity = s.Body.Humanity;
        if (cyber is not null)
        {
            var (band, color) = CyberText.Humanity(cyber.Humanity, humanity);
            _humanity.Set(humanity / Body.MaxHumanity, $"{humanity:0}", color);
            _humanityText.Text = band switch
            {
                "whole" => "Whole: their prosthetics have not changed who they are.",
                "strained" => $"Strained: mood cannot rise above {Cybernetics.MoodCap(cyber.Humanity, humanity):0}.",
                _ => $"Detached: mood at most {Cybernetics.MoodCap(cyber.Humanity, humanity):0}; their partner drifts away and their children suffer.",
            };
            _humanity.TooltipText = $"Humanity {humanity:0}/100: 100 minus what each prosthetic costs (more for an elective augmentation). " +
                                    $"Below {cyber.Humanity.MoodCapFrom:0} mood is capped; below {cyber.Humanity.Detached:0} partners and children suffer.";
        }
        _humanity.GetParent<Control>().Visible = _humanityText.Visible = cyber is not null;
        foreach (var (part, (icon, text, bar, detail)) in _parts)
        {
            var p = s.Body[part];
            var name = Spaced(part.ToString());
            detail.Visible = false;
            switch (p.Condition)
            {
                case PartCondition.Healthy:
                    text.Text = $"{name}: healthy";
                    text.TooltipText = part == BodyPart.Eyes ? "Eyes are never lost: an eye implant is always an elective augmentation." : "";
                    bar.Set(1, "100%", Palette.Good);
                    icon.Modulate = Palette.TextMuted;
                    break;
                case PartCondition.Injured:
                    text.Text = $"{name}: injured{(p.Severity >= sim.Data.Rules.Medical.WorsenThreshold ? " — worsening" : "")}";
                    bar.Set(1 - p.Severity, $"{ColonyText.Percent(p.Severity)} hurt", p.Severity >= sim.Data.Rules.Medical.WorsenThreshold ? Palette.Danger : Palette.Warn);
                    icon.Modulate = Palette.Danger;
                    break;
                case PartCondition.Missing:
                    text.Text = $"{name}: missing — fit a prosthetic at the Med Bay";
                    bar.Set(0, "lost", Palette.Danger);
                    icon.Modulate = Palette.TextDim;
                    break;
                default:
                    var item = p.ProstheticItemId is { } pid && sim.Scavenge?.Items.GetValueOrDefault(pid) is { } def ? def.Name : "prosthetic";
                    text.Text = $"{name}: {item}";
                    bar.Set(Math.Min(1, p.Efficiency), $"{ColonyText.Percent(p.Efficiency)} eff.", p.Efficiency >= 1 ? Palette.Good : Palette.Warn);
                    icon.Modulate = Palette.Amber;
                    if (cyber is not null && CyberText.Node(cyber, p) is { } node)
                    {
                        var lines = new List<string> { CyberText.TierAndSpec(cyber, node) + (p.Elective ? " · augmentation" : "") + $" · −{p.HumanityCost:0} humanity" };
                        if (CyberText.Upkeep(cyber, p, sim.Data.Rules.TicksPerDay) is { } upkeep) lines.Add(upkeep);
                        detail.Text = string.Join(" · ", lines);
                        detail.Visible = true;
                        detail.AddThemeColorOverride("font_color", p.Upkeep < 1 - 1e-9 ? Palette.Warn : Palette.TextMuted);
                        icon.Modulate = CyberText.Tint(cyber, node.Tier).Lerp(Palette.Amber, 0.3f);
                        text.TooltipText = string.Join(" · ", CyberText.ItemStats(sim.Scavenge!.Items[node.Id]).Concat(CyberText.Effects(node.Effects, sim.Data)));
                        text.MouseFilter = MouseFilterEnum.Stop;
                    }
                    break;
            }
        }
    }

    private static string Spaced(string pascal) => string.Concat(pascal.Select((c, i) => i > 0 && char.IsUpper(c) ? " " + char.ToLowerInvariant(c) : c.ToString()));

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

    private void RefreshJobs(ColonySim sim, Survivor s)
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
            for (var i = 0; i < labels.Count; i++)
            {
                var def = sim.Data.Buildings[buildings[i].DefId];
                _jobs.AddIconItem(Icons.Building(def.Id, def.Category.ToString(), 16), labels[i]);
            }
            _jobBuildingIds.AddRange(buildings.Select(b => b.Id));
            var want = previous ?? s.AssignedBuildingId;
            var index = want is { } w ? _jobBuildingIds.IndexOf(w) : -1;
            _jobs.Select(index >= 0 ? index : _jobBuildingIds.Count > 0 ? 0 : -1);
        }

        _jobs.Disabled = s.Away || s.IsInfant || s.PatientAtBuildingId is not null || sim.ClassOf(s) is not null || _jobBuildingIds.Count == 0;
        _assign.Disabled = _jobs.Disabled;
        _unassign.Disabled = s.AssignedBuildingId is null;
    }

    private void RefreshPairs(ColonySim sim, Survivor s)
    {
        var others = s.IsChild ? new List<Survivor>() : sim.State.Survivors.Where(o => o.Id != s.Id && !o.IsChild).OrderBy(o => o.Id).ToList();
        var labels = others.Select(o =>
        {
            var stance = Bonds.Stance(sim.State, s.Id, o.Id) switch
            {
                PairStance.Encourage => "  (encouraged)",
                PairStance.Discourage => "  (kept apart)",
                _ => "",
            };
            var tie = s.PartnerId == o.Id ? "partner" : "bond " + ColonyText.Percent(Bonds.Affinity(sim.State, s.Id, o.Id) / sim.Data.Rules.Relationships.CoupleThreshold);
            return $"{o.Name} — {tie}{stance}";
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
        if (SurvivorId is not { } id || _pairWith.Selected < 0 || _pairWith.Selected >= _pairIds.Count) return;
        _session.Execute(new Matchmake(id, _pairIds[_pairWith.Selected], stance));
    }

    private void Assign()
    {
        if (SurvivorId is not { } id || _jobs.Selected < 0) return;
        _session.Execute(new AssignJob(id, _jobBuildingIds[_jobs.Selected]));
    }

    private void Unassign()
    {
        if (SurvivorId is { } id) _session.Execute(new Unassign(id));
    }
}
