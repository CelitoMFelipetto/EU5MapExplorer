namespace EU5MapExplorer.Api.Services.GameDataLoad;

/// <summary>
/// Single-pass edge scan over a color map that identifies border edges between
/// adjacent locations and detects area neighbour pairs.
/// </summary>
public static class EdgeScanner
{
    /// <summary>Result of scanning edges from a color map.</summary>
    public sealed class ScanResult
    {
        /// <summary>
        /// Border edges grouped by (min(idxA, idxB), max(idxA, idxB)).
        /// -1 represents map edge / unmapped pixels.
        /// </summary>
        public Dictionary<(int, int), List<(Point V1, Point V2)>> BorderEdges { get; } = [];

        /// <summary>Detected area neighbour pairs (both directions, sorted alphabetically).</summary>
        public HashSet<(string, string)> AreaNeighbourPairs { get; } = [];
    }

    /// <summary>
    /// Scan the color map and extract all border edges and area neighbour pairs.
    /// </summary>
    /// <param name="colorMap">2D array [width, height] of location indices (-1 = unmapped).</param>
    /// <param name="width">Map width in pixels.</param>
    /// <param name="height">Map height in pixels.</param>
    /// <param name="getArea">Function to look up area name for a location index.</param>
    /// <returns>Scan result containing border edges as a dictionary with the key being a tuple of the two location color values and the value being a list of border edges.</returns>
    public static ScanResult Scan(int[,] colorMap, int width, int height, Func<int, string> getArea)
    {
        var result = new ScanResult();

        void AddEdge(int idxA, int idxB, Point v1, Point v2)
        {
            var key = idxA < idxB ? (idxA, idxB) : (idxB, idxA);
            if (!result.BorderEdges.TryGetValue(key, out var edges))
                result.BorderEdges[key] = edges = [];
            edges.Add((v1, v2));

            if (idxA >= 0 && idxB >= 0)
            {
                var aI = getArea(idxA);
                var aJ = getArea(idxB);
                if (!string.Equals(aI, aJ, StringComparison.OrdinalIgnoreCase))
                {
                    var pair =
                        string.Compare(aI, aJ, StringComparison.OrdinalIgnoreCase) < 0
                            ? (aI, aJ)
                            : (aJ, aI);
                    result.AreaNeighbourPairs.Add(pair);
                }
            }
        }

        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            var here = colorMap[x, y];
            if (here < 0)
                continue;

            // Right neighbour
            if (x < width - 1)
            {
                var right = colorMap[x + 1, y];
                if (here != right)
                    AddEdge(here, right, new(x + 1, y), new(x + 1, y + 1));
            }
            else
            {
                AddEdge(here, -1, new(x + 1, y), new(x + 1, y + 1));
            }

            // Bottom neighbour
            if (y < height - 1)
            {
                var below = colorMap[x, y + 1];
                if (here != below)
                    AddEdge(here, below, new(x, y + 1), new(x + 1, y + 1));
            }
            else
            {
                AddEdge(here, -1, new(x, y + 1), new(x + 1, y + 1));
            }

            // Top map edge
            if (y == 0)
                AddEdge(here, -1, new(x, 0), new(x + 1, 0));

            // Left map edge
            if (x == 0)
                AddEdge(here, -1, new(0, y), new(0, y + 1));
        }

        return result;
    }
}
