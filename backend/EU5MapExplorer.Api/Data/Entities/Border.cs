namespace EU5MapExplorer.Api.Data.Entities;

public class Border
{
    public int Id { get; set; }
    public string Key { get; set; } = null!; // "locA|locB", unique
    public string LocationA { get; set; } = null!; // alphabetically first
    public string LocationB { get; set; } = null!; // second (or "" for map edge)
    public int[][][] Paths { get; set; } = []; // RDP-simplified polylines (JSONB)
}
