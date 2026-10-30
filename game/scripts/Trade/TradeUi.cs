using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Core.Trade;
using Remnants.Game.Hud;

namespace Remnants.Game.Trade;

/// <summary>
/// Shared pieces of the trade screens: an offer chip per good (icon, amount, name, quality tag, mods / Mk, reference
/// value in the tooltip), a goods picker (what you have → amount → add), and value summaries for the fair-swap hint.
/// </summary>
public static class TradeUi
{
    public static TradeRules Rules => GameDataPaths.LoadTrade();

    public static string Name(TradeGood g) => TradeGoods.Name(g, GameDataPaths.Load(), GameDataPaths.LoadScavenge());

    public static double Value(IEnumerable<TradeGood> goods) => TradePrices.Of(goods, Rules, GameDataPaths.Load(), GameDataPaths.LoadScavenge());

    public static Texture2D Icon(TradeGood g, int size = 18)
    {
        if (g.Kind == GoodKind.Resource) return Icons.Resource(g.Id, size);
        var item = GameDataPaths.LoadScavenge().Items.GetValueOrDefault(g.Id);
        return item is null ? Icons.Get("item_generic", size) : Icons.Item(item.DefinedId, item.Kind.ToString(), size);
    }

    /// <summary>Debug runs (scripted trades, the Trading Post demo): a fresh colony gets the Radio Mast trading needs.</summary>
    public static void EnsureMastForDebug(Remnants.Core.Colony.ColonySim sim)
    {
        if (TradeUi.Rules.Courier.RequiresBuilding is not { } id || !sim.Data.Buildings.ContainsKey(id) || sim.State.Buildings.Any(b => b.DefId == id)) return;
        if (sim.FreePlots().FirstOrDefault(-1) is var plot and >= 0)
            sim.State.Buildings.Add(new Remnants.Core.Colony.Building { Id = sim.State.NextBuildingId++, DefId = id, Level = 1, Plot = plot });
    }

    public static string Describe(System.Collections.Generic.IEnumerable<TradeGood> goods) =>
        TradeGoods.Describe(goods, GameDataPaths.Load(), GameDataPaths.LoadScavenge());

    /// <summary>"Fine", "Crude" … for a quality variant, null for a base item.</summary>
    public static string? Quality(TradeGood g) =>
        g.Kind == GoodKind.Item && GameDataPaths.LoadScavenge().Items.GetValueOrDefault(g.Id) is { Quality: { } q } ? q : null;

    /// <summary>One good as a chip; <paramref name="onRemove"/> adds a small × to take it off the table.</summary>
    public static PanelContainer Chip(TradeGood g, Action? onRemove = null, bool dim = false)
    {
        var chip = new PanelContainer { ThemeTypeVariation = "Chip", MouseFilter = Control.MouseFilterEnum.Stop };
        var row = Widgets.Row(5);
        row.MouseFilter = Control.MouseFilterEnum.Ignore;
        chip.AddChild(row);
        row.AddChild(Icons.Rect(Icon(g, 18), 18, g.Kind == GoodKind.Resource ? Palette.TextMuted : null));
        var count = Widgets.Label(g.Count > 1 || g.Kind == GoodKind.Resource ? $"{g.Count}" : "", "Number", Palette.Amber, 15);
        count.Visible = count.Text.Length > 0;
        row.AddChild(count);
        row.AddChild(Widgets.Label(Name(g), color: dim ? Palette.TextMuted : Palette.Text, size: 14));
        if (Quality(g) is { } quality) row.AddChild(Tag(quality, Palette.Amber));
        if (g.Extras is { IsEmpty: false } extras)
        {
            if (extras.Mk is { } mk) row.AddChild(Tag($"Mk {mk}", Palette.Good));
            if (extras.Mods is { Count: > 0 } mods) row.AddChild(Tag($"{mods.Count} mod{(mods.Count == 1 ? "" : "s")}", Palette.Good));
            if (extras.Lots is { Count: > 0 } lots)
            {
                // Food: the soonest-to-spoil lot sets the tag; quality and taint show when they are not plain.
                var days = lots.Where(l => l.TicksLeft is not null).Select(l => l.TicksLeft!.Value).DefaultIfEmpty(-1).Min() / GameDataPaths.Load().Rules.TicksPerDay;
                if (days >= 0) row.AddChild(Tag($"keeps {days:0.#}d", days < 1 ? Palette.Warn : Palette.TextMuted));
                if (lots.Select(l => l.Quality).Distinct().SingleOrDefault() is { } q && q != Remnants.Core.Colony.FoodQuality.Standard) row.AddChild(Tag(q, Palette.Amber));
                if (lots.Any(l => l.Tainted)) row.AddChild(Tag("tainted", Palette.Danger));
            }
        }
        if (onRemove is not null)
        {
            var remove = Widgets.IconButton("ui_close", "", "Take it off the table", onRemove, variation: "Flat");
            remove.CustomMinimumSize = new Vector2(24, 24);
            row.AddChild(remove);
        }
        var modLine = g.Extras?.Mods is { Count: > 0 } m ? $"\nMods: {string.Join(", ", m)}" : "";
        chip.TooltipText = $"{g.Count} × {Name(g)}{modLine}\nReference value ≈ {Value([g]):0} scrap";
        return chip;
    }

    public static Label Tag(string text, Color color)
    {
        var tag = Widgets.Label(text.ToUpperInvariant(), "Small", color, 11);
        return tag;
    }

    /// <summary>Chips in a wrapping flow, or a muted placeholder when there are none.</summary>
    public static HFlowContainer Chips(IEnumerable<TradeGood> goods, Func<TradeGood, Action?>? remove = null, string empty = "nothing yet")
    {
        var flow = new HFlowContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        flow.AddThemeConstantOverride("h_separation", 4);
        flow.AddThemeConstantOverride("v_separation", 4);
        var any = false;
        foreach (var g in goods)
        {
            any = true;
            flow.AddChild(Chip(g, remove?.Invoke(g)));
        }
        if (!any) flow.AddChild(Widgets.Label(empty, "Muted"));
        return flow;
    }

    /// <summary>"≈ 40 scrap" value line with a fairness hint against the other side.</summary>
    public static string Fairness(double give, double get)
    {
        if (give <= 0 && get <= 0) return "Nothing on the table yet.";
        if (give <= 0) return Palette.Paint("A gift to you.", Palette.Good);
        if (get <= 0) return Palette.Paint("You get nothing back.", Palette.Warn);
        var ratio = get / give;
        return ratio switch
        {
            >= 1.25 => Palette.Paint($"Good for you (×{ratio:0.0}).", Palette.Good),
            >= 0.8 => Palette.Paint("About even.", Palette.Text),
            _ => Palette.Paint($"You give more (×{1 / ratio:0.0}).", Palette.Warn),
        };
    }
}

/// <summary>
/// A picker for one good: a list of what you have (with how many), an amount, and Add. The list comes from the caller
/// (colony stockpile and inventory, or the party's packs in the field).
/// </summary>
public partial class GoodPicker : HBoxContainer
{
    private readonly OptionButton _list = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill, FitToLongestItem = false, ClipText = true };
    private readonly SpinBox _amount = new() { MinValue = 1, MaxValue = 1, Value = 1, CustomMinimumSize = new Vector2(84, 0) };
    private List<(TradeGood Good, int Have)> _choices = new();

    public event Action<TradeGood>? Picked;

    public GoodPicker()
    {
        AddThemeConstantOverride("separation", 6);
        AddChild(_list);
        AddChild(_amount);
        AddChild(Widgets.IconButton("ui_plus", "Add", "Put this on the table", Add, variation: "Primary"));
        _list.ItemSelected += _ => Clamp();
    }

    /// <summary>What can be offered: a good (Count ignored) and how many are available.</summary>
    public void SetChoices(IEnumerable<(TradeGood Good, int Have)> choices)
    {
        var keep = _list.Selected >= 0 && _list.Selected < _choices.Count ? _choices[_list.Selected].Good.Key : null;
        _choices = choices.Where(c => c.Have > 0).ToList();
        _list.Clear();
        foreach (var (g, have) in _choices) _list.AddIconItem(TradeUi.Icon(g, 16), $"{TradeUi.Name(g)} ({have})");
        var index = keep is null ? 0 : Math.Max(0, _choices.FindIndex(c => c.Good.Key == keep));
        if (_choices.Count > 0) _list.Select(index);
        _list.Disabled = _choices.Count == 0;
        Clamp();
    }

    private void Clamp()
    {
        var have = _list.Selected >= 0 && _list.Selected < _choices.Count ? _choices[_list.Selected].Have : 1;
        _amount.MaxValue = Math.Max(1, have);
        _amount.Value = Math.Min(_amount.Value, _amount.MaxValue);
    }

    private void Add()
    {
        if (_list.Selected < 0 || _list.Selected >= _choices.Count) return;
        Picked?.Invoke(_choices[_list.Selected].Good with { Count = (int)_amount.Value });
    }
}
