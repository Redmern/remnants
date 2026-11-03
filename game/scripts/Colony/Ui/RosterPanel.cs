using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Core.Colony;
using Remnants.Game.Hud;
using Remnants.Game.Hud.Help;

namespace Remnants.Game.Colony.Ui;

/// <summary>
/// The survivor roster down the left edge: a <see cref="SurvivorCard"/> per person (adults first, then minors), a
/// header with the head count, the bars' legend (H health, M mood, each with its help) and a collapse toggle (collapsed
/// shows portraits only). The panel is as tall as its cards, so the town shows below a small colony; past the room it
/// has, the cards scroll. Picking a card selects that survivor (<see cref="SurvivorPanel"/> shows the details);
/// right-click opens Assign to… (buildings with a free slot), Send to the Med Bay, Make idle and Open details.
/// </summary>
public partial class RosterPanel : PanelContainer
{
    private const float Wide = 272, Narrow = 74;

    private readonly ColonySession _session;
    private readonly VBoxContainer _cards = Widgets.Column(Space.Xs);
    private readonly ScrollContainer _scroll;
    private readonly Label _count = Widgets.Label("", "Heading");
    private readonly Label _title = Widgets.Label("SURVIVORS", "Heading");
    private readonly HBoxContainer _legend = Widgets.Row(2);
    private readonly Button _collapse;
    private readonly Dictionary<int, SurvivorCard> _byId = new();
    private PopupMenu? _menu;
    private PopupMenu? _assignMenu;
    private readonly List<int> _assignIds = new();
    private int _menuSurvivor;
    private bool _collapsed;

    public int? SelectedSurvivorId { get; private set; }

    public event Action<int?>? SelectionChanged;

    public RosterPanel(ColonySession session)
    {
        _session = session;
        CustomMinimumSize = new Vector2(Wide, 0);
        SizeFlagsVertical = SizeFlags.ShrinkBegin;
        var box = Ui.Panel(this, Space.Sm);
        var header = Widgets.Row(Space.Xs);
        header.AddChild(Icons.Rect(Icons.Get("stat_pop", 16), 16, Palette.Rust));
        header.AddChild(_title);
        header.AddChild(_count);
        var spacer = Widgets.Spacer();
        header.AddChild(spacer);
        // The bars' legend: H and M with the topic behind each.
        foreach (var (letter, topic, tip) in new[] { ("H", "health", "H: health"), ("M", "mood", "M: mood") })
        {
            var label = Widgets.Label(letter, "Small", Palette.TextDim);
            label.AddThemeFontOverride("font", Fonts.Display);
            label.TooltipText = tip;
            label.MouseFilter = MouseFilterEnum.Stop;
            _legend.AddChild(label);
            _legend.AddChild(new InfoTip(topic));
        }
        header.AddChild(_legend);
        _collapse = Widgets.IconButton("ui_chevron_left", "", "Collapse the roster", ToggleCollapsed, variation: "Flat");
        _collapse.FocusMode = FocusModeEnum.All;
        header.AddChild(_collapse);
        box.AddChild(header);

        _scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _cards.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _scroll.AddChild(_cards);
        box.AddChild(_scroll);
        Fit.OnParentResized(this, FitHeight);
    }

    private void ToggleCollapsed()
    {
        _collapsed = !_collapsed;
        CustomMinimumSize = new Vector2(_collapsed ? Narrow : Wide, 0);
        _title.Visible = _count.Visible = _legend.Visible = !_collapsed;
        _collapse.Icon = Icons.Get(_collapsed ? "ui_chevron_right" : "ui_chevron_left", 18);
        _collapse.TooltipText = _collapsed ? "Expand the roster" : "Collapse the roster";
        foreach (var card in _byId.Values) card.Compact = _collapsed;
        FitHeight();
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

        // Cards are kept by id: a newcomer's card enters, a departed one leaves, the rest stay put.
        var ids = survivors.Select(s => s.Id).ToList();
        if (!ids.SequenceEqual(_byId.Keys))
        {
            foreach (var gone in _byId.Keys.Except(ids).ToList())
            {
                Motion.Exit(_byId[gone]);
                _byId.Remove(gone);
            }
            var entering = 0;
            foreach (var s in survivors)
            {
                if (_byId.ContainsKey(s.Id)) continue;
                var card = new SurvivorCard(s.Id) { Compact = _collapsed };
                card.Picked += id => Select(SelectedSurvivorId == id ? null : id);
                card.MenuRequested += ShowMenu;
                _byId[s.Id] = card;
                _cards.AddChild(card);
                Motion.Enter(card, entering++);
            }
            // Re-insert by roster order, so a child growing up or a newborn lands in its place.
            for (var i = 0; i < survivors.Count; i++) _cards.MoveChild(_byId[survivors[i].Id], i);
            // Rebuilt keys in roster order (the dictionary keeps insertion order for the sequence check).
            var ordered = survivors.Select(s => (s.Id, Card: _byId[s.Id])).ToList();
            _byId.Clear();
            foreach (var (id, card) in ordered) _byId[id] = card;
        }
        foreach (var s in survivors)
        {
            var card = _byId[s.Id];
            card.Selected = s.Id == SelectedSurvivorId;
            card.Refresh(sim, s);
        }
        var away = survivors.Count(s => s.Away);
        var count = away > 0 ? $"{survivors.Count} · {away} AWAY" : $"{survivors.Count}";
        if (_count.Text != count) _count.Text = count;
        FitHeight();
    }

    /// <summary>As tall as the cards, up to the room the HUD gives; past that the cards scroll.</summary>
    private void FitHeight()
    {
        var want = _cards.GetCombinedMinimumSize().Y;
        var chrome = GetCombinedMinimumSize().Y - _scroll.CustomMinimumSize.Y;
        var room = GetParentAreaSize().Y - chrome;
        var height = Mathf.Round(room > 0 ? Math.Min(want, room) : want);
        if (Math.Abs(_scroll.CustomMinimumSize.Y - height) >= 1) _scroll.CustomMinimumSize = new Vector2(0, height);
    }

    private void ShowMenu(int survivorId, Control at)
    {
        var sim = _session.Sim;
        if (sim.State.FindSurvivor(survivorId) is not { } s) return;
        if (_menu is null)
        {
            _menu = new PopupMenu { Theme = HudTheme.Shared, Name = "SurvivorMenu" };
            _assignMenu = new PopupMenu { Theme = HudTheme.Shared, Name = "AssignTo" };
            _assignMenu.IdPressed += id =>
            {
                if (id >= 0 && id < _assignIds.Count) _session.Execute(new AssignJob(_menuSurvivor, _assignIds[(int)id], Move: true));
            };
            _menu.AddChild(_assignMenu);
            _menu.IdPressed += OnMenu;
            AddChild(_menu, false, InternalMode.Back);
        }
        _menuSurvivor = survivorId;
        _menu.Clear();
        _assignMenu!.Clear();
        _assignIds.Clear();
        _menu.AddIconItem(Icons.Get("stat_pop", 16), $"Open {s.Name}", 0);
        // Buildings with a free slot, the best fit first; Core still decides when one is picked.
        var free = sim.State.Buildings
            .Where(b => sim.Data.Buildings[b.DefId].Job is not null && !s.Jobs.Contains(b.Id)
                        && sim.State.WorkersAt(b.Id).Count() < sim.Data.Buildings[b.DefId].Level(b.Level).WorkerSlots)
            .Select(b => (B: b, Fit: BuildingPanel.JobFit(sim.Data.Jobs[sim.Data.Buildings[b.DefId].Job!], s).Score))
            .OrderByDescending(x => x.Fit).ThenBy(x => x.B.Id).ToList();
        foreach (var (b, fit) in free)
        {
            var def = sim.Data.Buildings[b.DefId];
            _assignMenu.AddIconItem(Icons.Building(def.Id, def.Category.ToString(), 16),
                $"{ColonyText.BuildingName(sim, b)}  ({sim.State.WorkersAt(b.Id).Count()}/{def.Level(b.Level).WorkerSlots}) · fit {ColonyText.Percent(fit)}", _assignIds.Count);
            _assignIds.Add(b.Id);
        }
        _menu.AddSubmenuNodeItem("Assign to…", _assignMenu, 1);
        var canWork = !s.IsInfant && !s.Away && s.PatientAtBuildingId is null;
        _menu.SetItemDisabled(_menu.GetItemIndex(1), !canWork || free.Count == 0);
        if (free.Count == 0) _menu.SetItemTooltip(_menu.GetItemIndex(1), "No building has a free slot.");
        _menu.AddIconItem(Icons.Get("job_idle", 16), "Make idle", 2);
        _menu.SetItemDisabled(_menu.GetItemIndex(2), s.AssignedBuildingId is null);
        if (sim.Data.Rules.Medical is { } med && sim.State.Buildings.Any(b => b.DefId == med.BuildingId))
        {
            _menu.AddIconItem(Icons.Get("job_patient", 16), "Send to the Med Bay", 3);
            _menu.SetItemDisabled(_menu.GetItemIndex(3), s.Away || s.PatientAtBuildingId is not null || !Medicine.NeedsCare(s, sim.Data));
            if (!Medicine.NeedsCare(s, sim.Data)) _menu.SetItemTooltip(_menu.GetItemIndex(3), "Nothing for the Med Bay to treat.");
        }
        var scale = GetViewport().GetFinalTransform().Scale;
        _menu.Position = (Vector2I)(at.GetScreenPosition() + new Vector2(at.Size.X - 24, 8) * scale);
        _menu.ResetSize();
        _menu.Popup();
    }

    private void OnMenu(long id)
    {
        if (_session.Sim.State.FindSurvivor(_menuSurvivor) is not { } s) return;
        switch (id)
        {
            case 0: Select(s.Id); break;
            case 2: _session.Execute(new Unassign(s.Id)); break;
            case 3: _session.Execute(new AdmitPatient(s.Id)); break;
        }
    }
}
