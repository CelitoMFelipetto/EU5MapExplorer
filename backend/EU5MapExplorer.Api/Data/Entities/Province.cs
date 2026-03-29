namespace EU5MapExplorer.Api.Data.Entities;

public class Province
{
    public int Id { get; set; }
    public int AreaId { get; set; }
    public string Name { get; set; } = null!;
    public int[][][] Paths { get; set; } = [];

    public Area Area { get; set; } = null!;
    public List<Location> Locations { get; set; } = [];
}
