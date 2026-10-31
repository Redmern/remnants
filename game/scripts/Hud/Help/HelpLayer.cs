using System;
using System.Collections.Generic;
using Godot;
using Remnants.Core.Help;

namespace Remnants.Game.Hud.Help;

/// <summary>
/// Where help shows, above every screen: one hover tooltip shared by all <see cref="InfoTip"/>s, the detail popover a
/// click opens, and the searchable <see cref="HelpIndex"/> (F1, or Help in the pause menu). It lives on the tree root
/// (attached once at boot), runs while the game is paused and takes the HUD theme and the UI scale.
/// <list type="bullet">
/// <item>The tooltip appears <see cref="ShowDelay"/> after hovering (at once when moving from one tip to the next, so a
/// row of them never flickers), fades and slides in, sits under its anchor (above when there is no room) and is kept
/// inside the screen.</item>
/// <item>The popover sits beside its anchor, clamped to the screen; Esc, a click outside or the close button shuts it.
/// Links inside it open the linked topic in place, with Back.</item>
/// </list>
/// </summary>
public partial class HelpLayer : CanvasLayer
{
    private const float ShowDelay = 0.08f;
    private const float HideGrace = 0.12f;
    private const float TipWidth = 300;
    private const float PopoverWidth = 400;
    private const float Margin = 8;

    public static HelpLayer? Instance { get; private set; }

    private readonly Control _root = new() { MouseFilter = Control.MouseFilterEnum.Ignore };

    private readonly PanelContainer _tip = new() { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
    private readonly Label _tipTitle = Widgets.Label("", "Display", Palette.Amber, 15);
    private readonly Label _tipText = Widgets.Wrapped("");
    private readonly Label _tipHint = Widgets.Label("Click for details", "Small", Palette.TextDim);
    private InfoTip? _tipFor;
    private double _tipShowAt = -1, _tipHideAt = -1;

    private readonly Control _catcher = new() { MouseFilter = Control.MouseFilterEnum.Stop, Visible = false };
    private readonly PanelContainer _popover = new() { MouseFilter = Control.MouseFilterEnum.Stop, Visible = false };
    private readonly ScrollContainer _popScroll = new() { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
    private readonly TopicView _popView = new();
    private readonly Button _back;
    private readonly List<string> _history = new();
    private Control? _anchor;
    private Func<HelpContext>? _popContext;

    public HelpIndex Index { get; } = new();

    public static void Attach(SceneTree tree)
    {
        if (Instance is not null) return;
        Instance = new HelpLayer { Name = "HelpLayer" };
        tree.Root.CallDeferred(Node.MethodName.AddChild, Instance);
    }

    public HelpLayer()
    {
        Layer = 120;
        ProcessMode = ProcessModeEnum.Always;
        _root.Theme = HudTheme.Shared;
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps;
        AddChild(_root);

        // Tooltip: title, one or two sentences, the hint.
        _tip.AddThemeStyleboxOverride("panel", PanelArt.Box(new(Palette.Raised, Palette.EdgeDark, 0.03f, Strip: Palette.Amber, StripHeight: 2), 10, 8));
        _tip.CustomMinimumSize = new Vector2(TipWidth, 0);
        var tipBox = Widgets.Column(2);
        tipBox.MouseFilter = Control.MouseFilterEnum.Ignore;
        _tipText.AddThemeFontSizeOverride("font_size", 14);
        _tipText.MouseFilter = Control.MouseFilterEnum.Ignore;
        tipBox.AddChild(_tipTitle);
        tipBox.AddChild(_tipText);
        tipBox.AddChild(_tipHint);
        _tip.AddChild(tipBox);

        // Popover: a transparent full-screen catcher (a click outside closes) under the panel.
        _catcher.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _catcher.GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true }) CloseDetail();
        };
        _root.AddChild(_catcher);
        _popover.AddThemeStyleboxOverride("panel",
            PanelArt.Box(new(Palette.Panel, Palette.EdgeDark, 0.05f, Rivets: true, Strip: Palette.Amber, StripHeight: 3), 16, 14));
        _popover.CustomMinimumSize = new Vector2(PopoverWidth, 0);
        var popBox = Widgets.Column(6);
        _popover.AddChild(popBox);
        var bar = Widgets.Row(4);
        _back = Widgets.IconButton("ui_chevron_left", "Back", "The topic before", Back);
        _back.Visible = false;
        bar.AddChild(_back);
        bar.AddChild(Widgets.Spacer());
        bar.AddChild(Widgets.IconButton("ui_menu", "All topics", "Every help topic, searchable", () =>
        {
            var topic = _popView.Topic?.Id;
            CloseDetail();
            Index.OpenAt(topic);
        }, Hotkeys.KeyName("open_help"), "Flat"));
        bar.AddChild(Widgets.IconButton("ui_close", "", "Close", CloseDetail, Hotkeys.KeyName("pause_menu"), "Flat"));
        popBox.AddChild(bar);
        _popScroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _popScroll.AddChild(_popView);
        popBox.AddChild(_popScroll);
        _popView.Navigate += id => ShowTopic(id, push: true);
        _root.AddChild(_popover);

        _root.AddChild(Index);
        _root.AddChild(_tip);
    }

    public override void _EnterTree() => Instance = this;

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    private static double Now => Time.GetTicksMsec() / 1000.0;

    // ── Tooltip ──────────────────────────────────────────────────────────────────────────────────────────────────────

    public void ShowTip(InfoTip tip)
    {
        if (_popover.Visible) return;
        var switching = _tip.Visible && _tipFor is not null;
        _tipFor = tip;
        _tipHideAt = -1;
        if (switching) Present(tip, animate: false);
        else _tipShowAt = Now + ShowDelay;
    }

    public void HideTip(InfoTip tip)
    {
        if (_tipFor != tip) return;
        _tipShowAt = -1;
        _tipHideAt = Now + HideGrace;
    }

    private void Present(InfoTip tip, bool animate)
    {
        if (HelpLibrary.Find(tip.Topic) is not { } topic || !tip.IsVisibleInTree()) return;
        _tipTitle.Text = topic.Title;
        _tipText.Text = topic.Short;
        _tip.Size = new Vector2(TipWidth, 0);
        _tip.ResetSize();
        _tip.Visible = true;
        // Size is known after this frame's layout; place now with the minimum size, again once it settles.
        Place(tip);
        Callable.From(() => Place(tip)).CallDeferred();
        if (animate) Motion.FadeIn(_tip);
        else _tip.Modulate = Colors.White;
    }

    private void Place(InfoTip tip)
    {
        if (!_tip.Visible || _tipFor != tip) return;
        var screen = _root.GetViewportRect().Size;
        var anchor = tip.GetGlobalRect();
        var size = _tip.GetCombinedMinimumSize();
        size.X = Mathf.Max(size.X, TipWidth);
        var below = anchor.End.Y + 6;
        var y = below + size.Y + Margin <= screen.Y ? below : anchor.Position.Y - 6 - size.Y;
        var x = anchor.Position.X + anchor.Size.X / 2 - size.X / 2;
        _tip.Position = new Vector2(Mathf.Clamp(x, Margin, Mathf.Max(Margin, screen.X - size.X - Margin)), Mathf.Clamp(y, Margin, Mathf.Max(Margin, screen.Y - size.Y - Margin)));
    }

    public override void _Process(double delta)
    {
        var now = Now;
        if (_tipShowAt >= 0 && now >= _tipShowAt)
        {
            _tipShowAt = -1;
            if (_tipFor is { } tip && IsInstanceValid(tip)) Present(tip, animate: true);
        }
        if (_tipHideAt >= 0 && now >= _tipHideAt)
        {
            _tipHideAt = -1;
            _tipFor = null;
            Motion.Conceal(_tip);
        }
        // An anchor freed or hidden under the tooltip (its panel closed) takes the tooltip with it.
        if (_tip.Visible && (_tipFor is null || !IsInstanceValid(_tipFor) || !_tipFor.IsVisibleInTree()) && _tipHideAt < 0)
        {
            _tipFor = null;
            _tip.Visible = false;
        }
    }

    // ── Detail popover ───────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Opens the popover on <paramref name="topicId"/> beside <paramref name="anchor"/> (centred without one).</summary>
    public void OpenDetail(string topicId, Func<HelpContext>? context = null, Control? anchor = null)
    {
        if (HelpLibrary.Find(topicId) is null) return;
        _tipShowAt = _tipHideAt = -1;
        _tip.Visible = false;
        _anchor = anchor;
        _popContext = context;
        _history.Clear();
        ShowTopic(topicId, push: false);
        _catcher.Visible = true;
        _popover.Visible = true;
        PlacePopover();
        Motion.FadeIn(_popover);
        Callable.From(PlacePopover).CallDeferred();
    }

    private void ShowTopic(string id, bool push)
    {
        if (HelpLibrary.Find(id) is not { } topic) return;
        if (push && _popView.Topic is { } current) _history.Add(current.Id);
        _back.Visible = _history.Count > 0;
        _popView.Show(topic, HelpLibrary.Context(_popContext));
        _popScroll.ScrollVertical = 0;
        Callable.From(PlacePopover).CallDeferred();
    }

    private void Back()
    {
        if (_history.Count == 0) return;
        var id = _history[^1];
        _history.RemoveAt(_history.Count - 1);
        ShowTopic(id, push: false);
    }

    private void PlacePopover()
    {
        if (!_popover.Visible) return;
        var screen = _root.GetViewportRect().Size;
        var maxHeight = Mathf.Floor(screen.Y * 0.7f);
        var content = _popView.GetCombinedMinimumSize().Y + 70;
        var size = new Vector2(PopoverWidth, Mathf.Min(maxHeight, content));
        _popScroll.CustomMinimumSize = new Vector2(0, size.Y - 70);
        _popover.Size = size;
        Vector2 at;
        if (_anchor is { } a && IsInstanceValid(a) && a.IsVisibleInTree())
        {
            var r = a.GetGlobalRect();
            var right = r.End.X + 10;
            var x = right + size.X + Margin <= screen.X ? right : r.Position.X - 10 - size.X;
            at = new Vector2(x, r.Position.Y - 16);
        }
        else at = (screen - size) / 2;
        _popover.Position = new Vector2(Mathf.Clamp(at.X, Margin, Mathf.Max(Margin, screen.X - size.X - Margin)),
            Mathf.Clamp(at.Y, Margin, Mathf.Max(Margin, screen.Y - size.Y - Margin)));
    }

    public bool DetailOpen => _popover.Visible;

    public void CloseDetail()
    {
        _catcher.Visible = false;
        Motion.Conceal(_popover);
        if (_anchor is { } a && IsInstanceValid(a) && a.IsVisibleInTree() && a.FocusMode != Control.FocusModeEnum.None) a.GrabFocus();
        _anchor = null;
    }

    // ── Keys: Esc closes the popover first; F1 the index; F3 the frame-time overlay ─────────────────────────────────

    public override void _Input(InputEvent e)
    {
        if (_popover.Visible && Hotkeys.Pressed(e, "pause_menu"))
        {
            GetViewport().SetInputAsHandled();
            CloseDetail();
        }
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (Hotkeys.Pressed(e, "open_help"))
        {
            GetViewport().SetInputAsHandled();
            if (_popover.Visible) CloseDetail();
            if (Index.Visible) Index.Close();
            else Index.OpenAt(null);
        }
        else if (Hotkeys.Pressed(e, "toggle_frame_time"))
        {
            GetViewport().SetInputAsHandled();
            FrameTimeOverlay.Toggle(GetTree());
        }
    }
}
