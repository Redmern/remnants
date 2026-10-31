using Remnants.Core.Colony;
using Remnants.Core.Data;
using Remnants.Core.Help;
using Remnants.Core.Scavenge;
using static Remnants.Core.Tests.ScavengeTestData;

namespace Remnants.Core.Tests;

/// <summary>The help catalogue (data/help), its markup, search, and the live breakdowns its topics name.</summary>
public class HelpTests
{
    private static readonly Lazy<HelpCatalog> Shipped =
        new(() => HelpCatalog.Load(DataSource.FromDirectory(Path.Combine(AppContext.BaseDirectory, "data")).Sub("help")));

    private static HelpCatalog Catalog(string json) =>
        HelpCatalog.Load(DataSource.FromFiles(new Dictionary<string, string> { ["a.json"] = json }));

    [Fact]
    public void The_shipped_help_loads_validates_and_covers_the_core_concepts()
    {
        var help = Shipped.Value;
        Assert.Empty(help.Validate(Breakdowns.Ids));
        foreach (var id in new[]
                 {
                     "attributes", "skills", "jobs_output", "shifts", "food", "spoilage", "food_buffs", "health", "injuries", "infection",
                     "humanity", "prosthetic_tiers", "schematics", "research", "power", "weather", "raids", "defenses", "expeditions",
                     "travel", "rations", "vehicles", "fog", "stealth", "ambush", "overwatch", "hacking", "drones", "gear_mods", "loot",
                     "salvage", "trading", "coop", "alliances",
                 })
            Assert.True(help.Contains(id), $"no help topic '{id}'");
    }

    [Fact]
    public void The_embedded_help_matches_the_folder()
    {
        var embedded = HelpCatalog.Load(DataSource.FromAssembly(typeof(HelpTests).Assembly).Sub("help"));
        Assert.Equal(Shipped.Value.Topics.Select(t => t.Id), embedded.Topics.Select(t => t.Id));
    }

    [Fact]
    public void A_topic_takes_its_files_category_and_validation_names_every_problem()
    {
        var help = Catalog("""
            {"category": "Colony", "topics": [
              {"id": "a", "title": "A", "short": "Short.", "long": "See [[b]] and [[ghost]].", "related": ["nope"], "provider": "made_up"},
              {"id": "a", "title": "", "short": "", "long": ""}
            ]}
            """);
        Assert.Equal("Colony", help.Find("a")!.Category);
        var problems = string.Join("\n", help.Validate(Breakdowns.Ids));
        Assert.Contains("duplicate topic id 'a'", problems);
        Assert.Contains("unknown topic 'b'", problems);
        Assert.Contains("unknown topic 'ghost'", problems);
        Assert.Contains("unknown topic 'nope'", problems);
        Assert.Contains("unknown provider 'made_up'", problems);
        Assert.Contains("no title", problems);
    }

    [Fact]
    public void Markup_parses_headings_bullets_paragraphs_emphasis_and_links()
    {
        var blocks = HelpMarkup.Parse("First line\njoined.\n\n## Tips\n- **Bold** and *soft*\n- See [[food]] or [[food|the pantry]].", id => id == "food" ? "Food" : null);
        Assert.Equal([HelpBlockKind.Paragraph, HelpBlockKind.Heading, HelpBlockKind.Bullet, HelpBlockKind.Bullet], blocks.Select(b => b.Kind));
        Assert.Equal("First line joined.", blocks[0].Spans.Single().Text);
        Assert.Equal([HelpSpanKind.Bold, HelpSpanKind.Text, HelpSpanKind.Emphasis], blocks[2].Spans.Select(s => s.Kind));
        var links = blocks[3].Spans.Where(s => s.Kind == HelpSpanKind.Link).ToList();
        Assert.Equal(["Food", "the pantry"], links.Select(l => l.Text));
        Assert.All(links, l => Assert.Equal("food", l.Topic));
        Assert.Equal("a * b stays", string.Concat(HelpMarkup.Spans("a * b stays").Select(s => s.Text)));
    }

    [Fact]
    public void Search_needs_every_word_and_ranks_title_hits_first()
    {
        var help = Catalog("""
            {"category": "C", "topics": [
              {"id": "x", "title": "Rations", "short": "Food for the road.", "long": "Travel food."},
              {"id": "y", "title": "Food", "short": "What everyone eats.", "long": "Rations are for trips."},
              {"id": "z", "title": "Weather", "short": "Rain.", "long": "Storms."}
            ]}
            """);
        Assert.Equal(["y", "x"], help.Search("food").Select(t => t.Id));
        Assert.Equal(["x", "y"], help.Search("RATIONS").Select(t => t.Id));
        Assert.Equal(["x"], help.Search("rations road").Select(t => t.Id));
        Assert.Empty(help.Search("snow"));
        Assert.Equal(3, help.Search("  ").Count);
    }

    [Fact]
    public void The_job_output_breakdown_multiplies_to_what_the_colony_makes()
    {
        var sim = NewGame.Create(TestData.Real, 1, Real);
        foreach (var (res, amount) in sim.Data.Buildings["woodcutter"].Level(1).Cost) sim.State.Stockpile.Add(res, amount);
        var building = sim.State.FindBuilding(sim.Execute(new Construct("woodcutter")).EntityId!.Value)!;
        var worker = sim.State.Survivors.First(s => !s.IsChild && !s.IsInfant);
        Assert.True(sim.Execute(new AssignJob(worker.Id, building.Id)).Ok);

        var b = Breakdowns.For(Breakdowns.JobOutput, new HelpContext { Colony = sim, Survivor = worker })!;
        var perTick = sim.OutputPerTick(building);
        Assert.StartsWith($"{perTick:0.###}/tick", b.Total);
        Assert.Contains(b.Rows, r => r.Label == "Mood");
        Assert.Null(Breakdowns.For(Breakdowns.JobOutput, new HelpContext { Colony = sim }));
        Assert.Null(Breakdowns.For("nope", new HelpContext { Colony = sim, Survivor = worker }));
    }

    [Fact]
    public void The_travel_and_food_breakdowns_read_the_planner_and_the_pantry()
    {
        var sim = NewGame.Create(TestData.Real, 1, Real);
        var zone = Real.Zones.Values.First();
        var travel = Breakdowns.For(Breakdowns.Travel, new HelpContext { Colony = sim, Zone = zone })!;
        var days = WorldMap.Travel(sim, zone, sim.State.Tick).Ticks / (double)sim.Data.Rules.TicksPerDay;
        Assert.Equal($"{days:0.#} day{(Math.Abs(days - 1) < 0.05 ? "" : "s")} each way", travel.Total);
        Assert.Contains(travel.Rows, r => r.Label == "Rations per member");

        var food = Breakdowns.For(Breakdowns.FoodDays, new HelpContext { Colony = sim })!;
        Assert.Equal($"{sim.FoodDays:0.#} days left", food.Total);
    }

    [Fact]
    public void The_hit_chance_breakdown_ends_on_the_chance_the_attack_resolves_with()
    {
        var u = SurvivorUnit(pos: new(1, 1), weapon: "pipe");
        var enemy = EnemyUnit("husk", new(2, 1));
        var sim = Arena(5, 3, [u, enemy]);
        var action = sim.Profile(u).Actions[0];
        var b = Breakdowns.For(Breakdowns.HitChance, new HelpContext { Scavenge = sim, Attacker = u, Action = action, Target = enemy })!;
        Assert.Equal($"{Combat.ChanceToHit(sim, u, action, enemy) * 100:0}%", b.Total);
        Assert.Equal("Accuracy", b.Rows[0].Label);
    }

    [Fact]
    public void The_humanity_breakdown_starts_full_and_shows_the_mood_cap()
    {
        var sim = NewGame.Create(TestData.Real, 1, Real);
        var s = sim.State.Survivors[0];
        var b = Breakdowns.For(Breakdowns.Humanity, new HelpContext { Colony = sim, Survivor = s })!;
        Assert.Equal("Start", b.Rows[0].Label);
        Assert.Contains(b.Rows, r => r.Label == "Mood cap");
    }
}
