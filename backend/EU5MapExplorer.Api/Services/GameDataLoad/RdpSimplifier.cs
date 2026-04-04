namespace EU5MapExplorer.Api.Services.GameDataLoad;

/// <summary>Ramer-Douglas-Peucker polyline simplification.</summary>
public static class RdpSimplifier
{
    /// <summary>
    /// Simplify a polyline by removing points within <paramref name="epsilon"/>
    /// perpendicular distance of the line between retained neighbors.
    /// </summary>
    public static int[][] Simplify(IReadOnlyList<Point> pts, double epsilon)
    {
        if (pts.Count <= 2)
            return pts.Select(p => new[] { p.X, p.Y }).ToArray();

        var keep = new bool[pts.Count];
        keep[0] = true;
        keep[pts.Count - 1] = true;

        var stack = new Stack<(int Start, int End)>();
        stack.Push((0, pts.Count - 1));

        while (stack.Count > 0)
        {
            var (start, end) = stack.Pop();
            if (end - start < 2)
                continue;

            double maxDist = 0;
            int maxIdx = start;
            double ax = pts[start].X,
                ay = pts[start].Y;
            double bx = pts[end].X,
                by = pts[end].Y;
            double dx = bx - ax,
                dy = by - ay;
            double lenSq = dx * dx + dy * dy;

            for (int i = start + 1; i < end; i++)
            {
                double px = pts[i].X - ax,
                    py = pts[i].Y - ay;
                double dist =
                    lenSq == 0
                        ? Math.Sqrt(px * px + py * py)
                        : Math.Abs(px * dy - py * dx) / Math.Sqrt(lenSq);
                if (dist > maxDist)
                {
                    maxDist = dist;
                    maxIdx = i;
                }
            }

            if (maxDist > epsilon)
            {
                keep[maxIdx] = true;
                stack.Push((start, maxIdx));
                stack.Push((maxIdx, end));
            }
        }

        var result = new List<int[]>();
        for (int i = 0; i < pts.Count; i++)
            if (keep[i])
                result.Add([pts[i].X, pts[i].Y]);
        return [.. result];
    }
}
