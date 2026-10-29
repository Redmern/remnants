using System.Linq;
using System.Text;
using Godot;
using Remnants.Core.Scavenge;
using Remnants.Core.Survivors;
using UiKit = Remnants.Game.Colony.Ui.Ui;

namespace Remnants.Game.Scavenge.Ui;

/// <summary>The selected unit: HP bar, AP, status and bleed-out, hurt body parts, movement modes, attacks, gear and load.</summary>
public partial class UnitPanel : PanelContainer
{
    private readonly ScavengePresenter _presenter;
    private readonly Label _title = UiKit.Label(size: 17);
    private readonly Label _kind = UiKit.Label("", 13, UiKit.Muted);
    private readonly ProgressBar _hp = new() { ShowPercentage = false, CustomMinimumSize = new Vector2(0, 8), MaxValue = 1 };
    private readonly Label _vitals = UiKit.Label(size: 14);
    private readonly StyleBoxFlat _hpFill = HudTheme.Box(UiKit.Good, radius: 2, padX: 0, padY: 0);
    private readonly RichTextLabel _details = UiKit.Rich();

    public UnitPanel(ScavengePresenter presenter)
    {
        _presenter = presenter;
        CustomMinimumSize = new Vector2(300, 0);
        SizeFlagsVertical = SizeFlags.ShrinkBegin;
        MouseFilter = MouseFilterEnum.Stop;
        var box = UiKit.Panel(this, 4);
        var head = new HBoxContainer();
        head.AddChild(_title);
        _kind.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _kind.HorizontalAlignment = HorizontalAlignment.Right;
        head.AddChild(_kind);
        box.AddChild(head);
        _hp.AddThemeStyleboxOverride("fill", _hpFill);
        box.AddChild(_hp);
        box.AddChild(_vitals);
        box.AddChild(new HSeparator());
        box.AddChild(_details);
    }

    public void Present(Unit? unit)
    {
        var known = unit is not null && (_presenter.CanSee(unit) || _presenter.IsMine(unit));
        _kind.Visible = _hp.Visible = _vitals.Visible = known;
        if (!known)
        {
            _title.Text = "No unit selected";
            _details.Text = "[color=#aaa]Click a unit. Your units take orders on their turn.[/color]";
            return;
        }

        var data = _presenter.Sim.Data;
        var profile = _presenter.Sim.Profile(unit!);
        _title.Text = unit!.Name;
        _title.AddThemeColorOverride("font_color", SidePalette.For(_presenter, unit).Lightened(0.2f));
        _kind.Text = ScavengeText.Kind(unit);
        var bad = UiKit.Hex(UiKit.Bad);

        var health = unit.MaxHp > 0 ? Mathf.Clamp(unit.Hp / unit.MaxHp, 0, 1) : 0;
        _hp.Value = health;
        _hpFill.BgColor = health < 0.35 ? UiKit.Bad : health < 0.7 ? new Color(0.88f, 0.75f, 0.38f) : UiKit.Good;
        var pips = new string('●', unit.Ap) + new string('○', System.Math.Max(0, profile.MaxAp - unit.Ap));
        _vitals.Text = $"HP {unit.Hp:0}/{unit.MaxHp:0}    AP {pips}    {ScavengeText.Status(unit)}";
        _vitals.AddThemeColorOverride("font_color", unit.Status switch
        {
            UnitStatus.Active => HudTheme.Text,
            UnitStatus.Extracted or UnitStatus.Evacuated => UiKit.Good,
            _ => UiKit.Bad,
        });

        var b = new StringBuilder();
        var hurt = ScavengeText.BodyLines(unit.Body).Where(l => l.Condition != PartCondition.Healthy).ToList();
        b.Append("[color=#aaa]BODY[/color]  ");
        if (hurt.Count == 0) b.Append("[color=#bbbbbb]unhurt[/color]\n");
        else
        {
            b.Append('\n');
            foreach (var (text, condition) in hurt)
                b.Append($"[color=#{(condition == PartCondition.Prosthetic ? "70d8e8" : bad)}]{text}[/color]\n");
        }

        b.Append("[color=#aaa]MOVE[/color]  ").Append(ScavengeText.Movement(profile.Movement).Replace("\n", "\n      ")).Append('\n');

        b.Append("[color=#aaa]ATTACKS[/color]\n");
        foreach (var a in profile.Actions)
            b.Append($"{a.Name} [color=#aaa]{a.Pattern} {a.Reach} · {a.MinDamage:0}–{a.MaxDamage:0} dmg · {a.ApCost} AP · {a.Accuracy * 100:0}%[/color]\n");

        b.Append($"[color=#aaa]Init[/color] {profile.Initiative:0}  [color=#aaa]Sight[/color] {profile.Sight:0.#}  ");
        b.Append($"[color=#aaa]Armor[/color] {profile.Armor:0.#}  [color=#aaa]Dodge[/color] {profile.Dodge * 100:0}%\n");

        b.Append("[color=#aaa]GEAR[/color]\n");
        foreach (var (slot, id) in unit.Equipment.OrderBy(kv => kv.Key))
            b.Append($"[color=#aaa]{slot}[/color] {ScavengeText.Item(data, id)}\n");
        b.Append($"[color=#aaa]Pack[/color] {ScavengeText.Items(data, unit.Pack)}\n");
        var loadColor = profile.Load > profile.CarryCapacity * 0.85 ? bad : "bbbbbb";
        b.Append($"[color=#aaa]Load[/color] [color=#{loadColor}]{profile.Load:0.#}/{profile.CarryCapacity:0.#} kg[/color]");
        _details.Text = b.ToString();
    }
}
