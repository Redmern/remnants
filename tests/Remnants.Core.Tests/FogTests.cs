using System.Text.Json.Nodes;
using Remnants.Core.Items;
using Remnants.Core.Scavenge;
using Remnants.Core.Survivors;
using static Remnants.Core.Tests.ScavengeTestData;

namespace Remnants.Core.Tests;

/// <summary>
/// Fog of war beyond sight: stealth against detection, contacts, ambushes from the fog, and the defenses against them
/// (overwatch, brace, lookout, watching each other's backs, flares, motion sensors, scanners), plus how the Linked use it.
/// </summary>
public class FogTests
{
    private const string P1 = "player:1";

    /// <summary>A Linked boxed in at the end of a pylon corridor (it can shoot over pylons but not move), the survivor at (1, 4).</summary>
    private static readonly string[] Corridor =
    [
        "###########",
        "#.........#",
        "#.........#",
        "#.#########",
        "#..PPPPP.##",
        "#.#########",
        "#.........#",
        "#.........#",
        "###########",
    ];

    private static bool Did(ScavengeSim sim, Unit unit, ScavengeEventType type) =>
        sim.State.Events.Any(e => e.UnitId == unit.Id && e.Type == type);

    private static bool Shot(ScavengeSim sim, Unit unit, Unit target) =>
        sim.State.Events.Any(e => e.UnitId == unit.Id && e.TargetId == target.Id && e.Type is ScavengeEventType.Attacked or ScavengeEventType.Missed);

    [Fact]
    public void Stealth_and_detection_come_from_attributes_gear_and_body()
    {
        var data = Real;
        double Stealth(Unit u) => ProfileDeriver.Derive(data, u).Stealth;
        double Detect(Unit u) => ProfileDeriver.Derive(data, u).Detection;

        var plain = SurvivorUnit(body: "rag_coat");
        Assert.True(Stealth(SurvivorUnit(body: "camo_cloak")) > Stealth(plain) + 10);
        Assert.True(Stealth(SurvivorUnit(body: "riot_armor")) < Stealth(plain) - 15, "riot armor is heavy and loud");
        Assert.True(Stealth(SurvivorUnit(attributes: 80)) > Stealth(SurvivorUnit(attributes: 40)), "agility helps");
        Assert.True(Stealth(SurvivorUnit(body: "rag_coat", pack: ["scrap", "scrap", "scrap"])) < Stealth(plain), "loot is loud");

        var limping = SurvivorUnit(body: "rag_coat");
        limping.Body.Lose(BodyPart.LeftLeg);
        limping.Body.FitProsthetic(BodyPart.LeftLeg, 0.6);
        Assert.True(Stealth(limping) < Stealth(plain), "a peg leg is loud");

        Assert.True(Detect(SurvivorUnit(tool: "binoculars")) > Detect(plain));
        Assert.True(Detect(SurvivorUnit(tool: "scanner")) > Detect(SurvivorUnit(tool: "binoculars")));
        Assert.True(Detect(SurvivorUnit(attributes: 80)) > Detect(SurvivorUnit(attributes: 40)), "perception helps");
        Assert.True(Stealth(EnemyUnit("linked", default)) > Stealth(EnemyUnit("husk", default)) + 15, "the Linked hunt quietly");
    }

    [Fact]
    public void A_stealthy_unit_in_sight_is_only_seen_up_close_or_by_a_unit_on_lookout()
    {
        var u = SurvivorUnit(pos: new(0, 4), attributes: 50);
        var linked = EnemyUnit("linked", new(5, 4));
        var sim = Arena(12, 9, [u, linked]);
        var visible = sim.VisibleTo(1);

        Assert.Contains(sim.State.Map.Index(linked.Pos), visible); // the tile is in sight...
        Assert.False(Detection.SideSees(sim.Data, sim.State, P1, linked)); // ...but the Linked is not picked out
        Assert.True(Detection.SideSees(sim.Data, sim.State, Unit.EnemySide, u), "a bare survivor is easy to see");

        linked.Pos = new(1, 4);
        Assert.True(Detection.SideSees(sim.Data, sim.State, P1, linked), "close range always sees");
        linked.Pos = new(5, 4);

        Assert.True(sim.Execute(new Lookout(1, u.Id)).Ok);
        Assert.Equal(sim.Profile(u).MaxAp - sim.Data.Rules.Fog.Lookout.ApCost, u.Ap);
        Assert.True(Detection.SideSees(sim.Data, sim.State, P1, linked));
        Assert.Equal(ScavengeError.AlreadyOnLookout, sim.Execute(new Lookout(1, u.Id)).Error);
    }

    [Fact]
    public void A_strike_into_the_fog_gives_away_nothing_about_who_stands_there()
    {
        (ScavengeSim Sim, ScavengeResult Result) Strike(bool withLinked)
        {
            var u = SurvivorUnit(pos: new(0, 4), weapon: "pistol");
            var units = new List<Unit> { u };
            if (withLinked) units.Add(EnemyUnit("linked", new(5, 4)));
            else units.Add(EnemyUnit("linked", new(11, 0)));
            var sim = Arena(12, 9, units);
            Assert.All(units.Skip(1), l => Assert.False(Detection.SideSees(sim.Data, sim.State, P1, l)));
            return (sim, sim.Execute(new AttackTile(1, u.Id, new(5, 4), "shoot")));
        }
        var (sim, hidden) = Strike(withLinked: true);
        var (emptySim, empty) = Strike(withLinked: false);
        // A tile holding a hidden unit and an empty one answer alike: the strike is readied blind, aimed at nobody.
        Assert.True(hidden.Ok, hidden.Message);
        Assert.Equal((empty.Ok, empty.Error, empty.Message, empty.EntityId), (hidden.Ok, hidden.Error, hidden.Message, hidden.EntityId));
    }

    [Fact]
    public void The_log_never_names_or_places_a_unit_the_side_does_not_see()
    {
        bool Mine(int id) => id == 1;
        bool Known(int id) => id is 1 or 2; // 2 was seen before; 3 never
        bool SeenTile(Pos p) => true;
        var events = new List<ScavengeEvent>
        {
            // Our blast hits a hidden third unit: dropped, not "hit Ghost".
            new(1, ScavengeEventType.Attacked, "P1 hit Ghost's Torso", UnitId: 1, TargetId: 3, Pos: new(2, 2)),
            // Unit 2 was seen earlier but not when it moved: its move is not heard, even on a visible tile.
            new(1, ScavengeEventType.Moved, "Raider moved", UnitId: 2, Pos: new(4, 4), SeenBy: []),
            // ...but while seen it is.
            new(1, ScavengeEventType.Moved, "Raider moved", UnitId: 2, Pos: new(5, 4), SeenBy: [P1]),
            // Unseen when it shot us: anonymous.
            new(1, ScavengeEventType.Ambushed, "Raider ambushes P1", UnitId: 2, TargetId: 1, Pos: new(0, 0), Anonymous: "P1 was ambushed", SeenBy: []),
        };
        var heard = FogLog.Filter(events, P1, Mine, Known, SeenTile);
        Assert.Equal(["Raider moved", "P1 was ambushed"], heard.Select(e => e.Message));
        Assert.Equal(new Pos(5, 4), heard[0].Pos);
        Assert.Null(heard[1].UnitId);
        Assert.All(heard, e => Assert.Null(e.SeenBy));
    }

    [Fact]
    public void An_attack_from_the_fog_is_an_ambush_that_reveals_the_attacker_and_leaves_a_contact()
    {
        var u = SurvivorUnit(pos: new(1, 4), attributes: 20);
        var linked = EnemyUnit("linked", new(8, 4));
        u.Attributes[AttributeKind.Perception] = 10; // sight ~4.6, a muzzle flash ~6.9: the Linked at 7 stays unseen
        var sim = Arena(11, 9, [u, linked], Corridor);
        Assert.False(Detection.SideSees(sim.Data, sim.State, P1, linked));

        var burst = sim.Profile(linked).Action("burst")!;
        var plain = Combat.HitChance(sim.Data, sim.State.Map, linked.Pos, burst, u.Pos, sim.Profile(u));
        var ambush = Combat.ChanceToHit(sim, linked, burst, u);
        // No dodge against what you do not see coming (clumsiness still counts).
        Assert.Equal(plain + sim.Data.Rules.Fog.Ambush.HitBonus + Math.Min(0, sim.Profile(u).Dodge) - sim.Profile(u).Dodge, ambush, 6);

        var before = sim.State.Events.Count;
        // The Linked telegraphs from the fog; the shot comes down once the slow survivor has had a turn.
        for (var i = 0; i < 3 && !sim.State.Events.Skip(before).Any(e => e.Type == ScavengeEventType.Ambushed); i++)
            Assert.True(sim.Execute(new EndTurn(1, u.Id)).Ok);
        var fresh = sim.State.Events.Skip(before).ToList();

        Assert.Contains(fresh, e => e.Type == ScavengeEventType.Ambushed && e.UnitId == linked.Id && e.TargetId == u.Id);
        Assert.True(Detection.IsRevealed(sim.State, linked.Id, P1) || sim.State.Round > 2);
        var contact = sim.State.Contacts.Last(c => c.Side == P1 && c.Kind == ContactKind.Attack);
        Assert.Equal(u.Pos, contact.Toward);
        Assert.True(contact.Pos.Chebyshev(linked.Pos) <= sim.Data.Rules.Fog.ContactFuzz);

        // The survivor's side hears what happened to them, never who did it: the Linked is out of their sight.
        var heard = FogLog.For(sim, P1, fresh);
        Assert.Contains(heard, e => e.Type == ScavengeEventType.Ambushed && e.UnitId is null && e.Message.Contains("ambushed from the fog"));
        Assert.DoesNotContain(heard, e => e.Message.Contains("Linked") || e.UnitId == linked.Id);
        Assert.All(heard, e => Assert.Null(e.Anonymous));
    }

    [Fact]
    public void An_ally_next_to_the_target_halves_the_ambush_bonus()
    {
        var u = SurvivorUnit(pos: new(1, 4), attributes: 20);
        var buddy = SurvivorUnit(pos: new(1, 5), attributes: 20);
        var linked = EnemyUnit("linked", new(8, 4));
        var sim = Arena(11, 9, [u, buddy, linked], Corridor);
        var rules = sim.Data.Rules.Fog.Ambush;

        var covered = Combat.AmbushAgainst(sim.Data, sim.State, linked, u)!;
        Assert.True(covered.Backs);
        Assert.Equal(rules.HitBonus * (1 - rules.BacksReduction), covered.HitBonus, 6);

        buddy.Pos = new(1, 7);
        var alone = Combat.AmbushAgainst(sim.Data, sim.State, linked, u)!;
        Assert.Equal(rules.HitBonus, alone.HitBonus, 6);
        Assert.Equal(rules.DamageMultiplier, alone.DamageMultiplier, 6);
    }

    [Fact]
    public void A_flare_lights_up_everyone_in_its_radius_for_a_few_rounds()
    {
        var u = SurvivorUnit(pos: new(0, 4), pack: ["flare"]);
        var linked = EnemyUnit("linked", new(5, 4));
        var sim = Arena(12, 9, [u, linked]);
        Assert.False(Detection.SideSees(sim.Data, sim.State, P1, linked));

        Assert.Equal(ScavengeError.InvalidTile, sim.Execute(new UseItem(1, u.Id, "flare")).Error);
        Assert.Equal(ScavengeError.TooFar, sim.Execute(new UseItem(1, u.Id, "flare", At: new(9, 4))).Error);
        Assert.True(sim.Execute(new UseItem(1, u.Id, "flare", At: new(5, 5))).Ok);

        Assert.DoesNotContain("flare", u.Pack);
        Assert.True(Detection.SideSees(sim.Data, sim.State, P1, linked));
        Assert.True(Detection.SideSees(sim.Data, sim.State, Unit.EnemySide, linked));
        Assert.Contains(sim.State.Map.Index(new Pos(8, 5)), sim.VisibleTo(1)); // lit, though beyond plain sight

        var rounds = sim.Data.Items["flare"].Use!.LightRounds;
        sim.State.Round += rounds;
        Assert.False(Detection.SideSees(sim.Data, sim.State, P1, linked), "the flare burned out");
    }

    [Fact]
    public void A_motion_sensor_reveals_and_alerts_only_its_own_side()
    {
        var a = SurvivorUnit(1, new(0, 0), pack: ["motion_sensor"]);
        var b = SurvivorUnit(2, new(9, 0), body: "camo_cloak");
        var sim = Arena(12, 5, [a, b]);

        Assert.True(sim.Execute(new UseItem(1, a.Id, "motion_sensor", At: new(1, 0))).Ok);
        Assert.True(sim.Execute(new EndTurn(1, a.Id)).Ok);
        b.Mp = 9;
        Assert.True(Now(sim, new Move(2, b.Id, new(7, 0))).Ok);
        Assert.False(Detection.SideSees(sim.Data, sim.State, P1, b), "a camo cloak hides it out here");
        Assert.True(Now(sim, new Move(2, b.Id, new(5, 0))).Ok);
        Assert.False(Detection.IsRevealed(sim.State, b.Id, P1));
        Assert.True(Now(sim, new Move(2, b.Id, new(4, 0))).Ok);

        Assert.True(Detection.IsRevealed(sim.State, b.Id, P1));
        Assert.True(Detection.SideSees(sim.Data, sim.State, P1, b));
        Assert.Contains(sim.State.Contacts, c => c.Side == P1 && c.Kind == ContactKind.Sensor);
        var alert = Assert.Single(sim.State.Events, e => e.Type == ScavengeEventType.SensorTripped);
        Assert.Equal(P1, alert.Side);
        Assert.DoesNotContain(FogLog.For(sim, "player:2", sim.State.Events), e => e.Type is ScavengeEventType.SensorTripped or ScavengeEventType.SensorPlaced);
        Assert.Contains(FogLog.For(sim, P1, sim.State.Events), e => e.Type == ScavengeEventType.SensorTripped);
    }

    [Fact]
    public void A_scanner_sees_hostiles_through_walls_and_stealth()
    {
        var u = SurvivorUnit(pos: new(1, 1), tool: "scanner");
        var linked = EnemyUnit("linked", new(5, 1));
        var sim = Arena(8, 3, [u, linked], [
            "........",
            "...#....",
            "........",
        ]);
        Assert.True(Detection.SideSees(sim.Data, sim.State, P1, linked));

        u.Equipment.Remove(EquipSlot.Tool);
        Assert.False(Detection.SideSees(sim.Data, sim.State, P1, linked));
    }

    [Fact]
    public void Gunfire_leaves_a_noise_contact_for_every_side_in_earshot_that_did_not_see_the_shooter()
    {
        var listener = SurvivorUnit(1, new(0, 0));
        var shooter = SurvivorUnit(2, new(5, 5), weapon: "pistol", body: "camo_cloak");
        var husk = EnemyUnit("husk", new(8, 5));
        var sim = Arena(12, 9, [shooter, listener, husk]);
        Assert.False(Detection.SideSees(sim.Data, sim.State, P1, shooter));

        Assert.True(Blow(sim, shooter, husk, "shoot").Ok);

        var heard = Assert.Single(sim.State.Contacts, c => c.Side == P1);
        Assert.Equal(ContactKind.Noise, heard.Kind);
        Assert.Null(heard.Toward);
        Assert.True(Detection.IsRevealed(sim.State, shooter.Id, Unit.EnemySide));
        Assert.False(Detection.IsRevealed(sim.State, shooter.Id, P1));
    }

    [Fact]
    public void Contacts_fade_after_a_few_rounds()
    {
        var u = SurvivorUnit(pos: new(0, 0));
        var sim = Arena(8, 3, [u]);
        sim.State.Contacts.Add(new Contact(P1, new(5, 1), ContactKind.Noise, sim.State.Round));
        for (var i = 0; i < sim.Data.Rules.Fog.ContactRounds; i++) Assert.True(sim.Execute(new EndTurn(1, u.Id)).Ok);
        Assert.Empty(sim.State.Contacts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_drone_spots_for_a_linked_that_strikes_from_the_fog_unless_the_drone_is_jammed(bool jammer)
    {
        var u = SurvivorUnit(pos: new(0, 4), body: "camo_cloak", tool: jammer ? "signal_jammer" : null);
        var linked = EnemyUnit("linked", new(8, 4));
        var drone = EnemyUnit("linked_drone", new(3, 0));
        var sim = Arena(12, 9, [u, linked, drone]);
        Assert.False(Detection.Spots(sim.Data, sim.State, linked, u), "the Linked cannot pick out the cloaked survivor itself");
        Assert.True(Detection.Spots(sim.Data, sim.State, drone, u));

        ScavengeAi.TakeTurn(sim, linked);
        Assert.Equal(!jammer, Strikes(sim).Any(s => s.UnitId == linked.Id && s.Tile == u.Pos)); // spotted through the drone: the shot is telegraphed
        Assert.True(sim.Execute(new EndTurn(1, u.Id)).Ok);

        if (jammer) Assert.False(Shot(sim, linked, u));
        else Assert.Contains(sim.State.Events, e => e.Type == ScavengeEventType.Ambushed && e.UnitId == linked.Id && e.TargetId == u.Id);
    }

    [Fact]
    public void Same_seed_and_commands_give_the_same_fog_game()
    {
        string Run()
        {
            var sim = NewScavenge.Create(Real, "suburb_ruins", 11, [Party(1)]);
            sim.Start();
            PlayBot(sim, 400);
            return ScavengeSave.Save(sim.State);
        }
        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void Fog_state_survives_a_save_and_old_saves_load_without_it()
    {
        var u = SurvivorUnit(pos: new(0, 4), pack: ["flare", "motion_sensor"]);
        var linked = EnemyUnit("linked", new(5, 4));
        var sim = Arena(12, 9, [u, linked]);
        u.Ap = 3;
        Assert.True(Now(sim, new UseItem(1, u.Id, "flare", At: new(5, 5))).Ok);
        Assert.True(Now(sim, new UseItem(1, u.Id, "motion_sensor", At: new(1, 4))).Ok);
        Assert.True(sim.Execute(new Lookout(1, u.Id)).Ok);
        sim.State.Contacts.Add(new Contact(P1, new(6, 6), ContactKind.Attack, sim.State.Round, u.Pos));

        var json = ScavengeSave.Save(sim.State);
        var loaded = ScavengeSave.Load(Real, json);
        Assert.Equal(json, ScavengeSave.Save(loaded.State));
        Assert.Single(loaded.State.Sensors);
        Assert.Single(loaded.State.Flares);
        Assert.True(loaded.State.FindUnit(u.Id)!.Lookout);

        var old = JsonNode.Parse(json)!;
        old["version"] = 2;
        var scavenge = old["scavenge"]!.AsObject();
        foreach (var key in new[] { "contacts", "reveals", "flares", "sensors", "nextSensorId" }) scavenge.Remove(key);
        foreach (var unit in scavenge["units"]!.AsArray()) foreach (var key in new[] { "stance", "reactions", "lookout" }) unit!.AsObject().Remove(key);
        Assert.Contains("Unsupported scavenge save version", Assert.Throws<System.Text.Json.JsonException>(() => ScavengeSave.Load(Real, old.ToJsonString())).Message); // old runs reset
    }
}
