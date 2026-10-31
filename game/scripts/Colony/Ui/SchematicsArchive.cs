using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Core.Colony;
using Remnants.Core.Schematics;
using Remnants.Game.Hud;

namespace Remnants.Game.Colony.Ui;

/// <summary>
/// The Schematics archive (the Items panel's second tab): every schematic in the catalogue, grouped by what it unlocks
/// (Buildings, Weapons &amp; armor, Devices, Prosthetics, Techniques, Vehicles), with its status. <b>Known</b> rows list
/// what they unlock and offer a Copy (Research Data); <b>To decrypt</b> rows queue the Decrypt order at the Electronics
/// Lab (or say why not) and can be salvaged into Research Data; <b>Heard of</b> rows say where it was seen and what
/// researching it costs; <b>Unknown</b> rows are muted with where they are found; <b>Planned</b> ones are "coming".
/// Tooltips list every source. Encrypted schematics (Network chips) carry the Network's cyan. Core decides every action.
/// </summary>
public partial class SchematicsArchive : VBoxContainer
{
    /// <summary>Display status: Core's, plus a Decrypt order already queued or running.</summary>
    private enum Shown { ToDecrypt, Decrypting, HeardOf, Known, Unknown, Planned }

    private static readonly (string Label, Shown[]? Filter)[] Filters =
    [
        ("All", null), ("Known", [Shown.Known]), ("To decrypt", [Shown.ToDecrypt, Shown.Decrypting]), ("Heard of", [Shown.HeardOf]),
        ("Unknown", [Shown.Unknown, Shown.Planned]),
    ];

    private readonly ColonySession _session;
    private readonly HBoxContainer _summary = Widgets.Row(8);
    private readonly VBoxContainer _list = Widgets.Column(4);
    private readonly Dictionary<string, Button> _filterButtons = new();
    private readonly LineEdit _search = new()
    {
        PlaceholderText = "Search schematics (name, what it unlocks)",
        ClearButtonEnabled = true,
        SizeFlagsHorizontal = SizeFlags.ExpandFill,
        FocusMode = FocusModeEnum.All,
    };
    private readonly Label _nothing = Widgets.Label("Nothing matches.", "Muted");
    private readonly Label _empty = Widgets.Label("No schematics in this build.", "Muted");
    private readonly Dictionary<string, (VBoxContainer Section, Label Count, VBoxContainer Rows)> _sections = new();
    private readonly Dictionary<string, (Control Row, string Signature)> _rows = new();
    private readonly Unchanged _ui = new();
    private Shown[]? _filter;
    private string _signature = "";
    private string _summaryShown = "";

    /// <summary>"Research" pressed on a heard-of row: the screen selects the Town Hall.</summary>
    public event Action? ResearchRequested;

    public SchematicsArchive(ColonySession session)
    {
        _session = session;
        AddThemeConstantOverride("separation", 6);
        var top = Widgets.Row(8);
        _summary.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        top.AddChild(_summary);
        top.AddChild(new Hud.Help.InfoTip("schematics"));
        AddChild(top);

        var filters = Widgets.Row(3);
        var group = new ButtonGroup();
        foreach (var (label, filter) in Filters)
        {
            var button = Motion.OnPress(Widgets.TextButton(label, () => SetFilter(filter), $"Show {label.ToLowerInvariant()} schematics", "Tab"));
            button.ToggleMode = true;
            button.ButtonGroup = group;
            button.ButtonPressed = filter is null;
            button.FocusMode = FocusModeEnum.All;
            button.CustomMinimumSize = new Vector2(0, 26);
            button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            _filterButtons[label] = button;
            filters.AddChild(button);
        }
        AddChild(filters);
        _search.TextChanged += _ => Refresh();
        AddChild(_search);

        var scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(0, 430), SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        _list.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _list.AddChild(_empty);
        _list.AddChild(_nothing);
        foreach (var category in SchematicText.Categories)
        {
            var section = Widgets.Column(4);
            var count = Widgets.Label("", "Number", Palette.TextMuted, 13);
            count.TooltipText = "Known / in the game";
            count.MouseFilter = MouseFilterEnum.Stop;
            section.AddChild(Widgets.SectionHeader(category, SchematicText.CategoryIcon(category), count));
            var rows = Widgets.Column(4);
            section.AddChild(rows);
            _list.AddChild(section);
            _sections[category] = (section, count, rows);
        }
        scroll.AddChild(_list);
        AddChild(scroll);
    }

    private void SetFilter(Shown[]? filter)
    {
        _filter = filter;
        Refresh();
    }

    /// <summary>Debug / deep link: show only one status ("known", "decrypt", "heard", "unknown").</summary>
    public void ShowFilter(string label)
    {
        var match = Filters.FirstOrDefault(f => f.Label.Replace(" ", "").StartsWith(label, StringComparison.OrdinalIgnoreCase));
        if (match.Label is null) return;
        _filterButtons[match.Label].ButtonPressed = true;
        SetFilter(match.Filter);
    }

    /// <summary>Deep link from a "see the archive" button: every status, searched to one schematic by name.</summary>
    public void ShowSchematic(string schematicId)
    {
        if (_session.Sim.Data.Schematics.Find(schematicId) is not { } def) return;
        _filterButtons[Filters[0].Label].ButtonPressed = true;
        _filter = null;
        _search.Text = def.Name;
        Refresh();
    }

    private bool Matches(ColonySim sim, SchematicDef def, string category)
    {
        var query = _search.Text.Trim();
        if (query.Length == 0) return true;
        return def.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || category.Contains(query, StringComparison.OrdinalIgnoreCase)
            || SchematicText.Unlocks(sim, def).Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    public void Refresh()
    {
        if (!IsVisibleInTree()) return;
        var sim = _session.Sim;
        var catalog = sim.Data.Schematics;
        var labs = string.Join(",", sim.State.Buildings.Where(b => b.DefId == catalog.Rules.Decrypt.Building).Select(b => b.Level));
        var data = Math.Floor(SchematicText.ResearchData(sim));
        var rows = catalog.All.Values.Select(def => (Def: def, Status: StatusOf(sim, def), Category: SchematicText.Category(sim, def))).ToList();
        string RowSignature(SchematicDef def, Shown status) =>
            $"{status}:{sim.State.Inventory.Count(def.Id)}:{DecryptProgress(sim, def)}:{data}:{labs}:{sim.State.Schematics.Research?.SchematicId == def.Id}";
        var signature = string.Join(";", rows.Select(r => r.Def.Id + RowSignature(r.Def, r.Status))) + $"|{_filter?.FirstOrDefault()}|{_search.Text}";
        if (signature == _signature) return;
        _signature = signature;

        RefreshSummary(sim, rows.Select(r => r.Status).ToList());
        RefreshFilterCounts(rows.Select(r => r.Status).ToList());
        _ui.Visible(_empty, rows.Count == 0);
        var shownAny = false;
        foreach (var category in SchematicText.Categories)
        {
            var (section, count, box) = _sections[category];
            var inCategory = rows.Where(r => r.Category == category)
                .OrderBy(r => r.Status).ThenBy(r => r.Def.Tier).ThenBy(r => r.Def.Name, StringComparer.Ordinal).ToList();
            _ui.Text(count, $"{inCategory.Count(r => r.Status == Shown.Known)}/{inCategory.Count(r => r.Status != Shown.Planned)}");
            var any = false;
            for (var i = 0; i < inCategory.Count; i++)
            {
                var (def, status, _) = inCategory[i];
                var rowSignature = RowSignature(def, status);
                if (!_rows.TryGetValue(def.Id, out var kept) || kept.Signature != rowSignature)
                {
                    // Only the row whose state changed is built again; the rest of the 140-odd stay as they are.
                    var row = Row(sim, def, status);
                    if (kept.Row is not null)
                    {
                        box.AddChild(row);
                        box.MoveChild(row, kept.Row.GetIndex());
                        box.RemoveChild(kept.Row);
                        kept.Row.QueueFree();
                    }
                    else box.AddChild(row);
                    _rows[def.Id] = kept = (row, rowSignature);
                }
                if (kept.Row.GetIndex() != i) box.MoveChild(kept.Row, i);
                var visible = (_filter is null || _filter.Contains(status)) && Matches(sim, def, category);
                _ui.Visible(kept.Row, visible);
                any |= visible;
            }
            _ui.Visible(section, any);
            shownAny |= any;
        }
        _ui.Visible(_nothing, rows.Count > 0 && !shownAny);
        _ui.Text(_nothing, _search.Text.Trim().Length > 0 ? $"Nothing matches \"{_search.Text.Trim()}\"." : "Nothing here yet.");
    }

    /// <summary>"Known 9", "To decrypt 3": each filter tab says how many it holds.</summary>
    private void RefreshFilterCounts(List<Shown> statuses)
    {
        foreach (var (label, filter) in Filters)
        {
            var n = filter is null ? statuses.Count(s => s != Shown.Planned) : statuses.Count(filter.Contains);
            var text = $"{label} {n}";
            if (_filterButtons[label].Text != text) _filterButtons[label].Text = text;
        }
    }

    private void RefreshSummary(ColonySim sim, List<Shown> statuses)
    {
        var known = statuses.Count(s => s == Shown.Known);
        var real = statuses.Count(s => s != Shown.Planned);
        var toDecrypt = statuses.Count(s => s is Shown.ToDecrypt or Shown.Decrypting);
        var heard = statuses.Count(s => s == Shown.HeardOf);
        var running = sim.State.Schematics.Research is { } order ? sim.Data.Schematics.Find(order.SchematicId)?.Name : null;
        var signature = $"{Math.Floor(SchematicText.ResearchData(sim))}|{known}|{real}|{toDecrypt}|{heard}|{running}";
        if (signature == _summaryShown) return;
        _summaryShown = signature;
        Widgets.Clear(_summary);
        var dataTip = "Research Data: pays for research at the Town Hall and for copies. Duplicates and salvaged chips turn into it; the trader buys and sells it.";
        _summary.AddChild(SchematicText.DataChip(sim, tooltip: dataTip));
        _summary.AddChild(Widgets.Label("Research Data", "Small", Palette.TextMuted));
        _summary.AddChild(Widgets.Label("·", "Small", Palette.TextDim));
        _summary.AddChild(Widgets.Label($"{known}/{real} known", "Number", Palette.Text, 15));
        void Add(int n, string label, Color color)
        {
            if (n > 0) _summary.AddChild(Widgets.Label($"· {n} {label}", "Small", color));
        }
        Add(toDecrypt, "to decrypt", Palette.Network);
        Add(heard, "heard of", Palette.Amber);
        if (running is not null) Add(1, $"researching ({running})", Palette.Amber);
    }

    private static Shown StatusOf(ColonySim sim, SchematicDef def) => Schematics.Status(sim, def.Id) switch
    {
        SchematicStatus.Known => Shown.Known,
        SchematicStatus.Planned => Shown.Planned,
        SchematicStatus.ToDecrypt => Shown.ToDecrypt,
        _ when SchematicText.Decrypting(sim, def.Id) is { Started: true } => Shown.Decrypting,
        SchematicStatus.HeardOf => Shown.HeardOf,
        _ => Shown.Unknown,
    };

    private static int DecryptProgress(ColonySim sim, SchematicDef def) =>
        SchematicText.Decrypting(sim, def.Id) is { } d ? (int)(d.Done * 10) + (d.Started ? 100 : 0) : -1;

    private Control Row(ColonySim sim, SchematicDef def, Shown status)
    {
        var card = new PanelContainer { ThemeTypeVariation = status is Shown.Unknown or Shown.Planned ? "Inset" : "Card" };
        card.TooltipText = SchematicText.Tooltip(sim, def);
        card.MouseFilter = MouseFilterEnum.Pass;
        var row = Widgets.Row(8);
        row.MouseFilter = MouseFilterEnum.Ignore;
        card.AddChild(row);

        var muted = status is Shown.Unknown or Shown.Planned;
        var accent = muted ? Palette.TextDim
            : status == Shown.Known ? Palette.Good
            : def.Encrypted ? Palette.Network
            : Palette.Amber;
        row.AddChild(Icons.Rect(Icons.Get(muted ? "stat_lock" : SchematicText.Icon(def), 20), 20, accent));

        var text = Widgets.Column(0);
        text.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        text.MouseFilter = MouseFilterEnum.Ignore;
        var top = Widgets.Row(6);
        top.MouseFilter = MouseFilterEnum.Ignore;
        var name = Widgets.Label(def.Name, "", muted ? Palette.TextDim : Palette.Text, 14);
        name.AddThemeFontOverride("font", Fonts.BodyBold);
        name.ClipText = true;
        name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        name.MouseFilter = MouseFilterEnum.Ignore;
        top.AddChild(name);
        var tier = Widgets.Pips(def.Tier, 4, muted ? Palette.TextDim : SchematicText.Accent(def));
        tier.TooltipText = $"Tier {def.Tier}: {SchematicText.Region(def.Tier)}";
        top.AddChild(tier);
        text.AddChild(top);
        var detail = Widgets.Wrapped(Detail(sim, def, status), "Small", muted ? Palette.TextDim : Palette.TextMuted);
        detail.MouseFilter = MouseFilterEnum.Ignore;
        text.AddChild(detail);
        row.AddChild(text);

        var actions = Widgets.Row(4);
        actions.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        foreach (var action in Actions(sim, def, status)) actions.AddChild(action);
        if (actions.GetChildCount() == 0) actions.AddChild(StatusChip(status, def));
        row.AddChild(actions);
        return card;
    }

    private static Control StatusChip(Shown status, SchematicDef def)
    {
        var (text, color) = status switch
        {
            Shown.Known => ("KNOWN", Palette.Good),
            Shown.Decrypting => ("DECRYPTING", Palette.Network),
            Shown.HeardOf => ("HEARD OF", Palette.Amber),
            Shown.Planned => ("COMING", Palette.TextDim),
            Shown.ToDecrypt => ("TO DECRYPT", Palette.Network),
            _ => ("UNKNOWN", Palette.TextDim),
        };
        var chip = Widgets.Chip(null, text, color);
        chip.GetChild(0).GetChild<Label>(0).AddThemeFontSizeOverride("font_size", 12);
        return chip;
    }

    private static string Detail(ColonySim sim, SchematicDef def, Shown status)
    {
        var rules = sim.Data.Schematics.Rules;
        switch (status)
        {
            case Shown.Known:
            {
                var from = sim.State.Schematics.LearnedFrom.TryGetValue(def.Id, out var source) ? SchematicText.LearnedFrom(source) : "Known";
                return from + SchematicText.Arrow(sim, def);
            }
            case Shown.ToDecrypt:
            {
                var count = sim.State.Inventory.Count(def.Id);
                var lab = sim.Data.Buildings.GetValueOrDefault(rules.Decrypt.Building)?.Name ?? rules.Decrypt.Building;
                var queued = SchematicText.Decrypting(sim, def.Id) is { } d ? $" · queued at the {sim.Data.Buildings[d.Building.DefId].Name}" : "";
                return $"Encrypted chip{(count > 1 ? $" ×{count}" : "")}: decrypt at the {lab} L{Schematics.DecryptLevel(sim, def)}{SchematicText.Arrow(sim, def)}{queued}";
            }
            case Shown.Decrypting:
                return SchematicText.Decrypting(sim, def.Id) is { } running
                    ? $"Decrypting at the {sim.Data.Buildings[running.Building.DefId].Name}: {Fmt.Percent(running.Done)}{SchematicText.Arrow(sim, def)}"
                    : "Decrypting.";
            case Shown.HeardOf:
            {
                var where = sim.State.Schematics.HeardOf.GetValueOrDefault(def.Id, "somewhere");
                var cost = SchematicCatalog.At(rules.Research.Cost, def.Tier);
                var research = def.Has(SchematicSource.Research) && cost > 0
                    ? $"Research: {cost:0} Research Data at the Town Hall L{SchematicCatalog.At(rules.Research.TownHallLevel, def.Tier)}"
                    : "Cannot be researched: find it";
                return $"Seen {where} · {research}{SchematicText.Arrow(sim, def)}";
            }
            case Shown.Planned:
                return $"Coming: {SchematicText.Unlocks(sim, def)} is not in the game yet.";
            default:
                return $"Found in the {SchematicText.Region(def.Tier)}" + (def.Has(SchematicSource.Trade) ? ", or from the trader" : "")
                    + (def.StoryMissions.Count > 0 ? $" · also a reward for {string.Join(", ", def.StoryMissions.Select(m => SchematicText.Mission(sim, m)))}" : "");
        }
    }

    private IEnumerable<Control> Actions(ColonySim sim, SchematicDef def, Shown status)
    {
        var rd = SchematicText.ResearchData(sim);
        switch (status)
        {
            case Shown.Known:
            {
                var cost = Schematics.CopyCost(sim, def);
                var copy = Widgets.IconButton("res_research_data", $"Copy ({cost:0} Research Data)", $"Make a copy of this schematic to trade or give away, for {cost:0} Research Data (have {Fmt.Amount(rd)}).",
                    () => _session.Execute(new CopySchematic(def.Id)), variation: "Flat");
                copy.FocusMode = FocusModeEnum.All;
                copy.Disabled = rd + 1e-9 < cost;
                copy.CustomMinimumSize = new Vector2(0, 28);
                yield return StatusChip(status, def);
                yield return copy;
                break;
            }
            case Shown.ToDecrypt:
            {
                var queued = SchematicText.Decrypting(sim, def.Id) is not null;
                var lab = SchematicText.DecryptAt(sim, def, out var why);
                var recipeId = Schematics.DecryptRecipeId(def.Id);
                var check = lab is not null && sim.Data.Recipes.TryGetValue(recipeId, out var recipe) ? sim.CanQueue(lab, recipe) : null;
                var decrypt = Widgets.IconButton("item_chip", queued ? "Queued" : "Decrypt",
                    lab is null ? why : check is { Ok: false } ? check.Message
                    : $"Queue a Decrypt order at the {sim.Data.Buildings[lab.DefId].Name}: no inputs, {sim.Data.Recipes[recipeId].Work:0} work, a worker with {sim.Data.Jobs[sim.Data.Buildings[lab.DefId].Job!].Skill} {sim.Data.Recipes[recipeId].MinSkill:0}.",
                    () => { if (lab is not null) _session.Execute(new QueueRecipe(lab.Id, recipeId)); }, variation: "Primary");
                decrypt.FocusMode = FocusModeEnum.All;
                decrypt.Disabled = queued || lab is null || check is { Ok: false };
                decrypt.CustomMinimumSize = new Vector2(0, 28);
                var value = sim.Data.Schematics.DuplicateValue(def);
                var salvage = Widgets.IconButton("res_research_data", $"Salvage +{value:0}", $"Salvage the chip into {value:0} Research Data (the schematic stays unknown).",
                    () => _session.Execute(new SalvageSchematic(def.Id)), variation: "Flat");
                salvage.FocusMode = FocusModeEnum.All;
                salvage.CustomMinimumSize = new Vector2(0, 28);
                yield return decrypt;
                yield return salvage;
                break;
            }
            case Shown.HeardOf when def.Has(SchematicSource.Research) && SchematicCatalog.At(sim.Data.Schematics.Rules.Research.Cost, def.Tier) > 0:
            {
                var busy = sim.State.Schematics.Research?.SchematicId == def.Id;
                var research = Widgets.IconButton("ui_research", busy ? "Researching" : "Research",
                    busy ? "Being researched at the Town Hall." : "Research it at the Town Hall: opens the Town Hall's research.",
                    () => ResearchRequested?.Invoke(), variation: "Flat");
                research.FocusMode = FocusModeEnum.All;
                research.CustomMinimumSize = new Vector2(0, 28);
                yield return StatusChip(status, def);
                yield return research;
                break;
            }
        }
    }
}
