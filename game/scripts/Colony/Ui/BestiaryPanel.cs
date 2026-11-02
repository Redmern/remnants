using System.Linq;
using Godot;
using Remnants.Core.Scavenge;
using Remnants.Game.Hud;
using Remnants.Game.Hud.Help;

namespace Remnants.Game.Colony.Ui;

/// <summary>
/// The bestiary (Bestiary in the top bar): every enemy type as the colony knows it (<see cref="Bestiary.List"/>). A type
/// never seen shows as "???"; seen reveals its weapons and reach, fought its AP and movement, killed everything (health,
/// armor, dodge, attributes, skills, behaviour). The encounter counts show how far each type is from the next tier.
/// </summary>
public partial class BestiaryPanel : Overlay
{
    private readonly ColonySession _session;
    private readonly VBoxContainer _list = Widgets.Column(6);

    public BestiaryPanel() : this(null!) { }

    public BestiaryPanel(ColonySession session) : base(900)
    {
        _session = session;
        Visible = false;
        Body.AddChild(Ui.TitleRow("Bestiary", "ui_bestiary", Close, Hotkeys.KeyName("pause_menu")));
        var tag = Widgets.Row(8);
        tag.AddChild(Widgets.Label("KNOW WHAT IS OUT THERE BEFORE IT KNOWS YOU.", "Small", Palette.TextMuted));
        tag.AddChild(Widgets.Spacer());
        tag.AddChild(new InfoTip("bestiary"));
        Body.AddChild(tag);
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(0, 540), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _list.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(_list);
        Body.AddChild(scroll);
    }

    protected override void OnOpened() => Refresh();

    public void Refresh()
    {
        if (!Visible || _session.Sim.Scavenge is not { } data) return;
        Widgets.Clear(_list);
        var known = _session.Sim.State.Bestiary;
        var rules = data.Rules.Bestiary;
        foreach (var card in Bestiary.List(data, known))
        {
            var entry = known.GetValueOrDefault(card.Id);
            var box = new PanelContainer { ThemeTypeVariation = "Card" };
            var column = Widgets.Column(2);
            box.AddChild(column);
            var head = Widgets.Row(8);
            head.AddChild(Widgets.Label(card.Name, "Heading", card.Tier == BestiaryTier.Unknown ? Palette.TextDim : Palette.Text));
            head.AddChild(Widgets.Spacer());
            head.AddChild(Widgets.Label(card.Tier switch
            {
                BestiaryTier.Killed => "KILLED · ALL KNOWN",
                BestiaryTier.Fought => "FOUGHT",
                BestiaryTier.Seen => "SEEN",
                _ => "UNKNOWN",
            }, "Small", card.Tier switch { BestiaryTier.Killed => Palette.Good, BestiaryTier.Fought => Palette.Amber, BestiaryTier.Seen => Palette.Text, _ => Palette.TextDim }));
            head.AddChild(Widgets.Label($"seen {entry?.Seen ?? 0}/{rules.Seen} · fought {entry?.Fought ?? 0}/{rules.Fought} · killed {entry?.Killed ?? 0}/{rules.Killed}",
                "Small", Palette.TextMuted));
            column.AddChild(head);
            if (card.Weapons is { } weapons) column.AddChild(Line($"Weapons {string.Join(", ", weapons)} · reach {card.Reach}"));
            if (card.Ap is { } ap) column.AddChild(Line($"{ap} AP · moves {Remnants.Game.Scavenge.Ui.UnitPanel.MoveWords(card.Movement!)}"));
            if (card.Hp is { } hp)
            {
                column.AddChild(Line($"Health {hp:0} · armor {card.Armor:0.#} · dodge {card.Dodge * 100:0}%"));
                column.AddChild(Line(string.Join("  ", card.Attributes!.Select(kv => $"{kv.Key} {kv.Value:0}"))
                                     + (card.Skills!.Count > 0 ? "  ·  " + string.Join("  ", card.Skills.Select(kv => $"{kv.Key} {kv.Value:0}")) : "")));
                column.AddChild(Widgets.Wrapped(card.Behaviour ?? "", "Small", Palette.TextMuted));
            }
            if (card.Tier < BestiaryTier.Killed)
                column.AddChild(Widgets.Label(card.Tier switch
                {
                    BestiaryTier.Unknown => "Never seen: meet one to learn its weapons.",
                    BestiaryTier.Seen => "Fight one to learn how it moves and acts.",
                    _ => "Kill one to learn everything.",
                }, "Small", Palette.TextDim));
            _list.AddChild(box);
        }
    }

    private static Label Line(string text) => Widgets.Label(text, "Small", Palette.Text);
}
