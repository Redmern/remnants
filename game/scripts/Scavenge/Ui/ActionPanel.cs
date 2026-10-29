using System;
using System.Linq;
using Godot;
using Remnants.Core.Scavenge;
using UiKit = Remnants.Game.Colony.Ui.Ui;

namespace Remnants.Game.Scavenge.Ui;

/// <summary>
/// What the acting unit can do besides moving: pick the attack for map clicks, use a consumable (then click a
/// target), loot a cache in reach, recruit a stranger, extract on an exit. Every button sends a Core command
/// through the screen; Core decides whether it works.
/// </summary>
public partial class ActionPanel : PanelContainer
{
    private readonly ScavengePresenter _presenter;
    private readonly Label _hint = UiKit.Label("", 13, UiKit.Muted);
    private readonly VBoxContainer _content = new();

    /// <summary>The attack map clicks on enemies will use.</summary>
    public string? ActionId { get; private set; }

    public event Action<string>? ActionChosen;
    public event Action<string>? UseItemChosen;
    public event Action<IScavengeCommand>? CommandRequested;

    public ActionPanel(ScavengePresenter presenter)
    {
        _presenter = presenter;
        CustomMinimumSize = new Vector2(260, 0);
        var box = UiKit.Panel(this);
        box.AddChild(UiKit.Heading("Actions"));
        _hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        box.AddChild(_hint);
        _content.AddThemeConstantOverride("separation", 4);
        box.AddChild(_content);
    }

    public void SetAction(string? actionId)
    {
        ActionId = actionId;
        ActionChosen?.Invoke(actionId ?? "");
    }

    public void Refresh(bool busy, string? usingItem)
    {
        foreach (var child in _content.GetChildren()) child.QueueFree();
        var unit = _presenter.ActingUnit;
        if (unit is null || busy)
        {
            _hint.Text = _presenter.Done ? "The run is over." : "Waiting for the other units to act…";
            return;
        }

        var sim = _presenter.Sim;
        var data = sim.Data;
        var profile = sim.Profile(unit);
        if (ActionId is null || profile.Action(ActionId) is null) ActionId = profile.Actions.FirstOrDefault()?.Id;
        _hint.Text = usingItem is { } item
            ? $"Click who gets the {ScavengeText.Item(data, item)} (yourself or someone next to you). Right-click to cancel."
            : "Click a blue tile to move, a red target to attack. Right-click cancels.";

        _content.AddChild(UiKit.Label("Attack with", 13, UiKit.Muted));
        foreach (var a in profile.Actions)
        {
            var button = UiKit.Button($"{a.Name}  ·  {a.ApCost} AP  ·  {a.Pattern} {a.Reach}", () => SetAction(a.Id));
            button.ToggleMode = true;
            button.ButtonPressed = a.Id == ActionId;
            button.Disabled = a.ApCost > unit.Ap;
            _content.AddChild(button);
        }

        var usable = unit.Pack.Distinct().Where(id => data.Items[id].Use is not null).ToList();
        if (usable.Count > 0)
        {
            _content.AddChild(UiKit.Label("Use", 13, UiKit.Muted));
            foreach (var id in usable)
            {
                var use = data.Items[id].Use!;
                var count = unit.Pack.Count(p => p == id);
                var button = UiKit.Button($"{ScavengeText.Item(data, id)} x{count}  ·  {use.ApCost} AP", () => UseItemChosen?.Invoke(id));
                button.ToggleMode = true;
                button.ButtonPressed = id == usingItem;
                _content.AddChild(button);
            }
        }

        var caches = _presenter.CachesInReach(unit).ToList();
        if (caches.Count > 0)
        {
            _content.AddChild(UiKit.Label($"Loot  ·  {data.Rules.Turn.PickUpApCost} AP each", 13, UiKit.Muted));
            foreach (var cache in caches)
                foreach (var id in cache.Items.Distinct())
                {
                    var weight = data.Items[id].Weight;
                    var cacheId = cache.Id;
                    _content.AddChild(UiKit.Button($"Take {ScavengeText.Item(data, id)} ({weight:0.#} kg)",
                        () => CommandRequested?.Invoke(new PickUp(_presenter.PlayerId, unit.Id, cacheId, id))));
                }
        }

        foreach (var npc in _presenter.RecruitableNear(unit))
        {
            var npcId = npc.Id;
            _content.AddChild(UiKit.Button($"Recruit {npc.Name}  ·  {data.Rules.Turn.RecruitApCost} AP",
                () => CommandRequested?.Invoke(new Recruit(_presenter.PlayerId, unit.Id, npcId))));
        }

        if (_presenter.State.Map.IsExit(unit.Pos))
        {
            var extract = UiKit.Button("Extract here", () => CommandRequested?.Invoke(new Extract(_presenter.PlayerId, unit.Id)));
            extract.AddThemeColorOverride("font_color", UiKit.Good);
            _content.AddChild(extract);
        }
    }
}
