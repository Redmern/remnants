using System;
using Godot;
using Remnants.Game.Hud;

namespace Remnants.Game;

/// <summary>
/// New Game: Story (a fixed start with Pell, the Radio Mast's voices and the missions) or Sandbox (a random colony,
/// radio contracts only, as before). Calls back with true for Story.
/// </summary>
public partial class NewGameChoice : Overlay
{
    public NewGameChoice() : this(_ => { }) { }

    public NewGameChoice(Action<bool> chosen) : base(620)
    {
        Body.AddChild(Title("NEW GAME"));
        var row = Widgets.Row(14);
        row.AddChild(Option("ui_radio", "Story", "Keep them alive. Keep them human.",
            "A radio, a shed, a stranger. Pell is one of your children, Iris is on the radio, and the missions open as your colony grows. Free play goes on after the end.",
            () => { Close(); chosen(true); }, primary: true));
        row.AddChild(Option("ui_new_game", "Sandbox", "Just the valley.",
            "A random colony with no named characters and no missions; the radio brings contracts and traders only.",
            () => { Close(); chosen(false); }, primary: false));
        Body.AddChild(row);
        Body.AddChild(Widgets.TextButton("Back", Close, "Back to the menu (Esc)", "Flat"));
        Closed += QueueFree;
        CallDeferred(Overlay.MethodName.Open);
    }

    private static Control Option(string icon, string title, string tagline, string text, Action pick, bool primary)
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
        var button = Widgets.TextButton($"Start {title}", pick, variation: primary ? "Primary" : "");
        button.CustomMinimumSize = new Vector2(0, 40);
        col.AddChild(button);
        return card;
    }
}
