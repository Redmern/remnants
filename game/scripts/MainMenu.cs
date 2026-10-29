using Godot;
using Remnants.Game.Scavenge;

namespace Remnants.Game;

public partial class MainMenu : Control
{
    private Label _status = null!;

    public override void _Ready()
    {
        Theme = HudTheme.Shared;
        _status = GetNode<Label>("Center/Menu/Status");
        GetNode<Button>("Center/Menu/ColonyButton").Pressed += OnColonyPressed;
        GetNode<Button>("Center/Menu/ScavengeButton").Pressed += OnScavengePressed;

        // `-- --colony` skips the menu (handy with DebugCapture's --screenshot).
        if (DebugCapture.HasFlag("--colony")) CallDeferred(MethodName.OnColonyPressed);
        // `-- --scavenge` starts the debug mission (see ScavengeSession.DebugParty).
        else if (DebugCapture.HasFlag("--scavenge")) CallDeferred(MethodName.OnScavengePressed);
    }

    private void OnColonyPressed() => GetTree().ChangeSceneToFile("res://scenes/Colony.tscn");

    private void OnScavengePressed() => ScavengeSession.BeginDebug(GetTree());
}
