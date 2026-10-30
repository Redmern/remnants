using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Core.Colony;
using Remnants.Core.Data;
using Remnants.Core.Survivors;
using Remnants.Game.Hud;

namespace Remnants.Game.Colony.Ui;

/// <summary>
/// The survivor panel's Perks tab: level, XP bar and unspent points; a discipline switcher; the discipline's perk tree
/// (tiers top to bottom, lines from each prerequisite) where each node shows owned / open / missing requirements /
/// locked behind a datashard, with requirements in its tooltip; and the picked perk in detail with the Learn button.
/// Core decides (<see cref="ColonySim.PerkStatus"/>, <see cref="ChoosePerk"/>).
/// </summary>
public partial class PerksTab : VBoxContainer
{
    private readonly ColonySession _session;
    private readonly Label _level = Widgets.Label("", "Heading");
    private readonly StatBar _xp = new("stat_xp");
    private readonly PanelContainer _points = new() { ThemeTypeVariation = "Chip" };
    private readonly Label _pointsText = Widgets.Label("", "Number", Palette.Amber, 15);
    private readonly HBoxContainer _disciplines = Widgets.Row(4);
    private readonly Label _disciplineText = Widgets.Wrapped("", "Small");
    private readonly PerkTree _tree = new();
    private readonly RichTextLabel _detail = Ui.Rich();
    private readonly Button _learn;
    private string _discipline = "";
    private string? _picked;
    private string _signature = "";
    private int? _survivorId;

    public PerksTab(ColonySession session)
    {
        _session = session;
        Name = "Perks";
        AddThemeConstantOverride("separation", 6);

        var head = Widgets.Row(6);
        head.AddChild(Icons.Rect(Icons.Get("stat_xp", 16), 16, Palette.Amber));
        head.AddChild(_level);
        _xp.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _xp.TooltipText = "Experience. Work, crafting, study and missions earn it; each level gives a perk point.";
        head.AddChild(_xp);
        var pointsRow = Widgets.Row(4);
        pointsRow.MouseFilter = MouseFilterEnum.Ignore;
        pointsRow.AddChild(Icons.Rect(Icons.Get("stat_perk", 14), 14, Palette.Amber));
        pointsRow.AddChild(_pointsText);
        _points.AddChild(pointsRow);
        _points.TooltipText = "Perk points to spend. Pick an open perk below and Learn it.";
        head.AddChild(_points);
        AddChild(head);

        var group = new ButtonGroup();
        var first = true;
        foreach (var d in session.Sim.Data.Progression.Disciplines)
        {
            var id = d.Id;
            var button = Widgets.IconButton($"perk_{d.Icon}", "", $"{d.Name}: {d.Description}", () => SetDiscipline(id), variation: "Tab");
            button.ToggleMode = true;
            button.ButtonGroup = group;
            button.ButtonPressed = first;
            button.CustomMinimumSize = new Vector2(0, 28);
            button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            button.IconAlignment = HorizontalAlignment.Center;
            if (first) _discipline = d.Id;
            first = false;
            _disciplines.AddChild(button);
        }
        AddChild(_disciplines);
        _disciplineText.AddThemeColorOverride("font_color", Palette.TextMuted);
        AddChild(_disciplineText);

        _tree.Picked += id =>
        {
            _picked = id;
            _signature = "";
            Refresh(_survivorId);
        };
        AddChild(_tree);

        var detail = new PanelContainer { ThemeTypeVariation = "Inset" };
        var col = Widgets.Column(6);
        detail.AddChild(col);
        col.AddChild(_detail);
        _learn = Widgets.IconButton("stat_perk", "Learn", "Spend a perk point on this perk", Learn, variation: "Primary");
        col.AddChild(_learn);
        AddChild(detail);
    }

    private void SetDiscipline(string id)
    {
        _discipline = id;
        _picked = null;
        _signature = "";
        Refresh(_survivorId);
    }

    public void Refresh(int? survivorId)
    {
        if (survivorId != _survivorId) _signature = "";
        _survivorId = survivorId;
        var sim = _session.Sim;
        if (survivorId is not { } id || sim.State.FindSurvivor(id) is not { } s) return;
        var xp = sim.Data.Progression.Xp;

        _level.Text = $"LEVEL {s.Level}";
        var next = s.Level >= xp.MaxLevel ? "max level" : $"{Fmt.Amount(s.Xp)}/{Fmt.Amount(Progression.XpForLevel(s.Level + 1, xp))} XP";
        _xp.Set(Progression.LevelProgress(s, xp), next, Palette.Amber);
        _pointsText.Text = s.PerkPoints.ToString();
        _points.Modulate = s.PerkPoints > 0 ? Colors.White : new Color(1, 1, 1, 0.5f);

        if (!sim.Data.Disciplines.TryGetValue(_discipline, out var discipline)) return;
        var perks = sim.Data.PerksOf(_discipline).ToList();
        var statuses = perks.Select(p => sim.PerkStatus(s, p)).ToList();
        _picked ??= statuses.FirstOrDefault(st => st.Block == PerkBlock.Ok)?.Perk.Id ?? perks.FirstOrDefault()?.Id;
        var signature = $"{s.Id}|{_discipline}|{_picked}|{s.PerkPoints}|" + string.Join(",", statuses.Select(st => $"{st.Block}:{string.Join(";", st.Missing)}"));
        if (signature == _signature) return;
        _signature = signature;

        _disciplineText.Text = $"{discipline.Name.ToUpperInvariant()}: {discipline.Description} Studied at the {sim.Data.Buildings[discipline.StudyAt].Name}.";
        _tree.Build(statuses, _picked, st => Tooltip(sim, st));
        var picked = statuses.FirstOrDefault(st => st.Perk.Id == _picked);
        _detail.Text = picked is null ? "" : Detail(sim, s, picked);
        _learn.Visible = picked is not null && !picked.Owned;
        _learn.Disabled = picked?.Block != PerkBlock.Ok;
        _learn.TooltipText = picked?.Block switch
        {
            PerkBlock.Ok => $"Spend {picked.Perk.Cost} perk point{(picked.Perk.Cost == 1 ? "" : "s")} on {picked.Perk.Name}.",
            PerkBlock.NoPoints => "No perk point to spend: level up through work, study and missions, or learn it from a mentor at the study building.",
            PerkBlock.Locked => "Locked: study the datashard at the Electronics Lab first (Items).",
            _ => "Requirements not met yet (see above).",
        };
    }

    private void Learn()
    {
        if (_survivorId is { } id && _picked is { } perk) _session.Execute(new ChoosePerk(id, perk));
        _signature = "";
    }

    /// <summary>Description, effects and each requirement ticked or crossed.</summary>
    private static string Detail(ColonySim sim, Survivor s, PerkStatus st)
    {
        var p = st.Perk;
        var state = st.Block switch
        {
            PerkBlock.Owned => Palette.Paint("Learned", Palette.Good),
            PerkBlock.Ok => Palette.Paint("Ready to learn", Palette.Amber),
            PerkBlock.NoPoints => Palette.Paint("Needs a perk point", Palette.Warn),
            PerkBlock.Locked => Palette.Paint("Locked: datashard needed", Palette.Danger),
            _ => Palette.Paint("Requirements missing", Palette.TextMuted),
        };
        var lines = new List<string>
        {
            $"[b]{p.Name}[/b]  ·  tier {p.Tier}  ·  {state}",
            Palette.Paint(p.Description, Palette.TextMuted),
        };
        if (Effects(sim, p) is { Length: > 0 } fx) lines.Add(fx);
        var reqs = Requirements(sim, s, p).ToList();
        if (reqs.Count > 0) lines.Add("Needs: " + string.Join("  ", reqs));
        return string.Join("\n", lines);
    }

    private static string Tooltip(ColonySim sim, PerkStatus st)
    {
        var p = st.Perk;
        var tip = $"{p.Name} (tier {p.Tier})\n{p.Description}";
        if (Effects(sim, p, bb: false) is { Length: > 0 } fx) tip += "\n" + fx;
        if (st.Owned) return tip + "\nLearned.";
        tip += st.Missing.Count == 0 ? "\nAll requirements met." : "\nMissing:\n  " + string.Join("\n  ", st.Missing);
        if (st.Block == PerkBlock.NoPoints) tip += "\nNeeds a perk point.";
        return tip;
    }

    /// <summary>Every requirement, ✓ when met and ✗ (red) when not.</summary>
    private static IEnumerable<string> Requirements(ColonySim sim, Survivor s, PerkDef p)
    {
        var r = p.Requires;
        string Mark(bool ok, string text) => Palette.Paint((ok ? "✓ " : "✗ ") + text, ok ? Palette.Good : Palette.Danger);
        if (r.Level > 1) yield return Mark(s.Level >= r.Level, $"Level {r.Level}");
        foreach (var (skill, need) in r.Skills) yield return Mark(s.Get(skill) + 1e-9 >= need, $"{sim.Data.SkillInfo(skill).Name} {need:0}");
        foreach (var (a, need) in r.Attributes) yield return Mark(s.Get(a) + 1e-9 >= need, $"{a} {need:0}");
        foreach (var id in r.Perks) yield return Mark(s.HasPerk(id), sim.Data.Perks[id].Name);
        if (r.Building is { } b)
            yield return Mark(sim.State.Buildings.Any(x => x.DefId == b && x.Level >= r.BuildingLevel),
                $"{sim.Data.Buildings[b].Name}{(r.BuildingLevel > 1 ? $" L{r.BuildingLevel}" : "")}");
        if (r.Blueprint is { } bp)
            yield return Mark(sim.State.KnownBlueprints.Contains(bp), sim.Scavenge?.Items.GetValueOrDefault(bp)?.Name ?? bp);
        if (r.AdultsOnly) yield return Mark(!s.IsChild, "Adult");
    }

    /// <summary>"+10% Electronics output · Hack chance +10" — colony effects, then the field hooks.</summary>
    private static string Effects(ColonySim sim, PerkDef p, bool bb = true)
    {
        var e = p.Effects;
        var parts = new List<string>();
        foreach (var (job, v) in e.JobOutput) parts.Add($"+{v * 100:0}% {sim.Data.Jobs[job].Name} output");
        foreach (var (job, v) in e.CraftQuality) parts.Add($"+{v * 100:0} {sim.Data.Jobs[job].Name} quality");
        if (e.StudySpeed > 0) parts.Add($"+{e.StudySpeed * 100:0}% study speed");
        if (e.XpBonus > 0) parts.Add($"+{e.XpBonus * 100:0}% XP");
        foreach (var (hook, v) in e.Hooks) parts.Add(HookText(hook, v));
        var text = string.Join(" · ", parts);
        return bb && text.Length > 0 ? Palette.Paint(text, Palette.Amber) : text;
    }

    private static string HookText(string hook, double v) => hook switch
    {
        PerkHooks.HackChanceBonus => $"Hack chance +{v * 100:0}",
        PerkHooks.HackAlarmReduction => $"Hacks {v * 100:0}% quieter",
        PerkHooks.CanHackLinked => "Can hack Linked",
        PerkHooks.FieldRepair => "Field repairs",
        PerkHooks.HackApDiscount => $"Hacks cost {v:0} AP less",
        PerkHooks.DroneRangeBonus => $"Drone range +{v:0}",
        PerkHooks.DroneBatteryBonus => $"Drone battery +{v:0}",
        PerkHooks.DroneCanMark => "Drone marks targets",
        PerkHooks.DronePayload => "Drone payload",
        PerkHooks.AccuracyBonus => $"Accuracy +{v * 100:0}",
        PerkHooks.MeleeDamageBonus => $"Melee damage +{v * 100:0}%",
        PerkHooks.OverwatchReactionsBonus => $"+{v:0} overwatch reaction",
        PerkHooks.BraceCoverBonus => $"Braced cover +{v * 100:0}%",
        PerkHooks.StealthBonus => $"Stealth +{v:0}",
        PerkHooks.DetectionBonus => $"Detection +{v:0}",
        PerkHooks.SightBonus => $"Sight +{v:0}",
        PerkHooks.CarryBonus => $"Carry +{v:0} kg",
        PerkHooks.LootBonus => $"Loot +{v * 100:0}%",
        PerkHooks.FieldHealBonus => $"Healing items +{v * 100:0}%",
        PerkHooks.ReviveHpBonus => $"Revive +{v:0} HP",
        PerkHooks.BleedoutRoundsBonus => $"Bleed-out +{v:0} round",
        _ => $"{hook} {v:0.##}",
    };
}

/// <summary>
/// A discipline's perks as a tree: one row per tier, a node per perk, lines from each prerequisite (drawn behind the
/// nodes). Clicking a node picks it.
/// </summary>
public partial class PerkTree : Control
{
    private readonly VBoxContainer _rows = Widgets.Column(14);
    private readonly Dictionary<string, PerkNode> _nodes = new();
    private List<(string From, string To)> _links = new();

    public event System.Action<string>? Picked;

    public PerkTree()
    {
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        MouseFilter = MouseFilterEnum.Pass;
        AddChild(_rows);
        _rows.Resized += () =>
        {
            CustomMinimumSize = new Vector2(0, _rows.Size.Y);
            QueueRedraw();
        };
        Resized += () =>
        {
            _rows.Size = new Vector2(Size.X, _rows.Size.Y);
            QueueRedraw();
        };
    }

    public void Build(List<PerkStatus> perks, string? picked, System.Func<PerkStatus, string> tooltip)
    {
        Widgets.Clear(_rows);
        _nodes.Clear();
        foreach (var tier in perks.GroupBy(p => p.Perk.Tier).OrderBy(g => g.Key))
        {
            var row = Widgets.Row(6);
            row.Alignment = BoxContainer.AlignmentMode.Center;
            foreach (var st in tier)
            {
                var node = new PerkNode(st, st.Perk.Id == picked) { TooltipText = tooltip(st) };
                var id = st.Perk.Id;
                node.Pressed += () => Picked?.Invoke(id);
                node.ItemRectChanged += QueueRedraw;
                _nodes[id] = node;
                row.AddChild(node);
            }
            _rows.AddChild(row);
        }
        _links = perks.SelectMany(p => p.Perk.Requires.Perks.Where(_nodes.ContainsKey).Select(from => (from, p.Perk.Id))).ToList();
        _rows.Size = new Vector2(Size.X, 0);
        QueueRedraw();
    }

    public override void _Draw()
    {
        foreach (var (from, to) in _links)
        {
            if (!_nodes.TryGetValue(from, out var a) || !_nodes.TryGetValue(to, out var b) || !a.IsInsideTree() || !b.IsInsideTree()) continue;
            var ra = a.GetGlobalRect();
            var rb = b.GetGlobalRect();
            var origin = GetGlobalRect().Position;
            var start = new Vector2(ra.GetCenter().X, ra.End.Y) - origin;
            var end = new Vector2(rb.GetCenter().X, rb.Position.Y) - origin;
            var mid = (start.Y + end.Y) / 2;
            var color = a.Owned ? Palette.Amber : Palette.EdgeLight;
            DrawPolyline([start, new Vector2(start.X, mid), new Vector2(end.X, mid), end], color, 2);
        }
    }
}

/// <summary>One perk in the tree: a small card with a state icon and the name, coloured by state.</summary>
public partial class PerkNode : Button
{
    public bool Owned { get; }

    public PerkNode(PerkStatus st, bool picked)
    {
        Owned = st.Owned;
        FocusMode = FocusModeEnum.None;
        MouseDefaultCursorShape = CursorShape.PointingHand;
        CustomMinimumSize = new Vector2(80, 44);
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        ToggleMode = true;
        ButtonPressed = picked;
        AutowrapMode = TextServer.AutowrapMode.WordSmart;
        Text = st.Perk.Name;
        AddThemeFontSizeOverride("font_size", 13);
        var (icon, tint) = st.Block switch
        {
            PerkBlock.Owned => ("stat_check", Palette.Good),
            PerkBlock.Ok => ("stat_perk", Palette.Amber),
            PerkBlock.NoPoints => ("stat_perk", Palette.TextMuted),
            PerkBlock.Locked => ("stat_lock", Palette.Danger),
            _ => ("stat_lock", Palette.TextDim),
        };
        Icon = Icons.Get(icon, 14);
        AddThemeColorOverride("icon_normal_color", tint);
        AddThemeColorOverride("icon_hover_color", tint);
        AddThemeColorOverride("icon_pressed_color", tint);
        AddThemeColorOverride("icon_hover_pressed_color", tint);
        var text = st.Block switch
        {
            PerkBlock.Owned => Palette.Good.Lerp(Palette.Text, 0.4f),
            PerkBlock.Ok => Palette.Text,
            PerkBlock.NoPoints => Palette.TextMuted,
            _ => Palette.TextDim,
        };
        AddThemeColorOverride("font_color", text);
        AddThemeColorOverride("font_hover_color", text.Lightened(0.2f));
        AddThemeColorOverride("font_pressed_color", Palette.Text);
        AddThemeColorOverride("font_hover_pressed_color", Palette.Text);
        if (st.Owned) AddThemeStyleboxOverride("normal", HudTheme.CardSelectedStyle);
    }
}
