using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Core.Colony;
using Remnants.Core.Items;
using Remnants.Game.Hud;

namespace Remnants.Game.Colony.Ui;

/// <summary>
/// The colony's items by kind (with their quality), and on a second tab the Schematics archive
/// (<see cref="SchematicsArchive"/>). Toggled from the top bar (Items, <c>I</c>).
/// </summary>
public partial class InventoryPanel : PanelContainer
{
    private const float ItemsWidth = 340, ArchiveWidth = 580;

    private readonly ColonySession _session;
    private readonly VBoxContainer _items = Widgets.Column(4);
    private readonly ScrollContainer _itemsScroll;
    private string _itemsShown = "";
    private readonly Button _itemsTab;
    private readonly Button _archiveTab;

    public SchematicsArchive Archive { get; }

    public InventoryPanel(ColonySession session)
    {
        _session = session;
        CustomMinimumSize = new Vector2(ItemsWidth, 0);
        SizeFlagsVertical = SizeFlags.ShrinkBegin;
        Visible = false;
        var box = Ui.Panel(this);
        box.AddChild(Ui.TitleRow("Items", "ui_items", Toggle, Hotkeys.KeyName("open_items")));

        var tabs = Widgets.Row(3);
        var group = new ButtonGroup();
        _itemsTab = Widgets.IconButton("ui_items", "Items", "The colony's stores", () => ShowArchive(false), variation: "Tab");
        _archiveTab = Widgets.IconButton("item_schematic", "Schematics", "The Schematics archive: what the colony knows, can decrypt, has heard of",
            () => ShowArchive(true), variation: "Tab");
        foreach (var tab in new[] { _itemsTab, _archiveTab })
        {
            tab.ToggleMode = true;
            tab.ButtonGroup = group;
            tab.CustomMinimumSize = new Vector2(0, 28);
            tabs.AddChild(tab);
        }
        _itemsTab.ButtonPressed = true;
        box.AddChild(tabs);

        _itemsScroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(0, 320) };
        _items.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _itemsScroll.AddChild(_items);
        box.AddChild(_itemsScroll);
        Archive = new SchematicsArchive(session) { Visible = false };
        box.AddChild(Archive);
    }

    public void Toggle()
    {
        Motion.Toggle(this);
        Refresh();
    }

    /// <summary>Opens the panel (if closed) on the Items or the Schematics tab.</summary>
    public void Open(bool archive)
    {
        if (!Visible) Motion.Toggle(this);
        ShowArchive(archive);
    }

    private void ShowArchive(bool archive)
    {
        _itemsTab.SetPressedNoSignal(!archive);
        _archiveTab.SetPressedNoSignal(archive);
        _itemsScroll.Visible = !archive;
        Archive.Visible = archive;
        CustomMinimumSize = new Vector2(archive ? ArchiveWidth : ItemsWidth, 0);
        Size = Vector2.Zero;
        Refresh();
    }

    public void Refresh()
    {
        if (!Visible || _session.Sim.Scavenge is not { } data) return;
        if (Archive.Visible) Archive.Refresh();
        else RefreshItems(data, _session.Sim.State.Inventory);
    }

    /// <summary>Items grouped by kind: icon, name, quality (stars and colour) and count.</summary>
    private void RefreshItems(Remnants.Core.Scavenge.ScavengeData data, ItemInventory inventory)
    {
        var signature = string.Join(",", inventory.Counts.Select(kv => $"{kv.Key}:{kv.Value}"));
        if (signature == _itemsShown) return;
        _itemsShown = signature;
        Widgets.Clear(_items);
        var groups = inventory.Counts
            .Select(kv => (Def: data.Items[kv.Key], Count: kv.Value))
            .Where(e => e.Def.Kind != ItemKind.Schematic)
            .GroupBy(e => e.Def.Kind)
            .OrderBy(g => g.Key)
            .ToList();
        if (groups.Count == 0) _items.AddChild(Widgets.Label("No items yet. Craft them or bring them back from expeditions.", "Muted"));
        if (inventory.Counts.Keys.Any(id => data.Items.GetValueOrDefault(id)?.Kind == ItemKind.Schematic))
            _items.AddChild(Widgets.Label("Schematic copies and chips are on the Schematics tab.", "Small"));
        foreach (var group in groups)
        {
            _items.AddChild(Widgets.SectionHeader(group.Key.ToString(), $"item_{group.Key.ToString().ToLowerInvariant()}"));
            foreach (var (def, count) in group.OrderBy(e => e.Def.DefinedId).ThenBy(e => e.Def.Id))
            {
                var row = Widgets.Row(6);
                row.AddChild(Icons.Rect(Icons.Item(def.DefinedId, def.Kind.ToString(), 18), 18, Palette.TextMuted));
                var (stars, color, tier) = def.Quality switch
                {
                    "fine" => (3, Palette.Good, "Fine"),
                    "crude" => (1, Palette.TextMuted, "Crude"),
                    _ => (def.BaseId is null ? 0 : 2, Palette.Text, "Standard"),
                };
                var name = Widgets.Label(def.Name, "", color);
                name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                name.ClipText = true;
                row.AddChild(name);
                if (stars > 0)
                {
                    var quality = new Pips(stars, 3, color) { TooltipText = $"Quality: {tier}" };
                    row.AddChild(quality);
                }
                row.AddChild(Widgets.Label($"×{count}", "Number"));
                row.TooltipText = $"{def.Name} · {def.Kind}{(stars > 0 ? $" · {tier} quality" : "")}";
                _items.AddChild(row);
            }
        }
    }
}
