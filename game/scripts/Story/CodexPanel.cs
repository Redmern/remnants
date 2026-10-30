using System;
using System.Linq;
using Godot;
using Remnants.Core.Story;
using Remnants.Game.Hud;

namespace Remnants.Game.Story;

/// <summary>
/// The Codex: character pages, logs found in the field, and the story so far, each page unlocked by the campaign
/// (<see cref="StoryWorld.Codex"/>) and grown by what has happened since. Tabs: People, Logs, Story.
/// </summary>
public partial class CodexPanel : Overlay
{
    private readonly StoryHud _hud;
    private readonly VBoxContainer _list = Widgets.Column(10);
    private readonly HBoxContainer _tabs = Widgets.Row(6);
    private string _tab = "people";

    public CodexPanel() : this(null!) { }

    public CodexPanel(StoryHud hud) : base(640)
    {
        _hud = hud;
        var head = Widgets.Row(8);
        head.AddChild(Icons.Rect(Icons.Get("ui_journal", 22), 22, Palette.Amber));
        head.AddChild(Widgets.Label("Codex", "Title"));
        head.AddChild(Widgets.Spacer());
        head.AddChild(Widgets.IconButton("ui_close", "", "Close (Esc)", Close, variation: "Flat"));
        Body.AddChild(head);
        Body.AddChild(_tabs);
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(0, 480), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _list.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(_list);
        Body.AddChild(scroll);
    }

    protected override void OnOpened() => Refresh();

    public void Show(string tab)
    {
        _tab = tab;
        Open();
    }

    public void Refresh()
    {
        var pages = StoryWorld.Codex(_hud.Sim);
        Widgets.Clear(_tabs);
        foreach (var (id, name) in new[] { ("people", "People"), ("logs", "Logs"), ("story", "The story so far") })
        {
            var count = pages.Count(p => p.Def.Tab == id);
            var tab = id;
            _tabs.AddChild(Widgets.TextButton($"{name} · {count}", () => { _tab = tab; Refresh(); }, variation: id == _tab ? "Primary" : ""));
        }
        Widgets.Clear(_list);
        var shown = pages.Where(p => p.Def.Tab == _tab).ToList();
        if (shown.Count == 0) _list.AddChild(Widgets.Wrapped("Nothing here yet.", "Small", Palette.TextMuted));
        foreach (var (def, text) in shown)
        {
            var card = Widgets.Column(4);
            card.AddChild(Widgets.Label(def.Title, "Heading", Palette.Amber));
            foreach (var paragraph in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                card.AddChild(Widgets.Wrapped(paragraph, _tab == "logs" ? "Muted" : "Small"));
            _list.AddChild(card);
        }
    }
}

/// <summary>
/// The epilogue after an ending: one still at a time over the colony, dimmed and letterboxed, with the story
/// character the still is about (when there is one) and the line the campaign earned (<see cref="StoryWorld.Epilogue"/>).
/// The last still offers free play, or, after the Calm, only the way out.
/// </summary>
public partial class EpiloguePanel : Control
{
    private readonly StoryHud _hud;
    private readonly TextureRect _portrait = new()
    {
        CustomMinimumSize = new Vector2(180, 180), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
        StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
    };
    private readonly Label _title = Widgets.Label("", "Title", Palette.Amber);
    private readonly Label _caption = Widgets.Label("", "Small", Palette.TextMuted);
    private readonly Label _text = Widgets.Wrapped("", "");
    private readonly HBoxContainer _buttons = Widgets.Row(10);
    private int _index;

    /// <summary>The player chose to play on (or, after the Calm, to leave).</summary>
    public event Action? Finished;

    /// <summary>Who each still is about, for the portrait beside the text.</summary>
    private static string? Subject(string still) => still switch
    {
        "iris_mast" => "iris", "caravan_road" => "marrow", "doc_hospital" or "doc_chair" => "hale", "tally_camp" => "cole",
        "seven" => "seven", "pell" => "pell", "channel_nine" or "spire_night" => "halcyon", _ => null,
    };

    public EpiloguePanel() : this(null!) { }

    public EpiloguePanel(StoryHud hud)
    {
        _hud = hud;
        Theme = HudTheme.Shared;
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        ProcessMode = ProcessModeEnum.Always;
        var dim = new ColorRect { Color = new Color(0.01f, 0.01f, 0.015f, 0.55f), MouseFilter = MouseFilterEnum.Ignore };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(dim);
        foreach (var top in new[] { true, false })
        {
            var bar = new ColorRect { Color = Colors.Black, MouseFilter = MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(0, 110) };
            bar.SetAnchorsPreset(top ? LayoutPreset.TopWide : LayoutPreset.BottomWide);
            if (!top) bar.OffsetTop = -110;
            else bar.OffsetBottom = 110;
            AddChild(bar);
        }
        var box = new PanelContainer();
        box.AddThemeStyleboxOverride("panel", PanelArt.Box(new(Palette.Panel, Palette.EdgeDark, 0.05f, Strip: Palette.Amber), 24, 18));
        box.SetAnchorsPreset(LayoutPreset.CenterBottom);
        box.GrowHorizontal = GrowDirection.Both;
        box.GrowVertical = GrowDirection.Begin;
        box.OffsetBottom = -130;
        box.CustomMinimumSize = new Vector2(980, 0);
        AddChild(box);
        var row = Widgets.Row(18);
        box.AddChild(row);
        row.AddChild(_portrait);
        var col = Widgets.Column(8);
        col.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(col);
        col.AddChild(_title);
        col.AddChild(_caption);
        _text.CustomMinimumSize = new Vector2(700, 0);
        col.AddChild(_text);
        col.AddChild(Widgets.Spacer());
        col.AddChild(_buttons);
    }

    public void Show(int index = 0)
    {
        _index = index;
        Visible = true;
        Refresh();
    }

    private void Refresh()
    {
        var sim = _hud.Sim;
        var slides = StoryWorld.Epilogue(sim);
        if (slides.Count == 0) { Visible = false; return; }
        _index = Math.Clamp(_index, 0, slides.Count - 1);
        var (still, text) = slides[_index];
        var ending = sim.Story?.Endings.GetValueOrDefault(sim.State.Campaign?.Ending ?? "");
        _title.Text = ending?.Title ?? "Epilogue";
        _caption.Text = $"{_index + 1} / {slides.Count}";
        _text.Text = text;
        var who = Subject(still);
        if (who == "marrow" && sim.State.Campaign?.Character("marrow").Dead == true) who = "dee";
        _portrait.Texture = who is null ? null : StoryArt.Portrait(who, "neutral", radio: false, sim);
        _portrait.Visible = _portrait.Texture is not null;
        Widgets.Clear(_buttons);
        if (_index > 0) _buttons.AddChild(Widgets.TextButton("Back", () => { _index--; Refresh(); }));
        _buttons.AddChild(Widgets.Spacer());
        if (_index < slides.Count - 1) _buttons.AddChild(Widgets.TextButton("Next", () => { _index++; Refresh(); }, variation: "Primary"));
        else
            _buttons.AddChild(Widgets.TextButton(sim.State.Campaign?.Over == true ? "The end" : "Play on", () =>
            {
                Visible = false;
                Finished?.Invoke();
            }, variation: "Primary"));
    }
}
