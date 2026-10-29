using Godot;

namespace Remnants.Game;

public partial class MainMenu : Control
{
    private Label _status = null!;

    public override void _Ready()
    {
        _status = GetNode<Label>("Center/Menu/Status");
        GetNode<Button>("Center/Menu/ColonyButton").Pressed += OnColonyPressed;
        GetNode<Button>("Center/Menu/ScavengeButton").Pressed += OnScavengePressed;
    }

    private void OnColonyPressed() => GetTree().ChangeSceneToFile("res://scenes/ColonyPlaceholder.tscn");

    private void OnScavengePressed() => _status.Text = "Scavenge mode is not built yet.";
}
