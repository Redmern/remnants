using Godot;

namespace Remnants.Game.Hud;

/// <summary>
/// The colour tokens of the HUD. Survivor-made: dark worn metal and concrete, rust and amber for the player,
/// moss for good, red for danger. Cold cyan is reserved for the AI Network (the Linked) and nothing else.
/// </summary>
public static class Palette
{
    // Surfaces, darkest first.
    public static readonly Color Void = new("0b0a09");
    public static readonly Color Inset = new("11100e");
    public static readonly Color Panel = new("1b1916");
    public static readonly Color Raised = new("26231f");
    public static readonly Color RaisedHover = new("312c26");
    public static readonly Color Edge = new("3b342c");
    public static readonly Color EdgeLight = new("5c4f40");
    public static readonly Color EdgeDark = new("070606");

    // Text.
    public static readonly Color Text = new("e9e2d4");
    public static readonly Color TextMuted = new("a69d8d");
    public static readonly Color TextDim = new("70695f");

    // The player: rust for structure, amber for focus and selection.
    public static readonly Color Rust = new("c0622c");
    public static readonly Color RustDark = new("6e3518");
    public static readonly Color Amber = new("eaa53e");

    // Meaning.
    public static readonly Color Good = new("8cb55a");
    public static readonly Color Danger = new("e0533f");
    public static readonly Color Warn = new("e8b44a");

    /// <summary>
    /// Allies (co-op): another player's party on the local team. A soft green, cooler than <see cref="Good"/> (your
    /// recruits) and well clear of the Network's cyan and the hostile players' violet.
    /// </summary>
    public static readonly Color Ally = new("74c98a");

    /// <summary>The AI Network. Only the Linked, pylons and Network effects use it.</summary>
    public static readonly Color Network = new("3fd8e6");

    /// <summary>BBCode colour value ("#rrggbb") for a token.</summary>
    public static string Hex(Color c) => "#" + c.ToHtml(false);

    /// <summary>Wraps text in a BBCode colour tag.</summary>
    public static string Paint(string text, Color c) => $"[color={Hex(c)}]{text}[/color]";

    /// <summary>Green → amber → red for a 0..1 fraction where high is good (health, mood).</summary>
    public static Color Gauge(double fraction) =>
        fraction >= 0.66 ? Good : fraction >= 0.33 ? Warn : Danger;
}
