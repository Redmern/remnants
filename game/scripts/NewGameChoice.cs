using System;
using Godot;
using Remnants.Core.Colony;
using Remnants.Game.Hud;

namespace Remnants.Game;

/// <summary>
/// New Game: Story (a fixed start with Pell, the Radio Mast's voices and the missions) or Sandbox (a random colony,
/// radio contracts only, as before). Calls back with true for Story and the new-game options (Sandbox: the starting
/// habitants picked on its card, remembered in <see cref="GameSettings.SandboxHabitants"/>).
/// </summary>
public partial class NewGameChoice : Overlay
{
    public NewGameChoice() : this((_, _) => { }) { }

    public NewGameChoice(Action<bool, NewGameOptions> chosen) : base(620)
    {
        Body.AddChild(Title("NEW GAME"));
        var row = Widgets.Row(14);
        row.AddChild(Option("ui_radio", "Story", "Keep them alive. Keep them human.",
            "A radio, a shed, a stranger. Pell is one of your children, Iris is on the radio, and the missions open as your colony grows. Free play goes on after the end.",
            () => { Close(); chosen(true, new NewGameOptions()); }, primary: true));
        var habitants = HabitantsPicker();
        row.AddChild(Option("ui_new_game", "Sandbox", "Just the valley.",
            "A random colony with no named characters and no missions; the radio brings contracts and traders only.",
            () =>
            {
                Close();
                GameSettings.Save();
                chosen(false, new NewGameOptions { StartingHabitants = GameSettings.SandboxHabitants > 0 ? GameSettings.SandboxHabitants : null });
            }, primary: false, extra: habitants));
        Body.AddChild(row);
        Body.AddChild(Widgets.TextButton("Back", Close, "Back to the menu (Esc)", "Flat"));
        Closed += QueueFree;
        CallDeferred(Overlay.MethodName.Open);
    }

    /// <summary>Sandbox: how many people the colony starts with (rules.newGame: the default and the range).</summary>
    private static Control HabitantsPicker()
    {
        var rules = GameDataPaths.Load().Rules.NewGame;
        var (fewest, most) = (rules.HabitantsRange[0], rules.HabitantsRange[1]);
        var picker = new OptionButton { FocusMode = FocusModeEnum.All, TooltipText = $"How many survivors the colony starts with ({rules.Adults} adults and {rules.Children} children by default; a bigger or smaller start keeps about that share of children). Fewer hands, more work for each." };
        picker.AddItem($"Default ({rules.Habitants})", 0);
        for (var n = fewest; n <= most; n++) picker.AddItem(n.ToString(), n);
        // A remembered pick outside today's range (older data, a hand edit) falls back to the default it shows.
        if (GameSettings.SandboxHabitants < fewest || GameSettings.SandboxHabitants > most) GameSettings.SandboxHabitants = 0;
        var chosen = GameSettings.SandboxHabitants;
        picker.Select(chosen == 0 ? 0 : picker.GetItemIndex(chosen));
        picker.ItemSelected += i => GameSettings.SandboxHabitants = picker.GetItemId((int)i);
        var line = Widgets.Row(8);
        line.AddChild(Widgets.Label("Starting survivors", "Small"));
        line.AddChild(picker);
        return line;
    }

    private static Control Option(string icon, string title, string tagline, string text, Action pick, bool primary, Control? extra = null)
    {
        var card = new PanelContainer { ThemeTypeVariation = primary ? "CardSelected" : "Card", CustomMinimumSize = new Vector2(280, 0) };
        var col = Widgets.Column(8);
        card.AddChild(col);
        var head = Widgets.Row(8);
        head.AddChild(Icons.Rect(Icons.Get(icon, 24), 24, primary ? Palette.Amber : Palette.Text));
        head.AddChild(Widgets.Label(title, "Display", size: 26));
        col.AddChild(head);
        col.AddChild(Widgets.Label(tagline, "Muted"));
        col.AddChild(Widgets.Wrapped(text, "Small"));
        col.AddChild(Widgets.Spacer());
        if (extra is not null) col.AddChild(extra);
        var button = Widgets.TextButton($"Start {title}", pick, variation: primary ? "Primary" : "");
        button.CustomMinimumSize = new Vector2(0, 40);
        col.AddChild(button);
        return card;
    }
}
