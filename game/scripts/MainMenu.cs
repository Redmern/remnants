using System.Linq;
using Godot;
using Remnants.Game.Multiplayer;
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
        GetNode<Button>("Center/Menu/MultiplayerButton").Pressed += OnMultiplayerPressed;

        // `-- --colony` skips the menu (handy with DebugCapture's --screenshot).
        if (DebugCapture.HasFlag("--colony")) CallDeferred(MethodName.OnColonyPressed);
        // `-- --scavenge` starts the debug mission (see ScavengeSession.DebugParty).
        else if (DebugCapture.HasFlag("--scavenge")) CallDeferred(MethodName.OnScavengePressed);
        // `-- --multiplayer` (or any --mp-host / --mp-join) opens the multiplayer menu (see MultiplayerMenu).
        else if (DebugCapture.HasFlag("--multiplayer") || OS.GetCmdlineUserArgs().Any(a => a.StartsWith("--mp-")))
            CallDeferred(MethodName.OnMultiplayerPressed);
    }

    private void OnColonyPressed() => GetTree().ChangeSceneToFile("res://scenes/Colony.tscn");

    private void OnScavengePressed() => ScavengeSession.BeginDebug(GetTree());

    private void OnMultiplayerPressed() => GetTree().ChangeSceneToFile(MultiplayerSession.MenuScene);
}
