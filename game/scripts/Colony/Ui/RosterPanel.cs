using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Game.Hud;

namespace Remnants.Game.Colony.Ui;

/// <summary>
/// The survivor roster down the left edge: a <see cref="SurvivorCard"/> per person (adults first, then minors), a
/// header with the head count and a collapse toggle (collapsed shows portraits only). Picking a card selects that
/// survivor; <see cref="SurvivorPanel"/> shows the details.
/// </summary>
public partial class RosterPanel : PanelContainer
{
    private const float Wide = 272, Narrow = 74;

    private readonly ColonySession _session;
    private readonly VBoxContainer _cards = Widgets.Column(4);
    private readonly Label _count = Widgets.Label("", "Heading");
    private readonly Label _title = Widgets.Label("SURVIVORS", "Heading");
    private readonly Button _collapse;
    private readonly Dictionary<int, SurvivorCard> _byId = new();
    private bool _collapsed;

    public int? SelectedSurvivorId { get; private set; }

    public event Action<int?>? SelectionChanged;

    public RosterPanel(ColonySession session)
    {
        _session = session;
        CustomMinimumSize = new Vector2(Wide, 0);
        SizeFlagsVertical = SizeFlags.ExpandFill;
        var box = Ui.Panel(this, 6);
        var header = Widgets.Row(6);
        header.AddChild(Icons.Rect(Icons.Get("stat_pop", 16), 16, Palette.Rust));
        _title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        header.AddChild(_title);
        header.AddChild(_count);
        _collapse = Widgets.IconButton("ui_chevron_left", "", "Collapse the roster", ToggleCollapsed, variation: "Flat");
        header.AddChild(_collapse);
        box.AddChild(header);

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        _cards.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(_cards);
        box.AddChild(scroll);
    }

    private void ToggleCollapsed()
    {
        _collapsed = !_collapsed;
        CustomMinimumSize = new Vector2(_collapsed ? Narrow : Wide, 0);
        _title.Visible = _count.Visible = !_collapsed;
        _collapse.Icon = Icons.Get(_collapsed ? "ui_chevron_right" : "ui_chevron_left", 18);
        _collapse.TooltipText = _collapsed ? "Expand the roster" : "Collapse the roster";
        foreach (var card in _byId.Values) card.Compact = _collapsed;
    }

    public void Select(int? survivorId)
    {
        SelectedSurvivorId = survivorId;
        SelectionChanged?.Invoke(survivorId);
        Refresh();
    }

    /// <summary>Selects the next survivor in roster order (Tab), wrapping around.</summary>
    public void SelectNext()
    {
        var ids = Ordered().Select(s => s.Id).ToList();
        if (ids.Count == 0) return;
        var at = SelectedSurvivorId is { } id ? ids.IndexOf(id) : -1;
        Select(ids[(at + 1) % ids.Count]);
    }

    private List<Remnants.Core.Survivors.Survivor> Ordered() =>
        _session.Sim.State.Survivors.OrderBy(s => s.IsChild).ThenBy(s => s.Id).ToList();

    public void Refresh()
    {
        var sim = _session.Sim;
        var survivors = Ordered();
        if (SelectedSurvivorId is { } sel && survivors.All(s => s.Id != sel))
        {
            SelectedSurvivorId = null;
            SelectionChanged?.Invoke(null);
        }

        var ids = survivors.Select(s => s.Id).ToList();
        if (!ids.SequenceEqual(_byId.Keys))
        {
            Widgets.Clear(_cards);
            _byId.Clear();
            foreach (var s in survivors)
            {
                var card = new SurvivorCard(s.Id) { Compact = _collapsed };
                card.Picked += id => Select(SelectedSurvivorId == id ? null : id);
                _byId[s.Id] = card;
                _cards.AddChild(card);
            }
        }
        foreach (var s in survivors)
        {
            var card = _byId[s.Id];
            card.Selected = s.Id == SelectedSurvivorId;
            card.Refresh(sim, s);
        }
        var away = survivors.Count(s => s.Away);
        _count.Text = away > 0 ? $"{survivors.Count} · {away} AWAY" : $"{survivors.Count}";
    }
}
