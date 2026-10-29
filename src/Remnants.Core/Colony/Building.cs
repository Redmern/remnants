namespace Remnants.Core.Colony;

public sealed class Building
{
    public int Id { get; set; }
    public string DefId { get; set; } = "";
    public int Level { get; set; } = 1;

    /// <summary>Index into the town's plot grid (row-major, see <c>rules.layout</c>). One building per plot.</summary>
    public int Plot { get; set; }

    /// <summary>Production buildings: the recipe being worked, if any (see <c>SetRecipe</c>).</summary>
    public string? RecipeId { get; set; }

    /// <summary>Effort put into the current batch, towards the recipe's work.</summary>
    public double CraftProgress { get; set; }

    /// <summary>The current batch's inputs have been taken from the stockpile.</summary>
    public bool CraftPaid { get; set; }
}
