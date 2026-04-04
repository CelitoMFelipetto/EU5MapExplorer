using EU5MapExplorer.Api.Services.GameDataLoad;

namespace EU5MapExplorer.Api.Tests.Services.GameDataLoad;

public class EdgeScannerTests
{
    /// <summary>
    /// 2×2 grid, single location (index 0) fills all pixels.
    /// Expect only map-edge borders (0, -1).
    /// </summary>
    [Fact]
    public void SingleLocation_OnlyMapEdgeBorders()
    {
        // 2×2 grid, all pixels = location 0
        var colorMap = new int[2, 2];
        // all default to 0

        var result = EdgeScanner.Scan(colorMap, 2, 2, _ => "area_a");

        // Only one border group key: (min(-1,0), max(-1,0)) = (-1, 0)
        Assert.Single(result.BorderEdges);
        Assert.True(result.BorderEdges.ContainsKey((-1, 0)));

        // Map edge has 8 edges total for a 2×2 grid:
        // top: 2, bottom: 2, left: 2, right: 2
        Assert.Equal(8, result.BorderEdges[(-1, 0)].Count);

        Assert.Empty(result.AreaNeighbourPairs);
    }

    /// <summary>
    /// 2×1 grid with two different locations side by side.
    /// Expect a border between them plus map edges.
    /// </summary>
    [Fact]
    public void TwoLocations_SideBySide_ProducesBorder()
    {
        // 2×1 grid: [0][1]
        var colorMap = new int[2, 1];
        colorMap[0, 0] = 0;
        colorMap[1, 0] = 1;

        var areas = new[] { "area_a", "area_b" };
        var result = EdgeScanner.Scan(colorMap, 2, 1, idx => areas[idx]);

        // Should have border group (0, 1) — the internal border
        Assert.True(result.BorderEdges.ContainsKey((0, 1)));

        // The internal border at x=1 is a single vertical edge: (1,0)→(1,1)
        var internalEdges = result.BorderEdges[(0, 1)];
        Assert.Single(internalEdges);

        // Area neighbours detected
        Assert.Contains(("area_a", "area_b"), result.AreaNeighbourPairs);
    }

    /// <summary>
    /// 3×1 grid: [A][B][A] — locations 0 and 1 alternate.
    /// Should produce two separate border edges between 0 and 1.
    /// </summary>
    [Fact]
    public void AlternatingLocations_TwoBorderEdges()
    {
        var colorMap = new int[3, 1];
        colorMap[0, 0] = 0;
        colorMap[1, 0] = 1;
        colorMap[2, 0] = 0;

        var result = EdgeScanner.Scan(colorMap, 3, 1, _ => "area_a");

        Assert.True(result.BorderEdges.ContainsKey((0, 1)));
        // Two separate vertical edges: x=1 and x=2
        Assert.Equal(2, result.BorderEdges[(0, 1)].Count);
    }

    /// <summary>
    /// 2×2 grid with four different locations, all in different areas.
    /// </summary>
    [Fact]
    public void FourLocations_FourAreas_DetectsAllNeighbours()
    {
        // [0][1]
        // [2][3]
        var colorMap = new int[2, 2];
        colorMap[0, 0] = 0;
        colorMap[1, 0] = 1;
        colorMap[0, 1] = 2;
        colorMap[1, 1] = 3;

        var areas = new[] { "a", "b", "c", "d" };
        var result = EdgeScanner.Scan(colorMap, 2, 2, idx => areas[idx]);

        // Internal border groups: (0,1), (0,2), (1,3), (2,3)
        Assert.True(result.BorderEdges.ContainsKey((0, 1)));
        Assert.True(result.BorderEdges.ContainsKey((0, 2)));
        Assert.True(result.BorderEdges.ContainsKey((1, 3)));
        Assert.True(result.BorderEdges.ContainsKey((2, 3)));

        // All 4 area pairs are neighbours (diagonal not directly, but via edges)
        Assert.Contains(("a", "b"), result.AreaNeighbourPairs);
        Assert.Contains(("a", "c"), result.AreaNeighbourPairs);
        Assert.Contains(("b", "d"), result.AreaNeighbourPairs);
        Assert.Contains(("c", "d"), result.AreaNeighbourPairs);
    }

    [Fact]
    public void UnmappedPixels_AreSkipped()
    {
        // 2×1 grid: [-1][0]
        var colorMap = new int[2, 1];
        colorMap[0, 0] = -1;
        colorMap[1, 0] = 0;

        var result = EdgeScanner.Scan(colorMap, 2, 1, _ => "area_a");

        // Only map-edge borders for location 0 plus the border with -1 at x=1
        Assert.True(result.BorderEdges.ContainsKey((-1, 0)));
        Assert.Empty(result.AreaNeighbourPairs);
    }
}
