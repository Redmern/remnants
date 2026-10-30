using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Core.Colony;
using Remnants.Core.Data;
using Remnants.Core.Items;
using Remnants.Core.Scavenge;
using Remnants.Core.Survivors;
using Remnants.Game.Hud;
using ScavengeText = Remnants.Game.Scavenge.ScavengeText;

namespace Remnants.Game.Colony.Ui;

/// <summary>
/// The Gear Bench (from the production panel of an Armory, Workshop or Tailor): the colony's moddable gear on the left
/// (weapons, worn gear, headgear, and survivors' fitted gun arms), grouped, each with its mods; the picked piece's slots
/// in the middle (every slot of its type, those past its Mk's count locked with the Mk that opens them), the mod in each
/// with Remove, the fitting mods from the stores per slot (hover previews, click fits), and the Mk kit upgrade; on the
/// right a before/after comparison of <see cref="GearBench.Stats"/> with good/bad deltas and, for worn gear, a body
/// silhouette of armor per region. Along the bottom the mod and kit orders of the bench buildings, and a picker to queue
/// one. Network-grade mods are cyan. Core decides (<see cref="GearBench"/>, InstallMod / RemoveMod / UpgradeGear).
/// </summary>
public partial class GearBenchPanel : Overlay
{
    /// <summary>A piece on the bench: a stack in the stores, or a gun arm fitted on a survivor.</summary>
    private sealed record Piece(string ItemId, int Count, int? SurvivorId = null, BodyPart? Part = null)
    {
        public string Key => SurvivorId is { } s ? $"arm:{s}:{Part}" : $"inv:{ItemId}";
    }

    /// <summary>What the comparison shows on top of the current build: a mod fitted, a slot cleared, or a kit fitted.</summary>
    private sealed record Change(string Label, GearBench.Outcome Outcome);

    private readonly ColonySession _session;
    private readonly VBoxContainer _list = Widgets.Column(4);
    private readonly VBoxContainer _slots = Widgets.Column(6);
    private readonly VBoxContainer _compare = Widgets.Column(6);
    private readonly HBoxContainer _orders = Widgets.Row(6);
    private readonly OptionButton _recipes = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill, FocusMode = FocusModeEnum.None, ClipText = true, FitToLongestItem = false };
    private readonly Label _recipeNote = Widgets.Label("", "Small", Palette.TextMuted);
    private readonly Button _queue;
    private readonly Label _benchNote = Widgets.Label("", "Small", Palette.TextMuted);
    private readonly List<(int Building, string Recipe)> _recipeIds = new();
    private string? _selected;
    private Change? _preview;
    private string _signature = "";
    private string _recipeSignature = "";

    public GearBenchPanel() : this(null!) { }

    public GearBenchPanel(ColonySession session) : base(1200)
    {
        _session = session;
        Visible = false;
        Body.AddThemeConstantOverride("separation", 8);
        Body.AddChild(Ui.TitleRow("Gear Bench", "bld_armory", Close, Hotkeys.KeyName("pause_menu")));
        var tag = Widgets.Row(8);
        tag.AddChild(Widgets.Label("STRIP IT. BOLT IT ON. MAKE IT YOURS.", "Small", Palette.TextMuted));
        tag.AddChild(Widgets.Spacer());
        tag.AddChild(_benchNote);
        Body.AddChild(tag);

        var main = Widgets.Row(10);
        main.AddChild(Column("Gear", "ui_items", _list, 270, scroll: true));
        main.AddChild(Column("Mod slots", "skill_crafting", _slots, 420, scroll: true));
        main.AddChild(Column("Compare", "stat_quality", _compare, 400, scroll: true));
        Body.AddChild(main);

        var strip = new PanelContainer { ThemeTypeVariation = "Card" };
        var stripCol = Widgets.Column(6);
        strip.AddChild(stripCol);
        stripCol.AddChild(Widgets.SectionHeader("Mods and kits in production", "skill_crafting"));
        var ordersScroll = new ScrollContainer { CustomMinimumSize = new Vector2(0, 44), VerticalScrollMode = ScrollContainer.ScrollMode.Disabled };
        ordersScroll.AddChild(_orders);
        stripCol.AddChild(ordersScroll);
        var queueRow = Widgets.Row(6);
        queueRow.AddChild(Widgets.Label("QUEUE", "Heading"));
        _recipes.CustomMinimumSize = new Vector2(360, 0);
        _recipes.SizeFlagsHorizontal = SizeFlags.Fill;
        queueRow.AddChild(_recipes);
        _queue = Widgets.IconButton("ui_plus", "Queue", "Add one to that building's production queue", Queue, variation: "Primary");
        queueRow.AddChild(_queue);
        _recipeNote.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _recipeNote.ClipText = true;
        queueRow.AddChild(_recipeNote);
        stripCol.AddChild(queueRow);
        Body.AddChild(strip);
        _recipes.ItemSelected += _ => RefreshRecipeNote();
    }

    private static PanelContainer Column(string title, string icon, Control content, float width, bool scroll)
    {
        var card = new PanelContainer { ThemeTypeVariation = "Inset", CustomMinimumSize = new Vector2(width, 0) };
        var col = Widgets.Column(6);
        card.AddChild(col);
        col.AddChild(Widgets.SectionHeader(title, icon));
        content.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        if (!scroll)
        {
            col.AddChild(content);
            return card;
        }
        var box = new ScrollContainer { CustomMinimumSize = new Vector2(width - 16, 400), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        box.AddChild(content);
        col.AddChild(box);
        return card;
    }

    /// <summary>Opens the bench, on a piece of gear (an inventory item id) when given.</summary>
    public void Open(string? itemId = null)
    {
        if (itemId is not null) _selected = $"inv:{itemId}";
        _preview = null;
        _signature = _recipeSignature = "";
        base.Open();
        Refresh();
    }

    /// <summary>Shows the comparison as if this mod were fitted to the picked piece (what hovering it does).</summary>
    public void PreviewMod(string modItemId)
    {
        if (Picked() is not { } piece || _session.Sim.Scavenge is not { } data) return;
        SetPreview(new Change($"+ {ScavengeText.Item(data, modItemId)}", GearBench.Install(data, piece.ItemId, modItemId)));
    }

    public void Refresh()
    {
        if (!Visible) return;
        var sim = _session.Sim;
        if (sim.Scavenge is not { GearMods: { } mods } data) return;
        var pieces = Pieces(sim, data, mods);
        if (_selected is null || pieces.All(p => p.Key != _selected))
        {
            // The same build moved (a mod went on): follow it by survivor and part, else pick the first.
            _selected = pieces.FirstOrDefault()?.Key;
            _preview = null;
        }
        var signature = $"{_selected}|{_preview?.Label}|{string.Join(",", pieces.Select(p => p.Key + p.Count))}|"
                        + string.Join(",", sim.State.Inventory.Counts.Select(kv => kv.Key + kv.Value)) + "|"
                        + string.Join(",", sim.State.Buildings.Select(b => $"{b.DefId}{b.Level}"));
        if (signature != _signature)
        {
            _signature = signature;
            RefreshList(data, pieces);
            RefreshSlots(sim, data, mods);
            RefreshCompare(data);
            RefreshBenchNote(sim, mods);
        }
        RefreshOrders(sim, data);
        RefreshRecipes(sim, data);
    }

    private Piece? Picked()
    {
        var sim = _session.Sim;
        if (sim.Scavenge is not { GearMods: { } mods } data) return null;
        return Pieces(sim, data, mods).FirstOrDefault(p => p.Key == _selected);
    }

    private static List<Piece> Pieces(ColonySim sim, ScavengeData data, GearModsData mods)
    {
        var list = sim.State.Inventory.Counts
            .Where(kv => kv.Value > 0 && data.Items.TryGetValue(kv.Key, out var item) && mods.Moddable(item.DefinedId) is not null)
            .Select(kv => new Piece(kv.Key, kv.Value))
            .OrderBy(p => Group(data.Items[p.ItemId])).ThenBy(p => data.Items[p.ItemId].DefinedId, StringComparer.Ordinal).ThenBy(p => p.ItemId, StringComparer.Ordinal)
            .ToList();
        foreach (var s in sim.State.Present.OrderBy(s => s.Id))
            foreach (var part in new[] { BodyPart.RightArm, BodyPart.LeftArm })
                if (s.Body[part] is { Condition: PartCondition.Prosthetic, ProstheticItemId: { } arm } && data.Items.TryGetValue(arm, out var def)
                    && mods.Moddable(def.DefinedId) is not null)
                    list.Add(new Piece(arm, 1, s.Id, part));
        return list;
    }

    private static int Group(ItemDef item) => item.Kind switch
    {
        ItemKind.Weapon => 0,
        ItemKind.Armor or ItemKind.Clothing => 1,
        ItemKind.Headgear => 2,
        _ => 3,
    };

    private static readonly string[] GroupNames = ["Weapons", "Armor and clothing", "Headgear", "Gun arms"];

    private void RefreshList(ScavengeData data, List<Piece> pieces)
    {
        Widgets.Clear(_list);
        if (pieces.Count == 0)
        {
            _list.AddChild(Widgets.Wrapped("No moddable gear in the stores. Weapons, armor, clothing and helmets show here.", "Muted"));
            return;
        }
        var group = -1;
        foreach (var piece in pieces)
        {
            var item = data.Items[piece.ItemId];
            var g = piece.SurvivorId is null ? Group(item) : 3;
            if (g != group)
            {
                group = g;
                _list.AddChild(Widgets.Label(GroupNames[g].ToUpperInvariant(), "Heading", Palette.TextMuted));
            }
            _list.AddChild(PieceButton(data, piece, item));
        }
    }

    private Control PieceButton(ScavengeData data, Piece piece, ItemDef item)
    {
        var picked = piece.Key == _selected;
        var card = new PanelContainer { ThemeTypeVariation = picked ? "CardSelected" : "Card", MouseFilter = MouseFilterEnum.Stop, MouseDefaultCursorShape = CursorShape.PointingHand };
        var row = Widgets.Row(6);
        row.MouseFilter = MouseFilterEnum.Ignore;
        card.AddChild(row);
        var icon = Icons.Rect(Icons.Item(item.DefinedId, item.Kind.ToString(), 20), 20, picked ? Palette.Amber : Palette.TextMuted);
        icon.MouseFilter = MouseFilterEnum.Ignore;
        row.AddChild(icon);
        var col = Widgets.Column(1);
        col.MouseFilter = MouseFilterEnum.Ignore;
        col.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var title = GearText.Title(data, item);
        if (piece.SurvivorId is { } sid && _session.Sim.State.FindSurvivor(sid) is { } who) title = $"{who.Name} · {ScavengeText.Part(piece.Part!.Value)}";
        var name = Widgets.Label(title, "", picked ? Palette.Text : Palette.Text.Darkened(0.08f), 14);
        name.ClipText = true;
        name.MouseFilter = MouseFilterEnum.Ignore;
        col.AddChild(name);
        if (piece.SurvivorId is not null) col.AddChild(Widgets.Label(item.Name.Split(" (")[0], "Small", Palette.TextMuted));
        if (item.Mods.Count > 0) col.AddChild(GearText.ModChips(data, item, 11));
        else
        {
            var none = Widgets.Label("no mods", "Small", Palette.TextDim);
            none.MouseFilter = MouseFilterEnum.Ignore;
            col.AddChild(none);
        }
        row.AddChild(col);
        if (piece.Count > 1) row.AddChild(Widgets.Label($"×{piece.Count}", "Number", Palette.TextMuted));
        var key = piece.Key;
        card.GuiInput += e =>
        {
            if (e is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) return;
            _selected = key;
            _preview = null;
            _signature = "";
            Refresh();
        };
        return card;
    }

    private void RefreshSlots(ColonySim sim, ScavengeData data, GearModsData mods)
    {
        Widgets.Clear(_slots);
        if (Picked() is not { } piece)
        {
            _slots.AddChild(Widgets.Wrapped("Pick a piece of gear.", "Muted"));
            return;
        }
        var item = data.Items[piece.ItemId];
        var type = mods.TypeOf(item.DefinedId)!;
        var open = mods.SlotCount(type, item.Mk);

        // Header: the model with its mods, the name, the Mk and slot use.
        var head = Widgets.Row(10);
        var render = new TextureRect
        {
            Texture = PortraitStudio.Object(GetTree(), $"gear|{piece.ItemId}", lib => GearModel(lib, piece.ItemId)),
            CustomMinimumSize = new Vector2(96, 96),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        };
        var frame = new PanelContainer { ThemeTypeVariation = "Card" };
        frame.AddChild(render);
        head.AddChild(frame);
        var names = Widgets.Column(2);
        names.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var title = Widgets.Label(GearText.Title(data, item), "Display");
        title.AddThemeFontSizeOverride("font_size", 20);
        title.ClipText = true;
        names.AddChild(title);
        var marks = Widgets.Row(6);
        marks.AddChild(Widgets.Label("MK", "Heading"));
        marks.AddChild(new Pips(item.Mk, Math.Max(item.Mk, mods.Moddable(item.DefinedId)?.Family is null ? 1 : mods.MaxMk), Palette.Amber));
        marks.AddChild(Widgets.Label($"{item.Mods.Count}/{open} slots used", "Small", Palette.TextMuted));
        names.AddChild(marks);
        if (piece.SurvivorId is { } sid && sim.State.FindSurvivor(sid) is { } who)
            names.AddChild(Widgets.Label($"Fitted on {who.Name}'s {ScavengeText.Part(piece.Part!.Value).ToLowerInvariant()}", "Small", Palette.TextMuted));
        else if (piece.Count > 1) names.AddChild(Widgets.Label($"{piece.Count} alike in the stores: one is worked on", "Small", Palette.TextMuted));
        head.AddChild(names);
        _slots.AddChild(head);
        _slots.AddChild(KitRow(sim, data, mods, piece, item));

        var fitting = GearBench.FittingMods(data, piece.ItemId, sim.State.Inventory.Counts.Where(kv => kv.Value > 0).Select(kv => kv.Key)).ToList();
        var installed = item.Mods.ToDictionary(m => mods.SlotOf(m)!, m => m);
        var free = open - item.Mods.Count;
        foreach (var slot in type.Slots)
        {
            installed.TryGetValue(slot, out var mod);
            var locked = mod is null && free <= 0;
            if (mod is null && !locked) free--;
            _slots.AddChild(SlotCard(sim, data, mods, piece, item, type, slot, mod, locked, fitting.Where(f => mods.SlotOf(f) == slot && f != mod).ToList()));
        }
    }

    private static Node3D GearModel(ModelLibrary lib, string itemId)
    {
        var model = lib.TryInstance(GearModels.ItemModel(itemId)) ?? new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.1f, 0.2f, 0.3f) },
            MaterialOverride = Placeholders.Material(new Color(0.4f, 0.38f, 0.35f)),
        };
        GearModels.FitMods(lib, model, itemId);
        return model;
    }

    /// <summary>The Mk that opens a slot for one more mod than now (null: never).</summary>
    private static int? MkFor(GearModsData mods, GearTypeDef type, int mk, int needed)
    {
        for (var m = mk + 1; m <= mods.MaxMk; m++)
            if (mods.SlotCount(type, m) >= needed) return m;
        return null;
    }

    private Control SlotCard(ColonySim sim, ScavengeData data, GearModsData mods, Piece piece, ItemDef item, GearTypeDef type, string slot,
        string? mod, bool locked, List<string> options)
    {
        var card = new PanelContainer { ThemeTypeVariation = mod is not null ? "CardSelected" : "Card" };
        var col = Widgets.Column(4);
        card.AddChild(col);
        var row = Widgets.Row(6);
        var slotName = Widgets.Label(mods.SlotName(slot).ToUpperInvariant(), "Heading", locked ? Palette.TextDim : null);
        slotName.CustomMinimumSize = new Vector2(120, 0);
        row.AddChild(slotName);
        if (mod is not null)
        {
            row.AddChild(GearText.ModChip(data, mod, 13));
            row.AddChild(Widgets.Spacer());
            var remove = Widgets.IconButton("ui_minus", "Remove", "Take it off: it goes back to the stores intact", () => Remove(piece, slot), variation: "Flat");
            remove.CustomMinimumSize = new Vector2(0, 26);
            var outcome = GearBench.Remove(data, piece.ItemId, slot);
            remove.MouseEntered += () => SetPreview(new Change($"− {ScavengeText.Item(data, mod)}", outcome));
            remove.MouseExited += () => SetPreview(null);
            row.AddChild(remove);
        }
        else if (locked)
        {
            row.AddChild(Icons.Rect(Icons.Get("stat_lock", 14), 14, Palette.TextDim));
            var next = MkFor(mods, type, item.Mk, item.Mods.Count + 1);
            var text = next is { } m && mods.Moddable(item.DefinedId)?.Family is not null ? $"locked · {mods.Mark(m)?.Name ?? $"Mk {m}"}" : "locked · slots full";
            var label = Widgets.Label(text, "Small", Palette.TextDim);
            label.TooltipText = $"{mods.SlotCount(type, item.Mk)} of {type.Slots.Count} slots may hold a mod at Mk {item.Mk}: take one off or fit a kit.";
            label.MouseFilter = MouseFilterEnum.Stop;
            row.AddChild(label);
        }
        else row.AddChild(Widgets.Label("empty", "Small", Palette.TextMuted));
        col.AddChild(row);
        if (locked) return card;

        if (options.Count == 0)
        {
            col.AddChild(Widgets.Label(mod is null ? "No fitting mod in the stores." : "", "Small", Palette.TextDim));
            return card;
        }
        var flow = new HFlowContainer();
        flow.AddThemeConstantOverride("h_separation", 4);
        flow.AddThemeConstantOverride("v_separation", 4);
        foreach (var option in options)
        {
            var outcome = GearBench.Install(data, piece.ItemId, option);
            var name = ScavengeText.Item(data, option);
            var count = sim.State.Inventory.Count(option);
            var button = Widgets.TextButton($"{(mod is null ? "+" : "⇄")} {name}{(count > 1 ? $" ×{count}" : "")}", () => Install(piece, option),
                outcome.Ok ? outcome.Message : outcome.Message, "Flat");
            button.CustomMinimumSize = new Vector2(0, 26);
            button.AddThemeFontSizeOverride("font_size", 13);
            var tint = GearText.ModColor(data, option, Palette.Amber);
            button.AddThemeColorOverride("font_color", tint);
            button.AddThemeColorOverride("font_hover_color", tint.Lightened(0.25f));
            button.Disabled = !outcome.Ok;
            button.MouseEntered += () => SetPreview(new Change($"+ {name}", outcome));
            button.MouseExited += () => SetPreview(null);
            flow.AddChild(button);
        }
        col.AddChild(flow);
        return card;
    }

    private Control KitRow(ColonySim sim, ScavengeData data, GearModsData mods, Piece piece, ItemDef item)
    {
        var card = new PanelContainer { ThemeTypeVariation = "Card" };
        var col = Widgets.Column(4);
        card.AddChild(col);
        col.AddChild(Widgets.SectionHeader("Upgrade", "ui_chevron_up"));
        var kit = mods.KitFor(item.DefinedId, item.Mk);
        if (piece.SurvivorId is not null || mods.Moddable(item.DefinedId)?.Family is null)
        {
            col.AddChild(Widgets.Label("Built in: no Mk upgrades.", "Small", Palette.TextMuted));
            return card;
        }
        if (kit is null)
        {
            col.AddChild(Widgets.Label($"{mods.Mark(item.Mk)?.Name ?? $"Mk {item.Mk}"}: the top mark.", "Small", Palette.Good));
            return card;
        }
        var next = mods.Mark(item.Mk + 1);
        var kitName = ScavengeText.Item(data, kit);
        var have = sim.State.Inventory.Count(kit);
        var at = mods.Def.Rules.UpgradeAt;
        var place = GearBenchHandlersView.Bench(sim, at);
        var row = Widgets.Row(6);
        var outcome = GearBench.Upgrade(data, piece.ItemId, kit);
        var button = Widgets.IconButton("ui_chevron_up", $"Fit {kitName}", outcome.Message, () => Upgrade(piece, kit), variation: "Primary");
        button.Disabled = have == 0 || place is null || piece.Count < 1;
        button.MouseEntered += () => SetPreview(new Change($"→ {next?.Name}", outcome));
        button.MouseExited += () => SetPreview(null);
        row.AddChild(button);
        var gains = next is null ? "" : item.Kind == ItemKind.Weapon
            ? $"damage ×{next.DamageMultiplier:0.##}, +{next.AccuracyBonus * 100:0}% accuracy"
            : $"armor +{next.ArmorBonus:0.#} ×{next.ArmorMultiplier:0.##}, coverage +{next.CoverageBonus * 100:0}%";
        var note = Widgets.Wrapped(
            have == 0 ? $"Needs a {kitName} (none in the stores)."
            : place is null ? $"Needs a {string.Join(" or ", at.Select(b => sim.Data.Buildings.TryGetValue(b, out var d) ? d.Name : b))}."
            : $"{have} in the stores. {next?.Name}: {gains}, +1 slot.", "Small", have == 0 || place is null ? Palette.Warn : Palette.TextMuted);
        row.AddChild(note);
        col.AddChild(row);
        return card;
    }

    private void SetPreview(Change? change)
    {
        if (_preview == change) return;
        _preview = change;
        if (_session.Sim.Scavenge is { } data) RefreshCompare(data);
    }

    private void RefreshCompare(ScavengeData data)
    {
        Widgets.Clear(_compare);
        if (Picked() is not { } piece) return;
        var before = GearBench.Stats(data, piece.ItemId);
        var afterId = _preview?.Outcome is { Ok: true, ItemId: { } id } ? id : null;
        var after = afterId is null ? before : GearBench.Stats(data, afterId);
        var item = data.Items[piece.ItemId];

        var head = Widgets.Row(6);
        head.AddChild(Widgets.Label("NOW", "Heading", Palette.TextMuted));
        head.AddChild(Widgets.Spacer());
        var with = _preview is null ? "hover a mod to preview" : _preview.Outcome.Ok ? _preview.Label : _preview.Outcome.Message;
        var withLabel = Widgets.Label(with, _preview is null ? "Small" : "Heading", _preview is null ? Palette.TextDim : _preview.Outcome.Ok ? Palette.Amber : Palette.Danger);
        withLabel.ClipText = true;
        withLabel.CustomMinimumSize = new Vector2(150, 0);
        withLabel.HorizontalAlignment = HorizontalAlignment.Right;
        head.AddChild(withLabel);
        _compare.AddChild(head);

        var grid = new GridContainer { Columns = 4 };
        grid.AddThemeConstantOverride("h_separation", 12);
        grid.AddThemeConstantOverride("v_separation", 3);
        var weapon = item.Actions.Count > 0;
        var worn = item.Kind is ItemKind.Armor or ItemKind.Clothing or ItemKind.Headgear;
        void Row(string label, string a, string b, double delta, bool higherIsBetter, string unit = "")
        {
            grid.AddChild(Widgets.Label(label.ToUpperInvariant(), "Small", Palette.TextMuted));
            grid.AddChild(Cell(a, Palette.Text));
            grid.AddChild(Cell(b, Math.Abs(delta) < 1e-6 ? Palette.Text : Palette.Amber.Lerp(Palette.Text, 0.3f)));
            var good = delta > 0 == higherIsBetter;
            grid.AddChild(Cell(Math.Abs(delta) < 1e-6 ? "—" : $"{(delta > 0 ? "+" : "−")}{Math.Abs(delta):0.##}{unit}",
                Math.Abs(delta) < 1e-6 ? Palette.TextDim : good ? Palette.Good : Palette.Danger));
        }
        if (weapon)
        {
            Row("Accuracy", $"{before.Accuracy * 100:0}%", $"{after.Accuracy * 100:0}%", Math.Round((after.Accuracy - before.Accuracy) * 100), true, "%");
            Row("Damage", $"{before.MinDamage:0}–{before.MaxDamage:0}", $"{after.MinDamage:0}–{after.MaxDamage:0}",
                (after.MinDamage + after.MaxDamage - before.MinDamage - before.MaxDamage) / 2, true);
            Row("Range", $"{before.Reach}", $"{after.Reach}", after.Reach - before.Reach, true);
            Row("AP", $"{before.ApCost}", $"{after.ApCost}", after.ApCost - before.ApCost, false);
            Row("Noise", $"{before.Noise}", $"{after.Noise}", after.Noise - before.Noise, false);
            Row("Reveal", $"{before.Reveal} rd", $"{after.Reveal} rd", after.Reveal - before.Reveal, false);
        }
        if (worn)
        {
            foreach (var region in Enum.GetValues<BodyRegion>())
            {
                var a = before.ArmorByRegion.GetValueOrDefault(region);
                var b = after.ArmorByRegion.GetValueOrDefault(region);
                if (a < 1e-6 && b < 1e-6) continue;
                Row($"Armor {region}", $"{a:0.#}", $"{b:0.#}", b - a, true);
            }
            if (before.MeleeArmor > 0 || after.MeleeArmor > 0)
                Row("vs melee", $"+{before.MeleeArmor:0.#}", $"+{after.MeleeArmor:0.#}", after.MeleeArmor - before.MeleeArmor, true);
            if (before.InjuryResist > 0 || after.InjuryResist > 0)
                Row("Injury resist", $"{before.InjuryResist * 100:0}%", $"{after.InjuryResist * 100:0}%", Math.Round((after.InjuryResist - before.InjuryResist) * 100), true, "%");
            Row("Move noise", $"{before.MoveNoise:+0;−0;0}", $"{after.MoveNoise:+0;−0;0}", after.MoveNoise - before.MoveNoise, false);
            Row("Stealth", $"{before.Stealth:+0;−0;0}", $"{after.Stealth:+0;−0;0}", after.Stealth - before.Stealth, true);
            Row("Carry", $"+{before.Carry:0}", $"+{after.Carry:0}", after.Carry - before.Carry, true, " kg");
        }
        Row("Weight", $"{before.Weight:0.##} kg", $"{after.Weight:0.##} kg", Math.Round(after.Weight - before.Weight, 2), false, " kg");
        Row("Slots", $"{before.SlotsUsed}/{before.SlotsOpen}", $"{after.SlotsUsed}/{after.SlotsOpen}", after.SlotsOpen - before.SlotsOpen, true);
        _compare.AddChild(grid);

        var tags = after.Tags.Select(t => (t, before.Tags.Contains(t) ? Palette.TextMuted : Palette.Good))
            .Concat(before.Tags.Where(t => !after.Tags.Contains(t)).Select(t => ("✗ " + t, Palette.Danger))).ToList();
        if (tags.Count > 0)
        {
            _compare.AddChild(Widgets.Label("EFFECTS", "Heading"));
            foreach (var (text, color) in tags)
                _compare.AddChild(Widgets.Wrapped(text, "Small", color));
        }

        if (worn)
        {
            var bodies = Widgets.Row(12);
            bodies.Alignment = BoxContainer.AlignmentMode.Center;
            bodies.AddChild(Silhouette("NOW", before, Coverage(data, piece.ItemId)));
            bodies.AddChild(Silhouette(afterId is null ? "—" : "WITH", after, Coverage(data, afterId ?? piece.ItemId)));
            _compare.AddChild(bodies);
        }
    }

    private static IReadOnlyDictionary<BodyRegion, double>? Coverage(ScavengeData data, string itemId) =>
        data.Items.TryGetValue(itemId, out var item) ? item.Coverage : null;

    private static Label Cell(string text, Color color)
    {
        var label = Widgets.Label(text, "Number", color, 15);
        label.HorizontalAlignment = HorizontalAlignment.Right;
        label.CustomMinimumSize = new Vector2(62, 0);
        return label;
    }

    private static Control Silhouette(string title, GearStats stats, IReadOnlyDictionary<BodyRegion, double>? coverage)
    {
        var col = Widgets.Column(2);
        var head = Widgets.Label(title, "Small", Palette.TextMuted);
        head.HorizontalAlignment = HorizontalAlignment.Center;
        col.AddChild(head);
        col.AddChild(new BodySilhouette(stats.ArmorByRegion, coverage) { TooltipText = string.Join("\n",
            stats.ArmorByRegion.Select(kv => $"{kv.Key}: {kv.Value:0.#} armor" + (coverage is null ? "" : $" ({coverage.GetValueOrDefault(kv.Key) * 100:0}% covered)"))) });
        return col;
    }

    private void RefreshBenchNote(ColonySim sim, GearModsData mods)
    {
        var bench = GearBenchHandlersView.Bench(sim, mods.Def.Rules.Benches);
        _benchNote.Text = bench is null
            ? $"No bench: build a {string.Join(", ", mods.Def.Rules.Benches.Select(b => sim.Data.Buildings.TryGetValue(b, out var d) ? d.Name : b))}"
            : $"Bench at the {sim.Data.Buildings[bench.DefId].Name} (level {bench.Level})";
        _benchNote.AddThemeColorOverride("font_color", bench is null ? Palette.Danger : Palette.TextMuted);
    }

    /// <summary>Recipes that make mods or Mk kits.</summary>
    private static bool IsGearRecipe(ScavengeData data, RecipeDef r) =>
        r.Item is { } item && (data.Items.GetValueOrDefault(item)?.Kind == ItemKind.Mod || data.GearMods?.Kit(item) is not null);

    private void RefreshOrders(ColonySim sim, ScavengeData data)
    {
        Widgets.Clear(_orders);
        var any = false;
        foreach (var b in sim.State.Buildings.OrderBy(b => b.Id))
            foreach (var order in b.Queue)
            {
                if (!sim.Data.Recipes.TryGetValue(order.RecipeId, out var recipe) || !IsGearRecipe(data, recipe)) continue;
                any = true;
                var chip = new PanelContainer { ThemeTypeVariation = order.Paid ? "CardSelected" : "Card", CustomMinimumSize = new Vector2(190, 0) };
                var col = Widgets.Column(2);
                chip.AddChild(col);
                var name = Widgets.Label($"{recipe.Name}{(order.Repeat ? " ↻" : order.Remaining > 1 ? $" ×{order.Remaining}" : "")}", "", GearText.ModColor(data, recipe.Item!, Palette.Text), 13);
                name.ClipText = true;
                col.AddChild(name);
                var bar = new ProgressBar { ShowPercentage = false, CustomMinimumSize = new Vector2(0, 5), MaxValue = 1, Value = order.Progress / Math.Max(1e-6, recipe.Work) };
                bar.AddThemeStyleboxOverride("fill", HudTheme.Box(Palette.Amber, radius: 2, padX: 0, padY: 0));
                col.AddChild(bar);
                col.AddChild(Widgets.Label($"{sim.Data.Buildings[b.DefId].Name} · {(order.Paid ? $"{order.Progress:0.#}/{recipe.Work:0}" : "waiting")}", "Small", Palette.TextMuted));
                var orderId = order.Id;
                var buildingId = b.Id;
                chip.TooltipText = "Right-click to cancel (a batch in progress gives its inputs back)";
                chip.MouseFilter = MouseFilterEnum.Stop;
                chip.GuiInput += e =>
                {
                    if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right }) _session.Execute(new CancelOrder(buildingId, orderId));
                };
                _orders.AddChild(chip);
            }
        if (!any) _orders.AddChild(Widgets.Label("Nothing queued. Mods and kits are made at the Workshop, Armory, Tailor and Electronics Lab.", "Muted"));
    }

    private void RefreshRecipes(ColonySim sim, ScavengeData data)
    {
        var signature = string.Join(",", sim.State.Buildings.Select(b => $"{b.Id}{b.Level}")) + string.Join(",", sim.State.KnownBlueprints);
        if (signature == _recipeSignature) return;
        _recipeSignature = signature;
        var keep = _recipes.Selected >= 0 && _recipes.Selected < _recipeIds.Count ? _recipeIds[_recipes.Selected] : ((int, string)?)null;
        _recipeIds.Clear();
        _recipes.Clear();
        var makers = sim.State.Buildings.GroupBy(b => b.DefId).Select(g => g.OrderByDescending(b => b.Level).First()).OrderBy(b => b.Id);
        foreach (var b in makers)
            foreach (var r in sim.Data.RecipesFor(b.DefId).Where(r => IsGearRecipe(data, r)))
            {
                var check = sim.CanQueue(b, r);
                var note = check.Error switch
                {
                    CommandError.RecipeLevelTooLow => $"  [L{r.MinLevel}]",
                    CommandError.RecipeLocked => "  [schematic]",
                    _ => "",
                };
                _recipeIds.Add((b.Id, r.Id));
                var def = data.Items[r.Item!];
                _recipes.AddIconItem(Icons.Item(def.DefinedId, def.Kind.ToString(), 16), $"{r.Name} · {sim.Data.Buildings[b.DefId].Name}{note}");
                _recipes.SetItemDisabled(_recipes.ItemCount - 1, !check.Ok);
            }
        var index = keep is { } k ? _recipeIds.IndexOf(k) : -1;
        if (index < 0) index = Enumerable.Range(0, _recipeIds.Count).FirstOrDefault(i => !_recipes.IsItemDisabled(i), -1);
        _recipes.Select(index);
        RefreshRecipeNote();
    }

    private void RefreshRecipeNote()
    {
        var sim = _session.Sim;
        if (_recipes.Selected < 0 || _recipes.Selected >= _recipeIds.Count || sim.State.FindBuilding(_recipeIds[_recipes.Selected].Building) is not { } b)
        {
            _recipeNote.Text = _recipeIds.Count == 0 ? "No building here makes mods or kits yet." : "";
            _queue.Disabled = true;
            return;
        }
        var r = sim.Data.Recipes[_recipeIds[_recipes.Selected].Recipe];
        var check = sim.CanQueue(b, r);
        var inputs = ColonyText.Cost(r.Inputs, sim.Data);
        if (r.ItemInputs.Count > 0)
            inputs += ", " + string.Join(", ", r.ItemInputs.Select(kv => $"{kv.Value} {ScavengeText.Item(sim.Scavenge!, kv.Key)}"));
        _recipeNote.Text = check.Ok ? $"Needs {inputs}, {r.Work:0} work" + (r.MinSkill > 0 ? $", skill {r.MinSkill:0}" : "") : check.Message;
        _recipeNote.AddThemeColorOverride("font_color", check.Ok ? Palette.TextMuted : Palette.Danger);
        _queue.Disabled = !check.Ok;
    }

    private void Queue()
    {
        if (_recipes.Selected < 0 || _recipes.Selected >= _recipeIds.Count) return;
        var (building, recipe) = _recipeIds[_recipes.Selected];
        _session.Execute(new QueueRecipe(building, recipe));
    }

    private void Install(Piece piece, string mod)
    {
        var result = _session.Execute(new InstallMod(piece.ItemId, mod, piece.SurvivorId, piece.Part));
        Follow(piece, result.Ok ? GearBench.Install(_session.Sim.Scavenge!, piece.ItemId, mod).ItemId : null);
    }

    private void Remove(Piece piece, string slot)
    {
        var result = _session.Execute(new RemoveMod(piece.ItemId, slot, piece.SurvivorId, piece.Part));
        Follow(piece, result.Ok ? GearBench.Remove(_session.Sim.Scavenge!, piece.ItemId, slot).ItemId : null);
    }

    private void Upgrade(Piece piece, string kit)
    {
        var result = _session.Execute(new UpgradeGear(piece.ItemId, kit));
        Follow(piece, result.Ok ? GearBench.Upgrade(_session.Sim.Scavenge!, piece.ItemId, kit).ItemId : null);
    }

    /// <summary>Keeps the reworked piece picked (its id changed with its build).</summary>
    private void Follow(Piece piece, string? newId)
    {
        if (newId is not null) _selected = piece.SurvivorId is { } s ? $"arm:{s}:{piece.Part}" : $"inv:{newId}";
        _preview = null;
        _signature = "";
        Refresh();
    }
}

/// <summary>The bench lookup the Core handlers use (the highest-level building of the types), for the UI.</summary>
internal static class GearBenchHandlersView
{
    public static Building? Bench(ColonySim sim, IEnumerable<string> types)
    {
        var set = types.ToHashSet();
        return sim.State.Buildings.Where(b => set.Contains(b.DefId)).OrderByDescending(b => b.Level).ThenBy(b => b.Id).FirstOrDefault();
    }
}

/// <summary>
/// A blocky body (head, torso, arms, legs) with each region shaded by the armor that reaches it (and its coverage
/// written on it): the worn-gear half of the Gear Bench comparison.
/// </summary>
public partial class BodySilhouette : Control
{
    private readonly IReadOnlyDictionary<BodyRegion, double> _armor;
    private readonly IReadOnlyDictionary<BodyRegion, double>? _coverage;

    public BodySilhouette(IReadOnlyDictionary<BodyRegion, double> armor, IReadOnlyDictionary<BodyRegion, double>? coverage)
    {
        _armor = armor;
        _coverage = coverage;
        CustomMinimumSize = new Vector2(104, 122);
        MouseFilter = MouseFilterEnum.Stop;
    }

    public override void _Draw()
    {
        var w = Size.X;
        var cx = w / 2;
        var font = GetThemeDefaultFont();
        void Block(Rect2 r, BodyRegion region, bool label)
        {
            var armor = _armor.GetValueOrDefault(region);
            var fill = armor <= 1e-6 ? Palette.Inset : Palette.Raised.Lerp(Palette.Amber, (float)Math.Clamp(armor / 6, 0.15, 1));
            DrawRect(r, fill);
            DrawRect(r, armor > 1e-6 ? Palette.Amber.Darkened(0.3f) : Palette.EdgeLight, false, 1);
            if (!label || font is null) return;
            var text = armor <= 1e-6 ? "—" : $"{armor:0.#}";
            var size = font.GetStringSize(text, HorizontalAlignment.Left, -1, 11);
            DrawString(font, new Vector2(r.GetCenter().X - size.X / 2, r.GetCenter().Y + 4), text, HorizontalAlignment.Left, -1, 11,
                armor <= 1e-6 ? Palette.TextDim : Palette.Void);
        }
        Block(new Rect2(cx - 12, 2, 24, 22), BodyRegion.Head, true);
        Block(new Rect2(cx - 20, 27, 40, 44), BodyRegion.Torso, true);
        Block(new Rect2(cx - 42, 27, 19, 40), BodyRegion.Arms, false);
        Block(new Rect2(cx + 23, 27, 19, 40), BodyRegion.Arms, true);
        Block(new Rect2(cx - 19, 74, 17, 44), BodyRegion.Legs, false);
        Block(new Rect2(cx + 2, 74, 17, 44), BodyRegion.Legs, true);
    }
}
