using System.Linq;
using System.Text;
using Godot;
using Remnants.Core.Colony;

namespace Remnants.Game;

/// <summary>Placeholder until M2: starts a new colony through Core and prints its state.</summary>
public partial class ColonyPlaceholder : Control
{
    public override void _Ready()
    {
        GetNode<Button>("Margin/Layout/BackButton").Pressed +=
            () => GetTree().ChangeSceneToFile("res://scenes/MainMenu.tscn");

        var summary = GetNode<Label>("Margin/Layout/Summary");
        try
        {
            summary.Text = Describe(NewGame.Create(GameDataPaths.Load(), seed: 1));
            GD.Print(summary.Text);
        }
        catch (System.Exception e)
        {
            summary.Text = $"Could not start colony: {e.Message}";
            GD.PushError(e.ToString());
        }
    }

    private static string Describe(ColonySim sim)
    {
        var text = new StringBuilder("NEW COLONY\n\nSurvivors:\n");
        foreach (var s in sim.State.Survivors)
            text.AppendLine($"  {s.Name} ({s.Sex}, {s.LifeStage}, age {s.AgeTicks / sim.Data.Rules.TicksPerYear})");
        text.AppendLine("\nBuildings:");
        foreach (var b in sim.State.Buildings)
            text.AppendLine($"  {sim.Data.Buildings[b.DefId].Name} L{b.Level}");
        text.AppendLine("\nStock:");
        foreach (var (res, amount) in sim.State.Stockpile.Amounts.OrderBy(kv => kv.Key))
            text.AppendLine($"  {sim.Data.Resources[res].Name}: {amount:0}");
        text.AppendLine($"\nBeds: {sim.BedCapacity}");
        return text.ToString();
    }
}
