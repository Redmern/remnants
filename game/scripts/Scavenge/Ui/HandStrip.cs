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
/// one, the enemy plan a reaction answers, and Discard for a dead hard card nobody can use. Everything shown comes from
/// <see cref="TacticsView"/>; Core decides.
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
    private int? _target;
    private string? _signature;

    public event Action<IScavengeCommand>? CommandRequested;

    /// <summary>A card was played (for the strip's flourish and the sound).</summary>
    public event Action<string>? CardPlayed;

    public HandStrip(ScavengePresenter presenter)
    {
        _presenter = presenter;
        Name = "Hand";
        MouseFilter = MouseFilterEnum.Stop;
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
        _chosen = cardId;
        _chosenSlot = slotOf;
        _boost = false;
        _target = _presenter.ReactionTargets().FirstOrDefault()?.Id;
        _signature = null;
        Refresh(false);
    }

    public void Refresh(bool busy)
    {
        var hand = _presenter.Hand();
        var signature = hand is null || busy ? "" :
            $"{hand.Points}/{hand.Budget}|{hand.DrawCount}|{hand.UsedCount}|{_presenter.State.CurrentUnitId}|{_presenter.ActingUnit?.Ap}|" +
            string.Join(",", hand.Cards.Select(c => $"{c.Id}:{c.SlotOf}:{string.Join(".", c.PlayableBy)}:{c.Dead}")) +
            $"|{_chosen}:{_chosenSlot}:{_boost}:{_target}|{string.Join(",", _presenter.ReactionTargets().Select(u => u.Id))}";
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
        if (card.Reaction)
        {
            var targets = _presenter.ReactionTargets();
            if (targets.Count == 0) options.AddChild(Widgets.Label("No seen enemy plan to answer.", "Small", Palette.Warn));
            foreach (var t in targets)
                options.AddChild(Widgets.TextButton($"vs {t.Name}", () =>
                {
                    _target = t.Id;
                    _signature = null;
                    Refresh(false);
                }, $"Answer {t.Name}'s telegraphed plan", _target == t.Id ? "Primary" : ""));
        }
        if (card.Dead && !hand.DeadDiscardUsed && _presenter.ActingUnit is { } acting)
            options.AddChild(Widgets.TextButton("Discard (nobody can use it)", () => Send(new DiscardDeadCard(pid, acting.Id, card.Id)),
                "A hard card nobody in the party qualifies for: discard it for free, once a round"));
        if (options.GetChildCount() > 0) _picker.AddChild(options);

        foreach (var row in _presenter.Picker(card.Id, _boost, card.Reaction ? _target : null, card.SlotOf is not null)
                     .Where(r => card.SlotOf is null || r.UnitId == card.SlotOf))
        {
            var line = Widgets.Row(8);
            var play = Widgets.TextButton(row.Ok ? $"Play on {row.Name}" : row.Name,
                () => Send(new PlayCard(pid, row.UnitId, card.Id, card.Reaction ? _target : null, Boost: _boost, FromSlot: card.SlotOf is not null)),
                row.Ok ? row.Effect : row.Reason, row.Ok ? "Primary" : "", 150);
            play.Disabled = !row.Ok;
            line.AddChild(play);
            line.AddChild(Widgets.Wrapped(row.Ok ? row.Effect : row.Reason, "Small", row.Ok ? Palette.Good : Palette.TextMuted));
            _picker.AddChild(line);
        }
        Motion.FadeIn(_picker);
    }

    private void Send(IScavengeCommand command)
    {
        if (command is PlayCard play) CardPlayed?.Invoke(play.CardId);
        _chosen = null;
        _signature = null;
        CommandRequested?.Invoke(command);
    }
}
