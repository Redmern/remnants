using Remnants.Core.Colony;
using Remnants.Core.Random;
using Remnants.Core.Scavenge;
using Remnants.Core.Survivors;
using static Remnants.Core.Tests.ScavengeTestData;

namespace Remnants.Core.Tests;

/// <summary>Where cards come from: loot and salvage rows (into the run pouch), recipes; and where they go after a run.</summary>
public class CardSourceTests
{
    /// <summary>The real loot tables with a card that always drops from a Husk and from each rubble lot.</summary>
    private static readonly ScavengeData WithCardLoot = Build();

    private static ScavengeData Build()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "data");
        var files = Directory.GetFiles(dir, "loot_tables*.json").OrderBy(f => Path.GetFileName(f) == "loot_tables.json" ? 0 : 1)
            .ThenBy(f => f, StringComparer.Ordinal).Select(File.ReadAllText).ToList();
        files.Add("""{"extend": [{"table": "husk", "guaranteed": [{"card": "dash"}]}, {"table": "salvage_rubble", "guaranteed": [{"card": "take_cover"}]}]}""");
        return ScavengeDataLoader.WithLoot(Real, files, File.ReadAllText(Path.Combine(dir, "salvage.json")));
    }

    [Fact]
    public void A_card_from_a_kill_goes_into_the_killers_party_pouch_not_onto_the_ground()
    {
        var a = SurvivorUnit(1, new(0, 0));
        var husk = EnemyUnit("husk", new(1, 0));
        var sim = Arena(4, 3, [a, husk], data: WithCardLoot);
        Combat.ApplyDamage(sim, husk, BodyPart.Torso, 999, attacker: a);

        Assert.Equal(["dash"], sim.State.FindParty(1)!.CardPouch);
        Assert.DoesNotContain(sim.State.Caches.SelectMany(c => c.Items), id => id.Contains("dash"));
        Assert.Contains(sim.State.Events, e => e.Type == ScavengeEventType.CardFound && e.Side == Unit.PlayerSide(1));
    }

    [Fact]
    public void A_card_from_salvage_goes_into_the_workers_party_pouch()
    {
        var worker = SurvivorUnit(1, new Pos(1, 1));
        SalvageNode node = null!;
        var sim = Arena(6, 4, [worker], rows: ["......", "......", ".r....", "......"], data: WithCardLoot,
            setup: (d, s) => node = FieldSalvage.Add(d, s, d.Salvage.Node("rubble")!, new Pos(1, 2), new DeterministicRng(1)));
        for (var i = 0; i < 4 && sim.State.FindParty(1)!.CardPouch.Count == 0; i++)
        {
            worker.Ap = sim.Profile(worker).MaxAp;
            Assert.True(sim.Execute(new Salvage(1, worker.Id, node.Id)).Ok);
        }
        Assert.Equal(["take_cover"], sim.State.FindParty(1)!.CardPouch);
        Assert.DoesNotContain(worker.Pack, id => id.Contains("take_cover"));
    }

    [Fact]
    public void The_pouch_comes_home_only_with_someone_who_got_out_and_used_consumables_are_gone_either_way()
    {
        var a = SurvivorUnit(1, new(0, 0));
        var sim = Arena(4, 3, [a, SurvivorUnit(2, new(3, 2))]);
        sim.State.FindParty(1)!.CardPouch.Add("sprint");
        sim.State.Decks.Add(new SquadDeck { PlayerId = 1, Consumed = ["field_dressing"] });

        a.Status = UnitStatus.Extracted;
        sim.UpdateParties();
        var home = sim.ResultFor(1);
        Assert.Equal(["sprint"], home.Cards);
        Assert.Equal(["field_dressing"], home.ConsumedCards);

        var b = SurvivorUnit(1, new(0, 0));
        var wiped = Arena(4, 3, [b, SurvivorUnit(2, new(3, 2))]);
        wiped.State.FindParty(1)!.CardPouch.Add("sprint");
        wiped.State.Decks.Add(new SquadDeck { PlayerId = 1, Consumed = ["field_dressing"] });
        Combat.ApplyDamage(wiped, b, BodyPart.Torso, 999);
        var lost = wiped.ResultFor(1);
        Assert.Empty(lost.Cards);
        Assert.Equal(["field_dressing"], lost.ConsumedCards);
    }

    [Fact]
    public void Back_home_found_cards_join_the_collection_and_consumed_ones_leave_it()
    {
        var sim = NewGame.Create(TestData.Real, 7, Real);
        var adult = sim.State.Survivors.First(s => !s.IsChild);
        Assert.True(sim.Execute(new LaunchExpedition("outskirts", [new ExpeditionMember(adult.Id)])).Ok);
        var dressings = sim.State.Cards.Owned["field_dressing"];
        var result = new ExtractionResult(ColonySim.ExpeditionPlayerId, true,
            [new ReturnedSurvivor(adult, false, new(), [])], [], [], new()) { Cards = ["sprint", "sprint"], ConsumedCards = ["field_dressing"] };
        var sprints = sim.State.Cards.Owned.GetValueOrDefault("sprint");

        var applied = sim.Execute(new ApplyExtractionResult(result));
        Assert.True(applied.Ok, applied.Message);
        for (var i = 0; i < 2000 && sim.State.Expedition is not null; i++) sim.Tick();
        Assert.Equal(sprints + 2, sim.State.Cards.Owned["sprint"]);
        Assert.Equal(dressings - 1, sim.State.Cards.Owned.GetValueOrDefault("field_dressing"));
        Assert.Equal(dressings - 1, sim.State.Cards.Deck.Count(c => c == "field_dressing"));
    }

    [Fact]
    public void A_recipe_with_a_card_output_grants_a_copy()
    {
        var recipe = TestData.Real.Recipes.Values.First(r => r.Card is not null);
        Assert.NotNull(Real.Cards.Find(recipe.Card!));
        var sim = LoopTestData.Colony(townHallLevel: 3, food: 1000);
        var shop = sim.Build(recipe.Building);
        shop.Level = recipe.MinLevel;
        var worker = sim.AddSurvivor(attributes: 60);
        worker.Set(sim.Data.Jobs[sim.Data.Buildings[recipe.Building].Job!].Skill, Math.Max(recipe.MinSkill, 30));
        Assert.True(sim.Execute(new AssignJob(worker.Id, shop.Id)).Ok);
        foreach (var (res, n) in recipe.Inputs) sim.State.Stockpile.Add(res, n * 2);
        Assert.True(sim.Execute(new QueueRecipe(shop.Id, recipe.Id)).Ok);
        for (var i = 0; i < 3000 && shop.Queue.Count > 0; i++) sim.Tick();
        Assert.Empty(shop.Queue);
        Assert.Equal(1, sim.State.Cards.Owned.GetValueOrDefault(recipe.Card!));
    }

    [Fact]
    public void The_real_loot_tables_and_recipes_name_only_real_cards()
    {
        var rows = Real.Loot.Tables.Values.SelectMany(t => t.Entries.Select(e => e.Card).Concat(t.Guaranteed.Select(e => e.Card)).Concat(t.Rare.Select(r => r.Card)))
            .OfType<string>().ToList();
        Assert.NotEmpty(rows);
        Assert.All(rows, id => Assert.NotNull(Real.Cards.Find(id)));
        Assert.All(TestData.Real.Recipes.Values.Select(r => r.Card).OfType<string>(), id => Assert.NotNull(Real.Cards.Find(id)));
    }
}
