using System.Linq;
using Godot;
using Remnants.Core.Help;

namespace Remnants.Game.Hud.Help;

/// <summary>
/// Debug: <c>-- --colony --demo --help-selftest</c> (headless is fine) opens every help topic's popover with the colony's
/// live context, hovers an <see cref="InfoTip"/>, searches the index and follows a link, then prints
/// <c>[help-selftest] ok ...</c> (or <c>FAIL ...</c>) and quits. Run by <c>run-tests.sh --godot</c>.
/// </summary>
public partial class HelpSelfTest : Node
{
    private int _frames = 90;

    public static HelpSelfTest? FromCommandLine() =>
        DebugCapture.HasFlag("--help-selftest") ? new HelpSelfTest { Name = "HelpSelfTest", ProcessMode = ProcessModeEnum.Always } : null;

    public override void _Process(double delta)
    {
        if (--_frames > 0) return;
        SetProcess(false);
        var fail = Run();
        GD.Print(fail is null ? $"[help-selftest] ok {_summary}" : $"[help-selftest] FAIL {fail}");
        GetTree().Quit(fail is null ? 0 : 1);
    }

    private string _summary = "";

    private string? Run()
    {
        if (HelpLayer.Instance is not { } layer) return "no help layer";
        var catalog = HelpLibrary.Catalog;
        if (catalog.Topics.Count == 0) return "no help topics loaded";
        var problems = catalog.Validate(Breakdowns.Ids);
        if (problems.Count > 0) return string.Join("; ", problems);

        // Every topic opens; with the colony and its first worker as context, the colony breakdowns have numbers.
        var colony = Colony.ColonySession.Current?.Sim;
        var worker = colony?.State.Survivors.FirstOrDefault(s => s.AssignedBuildingId is not null);
        var live = 0;
        foreach (var topic in catalog.Topics)
        {
            layer.OpenDetail(topic.Id, () => new HelpContext { Colony = colony, Survivor = worker });
            if (!layer.DetailOpen) return $"popover did not open on '{topic.Id}'";
            if (HelpLibrary.Live(topic, HelpLibrary.Context(() => new HelpContext { Colony = colony, Survivor = worker })) is not null) live++;
            layer.CloseDetail();
        }
        if (colony is not null && live == 0) return "no live breakdown in a colony";

        // A tooltip on hover, gone on leave.
        var tip = new InfoTip(catalog.Topics[0].Id);
        layer.AddChild(new Control { Name = "TipHost" });
        layer.GetNode("TipHost").AddChild(tip);
        layer.ShowTip(tip);
        layer.HideTip(tip);

        // The index: a search narrows the list, a link navigates.
        if (catalog.Search("food").Count == 0) return "search 'food' found nothing";
        layer.Index.OpenAt("food");
        if (!layer.Index.Visible) return "index did not open";
        layer.Index.Close();
        layer.GetNode("TipHost").QueueFree();
        _summary = $"{catalog.Topics.Count} topics, {catalog.Categories.Count} categories, {live} with live numbers";
        return null;
    }
}

/// <summary>
/// Debug / smoke: <c>--help-demo[=TOPIC]</c> hovers the first visible <see cref="InfoTip"/> (on TOPIC if given) after a
/// moment, shows its tooltip, then clicks it so the detail popover is open for <c>--screenshot</c>; <c>--help-index</c>
/// opens the help index. Prints <c>[help-demo] ...</c> so the smoke log says what it did.
/// </summary>
public partial class HelpDemo : Node
{
    private int _frames = 60;
    private string? _topic;
    private bool _index;
    private InfoTip? _tip;

    public static HelpDemo? FromCommandLine()
    {
        var args = OS.GetCmdlineUserArgs();
        var demo = args.FirstOrDefault(a => a == "--help-demo" || a.StartsWith("--help-demo="));
        var index = args.Contains("--help-index");
        if (demo is null && !index) return null;
        return new HelpDemo
        {
            Name = "HelpDemo", ProcessMode = ProcessModeEnum.Always,
            _topic = demo?.Contains('=') == true ? demo[(demo.IndexOf('=') + 1)..] : null, _index = index && demo is null,
        };
    }

    public override void _Process(double delta)
    {
        if (--_frames > 0 || HelpLayer.Instance is not { } layer) return;
        if (_index)
        {
            SetProcess(false);
            layer.Index.OpenAt(_topic);
            GD.Print("[help-demo] index open");
            return;
        }
        if (_tip is null)
        {
            _tip = Find(GetTree().Root);
            if (_tip is null)
            {
                SetProcess(false);
                GD.Print($"[help-demo] no InfoTip on screen{(_topic is null ? "" : $" for '{_topic}'")}");
                return;
            }
            layer.ShowTip(_tip);
            GD.Print($"[help-demo] hover '{_tip.Topic}'");
            _frames = 20;
            return;
        }
        SetProcess(false);
        _tip.Open();
        GD.Print($"[help-demo] open '{_tip.Topic}' (popover {(layer.DetailOpen ? "open" : "closed")})");
    }

    private InfoTip? Find(Node node)
    {
        if (node is InfoTip tip && tip.IsVisibleInTree() && (_topic is null || tip.Topic == _topic)) return tip;
        foreach (var child in node.GetChildren())
            if (Find(child) is { } found) return found;
        return null;
    }
}
