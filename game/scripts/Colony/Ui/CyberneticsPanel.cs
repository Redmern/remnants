using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Core.Colony;
using Remnants.Core.Data;
using Remnants.Core.Items;
using Remnants.Core.Survivors;
using Remnants.Game.Hud;

namespace Remnants.Game.Colony.Ui;

/// <summary>
/// The cybernetics tree (opened from the Med Bay, the Workshop and the Electronics Lab): a slot switcher (arms, legs,
/// eyes), the slot's designs in tier columns T0..T4 with lines from each prerequisite, coloured locked / available /
/// known; and the picked design in detail — what it needs (missing in red), its components as chips, its effects,
/// humanity and upkeep, a Queue button at the building that makes it, and a comparison of a survivor's current limb
/// with it (with Fit / Book surgery when the colony owns one). Core decides (<see cref="Cybernetics.Status"/>).
/// </summary>
public partial class CyberneticsPanel : Overlay
{
    private readonly ColonySession _session;
    private readonly HBoxContainer _slots = Widgets.Row(4);
    private readonly Label _slotText = Widgets.Wrapped("", "Small");
    private readonly CyberTree _tree = new();
    private readonly RichTextLabel _detail = Ui.Rich();
    private readonly VBoxContainer _components = Widgets.Column(4);
    private readonly Button _queue;
    private readonly OptionButton _who = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill, FocusMode = FocusModeEnum.None, ClipText = true, FitToLongestItem = false };
    private readonly OptionButton _part = new() { FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(110, 0) };
    private readonly GridContainer _compare = new() { Columns = 3 };
    private readonly Button _fit;
    private readonly List<int> _whoIds = new();
    private readonly List<BodyPart> _partIds = new();
    private CyberSlot _slot = CyberSlot.Arm;
    private string? _picked;
    private string _signature = "";

    public CyberneticsPanel() : this(null!) { }

    public CyberneticsPanel(ColonySession session) : base(1200)
    {
        _session = session;
        Visible = false;
        Body.AddThemeConstantOverride("separation", 8);
        Body.AddChild(Ui.TitleRow("Cybernetics", "body_prosthetic", Close, Hotkeys.KeyName("pause_menu")));
        var tagline = Widgets.Label("KEEP THEM ALIVE. KEEP THEM HUMAN.", "Small", Palette.TextMuted);
        Body.AddChild(tagline);

        var group = new ButtonGroup();
        foreach (var (slot, label, icon) in new[] { (CyberSlot.Arm, "Arms", "body_left_arm"), (CyberSlot.Leg, "Legs", "body_left_leg"), (CyberSlot.Eye, "Eyes", "stat_sight") })
        {
            var button = Widgets.IconButton(icon, label, $"{label}: the {label.ToLowerInvariant()} tree", () => SetSlot(slot), variation: "Tab");
            button.ToggleMode = true;
            button.ButtonGroup = group;
            button.ButtonPressed = slot == _slot;
            button.CustomMinimumSize = new Vector2(120, 30);
            _slots.AddChild(button);
        }
        _slots.AddChild(_slotText);
        _slotText.AddThemeColorOverride("font_color", Palette.TextMuted);
        Body.AddChild(_slots);

        var main = Widgets.Row(14);
        var left = Widgets.Column(8);
        left.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var treeCard = new PanelContainer { ThemeTypeVariation = "Inset", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var treeScroll = new ScrollContainer { CustomMinimumSize = new Vector2(800, 330), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _tree.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        treeScroll.AddChild(_tree);
        treeCard.AddChild(treeScroll);
        left.AddChild(treeCard);
        main.AddChild(left);
        _tree.Picked += id =>
        {
            _picked = id;
            _signature = "";
            Refresh();
        };

        var side = new ScrollContainer { CustomMinimumSize = new Vector2(360, 500), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var col = Widgets.Column(6);
        col.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        side.AddChild(col);
        col.AddChild(_detail);
        col.AddChild(_components);
        _queue = Widgets.IconButton("ui_plus", "Queue", "Queue one at the building that makes it", Queue, variation: "Primary");
        col.AddChild(_queue);
        main.AddChild(side);

        // Compare under the tree: a survivor's limb now against the picked design.
        var compareCard = new PanelContainer { ThemeTypeVariation = "Card" };
        var compare = Widgets.Column(6);
        compareCard.AddChild(compare);
        var pick = Widgets.Row(6);
        pick.AddChild(Widgets.Label("COMPARE", "Heading"));
        pick.AddChild(_who);
        pick.AddChild(_part);
        _fit = Widgets.IconButton("body_prosthetic", "Book surgery", "Fit one from the stores on this part", Fit, variation: "Primary");
        pick.AddChild(_fit);
        compare.AddChild(pick);
        _compare.Columns = 3;
        _compare.AddThemeConstantOverride("h_separation", 14);
        _compare.AddThemeConstantOverride("v_separation", 2);
        compare.AddChild(_compare);
        left.AddChild(compareCard);
        Body.AddChild(main);

        _who.ItemSelected += _ => { FillParts(); _signature = ""; Refresh(); };
        _part.ItemSelected += _ => { _signature = ""; Refresh(); };
    }

    /// <summary>Opens on a slot (and a survivor's part to compare against), or where it was.</summary>
    public void Open(CyberSlot? slot = null, int? survivorId = null, BodyPart? part = null, string? nodeId = null)
    {
        if (slot is { } s) SetSlot(s, refresh: false);
        if (nodeId is not null) _picked = nodeId;
        FillWho(survivorId);
        if (part is { } p && _partIds.IndexOf(p) is var i and >= 0) _part.Select(i);
        _signature = "";
        base.Open();
        Refresh();
    }

    private void SetSlot(CyberSlot slot, bool refresh = true)
    {
        _slot = slot;
        _picked = null;
        var i = 0;
        foreach (var b in _slots.GetChildren().OfType<Button>()) b.SetPressedNoSignal(i++ == (int)slot);
        FillParts();
        _signature = "";
        if (refresh) Refresh();
    }

    public void Refresh()
    {
        if (!Visible) return;
        var sim = _session.Sim;
        if (sim.Data.Cybernetics is not { } cyber || sim.Scavenge is null) return;
        if (_whoIds.Count == 0 || _whoIds.Any(id => sim.State.FindSurvivor(id) is null)) FillWho(null);
        var nodes = cyber.Nodes.Values.Where(n => n.Slot == _slot).ToList();
        var statuses = nodes.Select(n => Cybernetics.Status(sim, n)).ToList();
        _picked = nodes.Any(n => n.Id == _picked) ? _picked
            : statuses.FirstOrDefault(st => st.State == CyberNodeState.Available)?.Node.Id ?? nodes.FirstOrDefault()?.Id;
        var who = Who(sim);
        var part = _part.Selected >= 0 && _part.Selected < _partIds.Count ? _partIds[_part.Selected] : (BodyPart?)null;
        var signature = $"{_slot}|{_picked}|{who?.Id}|{part}|{sim.State.Inventory.Counts.Sum(kv => kv.Value)}|{string.Join(",", sim.State.KnownCybernetics)}|"
                        + string.Join(",", statuses.Select(st => $"{st.State}{st.Owned}{st.Missing.Count}"))
                        + (who is null || part is null ? "" : $"|{who.Body[part.Value].Condition}{who.Body[part.Value].ProstheticItemId}{who.Body.Humanity}");
        if (signature == _signature) return;
        _signature = signature;

        var specs = cyber.Def.Specs.Select(s => s.Id).ToList();
        _slotText.Text = _slot switch
        {
            CyberSlot.Eye => "Eyes are never lost: an eye implant is always an elective augmentation (T3+).",
            _ => $"{statuses.Count(st => st.State == CyberNodeState.Known)} of {statuses.Count} designs known. Specialisations branch from T2 Powered.",
        };
        _tree.Build(cyber, statuses, specs, _picked, id => sim.Scavenge.Items[id].Name, st => Tooltip(sim, cyber, st));
        var picked = statuses.FirstOrDefault(st => st.Node.Id == _picked);
        if (picked is null) return;
        _detail.Text = Detail(sim, cyber, picked);
        RefreshComponents(sim, picked);
        var at = MakerBuilding(sim, picked.Recipe);
        _queue.Visible = picked.Recipe is not null;
        _queue.Disabled = at is null || picked.State == CyberNodeState.Locked || !sim.CanQueue(at, picked.Recipe!).Ok;
        _queue.Text = at is null ? "Queue" : $"Queue at {sim.Data.Buildings[at.DefId].Name}";
        _queue.TooltipText = picked.State == CyberNodeState.Locked ? "Locked: see what is missing above." : at is null
            ? $"Build a {sim.Data.Buildings[picked.Recipe!.Building].Name} first." : "Add one to that building's production queue.";
        RefreshCompare(sim, cyber, picked.Node, who, part);
    }

    private Survivor? Who(ColonySim sim) =>
        _who.Selected >= 0 && _who.Selected < _whoIds.Count ? sim.State.FindSurvivor(_whoIds[_who.Selected]) : null;

    private void FillWho(int? select)
    {
        var sim = _session.Sim;
        var people = sim.State.Present.Where(s => !s.IsInfant).OrderBy(s => s.Id).ToList();
        var keep = select ?? (Who(sim)?.Id);
        _whoIds.Clear();
        _who.Clear();
        foreach (var s in people)
        {
            _whoIds.Add(s.Id);
            _who.AddItem($"{s.Name} · humanity {s.Body.Humanity:0}");
        }
        var index = keep is { } k ? _whoIds.IndexOf(k) : -1;
        // Default to someone missing a limb of this slot, else the first.
        if (index < 0) index = people.FindIndex(s => CyberneticsData.Parts(_slot).Any(p => s.Body[p].Condition == PartCondition.Missing));
        _who.Select(index >= 0 ? index : _whoIds.Count > 0 ? 0 : -1);
        FillParts();
    }

    private void FillParts()
    {
        var keep = _part.Selected >= 0 && _part.Selected < _partIds.Count ? _partIds[_part.Selected] : (BodyPart?)null;
        _partIds.Clear();
        _part.Clear();
        foreach (var p in CyberneticsData.Parts(_slot))
        {
            _partIds.Add(p);
            _part.AddItem(CyberText.Part(p));
        }
        var who = Who(_session.Sim);
        var index = keep is { } k ? _partIds.IndexOf(k) : -1;
        if (index < 0 && who is not null) index = _partIds.FindIndex(p => who.Body[p].Condition != PartCondition.Healthy);
        _part.Select(index >= 0 ? index : 0);
    }

    private static Building? MakerBuilding(ColonySim sim, RecipeDef? recipe) =>
        recipe is null ? null : sim.State.Buildings.Where(b => b.DefId == recipe.Building).OrderByDescending(b => b.Level).ThenBy(b => b.Id).FirstOrDefault();

    private static string StateText(CyberNodeState state) => state switch
    {
        CyberNodeState.Known => Palette.Paint("Known", Palette.Good),
        CyberNodeState.Available => Palette.Paint("Can be made", Palette.Amber),
        _ => Palette.Paint("Locked", Palette.Danger),
    };

    private static string Detail(ColonySim sim, CyberneticsData cyber, CyberNodeStatus st)
    {
        var node = st.Node;
        var item = sim.Scavenge!.Items[node.Id];
        var tier = cyber.Tier(node.Tier);
        var lines = new List<string>
        {
            $"[font_size=20][b]{item.Name}[/b][/font_size]",
            $"{CyberText.TierAndSpec(cyber, node)}  ·  {StateText(st.State)}" + (st.Owned + st.Fitted > 0 ? Palette.Paint($"  ·  {st.Owned} in stores, {st.Fitted} fitted", Palette.TextMuted) : ""),
            Palette.Paint(node.Description, Palette.TextMuted),
        };
        if (node.Spec is { } spec && cyber.Specs.TryGetValue(spec, out var s)) lines.Add(Palette.Paint($"{s.Name}: {s.Description}", Palette.TextMuted));

        var effects = CyberText.ItemStats(item).Concat(CyberText.Effects(node.Effects, sim.Data)).ToList();
        lines.Add($"[b]Efficiency[/b] {ColonyText.Percent(item.Prosthetic!.Quality)}" + (effects.Count > 0 ? "  ·  " + Palette.Paint(string.Join(" · ", effects), Palette.Amber) : ""));
        foreach (var set in cyber.Def.Sets.Where(x => x.Spec == node.Spec && x.Slot == node.Slot && node.Tier >= x.MinTier))
            lines.Add(Palette.Paint($"Pair: {set.Name} — {set.Description}", Palette.Amber.Lerp(Palette.Text, 0.3f)));
        var elective = tier.ElectiveHumanity is { } e && node.Tier >= cyber.Surgery.ElectiveFromTier ? $"−{e:0} on a healthy limb" : "never on a healthy limb";
        lines.Add($"[b]Humanity[/b] −{tier.Humanity:0} as a replacement · {elective}");
        lines.Add(tier.ServiceDays > 0
            ? $"[b]Upkeep[/b] every {tier.ServiceDays:0} days: {string.Join(", ", tier.ServiceCost.Select(kv => $"{kv.Value:0.#} {sim.Data.Resources[kv.Key].Name}"))}"
            : "[b]Upkeep[/b] none");
        if (tier.Interference) lines.Add(Palette.Paint("Network-grade: Linked within 6 tiles, an Override hacker or any jammer can lock it up in the field.", Palette.Network));
        var surgery = node.Tier >= cyber.Surgery.FromTier
            ? $"Surgery at a Med Bay L{item.Prosthetic.MinMedBayLevel}" + (node.Tier >= cyber.Surgery.CyberSurgeonFromTier ? " by a Cyber-Surgeon" : "")
            : $"Straps on at a Med Bay L{item.Prosthetic.MinMedBayLevel}";
        lines.Add($"[b]Fitting[/b] {surgery}");

        lines.Add("[b]Needs[/b]");
        if (node.Requires.Count > 0)
        {
            var known = node.Requires.Any(r => Cybernetics.IsKnown(sim.State, cyber.Nodes[r]));
            lines.Add(Mark(known, "know " + string.Join(" or ", node.Requires.Select(r => sim.Scavenge.Items[r].Name))));
        }
        if (node.Perk is { } perk) lines.Add(Mark(Cybernetics.ColonyHasPerk(sim.State, perk), $"someone at home with {sim.Data.Perks[perk].Name}"));
        if (st.Recipe is { } r)
        {
            lines.Add(Mark(sim.State.Buildings.Any(b => b.DefId == r.Building && b.Level >= r.MinLevel), $"{sim.Data.Buildings[r.Building].Name} level {r.MinLevel}"));
            if (r.Blueprint is { } bp) lines.Add(Mark(sim.State.KnownBlueprints.Contains(bp), $"{sim.Scavenge.Items[bp].Name} studied"));
            if (r.MinSkill > 0) lines.Add(Palette.Paint($"   a worker with {sim.Data.Jobs[sim.Data.Buildings[r.Building].Job!].Skill} {r.MinSkill:0} · {r.Work:0} work", Palette.TextMuted));
        }
        else lines.Add(Palette.Paint("   no recipe: found or salvaged only", Palette.TextMuted));
        return string.Join("\n", lines);
    }

    private static string Mark(bool ok, string text) => Palette.Paint((ok ? "✓ " : "✗ ") + text, ok ? Palette.Good : Palette.Danger);

    private void RefreshComponents(ColonySim sim, CyberNodeStatus st)
    {
        Widgets.Clear(_components);
        if (st.Recipe is not { } r) return;
        _components.AddChild(Widgets.Label("COMPONENTS", "Heading"));
        _components.AddChild(Widgets.CostChips(r.Inputs.Select(kv =>
            (kv.Key, sim.Data.Resources[kv.Key].Name, kv.Value, sim.State.Stockpile.Get(kv.Key)))));
        if (r.ItemInputs.Count == 0) return;
        var flow = new HFlowContainer();
        flow.AddThemeConstantOverride("h_separation", 4);
        flow.AddThemeConstantOverride("v_separation", 4);
        foreach (var (id, need) in r.ItemInputs)
        {
            var item = sim.Scavenge!.Items[id];
            var have = sim.State.Inventory.Counts.Where(kv => sim.Scavenge.Items[kv.Key].DefinedId == id).Sum(kv => kv.Value);
            var short_ = have < need;
            flow.AddChild(Widgets.Chip(Icons.Item(id, item.Kind.ToString(), 16), $"{need} {item.Name}{(short_ ? " !" : "")}",
                short_ ? Palette.Danger : Palette.Text, $"{item.Name}: need {need}, have {have}", short_ ? Palette.Danger : Palette.TextMuted));
        }
        _components.AddChild(flow);
    }

    private void RefreshCompare(ColonySim sim, CyberneticsData cyber, CyberNodeDef node, Survivor? who, BodyPart? part)
    {
        Widgets.Clear(_compare);
        _fit.Visible = false;
        if (who is null || part is not { } p)
        {
            _compare.AddChild(Widgets.Label("Nobody to compare.", "Muted"));
            _fit.Visible = false;
            return;
        }
        var items = sim.Scavenge!.Items;
        var option = items[node.Id];
        var now = who.Body[p];
        var nowNode = CyberText.Node(cyber, now);
        var nowItem = nowNode is null ? null : items[nowNode.Id];
        var elective = now.Condition == PartCondition.Healthy || now.Condition == PartCondition.Prosthetic && now.Elective;
        var optionCost = cyber.HumanityCost(node.Tier, elective);
        var nowCost = now.Condition == PartCondition.Prosthetic ? now.HumanityCost : 0;

        // One row per aspect: what it is now, and with the option.
        var columns = new List<(string Label, string Now, string Option, Color? Color)>();
        void Row(string label, string a, string b, Color? bColor = null) => columns.Add((label, a, b, bColor));

        var nowName = now.Condition switch
        {
            PartCondition.Healthy => "own (healthy)",
            PartCondition.Injured => "own (injured)",
            PartCondition.Missing => "missing",
            _ => nowItem?.Name ?? "prosthetic",
        };
        Row("", "NOW", option.Name.ToUpperInvariant(), Palette.Amber);
        Row("Part", nowName, elective ? "augmentation" : "replacement", elective ? Palette.Warn : null);
        Row("Tier", nowNode is null ? "—" : CyberText.Tier(cyber, nowNode.Tier), CyberText.TierAndSpec(cyber, node));
        var optEff = option.Prosthetic!.Quality;
        Row("Efficiency", ColonyText.Percent(now.Efficiency), ColonyText.Percent(optEff), optEff > now.Efficiency + 1e-9 ? Palette.Good : optEff < now.Efficiency - 1e-9 ? Palette.Danger : null);
        var after = optionCost is { } c ? Math.Max(0, who.Body.Humanity + nowCost - c) : (double?)null;
        Row("Humanity", $"{who.Body.Humanity:0} (−{nowCost:0} here)",
            after is { } h ? $"{h:0} (−{optionCost:0} here)" : "not allowed",
            after is { } hh ? CyberText.Humanity(cyber.Humanity, hh).Color : Palette.Danger);
        var nowStats = nowItem is null ? new List<string>() : CyberText.ItemStats(nowItem).Concat(CyberText.Effects(nowNode!.Effects, sim.Data)).ToList();
        var optStats = CyberText.ItemStats(option).Concat(CyberText.Effects(node.Effects, sim.Data)).ToList();
        Row("Effects", nowStats.Count == 0 ? "—" : string.Join(" · ", nowStats), optStats.Count == 0 ? "—" : string.Join(" · ", optStats));
        foreach (var (label, a, b, tint) in columns)
        {
            _compare.AddChild(Widgets.Label(label.ToUpperInvariant(), "Small", Palette.TextMuted));
            _compare.AddChild(Cell(a, label.Length == 0 ? Palette.TextMuted : null));
            _compare.AddChild(Cell(b, tint));
        }

        var owned = sim.State.Inventory.Counts.Keys.FirstOrDefault(k => items[k].DefinedId == node.Id);
        _fit.Visible = owned is not null && now.Condition != PartCondition.Injured;
        if (owned is not null)
        {
            _fit.Text = CyberBooking.Verb(sim, who.Id, p, owned);
            _fit.SetMeta("item", owned);
        }
    }

    private static Label Cell(string text, Color? color)
    {
        var label = Widgets.Label(text, "", color, 13);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.CustomMinimumSize = new Vector2(320, 0);
        label.VerticalAlignment = VerticalAlignment.Top;
        return label;
    }

    private void Queue()
    {
        var sim = _session.Sim;
        if (_picked is null || Cybernetics.RecipeFor(sim.Data, sim.Data.Cybernetics!.Nodes[_picked]) is not { } recipe) return;
        if (MakerBuilding(sim, recipe) is { } at) _session.Execute(new QueueRecipe(at.Id, recipe.Id));
        _signature = "";
    }

    private void Fit()
    {
        var who = Who(_session.Sim);
        if (who is null || _part.Selected < 0 || !_fit.HasMeta("item")) return;
        CyberBooking.Book(_session, this, who.Id, _partIds[_part.Selected], _fit.GetMeta("item").AsString());
        _signature = "";
    }

    private static string Tooltip(ColonySim sim, CyberneticsData cyber, CyberNodeStatus st)
    {
        var item = sim.Scavenge!.Items[st.Node.Id];
        var tip = $"{item.Name} — {CyberText.TierAndSpec(cyber, st.Node)}\n{st.Node.Description}";
        var fx = CyberText.ItemStats(item).Concat(CyberText.Effects(st.Node.Effects, sim.Data)).ToList();
        if (fx.Count > 0) tip += "\n" + string.Join(" · ", fx);
        if (st.Node.Requires.Count > 0) tip += "\nAfter: " + string.Join(" or ", st.Node.Requires.Select(r => sim.Scavenge.Items[r].Name));
        if (st.Recipe is { ItemInputs.Count: > 0 } r)
            tip += "\nComponents: " + string.Join(", ", r.ItemInputs.Select(kv => $"{kv.Value} {sim.Scavenge.Items[kv.Key].Name}"));
        tip += st.Missing.Count == 0 ? "\nAll requirements met." : "\nMissing:\n  " + string.Join("\n  ", st.Missing);
        return tip;
    }
}

/// <summary>
/// A slot's designs in tier columns (T0 left to T4 right), standard parts first then by specialisation so branches
/// line up, with a line from each prerequisite to the designs it opens (drawn behind the nodes). Click picks.
/// </summary>
public partial class CyberTree : Control
{
    private readonly HBoxContainer _columns = Widgets.Row(12);
    private readonly Dictionary<string, CyberNode> _nodes = new();
    private List<(string From, string To)> _links = new();

    public event Action<string>? Picked;

    public CyberTree()
    {
        MouseFilter = MouseFilterEnum.Pass;
        AddChild(_columns);
        _columns.Resized += () =>
        {
            CustomMinimumSize = _columns.Size;
            QueueRedraw();
        };
    }

    public void Build(CyberneticsData cyber, List<CyberNodeStatus> statuses, List<string> specOrder, string? picked,
        Func<string, string> name, Func<CyberNodeStatus, string> tooltip)
    {
        Widgets.Clear(_columns);
        _nodes.Clear();
        foreach (var tier in cyber.Def.Tiers.OrderBy(t => t.Tier))
        {
            var col = Widgets.Column(8);
            col.CustomMinimumSize = new Vector2(152, 0);
            var head = Widgets.Label($"T{tier.Tier} {tier.Name.ToUpperInvariant()}", "Heading");
            head.HorizontalAlignment = HorizontalAlignment.Center;
            head.TooltipText = tier.Description;
            head.MouseFilter = MouseFilterEnum.Stop;
            if (tier.Tint is { } hex) head.AddThemeColorOverride("font_color", new Color(hex).Lerp(Palette.Text, 0.35f));
            col.AddChild(head);
            foreach (var st in statuses.Where(s => s.Node.Tier == tier.Tier)
                         .OrderBy(s => s.Node.Spec is null ? -1 : specOrder.IndexOf(s.Node.Spec)).ThenBy(s => s.Node.Id, StringComparer.Ordinal))
            {
                var node = new CyberNode(cyber, st, name(st.Node.Id), st.Node.Id == picked) { TooltipText = tooltip(st) };
                var id = st.Node.Id;
                node.Pressed += () => Picked?.Invoke(id);
                node.ItemRectChanged += QueueRedraw;
                _nodes[id] = node;
                col.AddChild(node);
            }
            _columns.AddChild(col);
        }
        _links = statuses.SelectMany(st => st.Node.Requires.Where(_nodes.ContainsKey).Select(from => (from, st.Node.Id))).ToList();
        QueueRedraw();
    }

    public override void _Draw()
    {
        var origin = GetGlobalRect().Position;
        foreach (var (from, to) in _links)
        {
            if (!_nodes.TryGetValue(from, out var a) || !_nodes.TryGetValue(to, out var b) || !a.IsInsideTree() || !b.IsInsideTree()) continue;
            var ra = a.GetGlobalRect();
            var rb = b.GetGlobalRect();
            var start = new Vector2(ra.End.X, ra.GetCenter().Y) - origin;
            var end = new Vector2(rb.Position.X, rb.GetCenter().Y) - origin;
            var mid = (start.X + end.X) / 2;
            var color = a.Known ? Palette.Amber : Palette.EdgeLight;
            DrawPolyline([start, new Vector2(mid, start.Y), new Vector2(mid, end.Y), end], color, 2);
        }
    }
}

/// <summary>One design in the tree: state icon, name, specialisation and how many the colony owns; tinted by tier.</summary>
public partial class CyberNode : Button
{
    public bool Known { get; }

    public CyberNode(CyberneticsData cyber, CyberNodeStatus st, string name, bool picked)
    {
        Known = st.State == CyberNodeState.Known;
        FocusMode = FocusModeEnum.None;
        MouseDefaultCursorShape = CursorShape.PointingHand;
        CustomMinimumSize = new Vector2(152, 44);
        ToggleMode = true;
        ButtonPressed = picked;
        ClipText = true;
        Alignment = HorizontalAlignment.Left;
        var spec = st.Node.Spec is { } s && cyber.Specs.TryGetValue(s, out var def) ? def.Name : "Standard";
        var count = st.Owned + st.Fitted > 0 ? $" ×{st.Owned + st.Fitted}" : "";
        Text = $"{name}\n{spec}{count}";
        AddThemeFontSizeOverride("font_size", 12);
        var (icon, tint) = st.State switch
        {
            CyberNodeState.Known => ("stat_check", Palette.Good),
            CyberNodeState.Available => ("stat_perk", Palette.Amber),
            _ => ("stat_lock", Palette.TextDim),
        };
        Icon = Icons.Get(icon, 14);
        AddThemeConstantOverride("icon_max_width", 14);
        foreach (var k in new[] { "icon_normal_color", "icon_hover_color", "icon_pressed_color", "icon_hover_pressed_color" }) AddThemeColorOverride(k, tint);
        var text = st.State switch
        {
            CyberNodeState.Known => Palette.Good.Lerp(Palette.Text, 0.45f),
            CyberNodeState.Available => Palette.Text,
            _ => Palette.TextDim,
        };
        AddThemeColorOverride("font_color", text);
        AddThemeColorOverride("font_hover_color", text.Lightened(0.2f));
        AddThemeColorOverride("font_pressed_color", Palette.Text);
        AddThemeColorOverride("font_hover_pressed_color", Palette.Text);
        if (Known && !picked) AddThemeStyleboxOverride("normal", HudTheme.CardSelectedStyle);
        // A strip in the tier's stand-in tint (the colour its model wears) along the left edge.
        if (cyber.Tier(st.Node.Tier).Tint is { } hex)
            AddChild(new ColorRect { Color = new Color(hex), Position = new Vector2(0, 4), Size = new Vector2(3, 36), MouseFilter = MouseFilterEnum.Ignore });
    }
}
