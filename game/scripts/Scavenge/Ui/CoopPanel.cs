using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Core.Scavenge;
using Remnants.Game.Hud;
using UiKit = Remnants.Game.Colony.Ui.Ui;

namespace Remnants.Game.Scavenge.Ui;

/// <summary>
/// Teamwork for the acting unit, as <see cref="ActionButton"/>s with AP pips and hotkeys: hand an item to an ally next
/// to it (G; the selected ally if it is one), carry or put down a downed unit of the team (C), team pings in one row of
/// icon buttons named in their tooltips (Z danger, V go here, N loot; then click a tile), and alliances: accept (Y) or decline (U) an offer, offer one to a rival
/// player whose party you see (Y), or announce leaving the team (no hotkey, it takes a round's warning). Hidden when
/// there is nothing to do. Every button sends a Core command through the screen; Core decides whether it works.
/// </summary>
public partial class CoopPanel : PanelContainer
{
    private readonly ScavengePresenter _presenter;
    private readonly VBoxContainer _content = Widgets.Column(4);
    private readonly List<ActionButton> _buttons = new();
    private readonly PanelRows _rows;

    public event Action<IScavengeCommand>? CommandRequested;

    /// <summary>Trade with an ally (multiplayer): open a trade window with that player.</summary>
    public event Action<int>? TradeRequested;

    /// <summary>Trading is possible (a multiplayer mission).</summary>
    public bool CanTrade { get; set; }

    /// <summary>A ping kind was picked: the next tile click places it.</summary>
    public event Action<PingKind>? PingChosen;

    public CoopPanel(ScavengePresenter presenter)
    {
        _rows = new PanelRows(_content);
        _presenter = presenter;
        CustomMinimumSize = new Vector2(ScavengeHud.ColumnWidth, 0);
        var box = UiKit.Panel(this, 4);
        box.AddChild(Widgets.SectionHeader("Team", "stat_ally", new Hud.Help.InfoTip("coop")));
        box.AddChild(_content);
    }

    /// <summary>What the next map click does while a ping is being placed (the right column's header line).</summary>
    public static string PingHint(PingKind kind) => $"Click a tile to ping \"{PingName(kind)}\" for your team. Right-click or Esc cancels.";

    public bool PressHotkey(InputEvent e)
    {
        if (!Visible) return false;
        var button = _buttons.FirstOrDefault(b => b.Hotkey is { } k && !b.Disabled && Hotkeys.Pressed(e, k));
        if (button is null) return false;
        if (button.ToggleMode) button.ButtonPressed = !button.ButtonPressed;
        button.EmitSignal(BaseButton.SignalName.Pressed);
        return true;
    }

    /// <param name="selected">The selected unit: an ally next to the acting unit gets the items.</param>
    /// <param name="pinging">The ping kind being placed, if any.</param>
    public void Refresh(bool busy, Unit? selected, PingKind? pinging)
    {
        _rows.Begin();
        _buttons.Clear();
        Build(busy, selected, pinging);
        _rows.End();
    }

    private void Build(bool busy, Unit? selected, PingKind? pinging)
    {
        var unit = _presenter.ActingUnit;
        if (unit is null || busy)
        {
            Presence.Set(this, false);
            return;
        }
        var sim = _presenter.Sim;
        var coop = sim.Data.Rules.Coop;
        var pid = _presenter.PlayerId;
        var allies = _presenter.Allies.ToList();
        if (allies.Count > 0)
            _rows.Note($"Allied with {string.Join(", ", allies.Select(_presenter.PlayerName))}: shared sight, no friendly fire.");

        // Alliance offers to answer come first: they are why the panel shows up.
        foreach (var offer in _presenter.OffersToMe)
        {
            var from = offer.FromPlayerId;
            Add("stat_check", $"Accept {_presenter.PlayerName(from)}'s alliance", "shared sight, no friendly fire", 0, "scav_ally",
                () => CommandRequested?.Invoke(new RespondAlliance(pid, unit.Id, from, true))).ThemeTypeVariation = "Primary";
            Add("ui_close", "Decline", $"stay hostile to {_presenter.PlayerName(from)}", 0, "scav_decline",
                () => CommandRequested?.Invoke(new RespondAlliance(pid, unit.Id, from, false)));
        }

        var target = selected is not null && _presenter.IsAlly(selected) && selected.IsActive && selected.Pos.Chebyshev(unit.Pos) <= 1
            ? selected
            : _presenter.GiveTargetsNear(unit).FirstOrDefault(u => _presenter.IsAlly(u));
        if (target is not null && unit.Pack.Count > 0)
        {
            _rows.Heading($"GIVE TO {target.Name.ToUpperInvariant()}", "coop");
            var first = true;
            foreach (var id in unit.Pack.Distinct())
            {
                var itemId = id;
                var targetId = target.Id;
                var button = Add($"item_{sim.Data.Items[id].Kind.ToString().ToLowerInvariant()}", $"Give {ScavengeText.Item(sim.Data, id)}",
                        $"goes home with {_presenter.PlayerName(target.PlayerId!.Value)}", coop.GiveApCost, first ? "scav_give" : null,
                    () => CommandRequested?.Invoke(new GiveItem(pid, unit.Id, targetId, itemId)));
                button.SetBlocked(CoopActions.CheckGive(sim, unit, target, id) is not null, coop.GiveApCost > unit.Ap);
                first = false;
            }
        }

        if (unit.CarryingId is { } carriedId && _presenter.State.FindUnit(carriedId) is { } carried)
        {
            _rows.Heading("CARRY", "carry_downed");
            var spot = _presenter.PutDownSpot(unit);
            var button = Add("ui_move", $"Put {carried.Name} down", "on a free tile next to you", 0, "scav_carry",
                () => { if (spot is { } at) CommandRequested?.Invoke(new PutDown(pid, unit.Id, at)); });
            button.SetBlocked(spot is null);
        }
        else
            foreach (var downed in _presenter.CarryableNear(unit).Take(1))
            {
                _rows.Heading("CARRY", "carry_downed");
                var id = downed.Id;
                var button = Add("stat_injured", $"Carry {downed.Name}", $"slower (-{coop.CarryRangePenalty} move) · out with you",
                    coop.CarryApCost, "scav_carry", () => CommandRequested?.Invoke(new Carry(pid, unit.Id, id)));
                button.SetBlocked(coop.CarryApCost > unit.Ap, coop.CarryApCost > unit.Ap);
            }

        if (allies.Count > 0)
        {
            var left = coop.PingsPerTurn - unit.PingsUsed;
            _rows.Heading($"PING ({left}/{coop.PingsPerTurn} LEFT)", "pings");
            // One row of three icon buttons, made once and kept.
            var pings = _rows.Row(() =>
            {
                var row = Widgets.Row(4);
                foreach (var (kind, hotkey) in Pings)
                {
                    var k = kind;
                    var b = new ActionButton(PingIcon(kind), "", "", 0, hotkey, $"Ping \"{PingName(kind)}\": mark a tile for your team (then click it)");
                    b.OnPressed = () => PingChosen?.Invoke(k);
                    row.AddChild(b);
                }
                return row;
            });
            for (var i = 0; i < Pings.Length; i++)
            {
                var button = pings.GetChild<ActionButton>(i);
                button.Toggle(pinging == Pings[i].Kind);
                button.SetBlocked(left <= 0);
                _buttons.Add(button);
            }
        }

        // Offer an alliance to a rival whose party yours sees (Core checks they see you too).
        foreach (var rival in _presenter.Rivals.Where(r => _presenter.OffersToMe.All(o => o.FromPlayerId != r.PlayerId)))
        {
            var to = rival.PlayerId;
            var pending = _presenter.State.Proposals.Any(p => p.FromPlayerId == pid && p.ToPlayerId == to);
            var seen = _presenter.State.Units.Any(u => u.PlayerId == to && u.IsActive && _presenter.CanSee(u));
            if (!seen && !pending) continue;
            var button = Add("stat_ally", pending ? $"Offer sent to {_presenter.PlayerName(to)}" : $"Offer {_presenter.PlayerName(to)} an alliance",
                pending ? "they answer while planning" : "both parties must see each other", 0,
                    pending || _presenter.OffersToMe.Any() ? null : "scav_ally", // Y accepts an open offer first
                () => CommandRequested?.Invoke(new ProposeAlliance(pid, unit.Id, to)));
            button.SetBlocked(pending);
        }

        if (CanTrade)
            foreach (var ally in allies)
            {
                var other = ally;
                Add("ui_trade", $"Trade with {_presenter.PlayerName(other)}", "swap pack items · free, any time", 0, null,
                    () => TradeRequested?.Invoke(other));
            }

        if (allies.Count > 0)
        {
            var leaving = _presenter.TeamBreaks.FirstOrDefault(b => b.PlayerId == pid);
            var button = Add("ui_leave", leaving is null ? "Leave the alliance" : $"Leaving at round {leaving.EffectiveRound}",
                    $"after {coop.BreakWarningRounds} round's warning both sides see", 0, null,
                    "Announce that you leave the team. Shared sight and the truce end when the warning is over.",
                () => CommandRequested?.Invoke(new BreakAlliance(pid, unit.Id)));
            button.SetBlocked(leaving is not null);
            button.ThemeTypeVariation = "Flat";
        }

        Presence.Set(this, _buttons.Count > 0);
    }

    private static readonly (PingKind Kind, string Hotkey)[] Pings =
        [(PingKind.Danger, "scav_ping_danger"), (PingKind.GoHere, "scav_ping_go"), (PingKind.Loot, "scav_ping_loot")];

    public static string PingName(PingKind kind) => kind switch
    {
        PingKind.Danger => "Danger",
        PingKind.GoHere => "Go here",
        _ => "Loot",
    };

    public static string PingIcon(PingKind kind) => kind switch
    {
        PingKind.Danger => "stat_warning",
        PingKind.GoHere => "ui_ping",
        _ => "stat_loot",
    };

    public static Color PingColor(PingKind kind) => kind switch
    {
        PingKind.Danger => Palette.Danger,
        PingKind.GoHere => Palette.Ally,
        _ => Palette.Amber,
    };

    private ActionButton Add(string icon, string text, string detail, int apCost, string? hotkey, Action onPressed) =>
        Add(icon, text, detail, apCost, hotkey, "", onPressed);

    private ActionButton Add(string icon, string text, string detail, int apCost, string? hotkey, string tooltip, Action onPressed)
    {
        var button = _rows.Button(icon, text, detail, apCost, hotkey, tooltip, onPressed);
        _buttons.Add(button);
        return button;
    }
}
