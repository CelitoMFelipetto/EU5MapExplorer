namespace EU5MapExplorer.Api.Services.GameDataLoad;

/// <summary>
/// Reconstructs closed polygon paths from border ring references and a border cache.
/// This mirrors the frontend's <c>reconstructPaths()</c> logic in MapService.
/// </summary>
public static class PolygonReconstructor
{
    /// <summary>
    /// Given a set of border rings and a cache of border paths, reconstruct
    /// the closed polygon(s) by concatenating border segments in ring order.
    /// </summary>
    /// <param name="rings">Ordered rings of border references.</param>
    /// <param name="borderCache">Border key → array of path segments. Each segment is int[][].</param>
    /// <param name="mapHeight">Map height for Y-flip (leaflet coordinate transform). Pass 0 to skip flip.</param>
    /// <returns>One polygon path per ring, each being a list of [lat, lng] points.</returns>
    public static List<List<int[]>> Reconstruct(
        IReadOnlyList<BorderRing> rings,
        IReadOnlyDictionary<string, int[][][]> borderCache,
        int mapHeight = 0
    )
    {
        var result = new List<List<int[]>>();

        foreach (var ring in rings)
        {
            var points = new List<int[]>();

            foreach (var bref in ring.Borders)
            {
                if (!borderCache.TryGetValue(bref.Key, out var borderPaths))
                    continue;

                foreach (var seg in borderPaths)
                {
                    var ordered = bref.Reversed ? seg.Reverse().ToArray() : seg;
                    int start = points.Count > 0 ? 1 : 0;
                    for (int i = start; i < ordered.Length; i++)
                    {
                        var pt = ordered[i];
                        if (mapHeight > 0)
                            points.Add([mapHeight - pt[1], pt[0]]);
                        else
                            points.Add([pt[0], pt[1]]);
                    }
                }
            }

            result.Add(points);
        }

        return result;
    }
}
