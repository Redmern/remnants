using System;
using System.Linq;
using Godot;
using Remnants.Core.Help;

namespace Remnants.Game.Hud.Help;

/// <summary>
/// One help topic laid out for reading, shared by the detail popover and the help index: title, the long text, the live
/// breakdown (terms, total, formula) when the topic has one and the context gives it something to show, and the related
/// topics as links. Links inside the text and the related buttons raise <see cref="Navigate"/>.
/// </summary>
public partial class TopicView : VBoxContainer
{
    private readonly Label _title = Widgets.Label("", "Title");
    private readonly Label _category = Widgets.Label("", "Small", Palette.TextDim);
    private readonly RichTextLabel _text = new()
    {
        BbcodeEnabled = true,
        FitContent = true,
        ScrollActive = false,
        SelectionEnabled = false,
        MouseFilter = MouseFilterEnum.Pass,
        SizeFlagsHorizontal = SizeFlags.ExpandFill,
        AutowrapMode = TextServer.AutowrapMode.WordSmart,
    };
    private readonly VBoxContainer _live = Widgets.Column(4);
    private readonly HFlowContainer _related = new();
    private readonly Label _relatedHeader = Widgets.Label("RELATED", "Heading");

    public event Action<string>? Navigate;

    /// <summary>The topic shown, if any.</summary>
    public HelpTopic? Topic { get; private set; }

    public TopicView()
    {
        AddThemeConstantOverride("separation", 8);
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _title.AddThemeFontSizeOverride("font_size", 20);
        var head = Widgets.Column(0);
        head.AddChild(_category);
        head.AddChild(_title);
        AddChild(head);
        _text.AddThemeFontSizeOverride("normal_font_size", 14);
        _text.AddThemeFontSizeOverride("bold_font_size", 14);
        _text.AddThemeFontSizeOverride("italics_font_size", 14);
        _text.AddThemeFontOverride("normal_font", Fonts.Body);
        _text.AddThemeFontOverride("bold_font", Fonts.BodyBold);
        _text.AddThemeFontOverride("italics_font", Fonts.BodyItalic);
        _text.AddThemeColorOverride("default_color", Palette.Text);
        _text.AddThemeConstantOverride("line_separation", 2);
        _text.MetaClicked += meta =>
        {
            var link = meta.AsString();
            if (link.StartsWith("topic:", StringComparison.Ordinal)) Navigate?.Invoke(link[6..]);
        };
        _text.MetaHoverStarted += _ => _text.MouseDefaultCursorShape = CursorShape.PointingHand;
        _text.MetaHoverEnded += _ => _text.MouseDefaultCursorShape = CursorShape.Arrow;
        AddChild(_text);
        AddChild(_live);
        _related.AddThemeConstantOverride("h_separation", 4);
        _related.AddThemeConstantOverride("v_separation", 4);
        AddChild(_relatedHeader);
        AddChild(_related);
    }

    public void Show(HelpTopic topic, HelpContext context)
    {
        Topic = topic;
        _category.Text = topic.Category.ToUpperInvariant();
        _title.Text = topic.Title;
        _text.Text = HelpLibrary.BbCode(topic.Long.Length > 0 ? topic.Long : topic.Short);

        Widgets.Clear(_live);
        if (HelpLibrary.Live(topic, context) is { } breakdown) _live.AddChild(Live(breakdown));
        _live.Visible = _live.GetChildCount() > 0;

        Widgets.Clear(_related);
        foreach (var id in topic.Related)
        {
            if (HelpLibrary.Catalog.Find(id) is not { } other) continue;
            var target = id;
            var link = Widgets.TextButton(other.Title, () => Navigate?.Invoke(target), other.Short, "Flat");
            link.CustomMinimumSize = new Vector2(0, 26);
            link.AddThemeColorOverride("font_color", Palette.Amber);
            _related.AddChild(Motion.OnPress(link));
        }
        _relatedHeader.Visible = _related.Visible = _related.GetChildCount() > 0;
    }

    /// <summary>The breakdown as an inset: "LIVE NUMBERS · title", term rows (label, value, note), the total, the formula.</summary>
    private static Control Live(Breakdown b)
    {
        var panel = new PanelContainer { ThemeTypeVariation = "Inset" };
        var box = Widgets.Column(4);
        panel.AddChild(box);
        box.AddChild(Widgets.SectionHeader($"Live · {b.Title}", "stat_sight"));
        var grid = new GridContainer { Columns = 3 };
        grid.AddThemeConstantOverride("h_separation", 10);
        grid.AddThemeConstantOverride("v_separation", 2);
        foreach (var row in b.Rows)
        {
            grid.AddChild(Widgets.Label(row.Label, "", Palette.TextMuted, 13));
            var value = Widgets.Label(row.Value, "Number", Palette.Text, 15);
            value.HorizontalAlignment = HorizontalAlignment.Right;
            grid.AddChild(value);
            var note = Widgets.Label(row.Note, "Small", Palette.TextDim);
            note.ClipText = true;
            note.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            note.CustomMinimumSize = new Vector2(60, 0);
            grid.AddChild(note);
        }
        box.AddChild(grid);
        var total = Widgets.Row(8);
        total.AddChild(Widgets.Label("RESULT", "Heading"));
        total.AddChild(Widgets.Spacer());
        total.AddChild(Widgets.Label(b.Total, "Number", Palette.Amber, 17));
        box.AddChild(new HSeparator());
        box.AddChild(total);
        if (b.Formula.Length > 0) box.AddChild(Widgets.Wrapped(b.Formula, "Small", Palette.TextDim));
        return panel;
    }

    /// <summary>The first focusable control (a related link), for keyboard users.</summary>
    public Control? FirstLink() => _related.GetChildren().OfType<Control>().FirstOrDefault();
}
