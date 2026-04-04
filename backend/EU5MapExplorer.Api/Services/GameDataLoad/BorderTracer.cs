namespace EU5MapExplorer.Api.Services.GameDataLoad;

/// <summary>
/// Traces continuous polyline paths from a set of grid edges using
/// a clockwise-priority walk algorithm.
/// </summary>
public static class BorderTracer
{
    /// <summary>Clockwise turn-priority lookup for the walk algorithm.</summary>
    private static readonly Dictionary<(int, int), (int, int)[]> CwOrder = new()
    {
        [(0, -1)] = [(1, 0), (0, -1), (-1, 0)],
        [(1, 0)] = [(0, 1), (1, 0), (0, -1)],
        [(0, 1)] = [(-1, 0), (0, 1), (1, 0)],
        [(-1, 0)] = [(0, -1), (-1, 0), (0, 1)],
    };

    /// <summary>
    /// Given a list of unit-length grid edges (vertex pairs), trace them into
    /// continuous polyline paths using the clockwise-priority walk.
    /// Returns one or more paths (disconnected components produce separate paths).
    /// </summary>
    public static List<List<Point>> Trace(IReadOnlyList<(Point V1, Point V2)> edges)
    {
        // Build adjacency
        var adj = new Dictionary<Point, HashSet<Point>>();
        void Link(Point a, Point b)
        {
            if (!adj.TryGetValue(a, out var sa))
                adj[a] = sa = [];
            if (!adj.TryGetValue(b, out var sb))
                adj[b] = sb = [];
            sa.Add(b);
            sb.Add(a);
        }
        foreach (var (v1, v2) in edges)
            Link(v1, v2);

        var remaining = adj.ToDictionary(kvp => kvp.Key, kvp => new HashSet<Point>(kvp.Value));
        void UseEdge(Point a, Point b)
        {
            if (remaining.TryGetValue(a, out var sa))
            {
                sa.Remove(b);
                if (sa.Count == 0)
                    remaining.Remove(a);
            }
            if (remaining.TryGetValue(b, out var sb))
            {
                sb.Remove(a);
                if (sb.Count == 0)
                    remaining.Remove(b);
            }
        }

        var paths = new List<List<Point>>();
        while (remaining.Count > 0)
        {
            // Prefer endpoints (degree 1) so open chains are walked in one pass
            // rather than being split at an interior starting vertex.
            var candidates = remaining.Where(kvp => kvp.Value.Count == 1).Select(kvp => kvp.Key);
            if (!candidates.Any())
                candidates = remaining.Keys;
            var start = candidates.OrderBy(v => v.Y).ThenBy(v => v.X).First();
            var firstNext = remaining[start].OrderBy(v => v.Y).ThenBy(v => v.X).First();
            UseEdge(start, firstNext);

            var pts = new List<Point> { start };
            var prev = start;
            var curr = firstNext;

            while (curr != start)
            {
                pts.Add(curr);
                if (!remaining.ContainsKey(curr))
                    break;
                var arrDir = (curr.X - prev.X, curr.Y - prev.Y);
                Point next = default;
                bool found = false;
                foreach (var tryDir in CwOrder[arrDir])
                {
                    var cand = new Point(curr.X + tryDir.Item1, curr.Y + tryDir.Item2);
                    if (remaining.TryGetValue(curr, out var nb) && nb.Contains(cand))
                    {
                        next = cand;
                        found = true;
                        break;
                    }
                }
                if (!found)
                    break;
                UseEdge(curr, next);
                prev = curr;
                curr = next;
            }

            paths.Add(pts);
        }

        return paths;
    }
}
