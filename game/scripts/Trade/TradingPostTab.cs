using System;
using System.Linq;
using Remnants.Core.Colony;
using Remnants.Game.Colony;
using Remnants.Game.Colony.Ui;
using Remnants.Game.Hud;

namespace Remnants.Game.Trade;

/// <summary>
/// The Radio Mast's Trading Post tab: what is in escrow and on the road, and a button that opens the full
/// <see cref="TradingPostPanel"/> (browse, post, my offers, claims). Separate from the Trader tab (the NPC caravan).
/// </summary>
public partial class TradingPostTab : RadioTab
{
    /// <summary>Open the Trading Post (the HUD wires it to <see cref="ColonyHud.OpenTradingPost"/>).</summary>
    public static event Action? OpenRequested;

    public TradingPostTab(ColonySession session, RadioPanel panel) : base(session, panel) { }

    public TradingPostTab() { }

    public override string? Badge(ColonySim sim) => sim.State.Trade.Deliveries.Count is > 0 and var n ? $"{n}" : null;

    protected override string StatusText(ColonySim sim) =>
        sim.State.Trade.Deliveries.Count == 0 ? "" : $"{sim.State.Trade.Deliveries.Count} courier(s) on the way.";

    protected override string Signature(ColonySim sim) =>
        $"{sim.State.Trade.Escrows.Count}:{string.Join(",", sim.State.Trade.Deliveries.Select(d => d.TxId))}";

    protected override void Build(ColonySim sim)
    {
        Body.AddChild(Widgets.Wrapped("Barter with other players' colonies: post an offer, take theirs, collect what you are owed. "
            + "Goods you post are held by the Trading Post until someone takes them, you cancel, or the offer expires.", "Muted"));
        var trade = sim.State.Trade;
        var held = trade.Escrows.Count(e => e.Market is not null);
        if (held > 0) Body.AddChild(Widgets.Label($"{held} request(s) waiting for the Trading Post.", "Small"));
        foreach (var d in trade.Deliveries)
        {
            var days = Math.Max(0, d.ArriveTick - sim.State.Tick) / (double)sim.Data.Rules.TicksPerDay;
            Body.AddChild(Widgets.Label($"Courier from {d.From}: {TradeUi.Describe(d.Goods)} · {days:0.#} d", "Small"));
        }
        Body.AddChild(Widgets.IconButton("ui_post", "Open the Trading Post", "Browse, post and claim (needs a Trading Post server)",
            () => OpenRequested?.Invoke(), variation: "Primary"));
    }
}
