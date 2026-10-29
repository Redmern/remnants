using System.Linq;
using System.Text;
using Godot;
using Remnants.Core.Scavenge;
using Remnants.Core.Survivors;
using UiKit = Remnants.Game.Colony.Ui.Ui;

namespace Remnants.Game.Scavenge.Ui;

/// <summary>The selected unit: HP, status and bleed-out, body parts, movement modes, gear and load.</summary>
public partial class UnitPanel : PanelContainer
{
    private readonly ScavengePresenter _presenter;
    private readonly Label _title = UiKit.Label(size: 17);
    private readonly RichTextLabel _details = UiKit.Rich(scroll: true);

    public UnitPanel(ScavengePresenter presenter)
    {
        _presenter = presenter;
        CustomMinimumSize = new Vector2(290, 0);
        var box = UiKit.Panel(this);
        box.AddChild(_title);
        box.AddChild(_details);
    }

    public void Present(Unit? unit)
    {
        if (unit is null || !(_presenter.CanSee(unit) || _presenter.IsMine(unit)))
        {
            _title.Text = "No unit selected";
            _details.Text = "[color=#aaa]Click a unit. Your units take orders on their turn.[/color]";
            return;
        }

        var data = _presenter.Sim.Data;
        var profile = _presenter.Sim.Profile(unit);
        _title.Text = unit.Name;
        var bad = UiKit.Hex(UiKit.Bad);
        var good = UiKit.Hex(UiKit.Good);
        var b = new StringBuilder();
        b.Append($"[color=#aaa]{ScavengeText.Kind(unit)}[/color]\n");

        var hpColor = unit.Hp < unit.MaxHp * 0.35 ? bad : unit.Hp < unit.MaxHp * 0.7 ? "e0c060" : good;
        b.Append($"HP [color=#{hpColor}]{unit.Hp:0}/{unit.MaxHp:0}[/color]   AP {unit.Ap}/{profile.MaxAp}\n");
        var status = ScavengeText.Status(unit);
        var statusColor = unit.Status switch
        {
            UnitStatus.Active => "dddddd",
            UnitStatus.Extracted or UnitStatus.Evacuated => good,
            _ => bad,
        };
        b.Append($"[color=#{statusColor}][b]{status}[/b][/color]\n");

        b.Append("\n[color=#aaa]BODY[/color]\n");
        foreach (var (text, condition) in ScavengeText.BodyLines(unit.Body))
        {
            var color = condition switch
            {
                PartCondition.Healthy => "bbbbbb",
                PartCondition.Prosthetic => "70d8e8",
                _ => bad,
            };
            b.Append($"[color=#{color}]{text}[/color]\n");
        }

        b.Append("\n[color=#aaa]MOVEMENT[/color]\n");
        b.Append(ScavengeText.Movement(profile.Movement)).Append('\n');

        b.Append("\n[color=#aaa]ATTACKS[/color]\n");
        foreach (var a in profile.Actions)
            b.Append($"{a.Name}: {a.Pattern} {a.Reach}, {a.MinDamage:0}–{a.MaxDamage:0} dmg, {a.ApCost} AP, {a.Accuracy:P0}\n");

        b.Append($"\nInitiative {profile.Initiative:0}   Sight {profile.Sight:0.#}\n");
        b.Append($"Armor {profile.Armor:0.#}   Dodge {profile.Dodge:P0}\n");

        b.Append("\n[color=#aaa]GEAR[/color]\n");
        foreach (var (slot, id) in unit.Equipment.OrderBy(kv => kv.Key))
            b.Append($"{slot}: {ScavengeText.Item(data, id)}\n");
        b.Append($"Pack: {ScavengeText.Items(data, unit.Pack)}\n");
        var loadColor = profile.Load > profile.CarryCapacity * 0.85 ? bad : "bbbbbb";
        b.Append($"Load [color=#{loadColor}]{profile.Load:0.#}/{profile.CarryCapacity:0.#} kg[/color]\n");
        _details.Text = b.ToString();
    }
}
