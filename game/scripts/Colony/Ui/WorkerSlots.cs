using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Core.Colony;
using Remnants.Core.Survivors;
using Remnants.Game.Hud;

namespace Remnants.Game.Colony.Ui;

/// <summary>How well a survivor suits a slot: a score 0..1 (shown as a percent) and the line saying what it is made of.</summary>
public readonly record struct SlotFit(double Score, string Detail);

/// <summary>
/// The people at one workplace as portraits plus a "+" for each free slot. "+" opens a <see cref="WorkerPicker"/> of the
/// colony's survivors; a click assigns through the owner's command (Core decides, its refusal shows in the picker). A
/// worker's portrait offers Unassign / Open survivor on a click; a right-click unassigns at once. New portraits enter
/// with <see cref="Motion.Enter"/>. Reused by the building panel (any job building, the Med Bay's
/// staff) and the guard posts; the owner says who is listed, how they fit and which commands to send.
/// </summary>
public partial class WorkerSlots : HFlowContainer
{
    private readonly ColonySession _session;
    private readonly int _size;
    private string _signature = "";
    private WorkerPicker? _picker;
    private PopupMenu? _menu;
    private int _menuSurvivor;
    private readonly HashSet<int> _shown = new();
    private static readonly StringName SurvivorMeta = "survivor";

    /// <summary>The picker's title ("Assign to Woodcutter").</summary>
    public Func<ColonySim, string> Title { get; set; } = _ => "Assign";

    /// <summary>Who the picker lists (default: everyone but babies and the dead); the command still decides.</summary>
    public Func<ColonySim, Survivor, bool> Candidate { get; set; } = (_, s) => !s.IsInfant;

    /// <summary>The slot's score for a survivor (skill and attributes for the job), or null for none.</summary>
    public Func<ColonySim, Survivor, SlotFit?> Fit { get; set; } = (_, _) => null;

    public Func<Survivor, IColonyCommand> Assign { get; set; } = null!;
    public Func<Survivor, IColonyCommand> Unassign { get; set; } = null!;

    /// <summary>The picker's shift-click: move them here, leaving their other jobs; null where there is no such move.</summary>
    public Func<Survivor, IColonyCommand>? Move { get; set; }

    /// <summary>Tooltip on a free slot and an assigned worker.</summary>
    public string FreeTip { get; set; } = "Free slot: click to assign someone.";
    public Func<Survivor, string> WorkerTip { get; set; } = s => s.Name;

    /// <summary>"Open survivor" on a worker's portrait.</summary>
    public event Action<int>? SurvivorRequested;

    public WorkerSlots(ColonySession session, int size = 38)
    {
        _session = session;
        _size = size;
        AddThemeConstantOverride("h_separation", 4);
        AddThemeConstantOverride("v_separation", 4);
    }

    /// <summary>Shows <paramref name="workers"/> and the free slots up to <paramref name="slots"/>; rebuilds only on change.</summary>
    public void Set(IReadOnlyList<Survivor> workers, int slots)
    {
        var signature = string.Join(",", workers.Select(w => w.Id)) + $"/{slots}";
        if (_picker is { Visible: true }) _picker.Refresh();
        if (signature == _signature) return;
        _signature = signature;
        _shown.Clear();
        foreach (var child in GetChildren().OfType<Portrait>()) if (child.HasMeta(SurvivorMeta)) _shown.Add(child.GetMeta(SurvivorMeta).AsInt32());
        Widgets.Clear(this);
        foreach (var w in workers)
        {
            var id = w.Id;
            var portrait = new Portrait(_size) { TooltipText = $"{WorkerTip(w)}\nClick: unassign or open their panel. Right-click: unassign.", MouseFilter = MouseFilterEnum.Stop };
            portrait.Set(PortraitStudio.For(GetTree(), w), PortraitStudio.Backdrop(w.Id));
            portrait.SetMeta(SurvivorMeta, id);
            portrait.MouseDefaultCursorShape = CursorShape.PointingHand;
            portrait.GuiInput += e =>
            {
                if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) ShowMenu(id, portrait);
                else if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right } && _session.Sim.State.FindSurvivor(id) is { } who)
                {
                    portrait.AcceptEvent();
                    _session.Execute(Unassign(who));
                }
            };
            AddChild(portrait);
            if (!_shown.Contains(id)) Motion.Enter(portrait);
        }
        for (var i = workers.Count; i < slots; i++)
        {
            var slot = new FreeSlot { CustomMinimumSize = new Vector2(_size, _size), TooltipText = FreeTip, Name = $"FreeSlot{i}", FocusMode = FocusModeEnum.All };
            slot.Pressed += () => OpenPicker(slot);
            AddChild(slot);
        }
    }

    /// <summary>Forget the shown workers (another building took the panel) and close the picker.</summary>
    public void Reset()
    {
        _signature = "";
        _picker?.Hide();
    }

    /// <summary>Opens the picker under the first free slot (debug: the smoke run's click on "+"); false when no slot is free.</summary>
    public bool OpenFirstFree()
    {
        if (GetChildren().OfType<FreeSlot>().FirstOrDefault() is not { } slot) return false;
        OpenPicker(slot);
        return true;
    }

    public WorkerPicker? Picker => _picker;

    private void OpenPicker(Control at)
    {
        if (_picker is null)
        {
            _picker = new WorkerPicker(_session, this);
            AddChild(_picker, false, InternalMode.Back);
        }
        _picker.Open(at);
    }

    internal bool Pick(Survivor s, bool move, out string message)
    {
        var result = _session.Execute(move && Move is { } replace ? replace(s) : Assign(s));
        message = result.Message;
        return result.Ok;
    }

    private void ShowMenu(int survivorId, Control at)
    {
        if (_menu is null)
        {
            _menu = new PopupMenu { Theme = HudTheme.Shared };
            _menu.AddIconItem(Icons.Get("ui_minus", 16), "Unassign", 0);
            _menu.AddIconItem(Icons.Get("stat_pop", 16), "Open survivor", 1);
            _menu.IdPressed += OnMenu;
            AddChild(_menu, false, InternalMode.Back);
        }
        _menuSurvivor = survivorId;
        var name = _session.Sim.State.FindSurvivor(survivorId)?.Name ?? "";
        _menu.SetItemText(0, $"Unassign {name}");
        _menu.Position = (Vector2I)(at.GetScreenPosition() + new Vector2(0, at.Size.Y + 2) * GetViewport().GetFinalTransform().Scale);
        _menu.ResetSize();
        _menu.Popup();
    }

    private void OnMenu(long id)
    {
        if (_session.Sim.State.FindSurvivor(_menuSurvivor) is not { } s) return;
        if (id == 0) _session.Execute(Unassign(s));
        else SurvivorRequested?.Invoke(s.Id);
    }

    /// <summary>A dashed square with a "+": a free slot, a button.</summary>
    private partial class FreeSlot : Button
    {
        public FreeSlot()
        {
            Flat = true;
            MouseDefaultCursorShape = CursorShape.PointingHand;
            MouseEntered += QueueRedraw;
            MouseExited += QueueRedraw;
            FocusEntered += QueueRedraw;
            FocusExited += QueueRedraw;
        }

        public override void _Draw()
        {
            var hover = IsHovered() || HasFocus();
            var edge = hover ? Palette.Amber : Palette.EdgeLight;
            DrawRect(new Rect2(Vector2.Zero, Size), new Color(hover ? Palette.Raised : Palette.Inset, 0.7f));
            DrawDashedLine(new Vector2(1, 1), new Vector2(Size.X - 1, 1), edge, 1, 3);
            DrawDashedLine(new Vector2(1, Size.Y - 1), new Vector2(Size.X - 1, Size.Y - 1), edge, 1, 3);
            DrawDashedLine(new Vector2(1, 1), new Vector2(1, Size.Y - 1), edge, 1, 3);
            DrawDashedLine(new Vector2(Size.X - 1, 1), new Vector2(Size.X - 1, Size.Y - 1), edge, 1, 3);
            var plus = hover ? Palette.Amber : Palette.TextDim;
            DrawLine(new Vector2(Size.X / 2 - 5, Size.Y / 2), new Vector2(Size.X / 2 + 5, Size.Y / 2), plus, 2);
            DrawLine(new Vector2(Size.X / 2, Size.Y / 2 - 5), new Vector2(Size.X / 2, Size.Y / 2 + 5), plus, 2);
        }
    }
}

/// <summary>
/// The list behind a "+" slot: each candidate's portrait, name, current job, their score for this slot and badges
/// (injured, tired, child, patient, away, in class), best first. Click assigns; a refusal stays on screen in red.
/// </summary>
public partial class WorkerPicker : PopupPanel
{
    private readonly ColonySession _session;
    private readonly WorkerSlots _slots;
    private readonly Label _title = Widgets.Label("", "Heading");
    private readonly Label _error = Widgets.Label("", "Small", Palette.Danger);
    private readonly VBoxContainer _rows = Widgets.Column(2);
    private string _signature = "";

    public WorkerPicker(ColonySession session, WorkerSlots slots)
    {
        _session = session;
        _slots = slots;
        Theme = HudTheme.Shared;
        // A popup is its own window: give it the HUD's panel look (the theme alone leaves it see-through).
        AddThemeStyleboxOverride("panel", PanelArt.Box(new(Palette.Panel, Palette.EdgeDark, 0.05f, Strip: Palette.Rust, StripHeight: 3), 12, 10));
        var box = Widgets.Column(6);
        box.CustomMinimumSize = new Vector2(330, 0);
        box.AddChild(_title);
        _error.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _error.Visible = false;
        box.AddChild(_error);
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(0, 300), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _rows.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        scroll.AddChild(_rows);
        box.AddChild(scroll);
        AddChild(box);
    }

    public void Open(Control at)
    {
        _error.Visible = false;
        _signature = "";
        Refresh();
        var scale = at.GetViewport().GetFinalTransform().Scale;
        Position = (Vector2I)(at.GetScreenPosition() + new Vector2(0, at.Size.Y + 4) * scale);
        ResetSize();
        Popup();
        // Keep it on screen (the building panel sits at the right edge).
        var screen = at.GetViewport().GetVisibleRect().Size * scale;
        Position = new Vector2I(Math.Clamp(Position.X, 0, Math.Max(0, (int)screen.X - Size.X)), Math.Clamp(Position.Y, 0, Math.Max(0, (int)screen.Y - Size.Y)));
    }

    /// <summary>The candidate rows, rebuilt when who is where changes.</summary>
    public void Refresh()
    {
        var sim = _session.Sim;
        _title.Text = _slots.Title(sim);
        var people = sim.State.Survivors.Where(s => _slots.Candidate(sim, s))
            .Select(s => (S: s, Fit: _slots.Fit(sim, s)))
            .OrderByDescending(x => Available(sim, x.S)).ThenByDescending(x => x.Fit?.Score ?? 0).ThenBy(x => x.S.Name).ToList();
        var signature = string.Join(";", people.Select(x => $"{x.S.Id}:{string.Join(",", x.S.Jobs)}:{Badges(sim, x.S).Count}:{x.Fit?.Score:0.00}"));
        if (signature == _signature) return;
        _signature = signature;
        Widgets.Clear(_rows);
        if (people.Count == 0) _rows.AddChild(Widgets.Label("Nobody to assign.", "Muted"));
        foreach (var (s, fit) in people) _rows.AddChild(Row(sim, s, fit));
    }

    /// <summary>First choice for "+": free to start now (not away, in the Med Bay or in class).</summary>
    private static bool Available(ColonySim sim, Survivor s) => !s.Away && s.PatientAtBuildingId is null && sim.ClassOf(s) is null;

    /// <summary>The candidates' buttons, best first (debug: the smoke run clicks one).</summary>
    public IEnumerable<Button> Rows => _rows.GetChildren().OfType<Button>();

    private Button Row(ColonySim sim, Survivor s, SlotFit? fit)
    {
        var id = s.Id;
        var button = new Button { Flat = true, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 44), Name = $"Pick{id}" };
        button.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
        var row = Widgets.Row(8);
        row.MouseFilter = Control.MouseFilterEnum.Ignore;
        row.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        row.OffsetLeft = 4;
        row.OffsetRight = -4;
        var portrait = new Portrait(36) { MouseFilter = Control.MouseFilterEnum.Ignore, Dim = !Available(sim, s) };
        portrait.Set(PortraitStudio.For(button.IsInsideTree() ? button.GetTree() : _slots.GetTree(), s), PortraitStudio.Backdrop(s.Id));
        portrait.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        row.AddChild(portrait);
        var names = Widgets.Column(-2);
        names.MouseFilter = Control.MouseFilterEnum.Ignore;
        names.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        names.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        names.AddChild(Widgets.Label(s.Name));
        names.AddChild(Widgets.Label(Job(sim, s), "Small", Palette.TextMuted));
        row.AddChild(names);
        var badges = Widgets.Row(2);
        badges.MouseFilter = Control.MouseFilterEnum.Ignore;
        badges.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        var tips = new List<string>();
        foreach (var (icon, color, tip) in Badges(sim, s))
        {
            var rect = Icons.Rect(Icons.Get(icon, 16), 16, color);
            rect.MouseFilter = Control.MouseFilterEnum.Ignore;
            badges.AddChild(rect);
            tips.Add(tip);
        }
        row.AddChild(badges);
        if (fit is { } f)
        {
            var score = Widgets.Label(ColonyText.Percent(f.Score), "Number", f.Score >= 0.6 ? Palette.Good : f.Score >= 0.35 ? Palette.Amber : Palette.TextMuted);
            score.CustomMinimumSize = new Vector2(40, 0);
            score.HorizontalAlignment = HorizontalAlignment.Right;
            score.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            row.AddChild(score);
            tips.Insert(0, f.Detail);
        }
        button.AddChild(row);
        if (_slots.Move is not null && s.Jobs.Count > 0) tips.Add("Click: add this job (their others stay). Shift-click: move them here (leave other jobs).");
        button.TooltipText = string.Join("\n", tips.Prepend($"{s.Name}: {Job(sim, s)}"));
        button.Pressed += () =>
        {
            if (sim.State.FindSurvivor(id) is not { } who) return;
            if (_slots.Pick(who, Input.IsKeyPressed(Key.Shift), out var message)) Hide();
            else
            {
                _error.Text = message;
                _error.Visible = true;
                ResetSize();
            }
        };
        return button;
    }

    private static string Job(ColonySim sim, Survivor s)
    {
        if (s.Away) return "Away on an expedition";
        if (s.PatientAtBuildingId is not null) return "Patient in the Med Bay";
        if (Remnants.Core.Schematics.Research.IsResearching(sim.State, s.Id)) return "Researching";
        if (Remnants.Core.Raids.Guards.PostOf(sim.State, s.Id) is { } post && sim.Raids?.Defense(post.DefId) is { } d) return $"Guard at the {d.Name}";
        return ColonyText.Job(sim, s);
    }

    private static List<(string Icon, Color Color, string Tip)> Badges(ColonySim sim, Survivor s)
    {
        var badges = new List<(string, Color, string)>();
        if (sim.IsWorsening(s)) badges.Add(("stat_injured", Palette.Danger, "Badly hurt and getting worse"));
        else if (s.IsInjured) badges.Add(("stat_injured", Palette.Warn, "Injured: works slower"));
        if (sim.IsTired(s)) badges.Add(("stat_tired", Palette.Warn, "Tired: works slower until they sleep"));
        if (s.IsChild) badges.Add(("job_baby", Palette.Amber, "A child: light work only, at reduced output"));
        if (s.PatientAtBuildingId is not null) badges.Add(("job_patient", Palette.Good, "In the Med Bay"));
        if (s.Away) badges.Add(("job_away", Palette.TextMuted, "Away on an expedition"));
        if (sim.ClassOf(s) is not null) badges.Add(("ui_study", Palette.Amber, "In class"));
        return badges;
    }
}
