using EU5MapExplorer.Api.Services.GameDataLoad;

namespace EU5MapExplorer.Api.Tests.Services.GameDataLoad;

public class RdpSimplifierTests
{
    [Fact]
    public void TwoPoints_ReturnedUnchanged()
    {
        var pts = new List<Point> { new(0, 0), new(10, 10) };
        var result = RdpSimplifier.Simplify(pts, 1.0);

        Assert.Equal(2, result.Length);
        Assert.Equal([0, 0], result[0]);
        Assert.Equal([10, 10], result[1]);
    }

    [Fact]
    public void CollinearPoints_CollapsedToEndpoints()
    {
        // Straight horizontal line — all interior points are within epsilon
        var pts = Enumerable.Range(0, 10).Select(x => new Point(x, 0)).ToList();
        var result = RdpSimplifier.Simplify(pts, 0.5);

        Assert.Equal(2, result.Length);
        Assert.Equal([0, 0], result[0]);
        Assert.Equal([9, 0], result[1]);
    }

    [Fact]
    public void DiagonalStaircase_SimplifiedToLine()
    {
        // Pixel staircase: (0,0)→(0,1)→(1,1)→(1,2)→(2,2)→(2,3)→(3,3)
        // Should simplify to roughly a straight diagonal
        var pts = new List<Point>
        {
            new(0, 0),
            new(0, 1),
            new(1, 1),
            new(1, 2),
            new(2, 2),
            new(2, 3),
            new(3, 3),
        };
        var result = RdpSimplifier.Simplify(pts, 0.7);

        // With epsilon=0.7, the staircase should collapse — keep start and end,
        // possibly one or two interior points. Definitely fewer than 7.
        Assert.True(
            result.Length < pts.Count,
            $"Expected simplification, got {result.Length} points"
        );
        Assert.Equal([0, 0], result[0]);
        Assert.Equal([3, 3], result[^1]);
    }

    [Fact]
    public void SharpCorner_Retained()
    {
        // L-shape: (0,0)→(5,0)→(5,5) — the corner at (5,0) should be kept
        var pts = new List<Point>
        {
            new(0, 0),
            new(1, 0),
            new(2, 0),
            new(3, 0),
            new(4, 0),
            new(5, 0),
            new(5, 1),
            new(5, 2),
            new(5, 3),
            new(5, 4),
            new(5, 5),
        };
        var result = RdpSimplifier.Simplify(pts, 0.5);

        // Should keep at least the 3 corners: (0,0), (5,0), (5,5)
        Assert.True(result.Length >= 3);
        Assert.Equal([0, 0], result[0]);
        Assert.Equal([5, 5], result[^1]);
        // The corner point (5,0) should be retained
        Assert.Contains(result, p => p[0] == 5 && p[1] == 0);
    }

    [Fact]
    public void SinglePoint_ReturnedAsIs()
    {
        var pts = new List<Point> { new(42, 99) };
        var result = RdpSimplifier.Simplify(pts, 1.0);

        Assert.Single(result);
        Assert.Equal([42, 99], result[0]);
    }

    [Fact]
    public void EmptyInput_ReturnsEmpty()
    {
        var result = RdpSimplifier.Simplify([], 1.0);
        Assert.Empty(result);
    }
}
