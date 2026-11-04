using System.Text.Json.Nodes;
using Remnants.Core.Colony;
using Remnants.Core.Help;
using Remnants.Core.Scavenge;
using Remnants.Core.Serialization;
using Remnants.Core.Survivors;
using static Remnants.Core.Tests.ScavengeTestData;

namespace Remnants.Core.Tests;

/// <summary>The bestiary: per enemy type, how often the colony's parties saw, fought and killed it, and what that reveals.</summary>
public class BestiaryTests
{
    private static BestiaryRules Rules => Real.Rules.Bestiary;

    [Fact]
    public void Tiers_follow_the_thresholds_in_the_data()
    {
        Assert.True(Rules.Seen >= 1 && Rules.Fought >= 1 && Rules.Killed >= 1);
        Assert.Equal(BestiaryTier.Unknown, Bestiary.TierOf(Rules, new BestiaryEntry()));
        Assert.Equal(BestiaryTier.Seen, Bestiary.TierOf(Rules, new BestiaryEntry { Seen = Rules.Seen }));
        Assert.Equal(BestiaryTier.Fought, Bestiary.TierOf(Rules, new BestiaryEntry { Seen = Rules.Seen, Fought = Rules.Fought }));
        Assert.Equal(BestiaryTier.Killed, Bestiary.TierOf(Rules, new BestiaryEntry { Seen = 1, Fought = 1, Killed = Rules.Killed }));
        // A kill alone (from the fog, never seen properly) still tells all.
        Assert.Equal(BestiaryTier.Killed, Bestiary.TierOf(Rules, new BestiaryEntry { Killed = Rules.Killed }));
    }

    [Fact]
    public void What_each_tier_reveals()
    {
        var unknown = Bestiary.Describe(Real, "linked", BestiaryTier.Unknown);
        Assert.Equal("Linked", unknown.Name);
        Assert.Null(unknown.Weapons);
        var seen = Bestiary.Describe(Real, "linked", BestiaryTier.Seen);
        Assert.NotEmpty(seen.Weapons!);
        Assert.True(seen.Reach > 0);
        Assert.Null(seen.Ap);
        var fought = Bestiary.Describe(Real, "linked", BestiaryTier.Fought);
        Assert.NotNull(fought.Ap);
        Assert.NotNull(fought.Movement);
        Assert.Null(fought.Hp);
        var killed = Bestiary.Describe(Real, "linked", BestiaryTier.Killed);
        Assert.Equal(Real.Enemies["linked"].Hp, killed.Hp);
        Assert.NotNull(killed.Armor);
        Assert.False(string.IsNullOrEmpty(killed.Behaviour));
    }

    [Fact]
    public void A_party_counts_each_enemy_it_sees_fights_and_kills_once()
    {
        var a = SurvivorUnit(1, new(0, 1), weapon: "pipe");
        var h1 = EnemyUnit("husk", new(2, 1));
        var h2 = EnemyUnit("husk", new(3, 2));
        var far = EnemyUnit("linked", new(14, 1)); // out of sight
        var rival = SurvivorUnit(2, new(1, 2));   // another player: never in the bestiary
        var sim = Arena(16, 3, [a, h1, h2, far, rival]);
        Bestiary.Observe(sim);
        Bestiary.Observe(sim);
        var counts = sim.State.FindParty(1)!.Bestiary;
        Assert.Equal(2, counts["husk"].Seen);
        Assert.False(counts.ContainsKey("linked"));
        Assert.Single(counts);

        a.Pos = new(1, 1);
        a.Ap = 2;
        Assert.True(Blow(sim, a, h1, "swing").Ok);
        Assert.True(Blow(sim, a, h1, "swing").Ok);
        Assert.Equal(1, counts["husk"].Fought);
        Combat.ApplyDamage(sim, h1, BodyPart.Torso, 999, attacker: a);
        Assert.Equal(1, counts["husk"].Killed);
        Combat.ApplyDamage(sim, h2, BodyPart.Torso, 999); // nobody of the party killed it
        Assert.Equal(1, counts["husk"].Killed);
    }

    [Fact]
    public void Being_attacked_by_an_enemy_counts_as_fighting_it()
    {
        var a = SurvivorUnit(1, new(0, 0));
        var husk = EnemyUnit("husk", new(1, 0));
        var last = SurvivorUnit(1, new(3, 2));
        var sim = Arena(4, 3, [a, husk, last]);
        int Fought() => sim.State.FindParty(1)!.Bestiary.TryGetValue("husk", out var e) ? e.Fought : 0;
        // The husk is in reach: it plans the blow (not a fight yet), which comes down when the round resolves.
        sim.Start();
        Assert.True(TileStrikes.Readied(sim.State, husk));
        Assert.Equal(0, Fought());
        Assert.True(sim.Execute(new EndTurn(1, a.Id)).Ok);
        Assert.True(sim.Execute(new EndTurn(1, last.Id)).Ok);
        Assert.Contains(sim.State.Events, e => e.UnitId == husk.Id && e.TargetId == a.Id && e.Type is ScavengeEventType.Attacked or ScavengeEventType.Missed);
        Assert.Equal(1, sim.State.FindParty(1)!.Bestiary["husk"].Fought);
    }

    [Fact]
    public void What_a_party_learned_comes_home_only_if_someone_got_out_and_adds_up()
    {
        var a = SurvivorUnit(1, new(0, 0));
        var sim = Arena(4, 3, [a, EnemyUnit("husk", new(2, 0))]);
        sim.State.FindParty(1)!.Bestiary["husk"] = new BestiaryEntry { Seen = 2, Fought = 1, Killed = 1 };
        a.Status = UnitStatus.Extracted;
        sim.UpdateParties();
        var result = sim.ResultFor(1);
        Assert.Equal(2, result.Bestiary["husk"].Seen);

        var colony = new ColonyState();
        colony.Bestiary["husk"] = new BestiaryEntry { Seen = 3 };
        Bestiary.Merge(colony, result.Bestiary);
        Assert.Equal((5, 1, 1), (colony.Bestiary["husk"].Seen, colony.Bestiary["husk"].Fought, colony.Bestiary["husk"].Killed));

        var b = SurvivorUnit(1, new(0, 0));
        var wiped = Arena(4, 3, [b, EnemyUnit("husk", new(2, 0))]);
        wiped.State.FindParty(1)!.Bestiary["husk"] = new BestiaryEntry { Seen = 2 };
        Combat.ApplyDamage(wiped, b, BodyPart.Torso, 999);
        Assert.Empty(wiped.ResultFor(1).Bestiary); // a wipe loses the run's learning, like the card pouch (user decision)
        Bestiary.Merge(colony, wiped.ResultFor(1).Bestiary);
        Assert.Equal(5, colony.Bestiary["husk"].Seen);
    }

    [Fact]
    public void The_bestiary_rides_the_colony_save_and_an_older_save_starts_empty()
    {
        var sim = NewGame.Create(TestData.Real, 3, Real);
        sim.State.Bestiary["linked"] = new BestiaryEntry { Seen = 4, Fought = 2, Killed = 1 };
        var json = SaveGame.Save(sim.State);
        var loaded = SaveGame.Load(TestData.Real, json, Real);
        Assert.Equal(4, loaded.State.Bestiary["linked"].Seen);

        var node = JsonNode.Parse(json)!;
        node["version"] = 26;
        node["colony"]!.AsObject().Remove("bestiary");
        node["colony"]!.AsObject().Remove("cards");
        Assert.Empty(SaveGame.Load(TestData.Real, node.ToJsonString(), Real).State.Bestiary);
        Assert.Equal(27, SaveGame.CardsVersion);

        sim.State.Bestiary["no_such_enemy"] = new BestiaryEntry { Seen = 1 };
        Assert.Throws<System.Text.Json.JsonException>(() => SaveGame.Load(TestData.Real, SaveGame.Save(sim.State), Real));
    }

    [Fact]
    public void A_returning_party_teaches_the_colony()
    {
        var sim = NewGame.Create(TestData.Real, 7, Real);
        var adult = sim.State.Survivors.First(s => !s.IsChild);
        Assert.True(sim.Execute(new LaunchExpedition("outskirts", [new ExpeditionMember(adult.Id)])).Ok);
        var result = new ExtractionResult(ColonySim.ExpeditionPlayerId, true, [new ReturnedSurvivor(adult, false, new(), [])], [], [], new())
            { Bestiary = new() { ["husk"] = new BestiaryEntry { Seen = 3, Fought = 2 } } };
        Assert.True(sim.Execute(new ApplyExtractionResult(result)).Ok);
        for (var i = 0; i < 2000 && sim.State.Expedition is not null; i++) sim.Tick();
        Assert.Equal(BestiaryTier.Fought, Bestiary.TierOf(Rules, sim.State.Bestiary["husk"]));
    }

    [Fact]
    public void What_a_side_knows_of_a_unit_adds_the_colony_to_the_run_and_never_gates_players()
    {
        var a = SurvivorUnit(1, new(0, 0));
        var husk = EnemyUnit("husk", new(2, 0));
        var rival = SurvivorUnit(2, new(3, 2));
        var sim = Arena(6, 3, [a, husk, rival]);
        var colony = new Dictionary<string, BestiaryEntry>();
        Assert.Equal(BestiaryTier.Unknown, Bestiary.Knowledge(sim, colony, 1, husk));
        Bestiary.Observe(sim);
        Assert.Equal(BestiaryTier.Seen, Bestiary.Knowledge(sim, colony, 1, husk)); // the run has seen it now
        colony["husk"] = new BestiaryEntry { Killed = Rules.Killed };
        Assert.Equal(BestiaryTier.Killed, Bestiary.Knowledge(sim, colony, 1, husk));
        Assert.Equal(BestiaryTier.Killed, Bestiary.Knowledge(sim, new Dictionary<string, BestiaryEntry>(), 1, rival));
    }

    [Fact]
    public void Health_shows_numbers_only_once_the_type_is_killed()
    {
        var husk = EnemyUnit("husk", new(2, 0));
        husk.Hp = husk.MaxHp * 0.75;
        Assert.Equal($"HP {husk.Hp:0}/{husk.MaxHp:0}", Bestiary.Health(husk, BestiaryTier.Killed));
        Assert.Equal("HP 75%", Bestiary.Health(husk, BestiaryTier.Fought));
        Assert.DoesNotContain($"{husk.MaxHp:0}", Bestiary.Health(husk, BestiaryTier.Seen));
    }

    [Fact]
    public void The_hit_chance_help_folds_the_targets_defence_into_one_row_below_killed()
    {
        var a = SurvivorUnit(1, new(1, 1), weapon: "pistol");
        var husk = EnemyUnit("husk", new(5, 1));
        var sim = Arena(8, 4, [a, husk]);
        husk.CardCover = 0.15;
        var shoot = sim.Profile(a).Actions.First(x => x.Reach > 1);
        HelpContext Ctx(BestiaryTier tier) => new() { Scavenge = sim, Attacker = a, Action = shoot, Target = husk, TargetKnown = tier };

        var full = Breakdowns.For("hit_chance", Ctx(BestiaryTier.Killed))!;
        Assert.Contains(full.Rows, r => r.Label == "Card cover");
        var hidden = Breakdowns.For("hit_chance", Ctx(BestiaryTier.Fought))!;
        var labels = hidden.Rows.Select(r => r.Label).ToList();
        Assert.DoesNotContain("Target's dodge", labels);
        Assert.DoesNotContain("Cover", labels);
        Assert.DoesNotContain("Brace", labels);
        var defence = Assert.Single(hidden.Rows, r => r.Label == "Target's defence");
        Assert.Contains("Bestiary", defence.Note);
        var exact = Combat.ChanceToHit(sim, a, shoot, husk);
        Assert.Equal($"{exact * 100:0}%", full.Total);
        Assert.Equal($"{Bestiary.ShownChance(Rules, exact, BestiaryTier.Fought) * 100:0}%", hidden.Total); // rounded to 5% below killed
    }

    [Fact]
    public void Hit_chances_against_a_type_not_yet_killed_show_to_the_nearest_five_percent()
    {
        Assert.Equal(0.65, Bestiary.ShownChance(Rules, 0.634, BestiaryTier.Fought), 9);
        Assert.Equal(0.60, Bestiary.ShownChance(Rules, 0.62, BestiaryTier.Unknown), 9);
        Assert.Equal(0.05, Bestiary.ShownChance(Rules, 0.05, BestiaryTier.Seen), 9);
        Assert.Equal(0.634, Bestiary.ShownChance(Rules, 0.634, BestiaryTier.Killed), 9);
    }

    [Theory]
    [InlineData(0.425, 0.45)] // a half step rounds up, despite 0.425 being a hair under it in binary
    [InlineData(0.95, 0.95)]
    [InlineData(0.99, 0.95)]  // never shows a sure thing
    [InlineData(0.02, 0.05)]  // nor a hopeless one
    [InlineData(0.05, 0.05)]
    public void Rounded_chances_round_half_steps_up_and_stay_within_one_step_of_the_ends(double chance, double shown)
    {
        Assert.Equal(0.05, Rules.ChanceStep, 9); // from scavenge.json
        Assert.Equal(shown, Bestiary.ShownChance(Rules, chance, BestiaryTier.Seen), 9);
    }

    [Fact]
    public void The_hit_previews_bucket_below_killed_and_stay_exact_for_players_and_known_types()
    {
        var a = SurvivorUnit(1, new(1, 1), weapon: "pistol");
        var husk = EnemyUnit("husk", new(5, 1));
        var rival = SurvivorUnit(2, new(5, 3), weapon: "pistol");
        var sim = Arena(8, 5, [a, husk, rival]);
        var shoot = sim.Profile(a).Actions.First(x => x.Reach > 1);
        var exact = Combat.ChanceToHit(sim, a, shoot, husk);
        Assert.NotEqual(exact, Bestiary.ShownChance(Rules, exact, BestiaryTier.Seen)); // the test needs an off-bucket chance

        var seen = TacticsView.HitPreview(sim, a, shoot, husk, tier: BestiaryTier.Seen);
        Assert.Equal(Bestiary.ShownChance(Rules, exact, BestiaryTier.Seen), seen.Chance, 9);
        Assert.Equal($"{seen.Chance * 100:0}%", seen.Breakdown!.Total);
        Assert.Equal(exact, TacticsView.HitPreview(sim, a, shoot, husk).Chance, 9);
        Assert.All(TacticsView.MovePreview(sim, a, husk, BestiaryTier.Fought).Values, c => Assert.Equal(0, Math.Round(c * 100) % 5));
        Assert.Contains(TacticsView.MovePreview(sim, a, husk).Values, c => Math.Round(c * 100, 6) % 5 != 0);

        var p = Combat.ChanceToHit(sim, a, shoot, rival);
        Assert.Equal(p, TacticsView.HitPreview(sim, a, shoot, rival,
            tier: Bestiary.Knowledge(sim, new Dictionary<string, BestiaryEntry>(), 1, rival)).Chance, 9); // players: exact
    }

    [Fact]
    public void A_miss_on_an_npc_enemy_logs_its_chance_to_the_nearest_five_percent()
    {
        var a = SurvivorUnit(1, new(1, 1), weapon: "pistol");
        var husk = EnemyUnit("husk", new(5, 1));
        var sim = Arena(8, 4, [a, husk]);
        var shoot = sim.Profile(a).Actions.First(x => x.Reach > 1);
        var shown = 0.0;
        for (var i = 0; i < 40 && !sim.State.Events.Any(e => e.Type == ScavengeEventType.Missed); i++)
        {
            a.Ap = sim.Profile(a).MaxAp;
            husk.Hp = husk.MaxHp;
            shown = Bestiary.ShownChance(Rules, Combat.ChanceToHit(sim, a, shoot, husk), BestiaryTier.Unknown);
            Blow(sim, a, husk, shoot.Id);
        }
        var miss = sim.State.Events.First(e => e.Type == ScavengeEventType.Missed);
        Assert.Contains($"{shown:P0}", miss.Message); // the log is shared and cannot know the colony's bestiary
    }
}
