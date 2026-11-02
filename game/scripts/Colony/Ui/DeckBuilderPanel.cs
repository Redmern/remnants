using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Core.Colony;
using Remnants.Core.Scavenge;
using Remnants.Game.Hud;
using Remnants.Game.Hud.Help;

namespace Remnants.Game.Colony.Ui;

/// <summary>
/// The village deck builder (Deck in the top bar): the squad deck parties take out, built from the collection
/// (<see cref="DeckBuilder.View"/>: copies owned, in the deck, in slots, free; the deck and copy caps from cards.json),
/// personal slot cards per adult (as many as their slots), and the deck upgrades bought with resources. Every change is
/// a colony command (SetDeck, SetCardSlots, BuyCardUpgrade); Core checks it.
/// </summary>
public partial class DeckBuilderPanel : Overlay
{
    private readonly ColonySession _session;
    private readonly Label _summary = Widgets.Label("", "Number", Palette.Amber, 16);
    private readonly Label _status = Widgets.Label("", "Small", Palette.TextMuted);
    private readonly VBoxContainer _page = Widgets.Column(4);
    private string _tab = "deck";
    private readonly Dictionary<string, Button> _tabs = new();

    public DeckBuilderPanel() : this(null!) { }

    public DeckBuilderPanel(ColonySession session) : base(980)
    {
        _session = session;
        Visible = false;
        Body.AddChild(Ui.TitleRow("Squad deck", "ui_cards", Close, Hotkeys.KeyName("pause_menu")));
        var tag = Widgets.Row(8);
        tag.AddChild(Widgets.Label("WHAT THE SQUAD CARRIES INTO THE ZONE.", "Small", Palette.TextMuted));
        tag.AddChild(Widgets.Spacer());
        tag.AddChild(_summary);
        tag.AddChild(new InfoTip("cards"));
        Body.AddChild(tag);
        var tabs = Widgets.Row(4);
        foreach (var (id, title) in new[] { ("deck", "Deck"), ("slots", "Personal slots"), ("upgrades", "Upgrades") })
        {
            var button = Widgets.TextButton(title, () => Show(id), "", "Tab");
            _tabs[id] = button;
            tabs.AddChild(button);
        }
        Body.AddChild(tabs);
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(0, 520), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _page.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(_page);
        Body.AddChild(scroll);
        Body.AddChild(_status);
    }

    protected override void OnOpened() => Refresh();

    /// <summary>Opens on a tab (debug captures use it too).</summary>
    public void Show(string tab)
    {
        _tab = tab;
        if (!Visible) Open();
        else Refresh();
    }

    public void Refresh()
    {
        if (!Visible || _session.Sim.Scavenge is null) return;
        var view = DeckBuilder.View(_session.Sim);
        _summary.Text = $"{view.DeckCount}/{view.MaxDeck} cards" + (view.Loadout is { } l ? $" · hand {l.HandSize} · {l.Budget} points" : "");
        foreach (var (id, button) in _tabs) button.ThemeTypeVariation = id == _tab ? "Primary" : "Tab";
        Widgets.Clear(_page);
        switch (_tab)
        {
            case "slots": Slots(view); break;
            case "upgrades": Upgrades(view); break;
            default: Deck(view); break;
        }
    }

    private void Deck(DeckBuilderView view)
    {
        _page.AddChild(Header("CARD", "KIND", "OWNED", "IN DECK", "IN SLOTS"));
        foreach (var row in view.Rows)
        {
            var line = Widgets.Row(8);
            var name = Widgets.Label(row.Name, "", Color(row.Category));
            name.CustomMinimumSize = new Vector2(220, 0);
            name.TooltipText = $"{row.Category} · {row.Rarity}\n{row.Text}";
            name.MouseFilter = MouseFilterEnum.Pass;
            line.AddChild(name);
            line.AddChild(Fixed(Widgets.Label($"{row.Category} · {row.Rarity}", "Small", Palette.TextMuted), 170));
            line.AddChild(Fixed(Widgets.Label($"×{row.Owned}", "Number", row.Owned > 0 ? Palette.Text : Palette.TextDim), 70));
            var minus = Widgets.TextButton("−", () => SetDeck(row.Id, -1), "One copy out of the deck", minWidth: 32);
            minus.Disabled = row.InDeck == 0;
            line.AddChild(minus);
            line.AddChild(Fixed(Widgets.Label($"{row.InDeck}/{row.MaxCopies}", "Number"), 54));
            var plus = Widgets.TextButton("+", () => SetDeck(row.Id, +1), "One more copy into the deck", minWidth: 32);
            plus.Disabled = row.Free == 0 || row.InDeck >= row.MaxCopies || view.DeckCount >= view.MaxDeck;
            line.AddChild(plus);
            line.AddChild(Fixed(Widgets.Label(row.InSlots > 0 ? $"{row.InSlots}" : "–", "Small", Palette.TextMuted), 60));
            if (row.Owned == 0) line.Modulate = new Color(1, 1, 1, 0.5f);
            _page.AddChild(line);
        }
    }

    private void Slots(DeckBuilderView view)
    {
        _page.AddChild(Widgets.Wrapped($"Slot cards are always to hand for their holder in the field, paid from the squad's points. " +
                                       $"Slots come from level, skills, gear and perks; at most {view.MaxSlotCards} each.", "Small", Palette.TextMuted));
        var free = view.Rows.Where(r => r.Free > 0).ToList();
        foreach (var s in view.Slots)
        {
            var line = Widgets.Row(8);
            line.AddChild(Fixed(Widgets.Label(s.Name), 160));
            line.AddChild(Fixed(Widgets.Label(s.Slots == 0 ? "no slots" : $"{s.Cards.Count}/{s.Slots} slots", "Small",
                s.Slots == 0 ? Palette.TextDim : Palette.Amber), 90));
            foreach (var (card, i) in s.Cards.Select((c, i) => (c, i)))
            {
                var name = _session.Sim.Scavenge!.Cards.Find(card)?.Name ?? card;
                var left = s.Cards.Where((_, j) => j != i).ToList();
                line.AddChild(Widgets.TextButton($"{name} ×", () => Run(new SetCardSlots(s.SurvivorId, left)), "Take it out of the slot"));
            }
            if (s.Cards.Count < s.Slots && free.Count > 0)
            {
                var pick = new OptionButton { TooltipText = "Put a free card into a slot" };
                pick.AddItem("+ card");
                foreach (var r in free) pick.AddItem(r.Name);
                pick.ItemSelected += index =>
                {
                    if (index > 0) Run(new SetCardSlots(s.SurvivorId, s.Cards.Append(free[(int)index - 1].Id).ToList()));
                };
                line.AddChild(pick);
            }
            _page.AddChild(line);
        }
    }

    private void Upgrades(DeckBuilderView view)
    {
        foreach (var u in view.Upgrades)
        {
            var line = Widgets.Row(8);
            line.AddChild(Fixed(Widgets.Label(u.Name), 180));
            line.AddChild(Fixed(Widgets.Label($"+{u.Amount} {(u.Stat == CardUpgradeStat.HandSize ? "hand size" : u.Stat == CardUpgradeStat.Budget ? "card points" : "draw")}", "Small"), 140));
            line.AddChild(Widgets.CostChips(u.Cost.Select(kv => (kv.Key, _session.Sim.Data.Resources.GetValueOrDefault(kv.Key)?.Name ?? kv.Key,
                (double)kv.Value, _session.Sim.State.Stockpile.Get(kv.Key)))));
            line.AddChild(Widgets.Spacer());
            if (u.Bought) line.AddChild(Widgets.Label("BUILT", "Small", Palette.Good));
            else
            {
                var buy = Widgets.TextButton("Build", () => Run(new BuyCardUpgrade(u.Id)), u.Available ? "Spend the resources" : "Build the one before it first", "Primary");
                buy.Disabled = !u.Available;
                line.AddChild(buy);
            }
            _page.AddChild(line);
        }
    }

    private void SetDeck(string id, int change)
    {
        var deck = _session.Sim.State.Cards.Deck.ToList();
        if (change > 0) deck.Add(id);
        else deck.Remove(id);
        Run(new SetDeck(deck));
    }

    private void Run(IColonyCommand command)
    {
        var result = _session.Execute(command);
        _status.Text = result.Message;
        _status.Modulate = result.Ok ? Palette.TextMuted : Palette.Danger;
        Refresh();
    }

    private static Control Header(params string[] titles)
    {
        var row = Widgets.Row(8);
        // Matches the row: name, kind, owned, the −/count/+ group (32 + 54 + 32 and two gaps), slots.
        var widths = new[] { 220, 170, 70, 134, 60 };
        foreach (var (t, i) in titles.Select((t, i) => (t, i)))
            row.AddChild(Fixed(Widgets.Label(t, "Small", Palette.TextDim), widths[i]));
        return row;
    }

    private static Control Fixed(Control c, int width)
    {
        c.CustomMinimumSize = new Vector2(width, 0);
        return c;
    }

    public static Color Color(CardCategory category) => category switch
    {
        CardCategory.Attack => Palette.Danger,
        CardCategory.Defense => Palette.Good,
        CardCategory.Movement => Palette.Amber,
        _ => Palette.Text,
    };
}
