using System.Linq;
using System.Text;
using Godot;
using Remnants.Core.Colony;

namespace Remnants.Game.Colony.Ui;

/// <summary>Details of the selected building: workers, output, beds, and the next upgrade with its cost and Town Hall cap.</summary>
public partial class BuildingPanel : PanelContainer
{
    private readonly ColonySession _session;
    private readonly RichTextLabel _info = Ui.Rich();
    private readonly Button _upgrade;

    public int? BuildingId { get; private set; }

    public BuildingPanel(ColonySession session)
    {
        _session = session;
        CustomMinimumSize = new Vector2(300, 0);
        var box = Ui.Panel(this);
        box.AddChild(Ui.Heading("Building"));
        box.AddChild(_info);
        _upgrade = Ui.Button("Upgrade", Upgrade);
        box.AddChild(_upgrade);
        Visible = false;
    }

    public void Show(int? buildingId)
    {
        BuildingId = buildingId;
        Refresh();
    }

    public void Refresh()
    {
        var sim = _session.Sim;
        var building = BuildingId is { } id ? sim.State.FindBuilding(id) : null;
        Visible = building is not null;
        if (building is null) return;

        var def = sim.Data.Buildings[building.DefId];
        var level = def.Level(building.Level);
        var text = new StringBuilder();
        text.AppendLine($"[b]{def.Name}[/b]  level {building.Level}/{def.MaxLevel}   [color=#aaa]{def.Category}[/color]");

        if (def.Job is not null)
        {
            var workers = sim.State.WorkersAt(building.Id).Select(s => s.Name).ToList();
            text.AppendLine($"Workers {workers.Count}/{level.WorkerSlots}: {(workers.Count == 0 ? "none" : string.Join(", ", workers))}");
        }
        if (def.Produces is not null)
            text.AppendLine($"Makes {sim.Data.Resources[def.Produces].Name}: {def.BaseOutput * level.OutputMultiplier:0.##}/worker/h at full skill");
        if (level.Beds > 0) text.AppendLine($"Beds: {level.Beds}");

        var cap = sim.MaxUpgradeLevel(def);
        var next = building.Level + 1;
        if (next > def.MaxLevel)
        {
            text.Append("[color=#aaa]Max level reached.[/color]");
            _upgrade.Disabled = true;
        }
        else
        {
            var cost = def.Level(next).Cost;
            var affordable = sim.State.Stockpile.CanAfford(cost);
            var costColor = Ui.Hex(affordable ? Ui.Good : Ui.Bad);
            text.AppendLine($"Next: level {next} costs [color=#{costColor}]{ColonyText.Cost(cost, sim.Data)}[/color]");
            if (def.Id != sim.Data.Rules.TownHallBuildingId)
            {
                var capColor = Ui.Hex(next > cap ? Ui.Bad : Ui.Muted);
                text.Append($"[color=#{capColor}]Town Hall L{sim.TownHallLevel} allows up to level {cap}.[/color]");
            }
            _upgrade.Disabled = false; // Core decides; a refused upgrade explains itself in the log
        }
        _info.Text = text.ToString().TrimEnd();
    }

    private void Upgrade()
    {
        if (BuildingId is { } id) _session.Execute(new Upgrade(id));
    }
}
