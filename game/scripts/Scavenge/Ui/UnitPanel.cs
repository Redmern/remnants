using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Core.Items;
using Remnants.Core.Scavenge;
using Remnants.Core.Survivors;
using Remnants.Game.Hud;
using UiKit = Remnants.Game.Colony.Ui.Ui;

namespace Remnants.Game.Scavenge.Ui;

/// <summary>
/// The selected unit as card sections: a header (portrait from the 3D model with its gear, name, kind, status, HP bar
/// and AP pips, bleed-out), Body (each part with its icon and a bar, like the colony Body tab), Field profile (movement,
/// initiative, sight, armor, dodge, attacks as chips) and Gear (slots, pack, load). An ally's unit (co-op) gets an
/// Ally section on top: whose party it is and that its orders are theirs (the card is read-only), and who carries whom.
/// </summary>
public partial class UnitPanel : PanelContainer
{
    private readonly ScavengePresenter _presenter;
    private readonly Portrait _portrait = new(64);
    private readonly Label _title = Widgets.Label("", "Display");
    private readonly Label _kind = Widgets.Label("", "Small");
    private readonly Label _status = Widgets.Label("", "Number");
    private readonly StatBar _hp = new("stat_hp");
    private readonly Pips _ap = new();
    private readonly Label _apLabel = Widgets.Label("AP", "Heading");
    private readonly VBoxContainer _sections = Widgets.Column(8);
    private readonly Dictionary<BodyPart, (TextureRect Icon, Label Text, StatBar Bar)> _parts = new();
    private readonly MarginContainer _profile = new();
    private readonly VBoxContainer _gear = Widgets.Column(3);
    private readonly Label _empty = Widgets.Wrapped("Click a unit. Your units take orders on their turn.", "Muted");
    private readonly Label _allyText = Widgets.Wrapped("", "Small");
    private PanelContainer _allyCard = null!;
    private PanelContainer _droneCard = null!;
    private PanelContainer _bodyCard = null!;
    private PanelContainer _gearCard = null!;
    private readonly Pips _battery = new();
    private readonly Label _droneText = Widgets.Wrapped("", "Small");
    private string _profileSignature = "";

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
        sub.AddChild(_kind);
        _status.AddThemeFontSizeOverride("font_size", 13);
        sub.AddChild(_status);
        names.AddChild(sub);
        names.AddChild(_hp);
        var ap = Widgets.Row(6);
        ap.AddChild(_apLabel);
        ap.AddChild(_ap);
        names.AddChild(ap);
        head.AddChild(names);
        box.AddChild(head);
        box.AddChild(_empty);
        box.AddChild(_sections);

        _allyCard = Section("Ally", "stat_ally", _allyText);
        _allyCard.AddThemeStyleboxOverride("panel", HudTheme.Box(Palette.Inset, Palette.Ally, 1, radius: 3, padX: 8, padY: 6));
        _sections.AddChild(_allyCard);
        var drone = Widgets.Column(4);
        var batteryRow = Widgets.Row(6);
        batteryRow.AddChild(Widgets.Label("BATTERY", "Heading"));
        batteryRow.AddChild(_battery);
        drone.AddChild(batteryRow);
        drone.AddChild(_droneText);
        _droneCard = Section("Scout drone", "skill_dronepiloting", drone);
        _sections.AddChild(_droneCard);
        _bodyCard = Section("Body", "body_full", BodyRows());
        _sections.AddChild(_bodyCard);
        _sections.AddChild(Section("Field profile", "ui_move", _profile));
        _gearCard = Section("Gear", "item_tool", _gear);
        _sections.AddChild(_gearCard);
    }

    private static PanelContainer Section(string title, string icon, Control content)
    {
        var card = new PanelContainer { ThemeTypeVariation = "Card" };
        var col = Widgets.Column(4);
        card.AddChild(col);
        col.AddChild(Widgets.SectionHeader(title, icon));
        col.AddChild(content);
        return card;
    }

    private VBoxContainer BodyRows()
    {
        var rows = Widgets.Column(3);
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
            rows.AddChild(row);
            _parts[part] = (icon, text, bar);
        }
        return rows;
    }

    public void Present(Unit? unit)
    {
        var known = unit is not null && (_presenter.CanSee(unit) || _presenter.IsMine(unit));
        _sections.Visible = _kind.Visible = _status.Visible = _hp.Visible = _ap.Visible = _apLabel.Visible = _portrait.Visible = known;
        _empty.Visible = !known;
        if (!known)
        {
            _title.Text = "No unit selected";
            _title.AddThemeColorOverride("font_color", Palette.TextMuted);
            return;
        }

        var data = _presenter.Sim.Data;
        var profile = _presenter.Sim.Profile(unit!);
        var side = SidePalette.For(_presenter, unit!);
        _portrait.Set(UnitPortrait.For(GetTree(), unit!), side.Darkened(0.55f));
        _portrait.Alert = unit!.Status == UnitStatus.Downed;
        _title.Text = unit.Name;
        _title.AddThemeColorOverride("font_color", side.Lightened(0.2f));
        _kind.Text = ScavengeText.Kind(unit);
        _status.Text = ScavengeText.Status(unit).ToUpperInvariant();
        _status.AddThemeColorOverride("font_color", unit.Status switch
        {
            UnitStatus.Active => Palette.TextMuted,
            UnitStatus.Extracted or UnitStatus.Evacuated => Palette.Good,
            _ => Palette.Danger,
        });

        var health = unit.MaxHp > 0 ? Mathf.Clamp(unit.Hp / unit.MaxHp, 0, 1) : 0;
        _hp.Set(health, $"{unit.Hp:0}/{unit.MaxHp:0}");
        _hp.TooltipText = unit.Status == UnitStatus.Downed
            ? $"Down: bleeds out in {unit.BleedOut} rounds unless stabilized{(unit.Stabilized ? " (stabilized)" : "")}"
            : $"Health {unit.Hp:0}/{unit.MaxHp:0}";
        _ap.Set(unit.Ap, profile.MaxAp, unit.Suppressed ? Palette.Warn : Palette.Amber);
        _ap.TooltipText = $"{unit.Ap} of {profile.MaxAp} action points" + (unit.Suppressed ? " · suppressed: fewer AP next turn" : "");

        _allyCard.Visible = _presenter.IsAlly(unit) || unit.CarryingId is not null || unit.CarriedById is not null;
        var owner = unit.PlayerId is { } pid ? _presenter.PlayerName(pid) : "";
        var lines = new List<string>();
        if (_presenter.IsAlly(unit)) lines.Add($"{owner}'s party: their orders, shared sight. Next to them you can hand items over, patch them up or carry them out.");
        if (unit.CarryingId is { } c && _presenter.State.FindUnit(c) is { } carried) lines.Add($"Carrying {carried.Name} (slower).");
        if (unit.CarriedById is { } by && _presenter.State.FindUnit(by) is { } carrier) lines.Add($"Carried by {carrier.Name}.");
        _allyText.Text = string.Join("\n", lines);
        RefreshDrone(data, unit);
        // Machines have no limbs or pack worth a card.
        _bodyCard.Visible = _gearCard.Visible = unit.Kind is not (UnitKind.Drone or UnitKind.Device);

        RefreshBody(data, unit);
        RefreshProfile(profile);
        RefreshGear(data, unit, profile);
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
        var max = op is not null && op.PlayerId == _presenter.PlayerId ? Drones.MaxBattery(data, op, item) : data.Items[item].Drone!.Battery;
        var charge = drone?.Battery ?? (op is not null ? Drones.PackedBattery(data, op, item) : max);
        var low = charge <= 1 ? Palette.Danger : charge < data.Rules.Drone.MinBattery ? Palette.Warn : Palette.Network.Lerp(Palette.Amber, 0.85f);
        _battery.Set(charge, Math.Max(max, charge), low);
        _battery.TooltipText = $"{charge} of {max} rounds of battery";
        var lines = new List<string>();
        if (drone is null) lines.Add(charge < data.Rules.Drone.MinBattery ? "In the pack, charging." : "In the pack, ready to launch (K).");
        else
        {
            if (op is not null && _presenter.CanSee(op) || op?.Side == _presenter.Side)
                lines.Add($"Flown by {op!.Name}{(op.PlayerId == _presenter.PlayerId ? $" · control range {Drones.ControlRange(data, op, item)}" : "")}.");
            lines.Add(Drones.Jammed(_presenter.Sim, drone.Pos) ? "Jammed: 1 tile a flight, battery drains fast." : $"Flies {data.Items[item].Drone!.FlyRange} tiles over anything.");
            if (drone.Id == unit.Id) lines.Add("Flies on its operator's AP; melee cannot reach it, the Linked shoot it down.");
        }
        _droneText.Text = string.Join("\n", lines);
    }

    private void RefreshBody(ScavengeData data, Unit unit)
    {
        var cyber = data.Cybernetics;
        foreach (var (part, (icon, text, bar)) in _parts)
        {
            var p = unit.Body[part];
            var name = ScavengeText.Part(part);
            switch (p.Condition)
            {
                case PartCondition.Healthy:
                    text.Text = name;
                    bar.Set(1, "ok", Palette.Good);
                    icon.Modulate = Palette.TextMuted;
                    break;
                case PartCondition.Injured:
                    text.Text = $"{name} · hurt";
                    bar.Set(1 - p.Severity, Fmt.Percent(p.Severity), p.Severity >= 0.5 ? Palette.Danger : Palette.Warn);
                    icon.Modulate = Palette.Danger;
                    break;
                case PartCondition.Missing:
                    text.Text = $"{name} · lost";
                    bar.Set(0, "lost", Palette.Danger);
                    icon.Modulate = Palette.TextDim;
                    break;
                default:
                    var node = CyberText.Node(cyber, p);
                    var item = p.ProstheticItemId is { } pid && data.Items.TryGetValue(pid, out var def) ? def : null;
                    if (p.Interfered)
                    {
                        // Network interference this round: the part is dead weight until the next roll.
                        text.Text = $"{name} · LOCKED";
                        bar.Set(0, "locked", Palette.Network);
                        icon.Modulate = Palette.Network;
                        text.TooltipText = $"{item?.Name ?? "Implant"}: locked up by Network interference this round (Linked nearby, an Override hacker or a jammer).";
                        break;
                    }
                    text.Text = node is null ? $"{name} · prosthetic" : $"{name} · T{node.Tier}";
                    bar.Set(Math.Min(1, p.Efficiency), Fmt.Percent(p.Efficiency), p.Efficiency >= 1 ? Palette.Good : Palette.Warn);
                    icon.Modulate = cyber is not null && node is not null ? CyberText.Tint(cyber, node.Tier).Lerp(Palette.Amber, 0.3f) : Palette.Amber;
                    text.TooltipText = item is null ? "" : string.Join("\n", new[] { item.Name + (cyber is not null && node is not null ? $" ({CyberText.TierAndSpec(cyber, node)})" : "") }
                        .Concat(CyberText.ItemStats(item))
                        .Concat(node is null ? [] : CyberText.Effects(node.Effects with { JobOutput = new(), CraftQuality = new() }))
                        .Append(cyber is not null && node is not null && cyber.Tier(node.Tier).Interference ? "Network-grade: Linked nearby can lock it up." : "")
                        .Where(l => l.Length > 0));
                    break;
            }
        }
    }

    private void RefreshProfile(UnitProfile profile)
    {
        var signature = $"{profile.MaxAp}{profile.Initiative}{profile.Sight}{profile.Load}{profile.Armor}{profile.Dodge}"
                        + string.Join(",", profile.Movement.Modes) + string.Join(",", profile.Actions.Select(a => a.Id + a.MinDamage + a.Accuracy));
        if (signature == _profileSignature) return;
        _profileSignature = signature;
        Widgets.Clear(_profile);
        _profile.AddChild(ProfileChips.Build(profile));
    }

    private void RefreshGear(ScavengeData data, Unit unit, UnitProfile profile)
    {
        Widgets.Clear(_gear);
        foreach (var slot in new[] { EquipSlot.Weapon, EquipSlot.Body, EquipSlot.Tool })
        {
            var row = Widgets.Row(6);
            var has = unit.Equipment.TryGetValue(slot, out var id);
            var def = has ? data.Items.GetValueOrDefault(id!) : null;
            var fallback = slot switch { EquipSlot.Weapon => "item_weapon", EquipSlot.Body => "item_armor", _ => "item_tool" };
            row.AddChild(Icons.Rect(def is null ? Icons.Get(fallback, 18) : Icons.Item(def.DefinedId, def.Kind.ToString(), 18), 18,
                has ? Palette.Text : Palette.TextDim));
            row.AddChild(Widgets.Label(slot.ToString().ToUpperInvariant(), "Heading"));
            var name = Widgets.Label(has ? ScavengeText.Item(data, id!) : "—", "", has ? Palette.Text : Palette.TextDim, 14);
            name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            name.HorizontalAlignment = HorizontalAlignment.Right;
            name.ClipText = true;
            row.AddChild(name);
            _gear.AddChild(row);
        }
        if (unit.Pack.Count > 0)
        {
            var flow = new HFlowContainer();
            flow.AddThemeConstantOverride("h_separation", 4);
            flow.AddThemeConstantOverride("v_separation", 4);
            foreach (var group in unit.Pack.GroupBy(p => p))
            {
                var def = data.Items.GetValueOrDefault(group.Key);
                flow.AddChild(Widgets.Chip(def is null ? Icons.Get("item_generic", 14) : Icons.Item(def.DefinedId, def.Kind.ToString(), 14),
                    $"{ScavengeText.Item(data, group.Key)}{(group.Count() > 1 ? $" ×{group.Count()}" : "")}", null, "In the pack"));
            }
            _gear.AddChild(flow);
        }
        var load = new StatBar("stat_load");
        var fraction = profile.CarryCapacity > 0 ? profile.Load / profile.CarryCapacity : 0;
        var (color, note) = LootText.Carry(data, profile.Load, profile.CarryCapacity);
        load.Set(fraction, $"{profile.Load:0.#}/{profile.CarryCapacity:0} kg", color);
        load.TooltipText = $"Load / carry capacity. Loaded units move fewer tiles a turn and are louder.{(note.Length > 0 ? $"\n{note}" : "")}";
        _gear.AddChild(load);
    }
}
