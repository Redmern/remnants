using Remnants.Core.Scavenge;
using static Remnants.Core.Tests.ScavengeTestData;

namespace Remnants.Core.Tests;

/// <summary>The Defend action: an Action-slot plan step, armed like a block card, its pattern and share from the unit's gear.</summary>
public class DefendTests
{
    private static readonly ScavengeData Sure = With(r => r with
    {
        Combat = r.Combat with { MinHitChance = 1, MaxHitChance = 1, Tiers = r.Combat.Tiers with { GrazeBelow = 0, SolidFrom = 1 } },
    });

    private static readonly Pos East = new(1, 0);
    private static readonly Pos West = new(-1, 0);

    private static (ScavengeSim Sim, Unit A, Unit B) Pair(ScavengeData? data = null, string? weaponA = "pipe", string? weaponB = "pipe")
    {
        var a = SurvivorUnit(1, new(3, 3), weapon: weaponA);
        var b = SurvivorUnit(2, new(4, 3), weapon: weaponB);
        return (Arena(12, 7, [a, b], data: data ?? Sure), a, b);
    }

    [Fact]
    public void Defend_needs_no_card_takes_the_action_slot_and_is_hidden_and_refunded()
    {
        var (sim, a, _) = Pair();
        var (ap, stamina, events) = (a.Ap, Economy.Stamina(sim, a), sim.State.Events.Count);
        Assert.True(sim.Execute(new Defend(1, a.Id, East)).Ok);
        Assert.Equal(PlanSlot.Action, Plans.SlotOf(Plans.Of(sim.State, a)!.Steps.Single()));
        Assert.Equal(ap - sim.Data.Rules.Combat.Defend.ApCost, a.Ap);
        Assert.True(Economy.Stamina(sim, a) < stamina);
        Assert.Equal(events, sim.State.Events.Count);
        Assert.Equal(ScavengeError.SlotFull, sim.Execute(new Recover(1, a.Id)).Error);
        Assert.True(sim.Execute(new UndoPlan(1, a.Id, Slot: PlanSlot.Action)).Ok);
        Assert.Equal((ap, stamina), (a.Ap, Economy.Stamina(sim, a)));
    }

    [Fact]
    public void Defend_is_refused_without_ap_or_a_direction()
    {
        var (sim, a, _) = Pair();
        Assert.Equal(ScavengeError.InvalidTile, sim.Execute(new Defend(1, a.Id, default)).Error);
        a.Ap = 0;
        Assert.Equal(ScavengeError.NotEnoughAp, sim.Execute(new Defend(1, a.Id, East)).Error);
    }

    [Fact]
    public void Defend_changes_a_full_action_slot()
    {
        var (sim, a, _) = Pair();
        Assert.True(sim.Execute(new Recover(1, a.Id)).Ok);
        var defend = new Defend(1, a.Id, East);
        Assert.Equal(PlanSlot.Action, Plans.Replaces(sim, defend));
        Assert.True(sim.Execute(new ChangePlan(1, a.Id, defend)).Ok);
        Assert.Equal("Defend", TacticsView.Slots(sim, a)[1].Text);
    }

    [Fact]
    public void Unarmed_guards_its_own_tile_and_one_in_front_with_the_rules_share()
    {
        var (sim, a, _) = Pair(weaponA: null);
        var guard = Defences.GuardOf(sim, a);
        Assert.Equal(sim.Data.Rules.Combat.Defend.Block, guard.Block);
        Assert.Equal([a.Pos, a.Pos + East], TacticsView.DefendAim(sim, a, East).Tiles);
    }

    [Fact]
    public void The_pattern_and_share_come_from_the_weapons_data()
    {
        var (sim, a, _) = Pair(weaponA: "machete");
        var def = sim.Data.Items["machete"].Defend!;
        Assert.Equal(def.Block, Defences.GuardOf(sim, a).Block);
        Assert.Equal(new[] { a.Pos, a.Pos + East, a.Pos + new Pos(1, -1), a.Pos + new Pos(1, 1) }.ToHashSet(), TacticsView.DefendAim(sim, a, East).Tiles.ToHashSet());
        var spear = Pair(weaponA: "spear");
        Assert.Equal([spear.A.Pos, spear.A.Pos + East, spear.A.Pos + new Pos(2, 0)], TacticsView.DefendAim(spear.Sim, spear.A, East).Tiles);
    }

    [Fact]
    public void A_ranged_weapon_guards_like_bare_hands()
    {
        var (sim, a, _) = Pair(weaponA: "pistol");
        Assert.Equal(sim.Data.Rules.Combat.Defend.Block, Defences.GuardOf(sim, a).Block);
    }

    [Fact]
    public void Every_block_card_is_stronger_than_any_defend()
    {
        var data = Real;
        var best = data.Items.Values.Select(i => i.Defend?.Block ?? 0).Append(data.Rules.Combat.Defend.Block).Max();
        foreach (var id in new[] { "parry", "shield_wall", "hold_the_gap" })
            Assert.True(data.Cards.Find(id)!.Effect.Block > best, id);
    }

    [Fact]
    public void A_defended_blow_does_less_stuns_by_the_stun_rules_and_fills_adrenaline()
    {
        var stunning = With(r => r with
        {
            Combat = r.Combat with { MinHitChance = 1, MaxHitChance = 1, Tiers = r.Combat.Tiers with { GrazeBelow = 0, SolidFrom = 1 } },
            Stun = r.Stun with { Min = 1, Max = 1 },
        });
        var (sim, a, b) = Pair(stunning);
        b.PriorityBonus = -20;
        Assert.True(sim.Execute(new Defend(1, a.Id, East)).Ok);
        Assert.True(sim.Execute(new AttackTile(2, b.Id, a.Pos, "swing")).Ok);
        Settle(sim);
        Assert.Contains(sim.State.Events, e => e.Type == ScavengeEventType.Blocked && e.UnitId == a.Id);
        Assert.Contains(sim.State.Events, e => e.Type == ScavengeEventType.Stunned && e.TargetId == b.Id);
        Assert.True(a.Adrenaline >= sim.Data.Rules.Adrenaline.GainBlock);
        Assert.True(Stuns.Active(sim.State, b));
    }

    [Fact]
    public void Defend_takes_its_share_off_the_blow()
    {
        double Taken(bool defend)
        {
            var (sim, a, b) = Pair();
            sim.State.RngState = 12345;
            if (defend) Assert.True(sim.Execute(new Defend(1, a.Id, East)).Ok);
            Assert.True(sim.Execute(new AttackTile(2, b.Id, a.Pos, "swing")).Ok);
            Settle(sim);
            return a.MaxHp - a.Hp;
        }
        Assert.True(Taken(true) < Taken(false));
    }

    [Fact]
    public void The_guard_holds_where_the_unit_really_ended_when_its_walk_stopped_short()
    {
        var (sim, a, b) = Pair();
        var wall = SurvivorUnit(2, new(1, 3), weapon: "pipe");
        sim.State.Units.Add(wall);
        wall.Id = 99;
        (wall.Ap, wall.Mp) = (1, 4);
        a.PriorityBonus = -20;
        wall.PriorityBonus = 20;
        Assert.True(sim.Execute(new Move(1, a.Id, new Pos(1, 2))).Ok);
        Plans.Of(sim.State, a)!.Steps[0].Path = [new Pos(2, 3), new Pos(1, 3)];
        Plans.Of(sim.State, a)!.Steps[0].To = new Pos(1, 3);
        Assert.True(sim.Execute(new Defend(1, a.Id, East)).Ok);
        Assert.True(sim.Execute(new AttackTile(2, wall.Id, new Pos(2, 3), "swing")).Ok);
        Settle(sim);
        Assert.Equal(new Pos(2, 3), a.Pos);
        Assert.Contains(sim.State.Events, e => e.Type == ScavengeEventType.Blocked && e.UnitId == a.Id);
    }

    [Fact]
    public void The_plan_preview_shows_the_guard_tiles_and_warns_of_no_ally_on_them()
    {
        var (sim, a, _) = Pair();
        var friend = SurvivorUnit(1, a.Pos + new Pos(0, 1));
        sim.State.Units.Add(friend);
        Assert.True(sim.Execute(new Defend(1, a.Id, new Pos(0, 1))).Ok);
        var preview = TacticsView.PlanPreview(sim, a);
        Assert.Contains(friend.Pos, preview.ActionTiles);
        Assert.Empty(preview.AllyTiles);
    }

    [Fact]
    public void The_resolve_playback_flashes_the_guard_pattern()
    {
        var (sim, a, _) = Pair();
        Assert.True(sim.Execute(new Defend(1, a.Id, East)).Ok);
        Settle(sim);
        var flash = sim.State.Events.Single(e => e.Type == ScavengeEventType.PatternResolved && e.UnitId == a.Id);
        Assert.Contains(a.Pos + East, flash.Tiles!);
        Assert.True(flash.Landed);
    }

    [Fact]
    public void Moving_the_plan_brings_the_guard_along()
    {
        var (sim, a, _) = Pair();
        Assert.True(sim.Execute(new Defend(1, a.Id, West)).Ok);
        Assert.True(sim.Execute(new Move(1, a.Id, new Pos(3, 2))).Ok);
        Assert.Contains(new Pos(3, 2), TacticsView.PlanPreview(sim, a).ActionTiles);
    }

    [Fact]
    public void A_disrupted_defend_leaves_its_tiles_open()
    {
        var (sim, a, b) = Pair();
        sim.State.Decks.Add(new SquadDeck { PlayerId = 2, Hand = ["spoil"], Points = 9, Budget = 9, HandSize = 9 });
        var striker = SurvivorUnit(2, new(3, 4), weapon: "pipe");
        sim.State.Units.Add(striker);
        striker.Id = 99;
        (striker.Ap, striker.Mp) = (1, 4);
        (b.PriorityBonus, striker.PriorityBonus) = (50, -50);
        Assert.True(sim.Execute(new Defend(1, a.Id, East)).Ok);
        Assert.True(sim.Execute(new PlayCard(2, b.Id, "spoil", a.Id)).Ok);
        Assert.True(sim.Execute(new AttackTile(2, striker.Id, a.Pos, "swing")).Ok);
        Settle(sim);
        Assert.True(a.Hp < a.MaxHp);
        Assert.DoesNotContain(sim.State.Events, e => e.Type == ScavengeEventType.Blocked);
    }

    [Fact]
    public void Same_plans_and_seed_give_the_same_events()
    {
        List<string> Run()
        {
            var (sim, a, b) = Pair(Real);
            sim.State.RngState = 777;
            Assert.True(sim.Execute(new Defend(1, a.Id, East)).Ok);
            Assert.True(sim.Execute(new AttackTile(2, b.Id, a.Pos, "swing")).Ok);
            Settle(sim);
            return sim.State.Events.Select(e => e.Message).ToList();
        }
        Assert.Equal(Run(), Run());
    }
}
