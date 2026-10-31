using System.Collections.Generic;
using Godot;
using Remnants.Game.Hud;
using Remnants.Game.Hud.Help;

namespace Remnants.Game.Scavenge.Ui;

/// <summary>
/// A card section that folds: a header row (chevron, icon, title, a one-line summary that stays readable while folded,
/// and an (i) for a help topic) over a body. Clicking the header opens or closes it (on press); whether it is open is
/// remembered per section for the session, so the unit card keeps the shape the player chose from unit to unit.
/// </summary>
public partial class Expander : PanelContainer
{
    private static readonly Dictionary<string, bool> Remembered = new();

    private readonly string _key;
    private readonly TextureRect _chevron;
    private readonly Label _title = Widgets.Label("", "Heading");
    private readonly Label _summary = Widgets.Label("", "Small");
    private readonly Button _header;
    private readonly Unchanged _set = new();

    /// <summary>What folds away.</summary>
    public VBoxContainer Body { get; } = Widgets.Column(3);

    public bool Open { get; private set; }

    public Expander() : this("", "", "ui_info", null, true) { }

    public Expander(string key, string title, string icon, string? topic, bool openByDefault)
    {
        _key = key;
        ThemeTypeVariation = "Card";
        Open = Remembered.TryGetValue(key, out var open) ? open : openByDefault;
        var col = Widgets.Column(4);
        AddChild(col);
        var head = Widgets.Row(4);
        col.AddChild(head);
        _header = new Button
        {
            ThemeTypeVariation = "Flat", FocusMode = FocusModeEnum.All, SizeFlagsHorizontal = SizeFlags.ExpandFill,
            MouseDefaultCursorShape = CursorShape.PointingHand, CustomMinimumSize = new Vector2(0, 22),
        };
        Motion.OnPress(_header);
        _header.Pressed += () => SetOpen(!Open);
        head.AddChild(_header);
        var row = Widgets.Row(6);
        row.MouseFilter = MouseFilterEnum.Ignore;
        row.SetAnchorsPreset(LayoutPreset.FullRect);
        _header.AddChild(row);
        _chevron = Icons.Rect(Icons.Get("ui_chevron_right", 12), 12, Palette.TextMuted);
        _chevron.MouseFilter = MouseFilterEnum.Ignore;
        _chevron.PivotOffset = new Vector2(6, 6);
        row.AddChild(_chevron);
        var mark = Icons.Rect(Icons.Get(icon, 14), 14, Palette.Rust);
        mark.MouseFilter = MouseFilterEnum.Ignore;
        row.AddChild(mark);
        _title.Text = title.ToUpperInvariant();
        _title.MouseFilter = MouseFilterEnum.Ignore;
        row.AddChild(_title);
        _summary.MouseFilter = MouseFilterEnum.Ignore;
        _summary.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _summary.HorizontalAlignment = HorizontalAlignment.Right;
        _summary.ClipText = true;
        row.AddChild(_summary);
        if (topic is not null) head.AddChild(new InfoTip(topic));
        col.AddChild(Body);
        Apply(animate: false);
    }

    public string Title
    {
        set => _set.Text(_title, value.ToUpperInvariant());
    }

    /// <summary>The line next to the title ("All parts ok", "Pistol · 4.7/21 kg"); always shown.</summary>
    public void Summary(string text, Color? color = null)
    {
        _set.Text(_summary, text);
        _set.FontColor(_summary, color ?? Palette.TextMuted);
        _set.Tooltip(_header, (Open ? "Fold" : "Unfold") + (text.Length > 0 ? $": {text}" : ""));
    }

    public void SetOpen(bool open)
    {
        if (open == Open) return;
        Open = open;
        Remembered[_key] = open;
        Apply(animate: true);
        Toggled?.Invoke(open);
    }

    /// <summary>The player opened or closed it (a folded body may skip its refreshes until then).</summary>
    public event System.Action<bool>? Toggled;

    private void Apply(bool animate)
    {
        _chevron.Texture = Icons.Get(Open ? "ui_chevron_down" : "ui_chevron_right", 12);
        // The summary stands in for the folded body; open, the body says it.
        _summary.Visible = !Open;
        if (!animate) Body.Visible = Open;
        else if (Open) Motion.Reveal(Body, new Vector2(0, -6));
        else Body.Visible = false; // closing at once: the card below moves up under the cursor's own click
        _header.TooltipText = Open ? "Fold" : "Unfold";
    }
}
