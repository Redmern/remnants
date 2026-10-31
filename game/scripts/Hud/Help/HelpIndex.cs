using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Core.Help;

namespace Remnants.Game.Hud.Help;

/// <summary>
/// Every help topic, searchable (F1, or Help in the pause menu): a search box over a list grouped by category on the
/// left, the chosen topic (<see cref="TopicView"/>, live numbers included) on the right. Typing filters as you go
/// (<see cref="HelpCatalog.Search"/>: every word must match, title hits first); Up/Down move through the results,
/// Enter opens the first, links in a topic jump to it.
/// </summary>
public partial class HelpIndex : Overlay
{
    private readonly LineEdit _search = new()
    {
        PlaceholderText = "Search help…  (food, ambush, power, F1 closes)",
        ClearButtonEnabled = true,
        SizeFlagsHorizontal = SizeFlags.ExpandFill,
        CustomMinimumSize = new Vector2(0, 34),
    };
    private readonly ItemList _list = new()
    {
        CustomMinimumSize = new Vector2(250, 0),
        SizeFlagsVertical = SizeFlags.ExpandFill,
        SameColumnWidth = true,
        AllowReselect = true,
    };
    private readonly Label _count = Widgets.Label("", "Small", Palette.TextDim);
    private readonly ScrollContainer _scroll = new()
    {
        HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        SizeFlagsHorizontal = SizeFlags.ExpandFill,
        SizeFlagsVertical = SizeFlags.ExpandFill,
    };
    private readonly TopicView _view = new();
    private readonly List<string?> _rows = new(); // topic id per list row; null for a category header

    public HelpIndex() : base(900)
    {
        Visible = false;
        Body.AddChild(Title("HELP"));
        var bar = Widgets.Row(8);
        bar.AddChild(_search);
        bar.AddChild(_count);
        Body.AddChild(bar);

        var split = Widgets.Row(12);
        split.CustomMinimumSize = new Vector2(0, 460);
        split.AddChild(_list);
        var right = new PanelContainer { ThemeTypeVariation = "Inset", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        right.AddChild(_scroll);
        var pad = new MarginContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        foreach (var side in new[] { "left", "right", "top", "bottom" }) pad.AddThemeConstantOverride($"margin_{side}", 10);
        pad.AddChild(_view);
        _scroll.AddChild(pad);
        split.AddChild(right);
        Body.AddChild(split);

        var close = Widgets.IconButton("stat_check", "Done", "Close help", Close, Hotkeys.KeyName("pause_menu"), "Primary");
        close.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
        close.CustomMinimumSize = new Vector2(120, 34);
        Body.AddChild(close);

        _search.TextChanged += _ => Fill(keep: false);
        _search.TextSubmitted += _ => Pick(FirstTopicRow());
        _search.GuiInput += SearchKeys;
        _list.ItemSelected += i => Pick((int)i);
        _view.Navigate += Select;
    }

    /// <summary>Opens the index, on <paramref name="topicId"/> if given (else the first topic), with the search box focused.</summary>
    public void OpenAt(string? topicId)
    {
        _search.Text = "";
        Fill(keep: false);
        Open();
        if (topicId is not null) Select(topicId);
        _search.CallDeferred(Control.MethodName.GrabFocus);
    }

    private void Select(string id)
    {
        var row = _rows.IndexOf(id);
        if (row < 0 && _search.Text.Length > 0)
        {
            _search.Text = "";
            Fill(keep: false);
            row = _rows.IndexOf(id);
        }
        Pick(row);
    }

    private void Fill(bool keep)
    {
        var current = _view.Topic?.Id;
        _list.Clear();
        _rows.Clear();
        var catalog = HelpLibrary.Catalog;
        var hits = catalog.Search(_search.Text);
        var searching = _search.Text.Trim().Length > 0;
        _count.Text = searching ? $"{hits.Count} of {catalog.Topics.Count}" : $"{catalog.Topics.Count} topics";
        if (searching)
            foreach (var t in hits) Add(t);
        else
            foreach (var category in catalog.Categories)
            {
                var header = _list.AddItem(category.ToUpperInvariant(), null, false);
                _list.SetItemCustomFgColor(header, Palette.Rust);
                _list.SetItemDisabled(header, true);
                _rows.Add(null);
                foreach (var t in catalog.Topics.Where(t => t.Category == category)) Add(t);
            }
        var again = keep && current is not null ? _rows.IndexOf(current) : -1;
        Pick(again >= 0 ? again : FirstTopicRow());
    }

    private void Add(HelpTopic t)
    {
        var i = _list.AddItem("  " + t.Title);
        _list.SetItemTooltip(i, t.Short);
        _rows.Add(t.Id);
    }

    private int FirstTopicRow() => _rows.FindIndex(id => id is not null);

    private void Pick(int row)
    {
        if (row < 0 || row >= _rows.Count || _rows[row] is not { } id || HelpLibrary.Find(id) is not { } topic) return;
        _list.Select(row);
        _list.EnsureCurrentIsVisible();
        if (_view.Topic?.Id == id) return;
        _view.Show(topic, HelpLibrary.Context());
        _scroll.ScrollVertical = 0;
    }

    /// <summary>Up / Down in the search box move through the results without leaving it.</summary>
    private void SearchKeys(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true } key || key.Keycode is not (Key.Up or Key.Down)) return;
        var selected = _list.GetSelectedItems();
        var at = selected.Length > 0 ? selected[0] : -1;
        var step = key.Keycode == Key.Down ? 1 : -1;
        for (var i = at + step; i >= 0 && i < _rows.Count; i += step)
            if (_rows[i] is not null)
            {
                Pick(i);
                break;
            }
        _search.AcceptEvent();
    }
}
