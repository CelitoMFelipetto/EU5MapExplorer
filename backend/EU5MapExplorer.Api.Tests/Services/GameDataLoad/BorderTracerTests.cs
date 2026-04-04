using EU5MapExplorer.Api.Services.GameDataLoad;

namespace EU5MapExplorer.Api.Tests.Services.GameDataLoad;

public class BorderTracerTests
{
    /// <summary>
    /// A simple square border (4 edges forming a 1×1 square).
    /// The tracer should produce a single closed path visiting all 4 corners.
    /// </summary>
    [Fact]
    public void Square_ProducesSingleClosedPath()
    {
        // Square: (0,0)→(1,0)→(1,1)→(0,1)→(0,0)
        var edges = new List<(Point, Point)>
        {
            (new(0, 0), new(1, 0)),
            (new(1, 0), new(1, 1)),
            (new(1, 1), new(0, 1)),
            (new(0, 1), new(0, 0)),
        };

        var paths = BorderTracer.Trace(edges);

        Assert.Single(paths);
        var path = paths[0];
        // Should visit 4 corners (the start point appears once — it's a closed walk)
        Assert.Equal(4, path.Count);
        // All 4 corners present
        Assert.Contains(new Point(0, 0), path);
        Assert.Contains(new Point(1, 0), path);
        Assert.Contains(new Point(1, 1), path);
        Assert.Contains(new Point(0, 1), path);
    }

    /// <summary>
    /// Two disconnected 1×1 squares should produce two separate paths.
    /// </summary>
    [Fact]
    public void TwoDisconnectedSquares_ProducesTwoPaths()
    {
        var edges = new List<(Point, Point)>
        {
            // Square 1 at origin
            (new(0, 0), new(1, 0)),
            (new(1, 0), new(1, 1)),
            (new(1, 1), new(0, 1)),
            (new(0, 1), new(0, 0)),
            // Square 2 at (5,5)
            (new(5, 5), new(6, 5)),
            (new(6, 5), new(6, 6)),
            (new(6, 6), new(5, 6)),
            (new(5, 6), new(5, 5)),
        };

        var paths = BorderTracer.Trace(edges);

        Assert.Equal(2, paths.Count);
        Assert.All(paths, p => Assert.Equal(4, p.Count));
    }

    /// <summary>
    /// A straight line of 3 edges (not closed) should still be traced as a path.
    /// </summary>
    [Fact]
    public void StraightLine_TracedAsPath()
    {
        // Horizontal line: (0,0)→(1,0)→(2,0)→(3,0)
        var edges = new List<(Point, Point)>
        {
            (new(0, 0), new(1, 0)),
            (new(1, 0), new(2, 0)),
            (new(2, 0), new(3, 0)),
        };

        var paths = BorderTracer.Trace(edges);

        Assert.Single(paths);
        Assert.Equal(4, paths[0].Count); // 4 vertices
        Assert.Equal(new Point(0, 0), paths[0][0]);
        Assert.Equal(new Point(3, 0), paths[0][^1]);
    }

    /// <summary>
    /// Integration: scan a 2×1 grid and trace the border between two locations.
    /// The border should be a single vertical edge from (1,0) to (1,1).
    /// </summary>
    [Fact]
    public void IntegrationWithEdgeScanner_TwoLocationBorder()
    {
        var colorMap = new int[2, 1];
        colorMap[0, 0] = 0;
        colorMap[1, 0] = 1;

        var scanResult = EdgeScanner.Scan(colorMap, 2, 1, _ => "area");
        var internalEdges = scanResult.BorderEdges[(0, 1)];

        var paths = BorderTracer.Trace(internalEdges);

        Assert.Single(paths);
        // Single vertical edge: 2 points
        Assert.Equal(2, paths[0].Count);
    }

    /// <summary>
    /// L-shaped border: edges forming an L should be traced as a single path.
    /// </summary>
    [Fact]
    public void LShapedBorder_SinglePath()
    {
        // L shape: (0,0)→(1,0)→(1,1)→(2,1)
        var edges = new List<(Point, Point)>
        {
            (new(0, 0), new(1, 0)),
            (new(1, 0), new(1, 1)),
            (new(1, 1), new(2, 1)),
        };

        var paths = BorderTracer.Trace(edges);

        Assert.Single(paths);
        Assert.Equal(4, paths[0].Count);
    }
}
