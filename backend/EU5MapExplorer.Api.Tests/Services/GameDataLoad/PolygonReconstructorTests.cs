using EU5MapExplorer.Api.Services.GameDataLoad;

namespace EU5MapExplorer.Api.Tests.Services.GameDataLoad;

public class PolygonReconstructorTests
{
    /// <summary>
    /// Single ring with two border segments that share a junction point.
    /// The junction point should appear only once (skip duplicate).
    /// </summary>
    [Fact]
    public void TwoBorders_JunctionPointDeduped()
    {
        var cache = new Dictionary<string, int[][][]>
        {
            ["a|b"] = [[[0, 0], [5, 0]]],         // segment 1
            ["b|c"] = [[[5, 0], [5, 5], [0, 5]]],  // segment 2
        };

        var ring = new BorderRing([
            new BorderRef("a|b", false),
            new BorderRef("b|c", false),
        ]);

        var result = PolygonReconstructor.Reconstruct([ring], cache, mapHeight: 0);

        Assert.Single(result);
        var poly = result[0];

        // a|b contributes: [0,0], [5,0] (2 points)
        // b|c contributes: [5,5], [0,5] (skip first [5,0] as duplicate junction)
        Assert.Equal(4, poly.Count);
        Assert.Equal([0, 0], poly[0]);
        Assert.Equal([5, 0], poly[1]);
        Assert.Equal([5, 5], poly[2]);
        Assert.Equal([0, 5], poly[3]);
    }

    /// <summary>
    /// Reversed border — points should come in reverse order.
    /// </summary>
    [Fact]
    public void ReversedBorder_PointsReversed()
    {
        var cache = new Dictionary<string, int[][][]>
        {
            ["a|b"] = [[[0, 0], [5, 0], [10, 0]]],
        };

        var ring = new BorderRing([
            new BorderRef("a|b", true),
        ]);

        var result = PolygonReconstructor.Reconstruct([ring], cache, mapHeight: 0);

        Assert.Single(result);
        var poly = result[0];
        Assert.Equal(3, poly.Count);
        Assert.Equal([10, 0], poly[0]);
        Assert.Equal([5, 0], poly[1]);
        Assert.Equal([0, 0], poly[2]);
    }

    /// <summary>
    /// With mapHeight > 0, coordinates are Y-flipped: [mapHeight - y, x].
    /// </summary>
    [Fact]
    public void MapHeightFlip_Applied()
    {
        var cache = new Dictionary<string, int[][][]>
        {
            ["a|b"] = [[[3, 7]]],
        };

        var ring = new BorderRing([
            new BorderRef("a|b", false),
        ]);

        var result = PolygonReconstructor.Reconstruct([ring], cache, mapHeight: 100);

        Assert.Single(result);
        Assert.Single(result[0]);
        // [mapHeight - y, x] = [100 - 7, 3] = [93, 3]
        Assert.Equal([93, 3], result[0][0]);
    }

    /// <summary>
    /// Missing border key in cache — gracefully skipped.
    /// </summary>
    [Fact]
    public void MissingBorderKey_Skipped()
    {
        var cache = new Dictionary<string, int[][][]>();

        var ring = new BorderRing([
            new BorderRef("missing|key", false),
        ]);

        var result = PolygonReconstructor.Reconstruct([ring], cache, mapHeight: 0);

        Assert.Single(result);
        Assert.Empty(result[0]);
    }

    /// <summary>
    /// Multiple rings produce multiple polygon paths.
    /// </summary>
    [Fact]
    public void MultipleRings_MultiplePolygons()
    {
        var cache = new Dictionary<string, int[][][]>
        {
            ["a|b"] = [[[0, 0], [1, 0]]],
            ["c|d"] = [[[5, 5], [6, 5]]],
        };

        var rings = new[]
        {
            new BorderRing([new BorderRef("a|b", false)]),
            new BorderRing([new BorderRef("c|d", false)]),
        };

        var result = PolygonReconstructor.Reconstruct(rings, cache, mapHeight: 0);

        Assert.Equal(2, result.Count);
        Assert.Equal(2, result[0].Count);
        Assert.Equal(2, result[1].Count);
    }

    /// <summary>
    /// Border with multiple path segments (non-contiguous border between two locations).
    /// All segments should be concatenated.
    /// </summary>
    [Fact]
    public void MultipleBorderSegments_AllConcatenated()
    {
        var cache = new Dictionary<string, int[][][]>
        {
            ["a|b"] = [
                [[0, 0], [1, 0]],    // segment 1
                [[3, 0], [4, 0]],    // segment 2
            ],
        };

        var ring = new BorderRing([
            new BorderRef("a|b", false),
        ]);

        var result = PolygonReconstructor.Reconstruct([ring], cache, mapHeight: 0);

        Assert.Single(result);
        // First segment: [0,0], [1,0] (2 points)
        // Second segment: skip first if duplicate? No, [1,0] != [3,0], so [3,0] is included
        // Actually: start=0 for first seg (empty points), then start=1 for second seg
        // Seg 1: all points → [0,0], [1,0]
        // Seg 2: skip index 0 → [4,0]
        Assert.Equal(3, result[0].Count);
        Assert.Equal([0, 0], result[0][0]);
        Assert.Equal([1, 0], result[0][1]);
        Assert.Equal([4, 0], result[0][2]);
    }
}
