using Godot;
using Remnants.Game.Hud;
using Remnants.Game.Multiplayer;

namespace Remnants.Game.Audio;

/// <summary>
/// UI sounds without touching the widgets: watches every node added to the tree and hooks buttons (click, or confirm
/// for <c>Primary</c> buttons; hover), overlays and side panels (open / close) and toasts. Also sets the music for
/// screens that have no audio script of their own (main menu, multiplayer lobby).
/// </summary>
public partial class UiAudio : Node
{
    private Node? _scene;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        GetTree().NodeAdded += OnNodeAdded;
    }

    public override void _ExitTree() => GetTree().NodeAdded -= OnNodeAdded;

    public override void _Process(double delta)
    {
        var scene = GetTree().CurrentScene;
        if (scene == _scene) return;
        _scene = scene;
        var music = AudioManager.Instance?.Music;
        switch (scene)
        {
            case MainMenu: music?.SetState("menu"); music?.SetLayer("linked_layer", false); break;
            case MultiplayerMenu: music?.SetState("coop_lobby"); music?.SetLayer("linked_layer", false); break;
        }
    }

    private static void OnNodeAdded(Node node)
    {
        switch (node)
        {
            case BaseButton button:
                button.Pressed += () => AudioManager.Fire(button.ThemeTypeVariation == "Primary" ? "ui.confirm" : "ui.click");
                button.MouseEntered += () =>
                {
                    if (!button.Disabled) AudioManager.Fire("ui.hover");
                };
                break;
            case Overlay overlay:
                Panel(overlay);
                break;
            case PanelContainer toast when toast.ThemeTypeVariation == "Toast":
                AudioManager.Fire("ui.toast");
                break;
            // Side panels the HUD shows and hides (build menu, survivor, world map): the HUD roots' direct panels.
            case Control { Visible: false } panel when panel.GetParent() is CanvasLayer:
                Panel(panel);
                break;
        }
    }

    private static void Panel(Control panel)
    {
        panel.VisibilityChanged += () =>
        {
            if (!panel.IsInsideTree()) return;
            AudioManager.Fire(panel.Visible ? "ui.panel_open" : "ui.panel_close");
        };
    }
}
