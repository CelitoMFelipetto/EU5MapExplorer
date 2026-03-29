namespace EU5MapExplorer.Api.Data.Entities;

/// <summary>Join table: which area data rows does a given game version use?</summary>
public class GameVersionArea
{
    public int GameVersionId { get; set; }
    public int AreaId { get; set; }

    public GameVersion GameVersion { get; set; } = null!;
    public Area Area { get; set; } = null!;
}
