namespace Remnants.Core.Colony;

public sealed class Building
{
    public int Id { get; set; }
    public string DefId { get; set; } = "";
    public int Level { get; set; } = 1;

    /// <summary>Index into the town's plot grid (row-major, see <c>rules.layout</c>). One building per plot.</summary>
    public int Plot { get; set; }
}
