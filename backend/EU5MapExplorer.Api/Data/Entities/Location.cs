namespace EU5MapExplorer.Api.Data.Entities;

public class Location
{
    public int Id { get; set; }
    public int ProvinceId { get; set; }
    public string Name { get; set; } = null!;
    public string Color { get; set; } = null!;
    public string Topography { get; set; } = null!;
    public string Climate { get; set; } = null!;
    public string? Vegetation { get; set; }
    public string? RawMaterial { get; set; }
    public string Rank { get; set; } = "rural_settlement";
    public double? CityX { get; set; }
    public double? CityY { get; set; }
    public int[][][] Paths { get; set; } = [];

    public Province Province { get; set; } = null!;
}
