using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Game.Hud;

namespace Remnants.Game.Story;

/// <summary>One line as the panel shows it (from Core's <c>DialogueView</c> in the colony, or a field prompt in a mission).</summary>
public sealed record DialogueLine(
    string Speaker, string Name, string Text, string Mood, bool Radio, Texture2D? Portrait, List<(int Index, string Text, string? Hint)> Choices,
    string Title, string? CallSign = null);

/// <summary>
/// The dialogue panel in the bottom third of the screen: portrait, name, text typed out (click, Space or Enter
/// finishes the line, then goes on), up to three choices (1–3) with their known consequence, Skip (to the next choice)
/// and a log of everything said. The radio variant has the radio frame, a grainy portrait and the call sign. It only
/// renders: <see cref="Current"/> reads the line from Core and the callbacks send the commands.
/// </summary>
public partial class DialoguePanel : Control
{
    private const float CharsPerSecond = 70;

    private readonly Func<DialogueLine?> _current;
    private readonly Action _advance;
    private readonly Action<int> _choose;
    private readonly Func<IEnumerable<(string Speaker, string Text, bool Choice)>> _log;

    private readonly PanelContainer _frame = new();
    private readonly TextureRect _portrait = new() { CustomMinimumSize = new Vector2(150, 150), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered };
    private readonly Label _initial = Widgets.Label("", "Display", Palette.Text, 54);
    private readonly ColorRect _grain = new() { Color = new Color(0.9f, 0.75f, 0.45f, 0.10f), MouseFilter = MouseFilterEnum.Ignore };
    private readonly Label _name = Widgets.Label("", "Heading", Palette.Amber);
    private readonly Label _callSign = Widgets.Label("", "Small", Palette.TextMuted);
    private readonly Label _title = Widgets.Label("", "Small", Palette.TextDim);
    private readonly RichTextLabel _text = new() { BbcodeEnabled = true, FitContent = true, ScrollActive = false, CustomMinimumSize = new Vector2(0, 64) };
    private readonly VBoxContainer _choices = Widgets.Column(6);
    private readonly Label _continue = Widgets.Label("Click or Space to go on", "Small", Palette.TextDim);
    private readonly ScrollContainer _logScroll = new() { CustomMinimumSize = new Vector2(0, 180), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, Visible = false };
    private readonly VBoxContainer _logList = Widgets.Column(4);
    private readonly VBoxContainer _body = Widgets.Column(6);
    private string _shownKey = "";
    private float _typed;

    public DialoguePanel() : this(() => null, () => { }, _ => { }, () => []) { }

    public DialoguePanel(Func<DialogueLine?> current, Action advance, Action<int> choose, Func<IEnumerable<(string Speaker, string Text, bool Choice)>> log)
    {
        _current = current;
        _advance = advance;
        _choose = choose;
        _log = log;
        Theme = HudTheme.Shared;
        ProcessMode = ProcessModeEnum.Always;
        SetAnchorsPreset(LayoutPreset.BottomWide);
        GrowVertical = GrowDirection.Begin;
        OffsetLeft = 140;
        OffsetRight = -140;
        OffsetBottom = -24;
        OffsetTop = -330;
        MouseFilter = MouseFilterEnum.Stop;
        Visible = false;

        // The art frame has a see-through centre: a solid backing keeps the town and the log out of the text.
        var backing = new Panel { MouseFilter = MouseFilterEnum.Ignore };
        backing.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(Palette.Void, 0.94f), CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4 });
        backing.SetAnchorsPreset(LayoutPreset.FullRect);
        backing.OffsetLeft = backing.OffsetTop = 10;
        backing.OffsetRight = backing.OffsetBottom = -10;
        AddChild(backing);
        AddChild(_frame);
        _frame.SetAnchorsPreset(LayoutPreset.FullRect);
        var row = Widgets.Row(18);
        _frame.AddChild(row);

        var portraitBox = new PanelContainer { SizeFlagsVertical = SizeFlags.ShrinkBegin };
        portraitBox.AddThemeStyleboxOverride("panel", PanelArt.Box(new(Palette.Inset, Palette.EdgeDark, 0.04f, Sunken: true), 3, 3));
        var stack = new Control { CustomMinimumSize = new Vector2(150, 150), ClipContents = true };
        stack.AddChild(_portrait);
        _portrait.SetAnchorsPreset(LayoutPreset.FullRect);
        _initial.HorizontalAlignment = HorizontalAlignment.Center;
        _initial.VerticalAlignment = VerticalAlignment.Center;
        _initial.SetAnchorsPreset(LayoutPreset.FullRect);
        stack.AddChild(_initial);
        _grain.SetAnchorsPreset(LayoutPreset.FullRect);
        stack.AddChild(_grain);
        portraitBox.AddChild(stack);
        row.AddChild(portraitBox);

        _body.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var header = Widgets.Row(10);
        header.AddChild(_name);
        header.AddChild(_callSign);
        header.AddChild(Widgets.Spacer());
        header.AddChild(_title);
        header.AddChild(Widgets.IconButton("ui_log", "", "Dialogue log", ToggleLog, variation: "Flat"));
        header.AddChild(Widgets.IconButton("ui_fast", "Skip", "Skip to the next choice", Skip, variation: "Flat"));
        _body.AddChild(header);
        _text.AddThemeFontOverride("normal_font", Fonts.Body);
        _text.AddThemeFontOverride("italics_font", Fonts.BodyItalic);
        _text.AddThemeFontSizeOverride("normal_font_size", 19);
        _text.AddThemeFontSizeOverride("italics_font_size", 18);
        _text.AddThemeColorOverride("default_color", Palette.Text);
        _text.MouseFilter = MouseFilterEnum.Ignore;
        _body.AddChild(_text);
        _body.AddChild(_choices);
        _body.AddChild(_continue);
        _logScroll.AddChild(_logList);
        _logList.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _body.AddChild(_logScroll);
        row.AddChild(_body);
    }

    /// <summary>Re-reads the line; shows the panel while there is one.</summary>
    public void Refresh()
    {
        var line = _current();
        Visible = line is not null;
        if (line is null) return;
        var key = $"{line.Title}|{line.Speaker}|{line.Text}|{line.Choices.Count}";
        if (key == _shownKey) return;
        _shownKey = key;
        _frame.AddThemeStyleboxOverride("panel", StoryArt.Frame(line.Radio));
        _portrait.Texture = line.Portrait;
        _initial.Text = line.Portrait is null && line.Name.Length > 0 ? line.Name[..1] : "";
        _portrait.Modulate = line.Radio ? new Color(0.92f, 0.86f, 0.72f) : Colors.White;
        _grain.Visible = line.Radio;
        _name.Text = line.Speaker == "narration" ? "" : line.Name;
        _callSign.Text = line.Radio && line.CallSign is { } sign ? $"· on the radio · {sign}" : "";
        _title.Text = line.Title;
        // An answer-only line shows what it answers.
        var said = _log().LastOrDefault(l => !l.Choice && l.Speaker.Length > 0);
        _text.Text = line.Text.Length == 0 && line.Choices.Count > 0
            ? $"[color={Palette.Hex(Palette.TextMuted)}]{Escape(said.Speaker ?? "")}: {Escape(said.Text ?? "")}[/color]"
            : line.Speaker == "narration" ? $"[i]{Escape(line.Text)}[/i]" : Escape(line.Text);
        _text.VisibleCharacters = 0;
        _typed = 0;
        _typing = true;
        Widgets.Clear(_choices);
        var number = 1;
        foreach (var (index, text, hint) in line.Choices)
        {
            var label = hint is null ? $"{number}.  {text}" : $"{number}.  {text}   — {hint}";
            var button = Widgets.TextButton(label, () => Choose(index), hint ?? "", variation: number == 1 ? "Primary" : "");
            button.Alignment = HorizontalAlignment.Left;
            _choices.AddChild(button);
            number++;
        }
        _choices.Visible = false;
        _continue.Visible = false;
        if (_logScroll.Visible) FillLog();
    }

    public override void _Process(double delta)
    {
        if (!Visible || !_typing) return;
        _typed += (float)delta * CharsPerSecond;
        _text.VisibleCharacters = (int)_typed;
        if (_typed >= _text.GetTotalCharacterCount()) Typed();
    }

    private bool _typing;

    private bool Typing => _typing;

    private void Typed()
    {
        _typing = false;
        _text.VisibleCharacters = -1;
        _choices.Visible = _choices.GetChildCount() > 0;
        _continue.Visible = _choices.GetChildCount() == 0;
    }

    /// <summary>A click or key: finish the typing, else go on (lines without choices).</summary>
    private void Next()
    {
        if (Typing) { Typed(); return; }
        if (_choices.GetChildCount() > 0) return;
        _advance();
    }

    private void Choose(int index)
    {
        if (Typing) { Typed(); return; }
        _choose(index);
    }

    /// <summary>Go on through plain lines until one has choices (or the dialogue ends).</summary>
    private void Skip()
    {
        for (var i = 0; i < 64 && _current() is { Choices.Count: 0 }; i++) _advance();
        Refresh();
        if (Visible) Typed();
    }

    private void ToggleLog()
    {
        _logScroll.Visible = !_logScroll.Visible;
        if (_logScroll.Visible) FillLog();
    }

    private void FillLog()
    {
        Widgets.Clear(_logList);
        foreach (var (speaker, text, choice) in _log().TakeLast(60))
        {
            var label = Widgets.Wrapped(speaker.Length == 0 ? text : choice ? $"▸ {speaker}: {text}" : $"{speaker}: {text}", "Small",
                choice ? Palette.Amber : speaker.Length == 0 ? Palette.TextMuted : Palette.Text);
            _logList.AddChild(label);
        }
        CallDeferred(MethodName.ScrollLogDown);
    }

    private void ScrollLogDown() => _logScroll.ScrollVertical = (int)_logScroll.GetVScrollBar().MaxValue;

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
        {
            Next();
            AcceptEvent();
        }
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!Visible || e is not InputEventKey { Pressed: true, Echo: false } key) return;
        if (key.Keycode is Key.Space or Key.Enter or Key.KpEnter) Next();
        else if (key.Keycode is >= Key.Key1 and <= Key.Key3 && _choices.Visible)
        {
            var i = (int)(key.Keycode - Key.Key1);
            if (i < _choices.GetChildCount()) ((Button)_choices.GetChild(i)).EmitSignal(BaseButton.SignalName.Pressed);
        }
        else return;
        GetViewport().SetInputAsHandled();
    }

    private static string Escape(string text) => text.Replace("[", "[lb]");
}
