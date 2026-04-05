namespace EU5MapExplorer.Api.Services.GameDataLoad;

/// <summary>
/// Assembles ordered border rings for a location/province from its border path segments.
/// Each ring is a sequence of (borderKey, reversed) references whose paths, when concatenated
/// in order, form a closed polygon.
/// </summary>
public static class RingBuilder
{
    /// <summary>A border path segment that a location participates in.</summary>
    public readonly record struct BorderEntry(
        string Key,
        int PathIndex,
        bool Reversed,
        Point Start,
        Point End
    );

    /// <summary>
    /// Build closed rings from a set of border entries. Each entry has a start/end vertex
    /// (already accounting for the reversed flag). The algorithm walks from each unused entry's
    /// end vertex to the next entry's start vertex to form chains.
    /// </summary>
    public static List<BorderRing> BuildRings(IReadOnlyList<BorderEntry> entries)
    {
        var used = new bool[entries.Count];

        // Build lookup: startVertex → list of entry indices that start there
        var startLookup = new Dictionary<Point, List<int>>();
        for (int i = 0; i < entries.Count; i++)
        {
            if (!startLookup.TryGetValue(entries[i].Start, out var list))
                startLookup[entries[i].Start] = list = [];
            list.Add(i);
        }

        var rings = new List<BorderRing>();
        for (int startEi = 0; startEi < entries.Count; startEi++)
        {
            if (used[startEi])
                continue;
            var refs = new List<BorderRef>();
            var currentEi = startEi;

            while (!used[currentEi])
            {
                used[currentEi] = true;
                var entry = entries[currentEi];
                refs.Add(new BorderRef(entry.Key, entry.Reversed, entry.PathIndex));

                // Find next unused entry whose start matches this entry's end
                int nextEi = -1;
                if (startLookup.TryGetValue(entry.End, out var candidates))
                {
                    foreach (var ci in candidates)
                    {
                        if (!used[ci])
                        {
                            nextEi = ci;
                            break;
                        }
                    }
                }
                if (nextEi < 0)
                    break;
                currentEi = nextEi;
            }

            if (refs.Count > 0)
                rings.Add(new BorderRing([.. refs]));
        }

        return rings;
    }
}
