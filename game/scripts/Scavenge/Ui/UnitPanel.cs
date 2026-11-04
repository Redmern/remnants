using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Core.Items;
using Remnants.Core.Scavenge;
using Remnants.Core.Survivors;
using Remnants.Game.Hud;
using Remnants.Game.Hud.Help;
using UiKit = Remnants.Game.Colony.Ui.Ui;

namespace Remnants.Game.Scavenge.Ui;

/// <summary>
/// The selected unit, summary first: a header (portrait from the 3D model with its gear, name, kind, status, a "Not
/// their turn" tag when one of yours is selected while another acts, HP bar, AP pips unless it is the acting unit, whose
/// AP sits next to End turn) and folding sections (<see cref="Expander"/>, remembered per section): Body ("All parts ok",
/// or only the hurt, lost and prosthetic parts with their bars, like the colony Body tab), Field profile (four labelled
/// values: Move, Sight, Armor, Dodge, each with an (i); AP, initiative and the attacks behind More) and Gear (folded by
/// default with a one-line summary; slots, pack, load). An ally's unit (co-op) gets an Ally section on top: whose party
/// it is and that its orders are theirs (the card is read-only), and who carries whom. Everything updates in place.
/// </summary>
public partial class UnitPanel : PanelContainer
{
    private readonly ScavengePresenter _presenter;
    private readonly Portrait _portrait = new(64);
    private readonly Label _title = Widgets.Label("", "Display");
    private readonly Label _kind = Widgets.Label("", "Small");
    private readonly Label _status = Widgets.Label("", "Number");
    private readonly Label _turnTag = Widgets.Label("", "Small", Palette.Warn);
    private readonly StatBar _hp = new("stat_hp");
    private readonly Pips _ap = new();
    private readonly Label _gauges = Widgets.Label("", "Small", Palette.TextMuted);
    private readonly HBoxContainer _apRow = Widgets.Row(6);
    private readonly VBoxContainer _sections = Widgets.Column(8);
    private readonly Dictionary<BodyPart, (HBoxContainer Row, TextureRect Icon, Label Text, StatBar Bar)> _parts = new();
    private readonly Unchanged _set = new();

    /// <summary>Conditions under the body rows: "Fracture (left leg) · Infected wound 40% · fever · in pain" (medicine.json).</summary>
    private readonly Label _conditions = Widgets.Label("", "Small", Palette.Warn);
    private readonly Label _bodyOk = Widgets.Label("All parts ok", "Small", Palette.Good);
    private readonly VBoxContainer _gear = Widgets.Column(3);
    private readonly Label _empty = Widgets.Wrapped("Click a unit to see it. Each of your units plans its round.", "Muted");
    private readonly Label _allyText = Widgets.Wrapped("", "Small");
    private PanelContainer _allyCard = null!;
    private PanelContainer _droneCard = null!;
    private readonly Expander _bodyCard = new("body", "Body", "body_full", "injuries", openByDefault: true);
    private readonly Expander _profileCard = new("profile", "Field profile", "ui_move", null, openByDefault: true);
    private readonly Expander _gearCard = new("gear", "Gear", "item_tool", "gear_mods", openByDefault: false);
    private readonly Pips _battery = new();
    private readonly Label _droneText = Widgets.Wrapped("", "Small");
    private readonly HFlowContainer _cargoRow = new();
    private Label _droneHeading = null!;
    private string _cargoSignature = "";

    // Field profile: four labelled values, the rest behind More.
    private readonly Label _move = Value();
    private readonly Label _sight = Value();
    private readonly Label _armor = Value();
    private readonly Label _dodge = Value();
    private readonly Label _apMax = Value();
    private readonly Label _initiative = Value();
    private readonly VBoxContainer _attacks = Widgets.Column(2);
    private readonly VBoxContainer _more = Widgets.Column(3);
    private readonly Button _moreButton;
    private static bool _moreOpen;
    private Unit? _shown;

    public UnitPanel(ScavengePresenter presenter)
    {
        _presenter = presenter;
        CustomMinimumSize = new Vector2(320, 0);
        SizeFlagsVertical = SizeFlags.ShrinkBegin;
        MouseFilter = MouseFilterEnum.Stop;
        var box = UiKit.Panel(this, 8);

        var head = Widgets.Row(10);
        head.AddChild(_portrait);
        var names = Widgets.Column(2);
        names.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _title.AddThemeFontSizeOverride("font_size", 22);
        _title.ClipText = true;
        names.AddChild(_title);
        var sub = Widgets.Row(6);
        _kind.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _kind.ClipText = true;
        sub.AddChild(_kind);
        _turnTag.MouseFilter = MouseFilterEnum.Pass;
        sub.AddChild(_turnTag);
        _status.AddThemeFontSizeOverride("font_size", 13);
        sub.AddChild(_status);
        names.AddChild(sub);
        names.AddChild(_hp);
        _apRow.AddChild(Widgets.Label("AP", "Heading"));
        _apRow.AddChild(_ap);
        names.AddChild(_apRow);
        _gauges.AddThemeFontSizeOverride("font_size", 13);
        names.AddChild(_gauges);
        head.AddChild(names);
        box.AddChild(head);
        box.AddChild(_empty);
        box.AddChild(_sections);

        _allyCard = Section("Ally", "stat_ally", _allyText, "coop");
        _allyCard.AddThemeStyleboxOverride("panel", HudTheme.Box(Palette.Inset, Palette.Ally, 1, radius: 3, padX: 8, padY: 6));
        _sections.AddChild(_allyCard);
        var drone = Widgets.Column(4);
        var batteryRow = Widgets.Row(6);
        batteryRow.AddChild(Widgets.Label("BATTERY", "Heading"));
        batteryRow.AddChild(_battery);
        drone.AddChild(batteryRow);
        drone.AddChild(_droneText);
        drone.AddChild(_cargoRow);
        _droneCard = Section("Scout drone", "skill_dronepiloting", drone, "drones");
        _droneHeading = _droneCard.GetChild(0).GetChild(0).GetChild<Label>(1);
        _sections.AddChild(_droneCard);

        BodyRows(_bodyCard.Body);
        _sections.AddChild(_bodyCard);

        var profile = _profileCard.Body;
        profile.AddChild(Stat("ui_move", "Move", _move, "expeditions"));
        profile.AddChild(Stat("stat_sight", "Sight", _sight, "fog"));
        profile.AddChild(Stat("stat_armor", "Armor", _armor, "gear_mods"));
        profile.AddChild(Stat("stat_dodge", "Dodge", _dodge, "stealth"));
        _moreButton = Widgets.TextButton("", ToggleMore, "AP per round, initiative and the attacks", "Flat");
        _moreButton.CustomMinimumSize = new Vector2(0, 22);
        _moreButton.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        _moreButton.AddThemeFontSizeOverride("font_size", 13);
        Motion.OnPress(_moreButton);
        profile.AddChild(_moreButton);
        _more.AddChild(Stat("stat_ap", "AP per round", _apMax, "controls"));
        _more.AddChild(Stat("stat_initiative", "Initiative", _initiative, null,
            "Speed: with stamina, the action and traits it makes up priority, who goes first when the round resolves."));
        _more.AddChild(_attacks);
        profile.AddChild(_more);
        _more.Visible = _moreOpen;
        _moreButton.Text = _moreOpen ? "Less" : "More";
        _sections.AddChild(_profileCard);

        _gearCard.Body.AddChild(_gear);
        _gearCard.Body.AddChild(_gearLoad);
        _sections.AddChild(_gearCard);
        // A folded card skips its refreshes; opening it catches up.
        _gearCard.Toggled += _ => Present(_shown);
        _bodyCard.Toggled += _ => Present(_shown);
    }

    private static Label Value()
    {
        var label = Widgets.Label("", "Number");
        label.AddThemeFontSizeOverride("font_size", 15);
        label.HorizontalAlignment = HorizontalAlignment.Right;
        label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        label.ClipText = true;
        label.MouseFilter = MouseFilterEnum.Pass;
        return label;
    }

    /// <summary>"[icon] MOVE ........ 3 tiles · straight (i)": one labelled field-profile value.</summary>
    private static HBoxContainer Stat(string icon, string caption, Label value, string? topic, string tooltip = "")
    {
        var row = Widgets.Row(6);
        row.AddChild(Icons.Rect(Icons.Get(icon, 14), 14, Palette.TextMuted));
        var label = Widgets.Label(caption.ToUpperInvariant(), "Heading");
        label.MouseFilter = MouseFilterEnum.Pass;
        label.TooltipText = tooltip;
        row.AddChild(label);
        row.AddChild(value);
        if (topic is not null) row.AddChild(new InfoTip(topic));
        else row.AddChild(new Control { CustomMinimumSize = new Vector2(InfoTip.Size_, 0), MouseFilter = MouseFilterEnum.Ignore });
        return row;
    }

    private void ToggleMore()
    {
        _moreOpen = !_moreOpen;
        _moreButton.Text = _moreOpen ? "Less" : "More";
        if (_moreOpen) Motion.Reveal(_more, new Vector2(0, -6));
        else _more.Visible = false;
        Present(_shown);
    }

    private static PanelContainer Section(string title, string icon, Control content, string? topic = null)
    {
        var card = new PanelContainer { ThemeTypeVariation = "Card" };
        var col = Widgets.Column(4);
        card.AddChild(col);
        col.AddChild(Widgets.SectionHeader(title, icon, topic is null ? null : new InfoTip(topic)));
        col.AddChild(content);
        return card;
    }

    private void BodyRows(VBoxContainer rows)
    {
        rows.AddChild(_bodyOk);
        foreach (var part in Enum.GetValues<BodyPart>())
        {
            var row = Widgets.Row(6);
            var icon = Icons.Rect(Icons.BodyPart(part.ToString(), 18), 18, Palette.TextMuted);
            row.AddChild(icon);
            var text = Widgets.Label("", "", size: 13);
            text.CustomMinimumSize = new Vector2(118, 0);
            text.ClipText = true;
            text.MouseFilter = MouseFilterEnum.Stop;
            row.AddChild(text);
            var bar = new StatBar(null) { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            row.AddChild(bar);
            row.Visible = false;
            rows.AddChild(row);
            _parts[part] = (row, icon, text, bar);
        }
        _conditions.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _conditions.MouseFilter = MouseFilterEnum.Stop;
        rows.AddChild(_conditions);
    }

    public void Present(Unit? unit)
    {
        _shown = unit;
        var known = unit is not null && (_presenter.CanSee(unit) || _presenter.IsMine(unit));
        _set.Visible(_sections, known);
        _set.Visible(_kind, known);
        _set.Visible(_hp, known);
        _set.Visible(_portrait, known);
        _set.Visible(_empty, !known);
        if (!known)
        {
            _set.Visible(_apRow, false);
            _set.Visible(_gauges, false);
            _set.Visible(_turnTag, false);
            _set.Visible(_status, false);
            _set.Text(_title, "No unit selected");
            _set.FontColor(_title, Palette.TextMuted);
            return;
        }

        var data = _presenter.Sim.Data;
        var profile = _presenter.Sim.Profile(unit!);
        // An enemy shows what the colony's bestiary knows: position, facing and readied strike always; AP and moves once fought;
        // health numbers, armor, dodge and damage once killed.
        _tier = _presenter.KnowledgeOf(unit!);
        var side = SidePalette.For(_presenter, unit!);
        _portrait.Set(UnitPortrait.For(GetTree(), unit!), side.Darkened(0.55f));
        _portrait.Alert = unit!.Status == UnitStatus.Downed;
        _set.Text(_title, unit.Name);
        _set.FontColor(_title, side.Lightened(0.2f));
        _set.Text(_kind, ScavengeText.Kind(data, unit));
        _set.Text(_status, ScavengeText.Status(unit).ToUpperInvariant());
        _set.FontColor(_status, unit.Status switch
        {
            UnitStatus.Active => Palette.TextMuted,
            UnitStatus.Extracted or UnitStatus.Evacuated => Palette.Good,
            _ => Palette.Danger,
        });

        // Selected vs acting: the right column gives the acting unit's orders; say so on another of yours.
        var acting = _presenter.ActingUnit;
        var isActing = acting is not null && acting.Id == unit.Id;
        var waiting = acting is not null && !isActing && _presenter.IsMine(unit) && unit.IsActive && unit.Kind != UnitKind.Drone;
        _set.Visible(_turnTag, waiting);
        _set.Visible(_status, !(waiting && unit.Status == UnitStatus.Active)); // "ACTIVE" says nothing next to the tag
        _set.Text(_turnTag, "NOT SELECTED FOR ORDERS");
        _set.Tooltip(_turnTag, waiting ? $"The orders on the right are {acting!.Name}'s. Select {unit.Name} to plan for them." : "");

        var health = unit.MaxHp > 0 ? Mathf.Clamp(unit.Hp / unit.MaxHp, 0, 1) : 0;
        var hpKnown = _tier >= BestiaryTier.Killed;
        _hp.Set(health, Bestiary.Health(unit, _tier)[3..]);
        _set.Tooltip(_hp, unit.Status == UnitStatus.Downed
            ? $"Down: bleeds out in {unit.BleedOut} rounds unless stabilized{(unit.Stabilized ? " (stabilized)" : "")}"
            : hpKnown ? $"Health {unit.Hp:0}/{unit.MaxHp:0}" : "How hurt it looks. Kill one to learn its health (Bestiary).");
        // The acting unit's AP is next to Ready in the turn bar; the card shows it for your side's others. A rival's AP spent is its secret.
        _set.Visible(_apRow, !isActing && unit.Side == _presenter.Side && unit.Kind is not (UnitKind.Device or UnitKind.Decoy) && _tier >= BestiaryTier.Fought);
        _ap.Set(unit.Ap, profile.MaxAp, Stuns.Active(_presenter.Sim.State, unit) ? Palette.Warn : Palette.Amber);
        _set.Tooltip(_ap, $"{unit.Ap} of {profile.MaxAp} action points" + (Stuns.Active(_presenter.Sim.State, unit) ? " · stunned: fewer AP and MP next round" : ""));

        // Stamina and movement are your own side's to read (Core redacts rivals'); adrenaline shows on everyone.
        _set.Visible(_gauges, unit.Kind is not (UnitKind.Device or UnitKind.Decoy));
        _set.Text(_gauges, (unit.Side == _presenter.Side ? $"MP {unit.Mp}/{profile.MaxMp} · STA {Economy.Stamina(_presenter.Sim, unit)}/{profile.MaxStamina} · " : "")
                           + $"ADR {unit.Adrenaline:0}" + (Stuns.Active(_presenter.Sim.State, unit) ? " · STUNNED" : ""));
        var allied = _presenter.IsAlly(unit) || unit.CarryingId is not null || unit.CarriedById is not null;
        _set.Visible(_allyCard, allied);
        if (allied)
        {
            var owner = unit.PlayerId is { } pid ? _presenter.PlayerName(pid) : "";
            var lines = new List<string>();
            if (_presenter.IsAlly(unit)) lines.Add($"{owner}'s party: their orders, shared sight. Next to them you can hand items over, patch them up or carry them out.");
            if (unit.CarryingId is { } c && _presenter.State.FindUnit(c) is { } carried) lines.Add($"Carrying {carried.Name} (slower).");
            if (unit.CarriedById is { } by && _presenter.State.FindUnit(by) is { } carrier) lines.Add($"Carried by {carrier.Name}.");
            _set.Text(_allyText, string.Join("\n", lines));
        }
        RefreshDrone(data, unit);
        // Machines have no limbs or pack worth a card.
        var person = unit.Kind is not (UnitKind.Drone or UnitKind.Device or UnitKind.Decoy);
        _set.Visible(_bodyCard, person);
        _set.Visible(_gearCard, person);

        if (person) RefreshBody(data, unit);
        RefreshProfile(profile);
        if (person) RefreshGear(data, unit, profile);
    }

    /// <summary>A drone's card (battery pips, who flies it, how far it may go), or an operator's drone in the air or in the pack.</summary>
    private void RefreshDrone(ScavengeData data, Unit unit)
    {
        var state = _presenter.State;
        var drone = unit.Kind == UnitKind.Drone ? unit : Drones.DroneOf(state, unit);
        var op = unit.Kind == UnitKind.Drone ? (unit.OperatorId is { } o ? state.FindUnit(o) : null) : unit;
        var packed = unit.Kind == UnitKind.Drone ? null : Drones.CarriedDrone(data, unit);
        _droneCard.Visible = drone is not null || packed is not null;
        if (!_droneCard.Visible) return;
        var item = drone?.DroneItemId ?? packed!;
        var spec = data.Items[item].Drone!;
        _droneHeading.Text = ScavengeText.Item(data, item).ToUpperInvariant();
        RefreshCargo(data, drone, spec);
        var max =op is not null && op.PlayerId == _presenter.PlayerId ? Drones.MaxBattery(data, op, item) : data.Items[item].Drone!.Battery;
        var charge = drone?.Battery ?? (op is not null ? Drones.PackedBattery(data, op, item) : max);
        var low = charge <= 1 ? Palette.Danger : charge < data.Rules.Drone.MinBattery ? Palette.Warn : Palette.Network.Lerp(Palette.Amber, 0.85f);
        _battery.Set(charge, Math.Max(max, charge), low);
        _battery.TooltipText = $"{charge} of {max} rounds of battery";
        var lines = new List<string>();
        if (drone is null) lines.Add(charge < data.Rules.Drone.MinBattery ? "In the pack, charging." : $"In the pack, ready to launch ({Hotkeys.KeyName("scav_drone")}).");
        else
        {
            if (op is not null && _presenter.CanSee(op) || op?.Side == _presenter.Side)
                lines.Add($"Flown by {op!.Name}{(op.PlayerId == _presenter.PlayerId ? $" · control range {Drones.ControlRange(data, op, item)}" : "")}.");
            lines.Add(Drones.Jammed(_presenter.Sim, drone.Pos) ? "Jammed: 1 tile a flight, battery drains fast." : $"Flies {data.Items[item].Drone!.FlyRange} tiles over anything.");
            if (drone.Id == unit.Id) lines.Add("Flies on its operator's AP; melee cannot reach it, the Linked shoot it down.");
        }
        lines.Add(spec.Role switch
        {
            DroneRole.Medic => $"Medic: drops a bandage or med-kit from the operator's pack on someone next to it ({Hotkeys.KeyName("scav_drone_action")}).",
            DroneRole.Cargo => $"Cargo: loads up to {spec.Cargo} items from a cache next to it ({Hotkeys.KeyName("scav_drone_action")}), ferries them out from an exit ({Hotkeys.KeyName("scav_ferry")}).",
            DroneRole.Combat => $"Combat: fires its gun at a hostile it sees ({Hotkeys.KeyName("scav_drone_action")}), aimed with the operator's Drone Piloting.",
            _ => "Scout: sees far and shares its sight with the team.",
        });
        _droneText.Text = string.Join("\n", lines);
    }

    /// <summary>A cargo drone's hold: one chip per item and an empty slot per free place.</summary>
    private void RefreshCargo(ScavengeData data, Unit? drone, DroneItemDef spec)
    {
        _cargoRow.Visible = spec.Role == DroneRole.Cargo;
        if (!_cargoRow.Visible) return;
        var hold = drone?.Pack ?? [];
        var signature = string.Join(",", hold) + $"/{spec.Cargo}";
        if (signature == _cargoSignature) return;
        _cargoSignature = signature;
        Widgets.Clear(_cargoRow);
        _cargoRow.AddThemeConstantOverride("h_separation", 4);
        _cargoRow.AddThemeConstantOverride("v_separation", 4);
        _cargoRow.AddChild(Widgets.Label($"HOLD {hold.Count}/{spec.Cargo}", "Heading"));
        foreach (var id in hold)
            _cargoRow.AddChild(Widgets.Chip(Icons.Item(data.Items[id].DefinedId, data.Items[id].Kind.ToString(), 16), ScavengeText.Item(data, id),
                tooltip: $"{ScavengeText.Item(data, id)} in the hold: ferried home from an exit, safe whatever happens."));
        for (var i = hold.Count; i < spec.Cargo; i++)
            _cargoRow.AddChild(Widgets.Chip(null, "empty", Palette.TextDim, "A free place in the hold."));
    }

    /// <summary>
    /// "All parts ok" while healthy; otherwise only the parts that are hurt, lost, locked or prosthetic, each with its
    /// bar (one convention: "hurt 85%" is the damage). The folded card's summary counts them.
    /// </summary>
    private void RefreshBody(ScavengeData data, Unit unit)
    {
        var cyber = data.Cybernetics;
        var med = data.Medicine;
        var tags = MedicalText.Tags(med, unit.Body);
        if (unit.CalmUntilRound >= _presenter.Sim.State.Round) tags.Add("calm");
        if (Exposed.Active(_presenter.Sim.State, unit)) tags.Add("exposed: acts later, short of breath");
        if (unit.CrashAp > 0) tags.Add($"stim crash −{unit.CrashAp} AP next round");
        int hurt = 0, lost = 0, fitted = 0;
        foreach (var (part, _) in _parts)
            switch (unit.Body[part].Condition)
            {
                case PartCondition.Injured: hurt++; break;
                case PartCondition.Missing: lost++; break;
                case PartCondition.Healthy: break;
                default: fitted++; break;
            }
        var summary = new List<string>();
        if (hurt > 0) summary.Add($"{hurt} hurt");
        if (lost > 0) summary.Add($"{lost} lost");
        if (fitted > 0) summary.Add($"{fitted} prosthetic");
        if (tags.Count > 0) summary.Add(string.Join(", ", tags));
        _bodyCard.Summary(summary.Count == 0 ? "all ok" : string.Join(" · ", summary), hurt + lost > 0 ? Palette.Warn : Palette.TextMuted);
        if (!_bodyCard.Open) return;

        _set.Text(_conditions, tags.Count == 0 ? "" : "⚕ " + string.Join(" · ", tags));
        _set.Visible(_conditions, tags.Count > 0);
        _set.Tooltip(_conditions, med is null ? "" : string.Join("\n", unit.Body.Conditions.Select(c => MedicalText.Line(med, c, field: true))));
        _set.Visible(_bodyOk, hurt + lost + fitted == 0);
        foreach (var (part, (row, icon, text, bar)) in _parts)
        {
            var p = unit.Body[part];
            _set.Visible(row, p.Condition != PartCondition.Healthy);
            if (p.Condition == PartCondition.Healthy) continue;
            var name = ScavengeText.Part(part);
            switch (p.Condition)
            {
                case PartCondition.Injured:
                    var broken = med is not null && unit.Body.Find(med.Fracture.Condition, part) is { } f ? (f.Treated ? " · splint" : " · broken") : "";
                    _set.Text(text, broken.Length > 0 ? $"{name}{broken}" : $"{name} · hurt");
                    _set.Tooltip(text, $"{name}: hurt {Fmt.Percent(p.Severity)}{(broken.Length > 0 ? broken.Replace(" · ", ", ") : "")}. The bar is what still works.");
                    bar.Set(1 - p.Severity, Fmt.Percent(p.Severity), p.Severity >= 0.5 ? Palette.Danger : Palette.Warn);
                    _set.Modulate(icon, Palette.Danger);
                    break;
                case PartCondition.Missing:
                    _set.Text(text, $"{name} · lost");
                    _set.Tooltip(text, "");
                    bar.Set(0, "lost", Palette.Danger);
                    _set.Modulate(icon, Palette.TextDim);
                    break;
                default:
                    var node = CyberText.Node(cyber, p);
                    var item = p.ProstheticItemId is { } pid && data.Items.TryGetValue(pid, out var def) ? def : null;
                    if (p.Interfered)
                    {
                        // Network interference this round: the part is dead weight until the next roll.
                        _set.Text(text, $"{name} · LOCKED");
                        bar.Set(0, "locked", Palette.Network);
                        _set.Modulate(icon, Palette.Network);
                        _set.Tooltip(text, $"{item?.Name ?? "Implant"}: locked up by Network interference this round (Linked nearby, an Override hacker or a jammer).");
                        break;
                    }
                    // The item's own name ("Crude arm") reads better than a tier code.
                    _set.Text(text, item is not null ? item.Name : $"{name} · prosthetic");
                    bar.Set(Math.Min(1, p.Efficiency), Fmt.Percent(p.Efficiency), p.Efficiency >= 1 ? Palette.Good : Palette.Warn);
                    _set.Modulate(icon, cyber is not null && node is not null ? CyberText.Tint(cyber, node.Tier).Lerp(Palette.Amber, 0.3f) : Palette.Amber);
                    _set.Tooltip(text, item is null ? "" : string.Join("\n", new[] { $"{name}: " + item.Name + (cyber is not null && node is not null ? $" ({CyberText.TierAndSpec(cyber, node)})" : "") }
                        .Concat(CyberText.ItemStats(item))
                        .Concat(node is null ? [] : CyberText.Effects(node.Effects with { JobOutput = new(), CraftQuality = new() }))
                        .Append(cyber is not null && node is not null && cyber.Tier(node.Tier).Interference ? "Network-grade: Linked nearby can lock it up." : "")
                        .Where(l => l.Length > 0)));
                    break;
            }
        }
    }

    /// <summary>How a move mode reads to a player: "3 tiles, straight lines" rather than "Rook 3".</summary>
    public static string MoveWords(MoveMode mode) => mode.Pattern switch
    {
        MovePattern.King => $"{mode.Range} {(mode.Range == 1 ? "tile" : "tiles")}, any direction",
        MovePattern.Rook => $"{mode.Range} tiles, straight lines",
        MovePattern.Bishop => $"{mode.Range} tiles, diagonals",
        MovePattern.Queen => $"{mode.Range} tiles, any straight line",
        MovePattern.Knight => "an L-shaped jump",
        _ => $"{mode.Pattern} {mode.Range}",
    } + (mode.ApCost > 1 ? $" ({mode.ApCost} AP)" : "");

    private void RefreshProfile(UnitProfile profile)
    {
        var modes = profile.Movement.Modes;
        var first = modes.FirstOrDefault();
        if (_tier < BestiaryTier.Killed && RefreshUnknown(profile, first)) return;
        _set.Text(_move, first is null ? "—" : MoveWords(first) + (modes.Count > 1 ? $" +{modes.Count - 1}" : ""));
        _set.Tooltip(_move, "How far one move goes, like a chess piece:\n" + string.Join("\n", modes.Select(m => MoveWords(m) + $" ({m.Source ?? "on foot"})")));
        _set.Text(_sight, $"{profile.Sight:0.#} tiles");
        _set.Tooltip(_sight, "How far this unit sees. Hidden units still need your detection to beat their stealth.");
        _set.Text(_armor, $"{profile.Armor:0.#}");
        _set.Tooltip(_armor, "Damage taken off each hit.");
        _set.Text(_dodge, Fmt.Percent(profile.Dodge));
        _set.Tooltip(_dodge, "Chance to make a hit miss (an ambush ignores it).");
        _profileCard.Summary(first is null ? "" : $"{(first.Pattern == MovePattern.Knight ? "jump" : $"move {first.Range}")} · sight {profile.Sight:0.#}");
        if (!_moreOpen) return;
        _set.Text(_apMax, $"{profile.MaxAp}");
        _set.Text(_initiative, $"{profile.Initiative:0}");
        var n = 0;
        foreach (var a in profile.Actions)
        {
            if (n == _attacks.GetChildCount())
            {
                var line = Widgets.Label("", "Small");
                line.ClipText = true;
                line.MouseFilter = MouseFilterEnum.Pass;
                _attacks.AddChild(line);
            }
            var label = _attacks.GetChild<Label>(n++);
            _set.Visible(label, true);
            _set.Text(label, $"{a.Name} · {a.Pattern} {a.Reach} · {a.MinDamage:0}–{a.MaxDamage:0} dmg · {Fmt.Percent(a.Accuracy)}");
            _set.Tooltip(label, $"{a.Name}: {a.Pattern} reach {a.Reach}, {a.MinDamage:0}–{a.MaxDamage:0} damage, {a.ApCost} AP, accuracy {Fmt.Percent(a.Accuracy)}");
        }
        for (var i = n; i < _attacks.GetChildCount(); i++) _set.Visible(_attacks.GetChild<Label>(i), false);
    }

    /// <summary>
    /// An enemy the bestiary has not opened up: reach always, moves once fought, the rest "?" until one is killed.
    /// Returns true when it drew the card.
    /// </summary>
    private bool RefreshUnknown(UnitProfile profile, MoveMode? first)
    {
        const string hidden = "Learned in the Bestiary: kill one to know it.";
        var fought = _tier >= BestiaryTier.Fought;
        _set.Text(_move, !fought || first is null ? "?" : MoveWords(first));
        _set.Tooltip(_move, fought ? "How far one move goes." : "Fight one to learn how it moves (Bestiary).");
        _set.Text(_sight, "?");
        _set.Tooltip(_sight, hidden);
        _set.Text(_armor, "?");
        _set.Tooltip(_armor, hidden);
        _set.Text(_dodge, "?");
        _set.Tooltip(_dodge, hidden);
        var reach = profile.Actions.Count == 0 ? 0 : profile.Actions.Max(a => a.Reach);
        _profileCard.Summary(_tier == BestiaryTier.Unknown ? "unknown" : $"reach {reach}");
        if (!_moreOpen) return true;
        _set.Text(_apMax, fought ? $"{profile.MaxAp}" : "?");
        _set.Text(_initiative, "?");
        var n = 0;
        foreach (var a in profile.Actions)
        {
            if (n == _attacks.GetChildCount())
            {
                var line = Widgets.Label("", "Small");
                line.ClipText = true;
                line.MouseFilter = MouseFilterEnum.Pass;
                _attacks.AddChild(line);
            }
            var label = _attacks.GetChild<Label>(n++);
            _set.Visible(label, true);
            _set.Text(label, _tier == BestiaryTier.Unknown ? "?" : $"{a.Name} · {a.Pattern} {a.Reach}");
            _set.Tooltip(label, hidden);
        }
        for (var i = n; i < _attacks.GetChildCount(); i++) _set.Visible(_attacks.GetChild<Label>(i), false);
        return true;
    }

    private BestiaryTier _tier = BestiaryTier.Killed;

    /// <summary>A Jury-rig order (fit a mod from the pack, or take one off): the screen runs it.</summary>
    public event Action<IScavengeCommand>? CommandRequested;

    private static readonly EquipSlot[] GearSlots = [EquipSlot.Weapon, EquipSlot.Body, EquipSlot.Head, EquipSlot.Tool];

    private string? _gearSignature;
    private readonly StatBar _gearLoad = new("stat_load");

    private void RefreshGear(ScavengeData data, Unit unit, UnitProfile profile)
    {
        // The one load bar on the card (the loot panel has its own while it is open), and the folded summary.
        var fraction = profile.CarryCapacity > 0 ? profile.Load / profile.CarryCapacity : 0;
        var (color, note) = LootText.Carry(data, profile.Load, profile.CarryCapacity);
        var weapon = unit.Equipment.TryGetValue(EquipSlot.Weapon, out var held) ? ScavengeText.Item(data, held) : "unarmed";
        _gearCard.Summary($"{weapon} · {profile.Load:0.#}/{profile.CarryCapacity:0} kg", color == Palette.Rust ? Palette.TextMuted : color);
        _gearLoad.Set(fraction, $"{profile.Load:0.#}/{profile.CarryCapacity:0} kg", color);
        _set.Tooltip(_gearLoad, $"Load / carry capacity. Loaded units move fewer tiles a round and are louder.{(note.Length > 0 ? $"\n{note}" : "")}");
        if (!_gearCard.Open) return;

        // The rows only change with the gear and pack (and, for Jury-rig, whose turn it is and the AP); Present runs
        // after every event, and rebuilding the rows each time cost a frame.
        var juryRig = data.GearMods is { } gm && unit.Hook(gm.Def.Rules.FieldHook) > 0 && _presenter.ActingUnit?.Id == unit.Id;
        var signature = $"{unit.Id}|{(juryRig ? unit.Ap : -1)}|{juryRig}|{_presenter.IsMine(unit)}|"
                        + string.Join(",", GearSlots.Select(s => unit.Equipment.GetValueOrDefault(s))) + "|" + string.Join(",", unit.Pack)
                        + "|" + string.Join(",", unit.Pack.Distinct().Select(id => ElectronicWarfare.ChargesLeft(data, unit, id)));
        if (signature == _gearSignature) return;
        _gearSignature = signature;
        Widgets.Clear(_gear);
        foreach (var slot in GearSlots)
        {
            var has = unit.Equipment.TryGetValue(slot, out var id);
            // An empty head slot stays out of the way (most survivors go bare-headed).
            if (!has && slot == EquipSlot.Head) continue;
            var row = Widgets.Row(6);
            var def = has ? data.Items.GetValueOrDefault(id!) : null;
            row.AddChild(Icons.Rect(def is null ? Icons.Get(Colony.Ui.ExpeditionPanel.SlotIcon(slot), 18) : Icons.Item(def.DefinedId, def.Kind.ToString(), 18), 18,
                has ? Palette.Text : Palette.TextDim));
            row.AddChild(Widgets.Label(slot.ToString().ToUpperInvariant(), "Heading"));
            var name = Widgets.Label(def is not null ? GearText.Title(data, def) : has ? ScavengeText.Item(data, id!) : "—", "", has ? Palette.Text : Palette.TextDim, 14);
            name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            name.HorizontalAlignment = HorizontalAlignment.Right;
            name.ClipText = true;
            name.MouseFilter = MouseFilterEnum.Stop;
            name.TooltipText = def is null ? "" : GearText.Line(data, def);
            row.AddChild(name);
            _gear.AddChild(row);
            if (def is { Mods.Count: > 0 })
            {
                var chips = GearText.ModChips(data, def, 12);
                chips.Alignment = FlowContainer.AlignmentMode.End;
                _gear.AddChild(chips);
            }
            if (def is not null) JuryRig(data, unit, slot, def);
        }
        if (unit.Pack.Count > 0)
        {
            var flow = new HFlowContainer();
            flow.AddThemeConstantOverride("h_separation", 4);
            flow.AddThemeConstantOverride("v_separation", 4);
            foreach (var group in unit.Pack.GroupBy(p => p))
            {
                var def = data.Items.GetValueOrDefault(group.Key);
                var charges = def is null || !_presenter.IsMine(unit) ? null : ElectronicWarfare.ChargesLeft(data, unit, group.Key);
                var text = $"{ScavengeText.Item(data, group.Key)}{(group.Count() > 1 ? $" ×{group.Count()}" : "")}" + (charges is { } c ? $" · {c} chg" : "");
                var tip = def?.Use is not null ? ScavengeText.UseTip(data, def, charges) + "\nIn the pack" : "In the pack";
                flow.AddChild(Widgets.Chip(def is null ? Icons.Get("item_generic", 14) : Icons.Item(def.DefinedId, def.Kind.ToString(), 14),
                    text, charges is 0 ? Palette.Danger : null, tip, def?.NetworkGrade == true ? Palette.Network : null));
            }
            _gear.AddChild(flow);
        }
    }

    /// <summary>
    /// Jury-rig (the field_repair perk hook): on the unit's own turn, Fit buttons for the mods in its pack that fit this
    /// piece and Remove buttons for the mods on it (FieldMod; Core checks the AP and the slots).
    /// </summary>
    private void JuryRig(ScavengeData data, Unit unit, EquipSlot slot, ItemDef item)
    {
        if (data.GearMods is not { } mods || unit.Hook(mods.Def.Rules.FieldHook) <= 0) return;
        if (_presenter.ActingUnit?.Id != unit.Id || mods.Moddable(item.DefinedId) is null) return;
        var fits = GearBench.FittingMods(data, item.Id, unit.Pack).ToList();
        if (fits.Count == 0 && item.Mods.Count == 0) return;
        var cost = mods.Def.Rules.FieldApCost;
        var flow = new HFlowContainer { Alignment = FlowContainer.AlignmentMode.End };
        flow.AddThemeConstantOverride("h_separation", 4);
        flow.AddThemeConstantOverride("v_separation", 4);
        var pid = _presenter.PlayerId;
        var tag = Widgets.Label($"JURY-RIG {cost} AP", "Small", Palette.TextMuted);
        tag.CustomMinimumSize = new Vector2(0, 22);
        flow.AddChild(tag);
        foreach (var mod in fits)
        {
            var outcome = GearBench.Install(data, item.Id, mod);
            var b = Widgets.TextButton($"+ {ScavengeText.Item(data, mod)}",
                () => CommandRequested?.Invoke(new FieldMod(pid, unit.Id, ModItemId: mod, Slot: slot)), $"Jury-rig ({cost} AP): {outcome.Message}", "Flat");
            b.CustomMinimumSize = new Vector2(0, 22);
            b.AddThemeFontSizeOverride("font_size", 12);
            b.Disabled = !outcome.Ok || unit.Ap < cost;
            b.AddThemeColorOverride("font_color", GearText.ModColor(data, mod, Palette.Amber));
            flow.AddChild(b);
        }
        foreach (var mod in item.Mods)
        {
            var modSlot = mods.SlotOf(mod)!;
            var b = Widgets.TextButton($"− {ScavengeText.Item(data, mod)}",
                () => CommandRequested?.Invoke(new FieldMod(pid, unit.Id, RemoveSlot: modSlot, Slot: slot)), $"Jury-rig ({cost} AP): take it off into the pack", "Flat");
            b.CustomMinimumSize = new Vector2(0, 22);
            b.AddThemeFontSizeOverride("font_size", 12);
            b.Disabled = unit.Ap < cost;
            b.AddThemeColorOverride("font_color", Palette.TextMuted);
            flow.AddChild(b);
        }
        _gear.AddChild(flow);
    }
}
