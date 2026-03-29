namespace EU5MapExplorer.Api.Data.Entities;

public class GameVersion
{
    public int Id { get; set; }
    public string Version { get; set; } = null!;
    public DateTime ExtractedAt { get; set; }
}
