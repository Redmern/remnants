using Remnants.Core.Colony;
using Remnants.Core.Data;

namespace Remnants.Core.Schematics;

/// <summary>Where a schematic stands for a colony (the archive's columns).</summary>
public enum SchematicStatus
{
    /// <summary>Learned: what it unlocks works.</summary>
    Known,

    /// <summary>An encrypted copy is in the inventory, waiting for a Decrypt order.</summary>
    ToDecrypt,

    /// <summary>Seen (trader stock, a dropped copy left behind, a failed hack, a story line): researchable.</summary>
    HeardOf,

    /// <summary>Not known, not seen.</summary>
    Unknown,

    /// <summary>Its content is not in the game yet.</summary>
    Planned,
}

/// <summary>What happened to a schematic that reached the colony.</summary>
public enum ReceiveOutcome
{
    /// <summary>Learned now.</summary>
    Learned,

    /// <summary>Encrypted: the item went into the inventory for a Decrypt order.</summary>
    ToDecrypt,

    /// <summary>Already known (or already waiting to be decrypted): turned into Research Data.</summary>
    Duplicate,
}

/// <summary>
/// Raised on <see cref="ColonySim.SchematicUnlocked"/> whenever a colony learns a schematic, from any source. Also logged
/// as <see cref="ColonyEventType.SchematicLearned"/>.
/// </summary>
public sealed record SchematicUnlocked(string SchematicId, SchematicSource Source, long Tick);

/// <summary>The result of <see cref="Schematics.Grant"/> / <see cref="Schematics.Receive"/>.</summary>
public sealed record ReceiveResult(ReceiveOutcome Outcome, string SchematicId, double ResearchData = 0)
{
    public bool Learned => Outcome == ReceiveOutcome.Learned;
}

/// <summary>
/// The schematics Core API. Everything that teaches a colony a schematic goes through <see cref="Grant"/> (learn now)
/// or <see cref="Receive"/> (a copy reached home: learn, queue for decrypting, or turn a duplicate into Research Data).
/// The story campaign uses <see cref="Grant"/> with <see cref="SchematicSource.Story"/>, <see cref="Has"/>, and
/// <see cref="HearOf"/> for schematics a dialogue mentions; it listens to <see cref="ColonySim.SchematicUnlocked"/>.
/// </summary>
public static class Schematics
{
    /// <summary>Prefix of the synthesized Decrypt recipes at the Electronics Lab.</summary>
    public const string DecryptRecipePrefix = "decrypt:";

    public static string DecryptRecipeId(string schematicId) => DecryptRecipePrefix + schematicId;

    public static SchematicCatalog Catalog(ColonySim sim) => sim.Data.Schematics;

    /// <summary>Whether the colony knows this schematic.</summary>
    public static bool Has(ColonySim colony, string schematicId) => colony.State.Schematics.Known.Contains(schematicId);

    /// <summary>
    /// The colony learns <paramref name="schematicId"/> now (no decrypting), from <paramref name="source"/>. Already known:
    /// it pays the duplicate value in Research Data instead. Raises <see cref="ColonySim.SchematicUnlocked"/>.
    /// </summary>
    public static ReceiveResult Grant(ColonySim colony, string schematicId, SchematicSource source)
    {
        var def = Require(colony, schematicId);
        if (Has(colony, def.Id)) return Duplicate(colony, def, source);
        Learn(colony, def, source);
        return new(ReceiveOutcome.Learned, def.Id);
    }

    /// <summary>
    /// A copy of a schematic reached home (a party's pack, a purchase). Plain: learned. Encrypted: into the inventory for a
    /// Decrypt order. Known, or a copy already waiting: Research Data.
    /// </summary>
    public static ReceiveResult Receive(ColonySim colony, string schematicId, SchematicSource source)
    {
        var def = Require(colony, schematicId);
        var state = colony.State;
        if (Has(colony, def.Id) || def.Encrypted && state.Inventory.Has(def.Id)) return Duplicate(colony, def, source);
        if (!def.Encrypted)
        {
            Learn(colony, def, source);
            return new(ReceiveOutcome.Learned, def.Id);
        }
        state.Inventory.Add(def.Id);
        state.Schematics.HeardOf.Remove(def.Id);
        state.Log(new(state.Tick, ColonyEventType.SchematicFound,
            $"{def.ItemName} is home. It is encrypted: queue a Decrypt at the {BuildingName(colony, colony.Data.Schematics.Rules.Decrypt.Building)} " +
            $"(L{DecryptLevel(colony, def)})."));
        return new(ReceiveOutcome.ToDecrypt, def.Id);
    }

    /// <summary>The colony has seen this schematic somewhere (<paramref name="where"/>): it becomes researchable. Known ones are ignored.</summary>
    public static bool HearOf(ColonySim colony, string schematicId, string where)
    {
        var def = Require(colony, schematicId);
        var s = colony.State.Schematics;
        if (s.Known.Contains(def.Id) || def.Planned || !s.HeardOf.TryAdd(def.Id, where)) return false;
        colony.State.Log(new(colony.State.Tick, ColonyEventType.SchematicHeardOf, $"Heard of the {def.Name} schematic ({where})."));
        return true;
    }

    public static SchematicStatus Status(ColonySim colony, string schematicId)
    {
        var def = Require(colony, schematicId);
        var s = colony.State.Schematics;
        if (s.Known.Contains(def.Id)) return SchematicStatus.Known;
        if (def.Planned) return SchematicStatus.Planned;
        if (colony.State.Inventory.Has(def.Id)) return SchematicStatus.ToDecrypt;
        return s.HeardOf.ContainsKey(def.Id) ? SchematicStatus.HeardOf : SchematicStatus.Unknown;
    }

    /// <summary>Whether a recipe's schematic (if any) is known.</summary>
    public static bool RecipeUnlocked(ColonySim colony, string recipeId) =>
        colony.Data.Schematics.GateOfRecipe(recipeId) is not { } gate || Has(colony, gate);

    /// <summary>Whether a perk's schematic (if any) is known.</summary>
    public static bool PerkUnlocked(ColonySim colony, string perkId) =>
        colony.Data.Schematics.GateOfPerk(perkId) is not { } gate || Has(colony, gate);

    /// <summary>Whether a building type at this level is unlocked (level 1 = the building itself).</summary>
    public static bool LevelUnlocked(ColonySim colony, string buildingId, int level) =>
        colony.Data.Schematics.GateOfBuildingLevel(buildingId, level) is not { } gate || Has(colony, gate);

    /// <summary>The highest level of a building type the known schematics allow (ignoring the Town Hall cap).</summary>
    public static int MaxKnownLevel(ColonySim colony, BuildingDef def)
    {
        var level = 0;
        while (level < def.MaxLevel && LevelUnlocked(colony, def.Id, level + 1)) level++;
        return level;
    }

    /// <summary>Everything a schematic unlocks, as names for the UI ("Hunting Rifle", "Workshop L3").</summary>
    public static IEnumerable<string> DescribeUnlocks(GameData data, SchematicDef def) => def.Unlocks.Select(u => u.Kind switch
    {
        UnlockKind.Building => data.Buildings.GetValueOrDefault(u.Id)?.Name ?? u.Id,
        UnlockKind.BuildingLevel => $"{data.Buildings.GetValueOrDefault(u.Id)?.Name ?? u.Id} L{u.Level}",
        UnlockKind.Recipe => data.Recipes.GetValueOrDefault(u.Id)?.Name ?? u.Id,
        UnlockKind.Perk => $"perk {data.Perks.GetValueOrDefault(u.Id)?.Name ?? u.Id}",
        _ => Humanize(u.Id),
    });

    /// <summary>Research Data a copy of this schematic costs (<see cref="MakeCopy"/>).</summary>
    public static double CopyCost(ColonySim colony, SchematicDef def) => SchematicCatalog.At(colony.Data.Schematics.Rules.CopyCost, def.Tier);

    /// <summary>
    /// Make a tradeable copy of a known schematic: its item goes into the inventory (to give, sell or trade; whoever
    /// brings it home learns it the usual way). Costs <see cref="CopyCost"/> Research Data. Also the
    /// <see cref="CopySchematic"/> command.
    /// </summary>
    public static CommandResult MakeCopy(ColonySim colony, string schematicId)
    {
        var state = colony.State;
        if (colony.Data.Schematics.Find(schematicId) is not { } def) return CommandResult.Fail(CommandError.NotASchematic, $"'{schematicId}' is not a schematic.");
        if (!Has(colony, def.Id)) return CommandResult.Fail(CommandError.SchematicNeeded, $"The colony does not know the {def.Name} schematic.");
        var cost = new Dictionary<string, double> { [colony.Data.Schematics.Rules.ResearchDataResource] = CopyCost(colony, def) };
        if (!state.Stockpile.CanAfford(cost)) return CommandResult.Fail(CommandError.CannotAfford, $"A copy takes {cost.Values.Single():0} Research Data.");
        state.Stockpile.Spend(cost);
        state.Inventory.Add(def.Id);
        var message = $"Made a copy of the {def.Name} schematic.";
        state.Log(new(state.Tick, ColonyEventType.SchematicFound, message));
        return CommandResult.Success(message);
    }

    /// <summary>Decrypt level needed at the Electronics Lab for this schematic.</summary>
    public static int DecryptLevel(ColonySim colony, SchematicDef def) => SchematicCatalog.At(colony.Data.Schematics.Rules.Decrypt.MinLevel, def.Tier);

    internal static SchematicDef Require(ColonySim colony, string schematicId) =>
        colony.Data.Schematics.Find(schematicId) ?? throw new ArgumentException($"Unknown schematic '{schematicId}'.", nameof(schematicId));

    internal static void Learn(ColonySim colony, SchematicDef def, SchematicSource source)
    {
        var state = colony.State;
        var s = state.Schematics;
        s.Known.Add(def.Id);
        s.HeardOf.Remove(def.Id);
        s.LearnedFrom[def.Id] = source;
        s.LearnedAt[def.Id] = state.Tick;
        var unlocks = string.Join(", ", DescribeUnlocks(colony.Data, def));
        state.Log(new(state.Tick, ColonyEventType.SchematicLearned, $"Learned the {def.Name} schematic: {unlocks}."));
        colony.RaiseSchematicUnlocked(new SchematicUnlocked(def.Id, source, state.Tick));
    }

    private static ReceiveResult Duplicate(ColonySim colony, SchematicDef def, SchematicSource source)
    {
        var state = colony.State;
        var value = colony.Data.Schematics.DuplicateValue(def);
        state.Stockpile.Add(colony.Data.Schematics.Rules.ResearchDataResource, value);
        state.Schematics.Duplicates++;
        var why = source == SchematicSource.Story ? "Already got that one" : "A copy of a known schematic";
        state.Log(new(state.Tick, ColonyEventType.SchematicFound, $"{why}: the {def.Name} schematic became {value:0.#} Research Data."));
        return new(ReceiveOutcome.Duplicate, def.Id, value);
    }

    internal static string BuildingName(ColonySim colony, string id) => colony.Data.Buildings.GetValueOrDefault(id)?.Name ?? id;

    private static string Humanize(string id) => id.Length == 0 ? id : char.ToUpperInvariant(id[0]) + id[1..].Replace('_', ' ');
}
