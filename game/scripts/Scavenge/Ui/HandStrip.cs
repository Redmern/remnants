using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Core.Scavenge;
using Remnants.Game.Hud;
using Remnants.Game.Hud.Help;
using UiKit = Remnants.Game.Colony.Ui.Ui;

namespace Remnants.Game.Scavenge.Ui;

/// <summary>
/// The squad's hand of tactical cards along the bottom: the round's card points (pips), draw and used pile counts, a
/// card per hand card and per ready personal slot card (cost, AP, category). Choosing a card opens the character
/// picker under the cards: every conscious unit of the party with a play button where the card can go on it now
/// (<see cref="TacticsView.Picker"/>, Core's own checks) and the reason where it cannot, the AP boost when the card has
/// one, and Discard for a dead hard card nobody can use. An aimed card (an ability at a unit or tile, or a reaction)
/// is not played from the picker: choosing who plays it asks the screen to aim it on the map (<see cref="AimRequested"/>,
/// <see cref="TacticsView.Targets"/>), and the row shows it is being aimed until it is played or cancelled. Everything
/// shown comes from <see cref="TacticsView"/>; Core decides.
/// </summary>
public partial class HandStrip : PanelContainer
{
    private readonly ScavengePresenter _presenter;
    private readonly Label _points = Widgets.Label("", "Number", Palette.Amber, 16);
    private readonly Label _piles = Widgets.Label("", "Small", Palette.TextMuted);
    private readonly HBoxContainer _cards = Widgets.Row(6);
    private readonly VBoxContainer _picker = Widgets.Column(4);
    private string? _chosen;
    private int? _chosenSlot;
    private bool _boost;
    private string? _signature;

    public event Action<IScavengeCommand>? CommandRequested;

    /// <summary>A card was played (for the strip's flourish and the sound).</summary>
    public event Action<string>? CardPlayed;

    /// <summary>An aimed card is to be aimed on the map: the card, who plays it, boost, from that unit's slot.</summary>
    public event Action<string, int, bool, bool>? AimRequested;

    /// <summary>The player cancelled the aim from the strip.</summary>
    public event Action? AimCancelled;

    /// <summary>The card and unit the screen is aiming on the map now (set by the screen), or null.</summary>
    public (string CardId, int UnitId)? Aiming { get; set; }

    public HandStrip(ScavengePresenter presenter)
    {
        _presenter = presenter;
        Name = "Hand";
        MouseFilter = MouseFilterEnum.Stop;
        // Room for a full hand of five plus a couple of slot cards without scrolling.
        CustomMinimumSize = new Vector2(720, 0);
        var box = UiKit.Panel(this, 4);
        var header = Widgets.Row(8);
        header.AddChild(Widgets.SectionHeader("Cards", "ui_items", new InfoTip("cards")));
        header.AddChild(Widgets.Label("POINTS", "Small", Palette.TextMuted));
        header.AddChild(_points);
        header.AddChild(Widgets.Spacer());
        header.AddChild(_piles);
        box.AddChild(header);
        var scroll = new ScrollContainer { VerticalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(0, 74) };
        scroll.AddChild(_cards);
        box.AddChild(scroll);
        box.AddChild(_picker);
    }

    /// <summary>The card the picker is open for (debug captures).</summary>
    public void Choose(string? cardId, int? slotOf = null)
    {
        // Another card (or none) chosen: an aim in progress is dropped.
        if (Aiming is { } aiming && (aiming.CardId != cardId || slotOf != _chosenSlot)) AimCancelled?.Invoke();
        _chosen = cardId;
        _chosenSlot = slotOf;
        _boost = false;
        _signature = null;
        Refresh(false);
    }

    public void Refresh(bool busy)
    {
        var hand = _presenter.Hand();
        var signature = hand is null || busy ? "" :
            $"{hand.Points}/{hand.Budget}|{hand.DrawCount}|{hand.UsedCount}|{_presenter.State.CurrentUnitId}|{_presenter.ActingUnit?.Ap}|" +
            string.Join(",", hand.Cards.Select(c => $"{c.Id}:{c.SlotOf}:{string.Join(".", c.PlayableBy)}:{c.Dead}")) +
            $"|{_chosen}:{_chosenSlot}:{_boost}|{Aiming}|{string.Join(",", _presenter.ReactionTargets().Select(u => u.Id))}";
        if (signature == _signature) return;
        _signature = signature;
        Presence.Set(this, hand is not null && !busy && !_presenter.Done);
        if (hand is null || busy) return;

        _points.Text = $"{new string('●', hand.Points)}{new string('○', Math.Max(0, hand.Budget - hand.Points))}  {hand.Points}/{hand.Budget}";
        _piles.Text = $"draw {hand.DrawCount} · used {hand.UsedCount}";
        Widgets.Clear(_cards);
        foreach (var card in hand.Cards) _cards.AddChild(CardButton(card));
        if (_chosen is not null && !hand.Cards.Any(c => c.Id == _chosen && c.SlotOf == _chosenSlot)) _chosen = null;
        BuildPicker(hand);
    }

    private Control CardButton(CardView card)
    {
        var playable = card.PlayableBy.Count > 0;
        var chosen = card.Id == _chosen && card.SlotOf == _chosenSlot;
        var slotOwner = card.SlotOf is { } owner ? _presenter.State.FindUnit(owner)?.Name : null;
        var text = $"{card.Name}\n{card.Cost} pt{(card.ApCost > 0 ? $" · {card.ApCost} AP" : "")}{(card.Reaction ? " · reaction" : "")}";
        var tip = $"{card.Name} ({card.Category}{(card.Kind == CardKind.Hard ? ", " + card.Requirement : "")})\n{card.Text}"
                  + (slotOwner is not null ? $"\nIn {slotOwner}'s slot: always to hand for them." : "")
                  + (card.Consumable ? "\nUsed up when played." : "")
                  + (card.ArmLock ? "\nNeeds working arms." : "")
                  + (card.Dead ? "\nNobody in the party can use it: discard it for free (once a round)." : "");
        var button = Widgets.TextButton(text, () => Choose(chosen ? null : card.Id, card.SlotOf), tip, chosen ? "Primary" : "", 112);
        button.CustomMinimumSize = new Vector2(112, 64);
        button.Modulate = playable || card.Dead ? Colors.White : new Color(1, 1, 1, 0.55f);
        button.AddThemeColorOverride("font_color", card.Category switch
        {
            CardCategory.Attack => Palette.Danger,
            CardCategory.Defense => Palette.Good,
            CardCategory.Movement => Palette.Amber,
            _ => Palette.Text,
        });
        if (slotOwner is not null) button.Text = $"◆ {button.Text}";
        return button;
    }

    private void BuildPicker(HandView hand)
    {
        Widgets.Clear(_picker);
        if (_chosen is not { } id || hand.Cards.FirstOrDefault(c => c.Id == id && c.SlotOf == _chosenSlot) is not { } card) return;
        var pid = _presenter.PlayerId;
        _picker.AddChild(Widgets.Wrapped($"{card.Name}: {card.Text}", "Small", Palette.TextMuted));
        var options = Widgets.Row(8);
        if (card.BoostAp > 0)
            options.AddChild(Widgets.TextButton(_boost ? $"Boost on (+{card.BoostAp} AP)" : $"Boost (+{card.BoostAp} AP)", () =>
            {
                _boost = !_boost;
                _signature = null;
                Refresh(false);
            }, "Spend more AP for the card's boost", _boost ? "Primary" : ""));
        var aimed = _presenter.IsAimed(card.Id);
        if (card.Reaction && _presenter.ReactionTargets().Count == 0)
            options.AddChild(Widgets.Label("No seen enemy plan to answer.", "Small", Palette.Warn));
        if (Aiming is { } now && now.CardId == card.Id)
            options.AddChild(Widgets.TextButton("Cancel aim", () => AimCancelled?.Invoke(), "Stop aiming (right-click or Esc)"));
        if (card.Dead && !hand.DeadDiscardUsed && _presenter.ActingUnit is { } acting)
            options.AddChild(Widgets.TextButton("Discard (nobody can use it)", () => Send(new DiscardDeadCard(pid, acting.Id, card.Id)),
                "A hard card nobody in the party qualifies for: discard it for free, once a round"));
        if (options.GetChildCount() > 0) _picker.AddChild(options);

        var fromSlot = card.SlotOf is not null;
        foreach (var row in _presenter.Picker(card.Id, _boost, null, fromSlot)
                     .Where(r => card.SlotOf is null || r.UnitId == card.SlotOf))
        {
            var line = Widgets.Row(8);
            if (aimed)
            {
                // Aimed: who plays it, then where on the map. Playable when some target works for this unit.
                var targets = _presenter.Targets(row.UnitId, card.Id, _boost, fromSlot);
                var valid = targets.Count(o => o.Ok);
                var reasons = targets.Select(o => o.Reason).Distinct().ToList();
                var why = valid > 0 ? $"{valid} target{(valid == 1 ? "" : "s")}: pick one on the map"
                    : targets.Count == 0 ? (row.Ok || row.Reason.Length == 0 ? "Nothing in sight to aim at." : row.Reason)
                    : reasons.Count == 1 ? reasons[0] : "No target works now.";
                var aiming = Aiming is { } a && a.CardId == card.Id && a.UnitId == row.UnitId;
                var aim = Widgets.TextButton(aiming ? $"Aiming: {row.Name}" : valid > 0 ? $"Aim with {row.Name}" : row.Name,
                    () => AimRequested?.Invoke(card.Id, row.UnitId, _boost, fromSlot), why, valid > 0 ? "Primary" : "", 150);
                aim.Disabled = valid == 0;
                line.AddChild(aim);
                line.AddChild(Widgets.Wrapped(aiming ? "Click a lit target on the map · right-click or Esc cancels" : why, "Small",
                    valid > 0 ? (aiming ? Palette.Amber : Palette.Good) : Palette.TextMuted));
                _picker.AddChild(line);
                continue;
            }
            var play = Widgets.TextButton(row.Ok ? $"Play on {row.Name}" : row.Name,
                () => Send(new PlayCard(pid, row.UnitId, card.Id, null, Boost: _boost, FromSlot: fromSlot)),
                row.Ok ? row.Effect : row.Reason, row.Ok ? "Primary" : "", 150);
            play.Disabled = !row.Ok;
            line.AddChild(play);
            line.AddChild(Widgets.Wrapped(row.Ok ? row.Effect : row.Reason, "Small", row.Ok ? Palette.Good : Palette.TextMuted));
            _picker.AddChild(line);
        }
        Motion.FadeIn(_picker);
    }

    /// <summary>The screen played the aimed card on the map: the strip's flourish, and the picker closes.</summary>
    public void Played(string cardId)
    {
        CardPlayed?.Invoke(cardId);
        _chosen = null;
        _signature = null;
    }

    private void Send(IScavengeCommand command)
    {
        if (command is PlayCard play) CardPlayed?.Invoke(play.CardId);
        _chosen = null;
        _signature = null;
        CommandRequested?.Invoke(command);
    }
}
