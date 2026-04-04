using EU5MapExplorer.Api.Services.GameDataLoad;
using Newtonsoft.Json;
using Xunit.Abstractions;

namespace EU5MapExplorer.Api.Tests.Services.GameDataLoad;

/// <summary>
/// End-to-end tests: scan a small color map → trace borders → build rings → reconstruct polygons.
/// Verifies the full pipeline produces correct closed shapes.
/// </summary>
public class EndToEndTests
{
    private readonly ITestOutputHelper _output;

    public EndToEndTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// 3×3 grid with a 2×2 block (location 0) surrounded by 5 pixels of location 1.
    ///
    ///   1 1 1
    ///   1 0 0
    ///   1 0 0
    ///
    /// Location 0 should produce a single closed square polygon.
    /// Location 1 should produce a polygon wrapping around location 0.
    /// </summary>
    [Fact]
    public void TwoLocations_3x3Grid_CorrectPolygons()
    {
        var colorMap = new int[3, 3];
        // Fill with location 1
        for (int y = 0; y < 3; y++)
        for (int x = 0; x < 3; x++)
            colorMap[x, y] = 1;
        // Location 0: bottom-right 2×2 block
        colorMap[1, 1] = 0;
        colorMap[2, 1] = 0;
        colorMap[1, 2] = 0;
        colorMap[2, 2] = 0;

        var locations = new[] { ("area_a", "loc0"), ("area_a", "loc1") };

        // Step 1: Edge scan
        var scanResult = EdgeScanner.Scan(colorMap, 3, 3, idx => locations[idx].Item1);

        // Step 2: Trace all borders
        var tracedBorders = new Dictionary<string, int[][][]>();
        foreach (var (key, edges) in scanResult.BorderEdges)
        {
            var paths = BorderTracer.Trace(edges);
            var nameA = key.Item1 >= 0 ? locations[key.Item1].Item2 : "";
            var nameB = key.Item2 >= 0 ? locations[key.Item2].Item2 : "";
            if (string.Compare(nameA, nameB, StringComparison.Ordinal) > 0)
                (nameA, nameB) = (nameB, nameA);
            var borderKey = $"{nameA}|{nameB}";

            tracedBorders[borderKey] = paths
                .Select(p => p.Select(pt => new[] { pt.X, pt.Y }).ToArray())
                .ToArray();
        }

        // Step 3: Build rings for location 0
        var loc0Entries = BuildEntriesForLocation(0, scanResult, tracedBorders, locations, colorMap, 3, 3);
        var loc0Rings = RingBuilder.BuildRings(loc0Entries);

        // Step 4: Reconstruct polygon
        var loc0Polys = PolygonReconstructor.Reconstruct(loc0Rings, tracedBorders, mapHeight: 0);

        // Location 0 should be a single closed polygon (2×2 square)
        Assert.Single(loc0Polys);
        var poly = loc0Polys[0];

        // The polygon should form a closed shape — first point == last point or
        // the points trace the boundary of pixels [1..3, 1..3]
        // Expected boundary vertices: (1,1), (3,1), (3,3), (1,3) — a 2×2 square
        var xs = poly.Select(p => p[0]).ToList();
        var ys = poly.Select(p => p[1]).ToList();
        Assert.Equal(1, xs.Min());
        Assert.Equal(3, xs.Max());
        Assert.Equal(1, ys.Min());
        Assert.Equal(3, ys.Max());

        // Verify it's actually closed: first and last points should be the same
        // (or the ring should form a loop by connecting the last border back to the first)
        Assert.True(poly.Count >= 4, $"Expected at least 4 points for a square, got {poly.Count}");
    }

    /// <summary>
    /// 6×3 horizontal grid: 0 0 1 1 1 1
    ///                      1 1 1 1 1 1
    ///                      1 1 1 1 0 0
    /// Location 0 has two disconnected regions → should produce two rings.
    /// Location 1 should produce a single ring.
    /// </summary>
    [Fact]
    public void DisconnectedLocation_ProducesTwoRings()
    {
        var colorMap = new int[6, 3];
        for (int y = 0; y < 3; y++)
        for (int x = 0; x < 6; x++)
            colorMap[x, y] = 1;
        colorMap[0, 0] = 0;
        colorMap[1, 0] = 0;
        colorMap[4, 2] = 0;
        colorMap[5, 2] = 0;

        var locations = new[] { ("area_a", "loc0"), ("area_a", "loc1") };

        var scanResult = EdgeScanner.Scan(colorMap, 6, 3, idx => locations[idx].Item1);
        _output.WriteLine("CEL: scanResult");
        _output.WriteLine(JsonConvert.SerializeObject(scanResult));

        var tracedBorders = new Dictionary<string, int[][][]>();
        foreach (var (key, edges) in scanResult.BorderEdges)
        {
            var paths = BorderTracer.Trace(edges);
            var nameA = key.Item1 >= 0 ? locations[key.Item1].Item2 : "";
            var nameB = key.Item2 >= 0 ? locations[key.Item2].Item2 : "";
            if (string.Compare(nameA, nameB, StringComparison.Ordinal) > 0)
                (nameA, nameB) = (nameB, nameA);
            var borderKey = $"{nameA}|{nameB}";

            tracedBorders[borderKey] = paths
                .Select(p => p.Select(pt => new[] { pt.X, pt.Y }).ToArray())
                .ToArray();
        }

        // Assert traced borders are correct before building rings.
        // loc0|loc1 must be exactly 2 paths (one per disconnected region of loc0).
        // The second region's 3 edges share no branching point — they form a single
        // open chain (4,3)→(4,2)→(5,2)→(6,2) and must NOT be split into two paths.
        Assert.True(
            tracedBorders.TryGetValue("loc0|loc1", out var loc01Paths),
            "Expected border key 'loc0|loc1' to exist"
        );
        Assert.Equal(2, loc01Paths!.Length);

        // First path: the L-shaped border around the top-left 2×1 region of loc0
        var firstPath = loc01Paths[0];
        Assert.Equal(4, firstPath.Length);
        Assert.Equal([2, 0], firstPath[0]);
        Assert.Equal([2, 1], firstPath[1]);
        Assert.Equal([1, 1], firstPath[2]);
        Assert.Equal([0, 1], firstPath[3]);

        // Second path: the single open chain for the bottom-right 2×1 region of loc0.
        // Expected: [4,3]→[4,2]→[5,2]→[6,2] (or reversed), all 4 vertices in one path.
        var secondPath = loc01Paths[1];
        Assert.Equal(4, secondPath.Length);
        var secondPathSet = secondPath.Select(p => (p[0], p[1])).ToHashSet();
        Assert.Contains((4, 3), secondPathSet);
        Assert.Contains((4, 2), secondPathSet);
        Assert.Contains((5, 2), secondPathSet);
        Assert.Contains((6, 2), secondPathSet);

        // Build rings for location A (index 0)
        var locAEntries = BuildEntriesForLocation(0, scanResult, tracedBorders, locations, colorMap, 6, 3);
        var locARings = RingBuilder.BuildRings(locAEntries);

        // Location 0 occupies two disconnected 2×1 pixels → should have 3 borders 2 against
        // the edges and 1 agaist the Location 1.
        _output.WriteLine("CEL: tracedBorders");
        _output.WriteLine(JsonConvert.SerializeObject(tracedBorders));
        _output.WriteLine("CEL: locARings");
        _output.WriteLine(JsonConvert.SerializeObject(locARings));
        _output.WriteLine("CEL: locAEntries");
        _output.WriteLine(JsonConvert.SerializeObject(locAEntries));
        Assert.Equal(2, locARings.Count);
    }

    /// <summary>
    /// 2×2 grid, 4 different locations. Each location should get a single ring
    /// forming a 1×1 square.
    /// </summary>
    [Fact]
    public void FourLocations_2x2_EachGetsSquareRing()
    {
        // [0][1]
        // [2][3]
        var colorMap = new int[2, 2];
        colorMap[0, 0] = 0;
        colorMap[1, 0] = 1;
        colorMap[0, 1] = 2;
        colorMap[1, 1] = 3;

        var locations = new[] { ("a", "loc0"), ("a", "loc1"), ("a", "loc2"), ("a", "loc3") };

        var scanResult = EdgeScanner.Scan(colorMap, 2, 2, idx => locations[idx].Item1);

        var tracedBorders = TraceBordersFromScan(scanResult, locations);

        // Verify each location gets exactly 1 ring
        for (int locIdx = 0; locIdx < 4; locIdx++)
        {
            var entries = BuildEntriesForLocation(locIdx, scanResult, tracedBorders, locations, colorMap, 2, 2);
            var rings = RingBuilder.BuildRings(entries);
            Assert.Single(rings);

            // Reconstruct and verify it's a 1×1 square
            var polys = PolygonReconstructor.Reconstruct(rings, tracedBorders, mapHeight: 0);
            Assert.Single(polys);
            Assert.True(
                polys[0].Count >= 4,
                $"Location {locIdx}: expected at least 4 points, got {polys[0].Count}"
            );
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private static Dictionary<string, int[][][]> TraceBordersFromScan(
        EdgeScanner.ScanResult scanResult,
        (string area, string name)[] locations
    )
    {
        var tracedBorders = new Dictionary<string, int[][][]>();
        foreach (var (key, edges) in scanResult.BorderEdges)
        {
            var paths = BorderTracer.Trace(edges);
            var nameA = key.Item1 >= 0 ? locations[key.Item1].name : "";
            var nameB = key.Item2 >= 0 ? locations[key.Item2].name : "";
            if (string.Compare(nameA, nameB, StringComparison.Ordinal) > 0)
                (nameA, nameB) = (nameB, nameA);
            var borderKey = $"{nameA}|{nameB}";

            tracedBorders[borderKey] = paths
                .Select(p => p.Select(pt => new[] { pt.X, pt.Y }).ToArray())
                .ToArray();
        }
        return tracedBorders;
    }

    private static List<RingBuilder.BorderEntry> BuildEntriesForLocation(
        int locIdx,
        EdgeScanner.ScanResult scanResult,
        Dictionary<string, int[][][]> tracedBorders,
        (string area, string name)[] locations,
        int[,] colorMap,
        int width,
        int height
    )
    {
        var entries = new List<RingBuilder.BorderEntry>();

        foreach (var (key, _) in scanResult.BorderEdges)
        {
            // Check if this location participates in this border
            if (key.Item1 != locIdx && key.Item2 != locIdx)
                continue;

            var nameA = key.Item1 >= 0 ? locations[key.Item1].name : "";
            var nameB = key.Item2 >= 0 ? locations[key.Item2].name : "";
            if (string.Compare(nameA, nameB, StringComparison.Ordinal) > 0)
                (nameA, nameB) = (nameB, nameA);
            var borderKey = $"{nameA}|{nameB}";

            if (!tracedBorders.TryGetValue(borderKey, out var paths))
                continue;

            for (int pi = 0; pi < paths.Length; pi++)
            {
                var path = paths[pi];
                if (path.Length == 0)
                    continue;

                // Determine direction geometrically: sample which pixel is on the right
                // side of the traced path, then reverse if this location is NOT on the right.
                var rightSideIdx = BorderSideHelper.GetRightSideIndex(path, colorMap, width, height);
                var reversed = BorderSideHelper.IsReversedForLocation(locIdx, rightSideIdx);

                var startPt = reversed
                    ? new Point(path[^1][0], path[^1][1])
                    : new Point(path[0][0], path[0][1]);
                var endPt = reversed
                    ? new Point(path[0][0], path[0][1])
                    : new Point(path[^1][0], path[^1][1]);

                entries.Add(new RingBuilder.BorderEntry(borderKey, pi, reversed, startPt, endPt));
            }
        }

        return entries;
    }
}
