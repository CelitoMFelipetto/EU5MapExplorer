namespace EU5MapExplorer.Api.Data.Entities;

public class Province
{
    public int Id { get; set; }
    public int AreaId { get; set; }
    public string Name { get; set; } = null!;
    public BorderRing[] BorderRings { get; set; } = [];
    public int MaxN { get; set; }
    public int MaxS { get; set; }
    public int MaxE { get; set; }
    public int MaxW { get; set; }

    public Area Area { get; set; } = null!;
    public List<Location> Locations { get; set; } = [];
}
