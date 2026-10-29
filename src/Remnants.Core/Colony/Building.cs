namespace Remnants.Core.Colony;

public sealed class Building
{
    public int Id { get; set; }
    public string DefId { get; set; } = "";
    public int Level { get; set; } = 1;

    /// <summary>Index into the town's plot grid (row-major, see <c>rules.layout</c>). One building per plot.</summary>
    public int Plot { get; set; }

    /// <summary>Production buildings: orders in work order; only the first is worked (see <c>QueueRecipe</c>).</summary>
    public List<CraftOrder> Queue { get; set; } = new();
}

/// <summary>
/// An order in a production building's queue: <see cref="Remaining"/> batches of a recipe, or a repeating order that
/// goes to the back of the queue after each batch (so it runs forever and lets other orders through).
/// </summary>
public sealed class CraftOrder
{
    public int Id { get; set; }
    public string RecipeId { get; set; } = "";
    public int Remaining { get; set; } = 1;
    public bool Repeat { get; set; }

    /// <summary>Effort put into the current batch, towards the recipe's work.</summary>
    public double Progress { get; set; }

    /// <summary>The current batch's inputs have been taken from the stockpile and inventory.</summary>
    public bool Paid { get; set; }

    /// <summary>Item ids (with their quality) taken for the current batch, so a cancel gives back the same items.</summary>
    public List<string> PaidItems { get; set; } = new();
}
