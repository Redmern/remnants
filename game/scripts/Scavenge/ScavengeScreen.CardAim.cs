using System.Collections.Generic;
using System.Linq;
using Remnants.Core.Scavenge;
using Remnants.Game.Scavenge.Ui;

namespace Remnants.Game.Scavenge;

/// <summary>
/// Aiming an ability card on the map: after the hand strip's picker chose who plays it, every target Core lists
/// (<see cref="TacticsView.Targets"/>) is lit, the ones it refuses greyed. Hovering one shows what comes of it (the landing
/// tile, the units it touches, the pattern it covers with allies in the warning colour, the hit chance and damage) or why
/// not; a pattern turns free of cost (with the cursor, or the turn key on a tile); a click on a lit one plays the card through the
/// normal command path; right-click or Esc cancels. The options are asked again after every sync, so they never go stale.
/// </summary>
public partial class ScavengeScreen
{
    /// <summary>An aim in progress: the card, who plays it, boost and slot, and the targets Core lists now.</summary>
    private sealed record CardAim(string CardId, string CardName, CardTarget Target, int UnitId, bool Boost, bool FromSlot, List<CardTargetOption> Options, int Turn = 0)
    {
        public CardTargetOption? At(Pos tile) => Options.FirstOrDefault(o => o.Tile == tile);
    }

    private CardAim? _cardAim;

    private void WireCardAim()
    {
        _hud.Hand.AimRequested += BeginCardAim;
        _hud.Hand.AimCancelled += () => CancelCardAim();
    }

    private void BeginCardAim(string cardId, int unitId, bool boost, bool fromSlot)
    {
        if (_busy || _presenter.Sim.Data.Cards.Find(cardId) is not { } card) return;
        _usingItem = null;
        _pinging = null;
        _aim = TechAim.None;
        _cardAim = new CardAim(card.Id, card.Name, card.Target, unitId, boost, fromSlot, _presenter.Targets(unitId, cardId, boost, fromSlot));
        _hud.Hand.Aiming = (cardId, unitId);
        // The camera goes to who plays it, so its targets are in view.
        if (_presenter.State.FindUnit(unitId) is { } u && _presenter.CanSee(u)) _camera.GlideTo(TileGrid.ToWorld(u.Pos));
        RefreshHud();
    }

    /// <summary>Drops the aim (returns whether there was one).</summary>
    private bool CancelCardAim()
    {
        if (_cardAim is null) return false;
        _cardAim = null;
        _hud.Hand.Aiming = null;
        _hud.TargetTip.HideTip();
        RefreshHud();
        return true;
    }

    /// <summary>After a sync: ask Core again (positions and AP changed), or drop the aim if the card or its player is gone.</summary>
    private void RefreshCardAim()
    {
        if (_cardAim is not { } aim) return;
        var stillHeld = _presenter.Hand()?.Cards.Any(c => c.Id == aim.CardId && (c.SlotOf is not null) == aim.FromSlot) == true;
        if (!stillHeld || _presenter.Done || _presenter.State.FindUnit(aim.UnitId) is not { IsActive: true })
        {
            CancelCardAim();
            return;
        }
        _cardAim = aim with { Options = _presenter.Targets(aim.UnitId, aim.CardId, aim.Boost, aim.FromSlot) };
    }

    /// <summary>A click while aiming: a lit target plays the card, anything else says why not. Always consumes the click.</summary>
    private void ClickCardAim(CardAim aim, Pos tile)
    {
        var option = aim.At(tile);
        if (option is null)
        {
            Toast($"Not a target for {aim.CardName}. Right-click cancels.");
            return;
        }
        if (!option.Ok)
        {
            Toast(option.Reason);
            return;
        }
        var command = new PlayCard(_presenter.PlayerId, aim.UnitId, aim.CardId, option.UnitId, aim.Boost, aim.FromSlot,
            aim.Target == CardTarget.Tile ? option.Tile : null, PatternWay(aim, option));
        CancelCardAim();
        _hud.Hand.Played(command);
        _ = Run(command);
    }

    /// <summary>Which way the card's pattern points at this target, null for a card without one.</summary>
    private Pos? PatternWay(CardAim aim, CardTargetOption option) =>
        _presenter.Sim.Data.Cards.Find(aim.CardId)?.Pattern is not null && _presenter.State.FindUnit(aim.UnitId) is { } user
            ? TacticsView.AimDirection(_presenter.Sim, user, option.Tile, aim.Turn) : null;

    /// <summary>Turns the aimed pattern an eighth, free of cost. A pattern played from the unit turns with the cursor instead.</summary>
    private void TurnCardAim()
    {
        if (_cardAim is not { Target: CardTarget.Tile } aim) return;
        _cardAim = aim with { Turn = aim.Turn + 1 };
        RefreshHud();
    }

    /// <summary>Your units' plans on the map, for your eyes: the walk, the tiles the actions cover, and any ally on those in the warning colour.</summary>
    private void HighlightPlans()
    {
        foreach (var unit in _presenter.State.Units.Where(u => u.OnMap && u.IsActive && _presenter.IsMine(u)))
        {
            var plan = TacticsView.PlanPreview(_presenter.Sim, unit);
            foreach (var p in plan.Path) _map.Highlight(p, HighlightKind.PlanPath);
            foreach (var p in plan.ActionTiles) _map.Highlight(p, HighlightKind.PlanAction);
            foreach (var p in plan.AllyTiles) _map.Highlight(p, HighlightKind.AllyWarning, "ALLY");
        }
    }

    /// <summary>Pointing a push or a dash: the tiles around where the plan ends, and for the hovered way what it covers or where it runs.</summary>
    private void HighlightLaunch(Unit unit, TechAim aim)
    {
        var from = Plans.Anchor(_presenter.State, unit);
        foreach (var d in Pos.Neighbours8) _map.Highlight(from + d, HighlightKind.Use);
        if (_hover is not { } hover || hover == from) return;
        var way = TacticsView.AimDirection(_presenter.Sim, unit, hover);
        if (aim == TechAim.Dash)
        {
            var run = TacticsView.DashAim(_presenter.Sim, unit, way);
            foreach (var p in run) _map.Highlight(p, HighlightKind.PlanPath);
            if (run.Count > 0) _map.Highlight(run[^1], HighlightKind.Landing, "LANDS");
            return;
        }
        ShowPattern(TacticsView.PushAim(_presenter.Sim, unit, way), HighlightKind.Attack);
    }

    private void ShowPattern(PatternAimView view, HighlightKind kind)
    {
        foreach (var p in view.Tiles) _map.Highlight(p, kind);
        foreach (var p in view.AllyTiles) _map.Highlight(p, HighlightKind.AllyWarning, "ALLY");
    }

    /// <summary>The tooltip for the target under the cursor while aiming.</summary>
    private void ShowCardAimTip(Pos? hover)
    {
        if (_cardAim is not { } aim || hover is not { } at || aim.At(at) is not { } option || _busy)
        {
            _hud.TargetTip.HideTip();
            return;
        }
        var name = option.UnitId is { } id && _presenter.State.FindUnit(id) is { } u ? u.Name : $"tile {at}";
        _hud.TargetTip.Show(aim.CardName, name, option, _hud.TargetTip.GetGlobalMousePosition());
    }

    /// <summary>
    /// The aim on the map: who plays it, every target (lit or greyed), and for the hovered valid one where its moved unit
    /// lands and the units it touches. Returns false when not aiming.
    /// </summary>
    private bool HighlightCardAim()
    {
        if (_cardAim is not { } aim || _busy || _presenter.Done) return false;
        if (_presenter.State.FindUnit(aim.UnitId) is { } user && user.OnMap) _map.Highlight(user.Pos, HighlightKind.Selected);
        var hovered = _hover is { } h ? aim.At(h) : null;
        foreach (var o in aim.Options)
            _map.Highlight(o.Tile, o.Ok ? HighlightKind.CardTarget : HighlightKind.CardInvalid, o == hovered ? (o.Ok ? "PLAY" : "✕") : null);
        if (hovered is not { Ok: true }) return true;
        foreach (var id in hovered.Affected)
            if (_presenter.State.FindUnit(id) is { OnMap: true } touched && (touched.Id != aim.UnitId || hovered.Landing is null) && touched.Pos != hovered.Tile)
                _map.Highlight(touched.Pos, HighlightKind.Affected);
        if (hovered.Landing is { } land) _map.Highlight(land, HighlightKind.Landing, "LANDS");
        if (PatternWay(aim, hovered) is { } way && _presenter.State.FindUnit(aim.UnitId) is { } aimer && _presenter.Sim.Data.Cards.Find(aim.CardId) is { } card)
            ShowPattern(TacticsView.PatternAim(_presenter.Sim, aimer, card.Id, hovered.Landing ?? (aim.Target == CardTarget.Tile ? hovered.Tile : null), way),
                card.Effect.Block > 0 ? HighlightKind.Use : HighlightKind.Attack);
        return true;
    }

    /// <summary>The acting header's hint while aiming.</summary>
    private string? CardAimHint() => _cardAim is { } aim
        ? $"{aim.CardName}: click a lit target · {aim.Options.Count(o => o.Ok)} of {aim.Options.Count} work{(aim.Target == CardTarget.Tile && _presenter.Sim.Data.Cards.Find(aim.CardId)?.Pattern is not null ? $" · {Hud.Hotkeys.KeyName("scav_turn_aim")} turns it" : "")} · right-click or Esc cancels."
        : null;
}
