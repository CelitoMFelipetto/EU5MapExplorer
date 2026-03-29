namespace EU5MapExplorer.Api.Data.Entities;

public class Area
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string ContentHash { get; set; } = null!;
    public string? Continent { get; set; }
    public string? SubContinent { get; set; }
    public string? Region { get; set; }
    /// <summary>Names of neighbouring areas (resolved by name at query time).</summary>
    public string[] Neighbors { get; set; } = [];

    public List<Province> Provinces { get; set; } = [];
    public List<GameVersionArea> GameVersionAreas { get; set; } = [];
}
