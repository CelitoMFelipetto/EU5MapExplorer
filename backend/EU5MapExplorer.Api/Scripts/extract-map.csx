#r "nuget: SixLabors.ImageSharp, 3.1.5"
#r "nuget: Npgsql, 8.0.6"
#r "..\bin\Debug\net10.0\EU5MapExplorer.Api.dll"
#nullable enable

using System.Collections.Concurrent;
using System.Text.Json;
using EU5MapExplorer.Api.Services;
using EU5MapExplorer.Api.Services.GameDataLoad;
using Npgsql;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using GdPoint = EU5MapExplorer.Api.Services.GameDataLoad.Point;

var config = ProjectConfig.Instance;

// ── Parse --subcontinent argument ────────────────────────────────────────────

string? subcontinentFilter = null;
for (int i = 0; i < Args.Count - 1; i++)
{
    if (Args[i] == "--subcontinent")
    {
        subcontinentFilter = Args[i + 1];
        break;
    }
}

if (subcontinentFilter != null)
    Console.WriteLine($"Filter: sub-continent = {subcontinentFilter}");
else
    Console.WriteLine("Filter: none (all sub-continents)");

// ── Step 1: Load location data from DB ──────────────────────────────────────

Console.WriteLine("\nStep 1: Loading locations from database...");

var conn = new NpgsqlConnection(config.ConnectionString);
conn.Open();

// Verify game version exists
int gameVersionId;
using (
    var cmd = new NpgsqlCommand(
        "SELECT \"Id\" FROM game_versions WHERE \"Version\" = @version",
        conn
    )
)
{
    cmd.Parameters.AddWithValue("version", config.GameVersion);
    var result = cmd.ExecuteScalar();
    if (result == null)
    {
        Console.Error.WriteLine(
            $"Error: Game version '{config.GameVersion}' not found. Run load-locations.csx first."
        );
        return;
    }
    gameVersionId = (int)result;
}

// Load all areas with their provinces and locations for this game version.
// We need ALL areas globally (not just the filtered subcontinent) to build a
// complete colorMap — cross-subcontinent borders need both sides visible.

// areaName → (subContinent, { provinceName → [locationNames] })
var allAreas = new Dictionary<string, Dictionary<string, List<string>>>(
    StringComparer.OrdinalIgnoreCase
);
var areaSubContinents = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

// areaName → areaId in DB
var areaDbIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

// provinceName (within area) → provinceId in DB
var provinceDbIds = new Dictionary<(string area, string province), int>();

// locationName → locationId in DB
var locationDbIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

// locationColors: parallel to the index used in colorMap
// (areaName, provinceName, locationName, r, g, b, hex)
var locationColors =
    new List<(string area, string province, string location, byte r, byte g, byte b, string hex)>();

using (
    var cmd = new NpgsqlCommand(
        @"
    SELECT a.""Id"", a.""Name"", a.""SubContinent"",
           p.""Id"", p.""Name"",
           l.""Id"", l.""Name"", l.""Color""
    FROM areas a
    JOIN game_version_areas gva ON gva.""AreaId"" = a.""Id""
    JOIN provinces p ON p.""AreaId"" = a.""Id""
    JOIN locations l ON l.""ProvinceId"" = p.""Id""
    WHERE gva.""GameVersionId"" = @versionId
    ORDER BY a.""Name"", p.""Name"", l.""Name""
",
        conn
    )
)
{
    cmd.Parameters.AddWithValue("versionId", gameVersionId);
    using var reader = cmd.ExecuteReader();
    while (reader.Read())
    {
        var areaId = reader.GetInt32(0);
        var areaName = reader.GetString(1);
        var subContinent = reader.IsDBNull(2) ? null : reader.GetString(2);
        var provinceId = reader.GetInt32(3);
        var provinceName = reader.GetString(4);
        var locationId = reader.GetInt32(5);
        var locationName = reader.GetString(6);
        var colorHex = reader.GetString(7);

        areaDbIds.TryAdd(areaName, areaId);
        areaSubContinents.TryAdd(areaName, subContinent);
        provinceDbIds.TryAdd((areaName, provinceName), provinceId);
        locationDbIds.TryAdd(locationName, locationId);

        if (!allAreas.TryGetValue(areaName, out var provMap))
            allAreas[areaName] = provMap = new(StringComparer.OrdinalIgnoreCase);
        if (!provMap.TryGetValue(provinceName, out var locList))
            provMap[provinceName] = locList = [];
        locList.Add(locationName);

        var r = Convert.ToByte(colorHex.Substring(0, 2), 16);
        var g = Convert.ToByte(colorHex.Substring(2, 2), 16);
        var b = Convert.ToByte(colorHex.Substring(4, 2), 16);
        locationColors.Add((areaName, provinceName, locationName, r, g, b, colorHex));
    }
}

if (locationColors.Count == 0)
{
    Console.Error.WriteLine(
        "Error: No locations found in the database. Run load-locations.csx first."
    );
    conn.Close();
    return;
}

Console.WriteLine($"  → {allAreas.Count} areas, {locationColors.Count} locations loaded from DB.");

// Build fast (R,G,B) → location index dictionary
var colorIndex = new Dictionary<(byte, byte, byte), int>();
for (int i = 0; i < locationColors.Count; i++)
    colorIndex.TryAdd((locationColors[i].r, locationColors[i].g, locationColors[i].b), i);

// Determine which areas are in the filtered subcontinent
var filteredAreaNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
if (subcontinentFilter != null)
{
    foreach (var (aName, subCont) in areaSubContinents)
    {
        if (string.Equals(subCont, subcontinentFilter, StringComparison.OrdinalIgnoreCase))
            filteredAreaNames.Add(aName);
    }
    Console.WriteLine($"  → {filteredAreaNames.Count} areas in filtered subcontinent.");
    if (filteredAreaNames.Count == 0)
    {
        Console.Error.WriteLine($"Error: No areas found for subcontinent '{subcontinentFilter}'.");
        conn.Close();
        return;
    }
}
else
{
    foreach (var aName in allAreas.Keys)
        filteredAreaNames.Add(aName);
}

// ── Step 2: Load locations.png and map every pixel to a location index ──────

Console.WriteLine("\nStep 2: Scanning image...");

var imagePath = Path.Combine(config.DataPath, "in_game", "map_data", "locations.png");
if (!File.Exists(imagePath))
{
    Console.Error.WriteLine($"Error: File not found: {imagePath}");
    conn.Close();
    return;
}

Console.WriteLine($"  Reading: {imagePath}");
var image = Image.Load<Rgba32>(imagePath);
int width = image.Width;
int height = image.Height;
Console.WriteLine($"  Size: {width} x {height} px");

var colorMap = new int[width, height];
for (int y = 0; y < height; y++)
for (int x = 0; x < width; x++)
    colorMap[x, y] = -1;

image.ProcessPixelRows(accessor =>
{
    for (int y = 0; y < accessor.Height; y++)
    {
        var row = accessor.GetRowSpan(y);
        for (int x = 0; x < row.Length; x++)
        {
            ref var px = ref row[x];
            if (colorIndex.TryGetValue((px.R, px.G, px.B), out var idx))
                colorMap[x, y] = idx;
        }
    }
});

image.Dispose();
Console.WriteLine("  Scan complete.");

// ── Step 3: Tagged Edge Scan — build border edges + detect area neighbours ──

Console.WriteLine("\nStep 3: Tagged edge scan (borders + neighbours)...");

var scanResult = EdgeScanner.Scan(colorMap, width, height, idx => locationColors[idx].area);
var borderEdges = scanResult.BorderEdges;
var areaNeighbourPairs = scanResult.AreaNeighbourPairs;

var areaNeighboursMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
foreach (var (a, b) in areaNeighbourPairs)
{
    if (!areaNeighboursMap.TryGetValue(a, out var listA))
        areaNeighboursMap[a] = listA = new();
    if (!areaNeighboursMap.TryGetValue(b, out var listB))
        areaNeighboursMap[b] = listB = new();
    listA.Add(b);
    listB.Add(a);
}
foreach (var list in areaNeighboursMap.Values)
    list.Sort(StringComparer.OrdinalIgnoreCase);

Console.WriteLine(
    $"  {borderEdges.Count} border groups, {areaNeighbourPairs.Count} area neighbour pairs."
);

// ── Step 4: Trace each border group → shared border paths ──────────────────

Console.WriteLine("\nStep 4: Tracing border groups...");

// Only trace borders where at least one side is in the filtered subcontinent.
var relevantBorderKeys = new List<(int, int)>();
foreach (var key in borderEdges.Keys)
{
    var (idxA, idxB) = key;
    bool aRelevant = idxA >= 0 && filteredAreaNames.Contains(locationColors[idxA].area);
    bool bRelevant = idxB >= 0 && filteredAreaNames.Contains(locationColors[idxB].area);
    if (aRelevant || bRelevant)
        relevantBorderKeys.Add(key);
}

Console.WriteLine(
    $"  {relevantBorderKeys.Count} relevant border groups (filtered from {borderEdges.Count})."
);

var tracedBorders = new ConcurrentDictionary<(int, int), int[][][]>();
// rightSideIdx per (idxA, idxB, pathIdx) — computed from original pre-RDP path so
// the unit-length axis-aligned steps give a reliable right-normal sample.
var borderRightSideIdx = new ConcurrentDictionary<(int, int, int), int>();

Parallel.ForEach(
    relevantBorderKeys,
    borderKey =>
    {
        var edges = borderEdges[borderKey];
        var tracedPaths = BorderTracer.Trace(edges);
        // Compute rightSideIdx from original paths before simplification
        for (int pi = 0; pi < tracedPaths.Count; pi++)
        {
            var tp = tracedPaths[pi];
            if (tp.Count >= 2)
            {
                var minPath = new[] { new[] { tp[0].X, tp[0].Y }, new[] { tp[1].X, tp[1].Y } };
                var rsi = BorderSideHelper.GetRightSideIndex(minPath, colorMap, width, height);
                borderRightSideIdx[(borderKey.Item1, borderKey.Item2, pi)] = rsi;
            }
        }
        tracedBorders[borderKey] = tracedPaths
            .Select(p => RdpSimplifier.Simplify(p, 0.7))
            .ToArray();
    }
);

Console.WriteLine($"  {tracedBorders.Count} border groups traced.");

// ── Step 4b: Build named border lookup ─────────────────────────────────────

string BorderKeyName(int idxA, int idxB)
{
    var nameA = idxA >= 0 ? locationColors[idxA].location : "";
    var nameB = idxB >= 0 ? locationColors[idxB].location : "";
    return string.Compare(nameA, nameB, StringComparison.Ordinal) <= 0
        ? $"{nameA}|{nameB}"
        : $"{nameB}|{nameA}";
}

var namedBorders = new Dictionary<string, (string LocA, string LocB, int[][][] Paths)>(
    StringComparer.Ordinal
);
foreach (var (key, paths) in tracedBorders)
{
    var keyName = BorderKeyName(key.Item1, key.Item2);
    var nameA = key.Item1 >= 0 ? locationColors[key.Item1].location : "";
    var nameB = key.Item2 >= 0 ? locationColors[key.Item2].location : "";
    if (string.Compare(nameA, nameB, StringComparison.Ordinal) > 0)
        (nameA, nameB) = (nameB, nameA);
    namedBorders[keyName] = (nameA, nameB, paths);
}

// ── Step 5: Assign borders to locations — build ordered rings ──────────────

Console.WriteLine("\nStep 5: Building border rings for locations...");

// Collect per-location border references: locIdx → [(borderKey, idxKey, pathIdx)]
var locationBorderEntries =
    new Dictionary<int, List<(string KeyName, (int, int) IdxKey, int PathIdx)>>();
foreach (var (idxKey, paths) in tracedBorders)
{
    var keyName = BorderKeyName(idxKey.Item1, idxKey.Item2);
    for (int pi = 0; pi < paths.Length; pi++)
    {
        foreach (var locIdx in new[] { idxKey.Item1, idxKey.Item2 })
        {
            if (locIdx < 0)
                continue;
            if (!locationBorderEntries.TryGetValue(locIdx, out var list))
                locationBorderEntries[locIdx] = list = new();
            list.Add((keyName, idxKey, pi));
        }
    }
}

var locationRings = new ConcurrentDictionary<int, List<BorderRing>>();

Parallel.ForEach(
    locationBorderEntries,
    kvp =>
    {
        var locIdx = kvp.Key;
        if (!filteredAreaNames.Contains(locationColors[locIdx].area))
            return;

        var ringEntries = new List<RingBuilder.BorderEntry>();
        foreach (var (keyName, idxKey, pathIdx) in kvp.Value)
        {
            var paths = tracedBorders[idxKey];
            if (pathIdx >= paths.Length || paths[pathIdx].Length == 0)
                continue;
            var path = paths[pathIdx];
            var rightSideIdx = borderRightSideIdx.GetValueOrDefault(
                (idxKey.Item1, idxKey.Item2, pathIdx), -1);
            var reversed = BorderSideHelper.IsReversedForLocation(locIdx, rightSideIdx);
            var startPt = reversed
                ? new GdPoint(path[^1][0], path[^1][1])
                : new GdPoint(path[0][0], path[0][1]);
            var endPt = reversed
                ? new GdPoint(path[0][0], path[0][1])
                : new GdPoint(path[^1][0], path[^1][1]);
            ringEntries.Add(new RingBuilder.BorderEntry(keyName, pathIdx, reversed, startPt, endPt));
        }

        locationRings[locIdx] = RingBuilder.BuildRings(ringEntries);
    }
);

Console.WriteLine($"  {locationRings.Count} locations have border rings.");

// ── Step 6: Derive province border rings + bounding boxes ──────────────────

Console.WriteLine("\nStep 6: Building province border rings + bounding boxes...");

var areaProvinceSets = new Dictionary<string, Dictionary<string, HashSet<int>>>(
    StringComparer.OrdinalIgnoreCase
);
for (int ci = 0; ci < locationColors.Count; ci++)
{
    var (aName, pName, locName, _, _, _, _) = locationColors[ci];
    if (!areaProvinceSets.TryGetValue(aName, out var provSets))
        areaProvinceSets[aName] = provSets = new(StringComparer.OrdinalIgnoreCase);
    if (!provSets.TryGetValue(pName, out var idxSet))
        provSets[pName] = idxSet = new HashSet<int>();
    idxSet.Add(ci);
}

var provinceBorderData =
    new ConcurrentDictionary<
        (string, string),
        (List<BorderRing> Rings, int MaxN, int MaxS, int MaxE, int MaxW)
    >();

foreach (var aName in filteredAreaNames)
{
    if (!areaProvinceSets.TryGetValue(aName, out var provSets))
        continue;

    Parallel.ForEach(
        provSets,
        provKvp =>
        {
            var provName = provKvp.Key;
            var provIndices = provKvp.Value;

            var provOutlineEntries = new List<(string KeyName, (int, int) IdxKey, int PathIdx)>();
            foreach (var (idxKey, paths) in tracedBorders)
            {
                var (idxA, idxB) = idxKey;
                bool aInProv = idxA >= 0 && provIndices.Contains(idxA);
                bool bInProv = idxB >= 0 && provIndices.Contains(idxB);
                if (aInProv == bInProv)
                    continue;

                var keyName = BorderKeyName(idxA, idxB);
                for (int pi = 0; pi < paths.Length; pi++)
                    provOutlineEntries.Add((keyName, idxKey, pi));
            }

            // Build RingBuilder entries using geometric side detection
            var ringEntries = new List<RingBuilder.BorderEntry>();
            foreach (var (keyName, idxKey, pathIdx) in provOutlineEntries)
            {
                var paths = tracedBorders[idxKey];
                if (pathIdx >= paths.Length || paths[pathIdx].Length == 0)
                    continue;
                var path = paths[pathIdx];
                var insideIdx = provIndices.Contains(idxKey.Item1) ? idxKey.Item1 : idxKey.Item2;
                var rightSideIdx = borderRightSideIdx.GetValueOrDefault(
                    (idxKey.Item1, idxKey.Item2, pathIdx), -1);
                var reversed = BorderSideHelper.IsReversedForLocation(insideIdx, rightSideIdx);
                var startPt = reversed
                    ? new GdPoint(path[^1][0], path[^1][1])
                    : new GdPoint(path[0][0], path[0][1]);
                var endPt = reversed
                    ? new GdPoint(path[0][0], path[0][1])
                    : new GdPoint(path[^1][0], path[^1][1]);
                ringEntries.Add(
                    new RingBuilder.BorderEntry(keyName, pathIdx, reversed, startPt, endPt)
                );
            }

            var rings = RingBuilder.BuildRings(ringEntries);

            int minX = int.MaxValue,
                maxX = int.MinValue,
                minY = int.MaxValue,
                maxY = int.MinValue;
            foreach (var (_, idxKey, pathIdx) in provOutlineEntries)
            {
                var paths = tracedBorders[idxKey];
                foreach (var pt in paths[pathIdx])
                {
                    if (pt[0] < minX)
                        minX = pt[0];
                    if (pt[0] > maxX)
                        maxX = pt[0];
                    if (pt[1] < minY)
                        minY = pt[1];
                    if (pt[1] > maxY)
                        maxY = pt[1];
                }
            }

            provinceBorderData[(aName, provName)] = (rings, minY, maxY, maxX, minX);
        }
    );
}

Console.WriteLine($"  {provinceBorderData.Count} province border rings built.");

// ── Step 7: Write borders + update locations/provinces/areas in DB ──────────

Console.WriteLine("\nStep 7: Writing to database...");

// Upsert all borders
Console.WriteLine("  Upserting borders...");
int bordersInserted = 0;
foreach (var (keyName, (locA, locB, paths)) in namedBorders)
{
    using var cmd = new NpgsqlCommand(
        "INSERT INTO borders (\"Key\", \"LocationA\", \"LocationB\", \"Paths\") "
            + "VALUES (@key, @locA, @locB, @paths::jsonb) "
            + "ON CONFLICT (\"Key\") DO UPDATE SET \"Paths\" = EXCLUDED.\"Paths\"",
        conn
    );
    cmd.Parameters.AddWithValue("key", keyName);
    cmd.Parameters.AddWithValue("locA", locA);
    cmd.Parameters.AddWithValue("locB", locB);
    cmd.Parameters.AddWithValue("paths", JsonSerializer.Serialize(paths));
    bordersInserted += cmd.ExecuteNonQuery();
}
Console.WriteLine($"  {bordersInserted} borders upserted ({namedBorders.Count} total).");

// Update location BorderRings
Console.WriteLine("  Updating location border rings...");
int locsUpdated = 0;
foreach (var (locIdx, rings) in locationRings)
{
    var locName = locationColors[locIdx].location;
    if (!locationDbIds.TryGetValue(locName, out var locDbId))
    {
        Console.WriteLine($"  [WARN] No DB id for location '{locName}' — skipping.");
        continue;
    }
    var borderRings = rings
        .Select(ring => new { Borders = ring.Borders.Select(b => new { b.Key, b.Reversed, b.PathIndex }).ToArray() })
        .ToArray();

    using var cmd = new NpgsqlCommand(
        "UPDATE locations SET \"BorderRings\" = @borderRings::jsonb WHERE \"Id\" = @id",
        conn
    );
    cmd.Parameters.AddWithValue("id", locDbId);
    cmd.Parameters.AddWithValue("borderRings", JsonSerializer.Serialize(borderRings));
    cmd.ExecuteNonQuery();
    locsUpdated++;
}
Console.WriteLine($"  {locsUpdated} locations updated.");

// Update province BorderRings + bounding boxes
Console.WriteLine("  Updating province border rings + bounds...");
int provsUpdated = 0;
foreach (var ((aName, provName), (provRings, maxN, maxS, maxE, maxW)) in provinceBorderData)
{
    if (!provinceDbIds.TryGetValue((aName, provName), out var provDbId))
    {
        Console.WriteLine($"  [WARN] No DB id for province '{provName}' in '{aName}' — skipping.");
        continue;
    }
    var borderRings = provRings
        .Select(ring => new { Borders = ring.Borders.Select(b => new { b.Key, b.Reversed, b.PathIndex }).ToArray() })
        .ToArray();

    using var cmd = new NpgsqlCommand(
        "UPDATE provinces SET \"BorderRings\" = @borderRings::jsonb, "
            + "\"MaxN\" = @maxN, \"MaxS\" = @maxS, \"MaxE\" = @maxE, \"MaxW\" = @maxW "
            + "WHERE \"Id\" = @id",
        conn
    );
    cmd.Parameters.AddWithValue("id", provDbId);
    cmd.Parameters.AddWithValue("borderRings", JsonSerializer.Serialize(borderRings));
    cmd.Parameters.AddWithValue("maxN", maxN);
    cmd.Parameters.AddWithValue("maxS", maxS);
    cmd.Parameters.AddWithValue("maxE", maxE);
    cmd.Parameters.AddWithValue("maxW", maxW);
    cmd.ExecuteNonQuery();
    provsUpdated++;
}
Console.WriteLine($"  {provsUpdated} provinces updated.");

// Update area Neighbors
Console.WriteLine("  Updating area neighbours...");
int areasUpdated = 0;
foreach (var aName in filteredAreaNames)
{
    if (!areaDbIds.TryGetValue(aName, out var areaDbId))
        continue;

    var allDetectedNeighbours = areaNeighboursMap.TryGetValue(aName, out var nbList)
        ? nbList.ToArray()
        : Array.Empty<string>();

    // Merge with existing neighbours (may have been set by a prior run for a different subcontinent)
    string[] existingNeighbours;
    using (var cmd = new NpgsqlCommand("SELECT \"Neighbors\" FROM areas WHERE \"Id\" = @id", conn))
    {
        cmd.Parameters.AddWithValue("id", areaDbId);
        var json = (string?)cmd.ExecuteScalar();
        existingNeighbours =
            json != null
                ? JsonSerializer.Deserialize<string[]>(json) ?? Array.Empty<string>()
                : Array.Empty<string>();
    }
    var mergedNeighbours = existingNeighbours
        .Concat(allDetectedNeighbours)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    using (
        var cmd = new NpgsqlCommand(
            "UPDATE areas SET \"Neighbors\" = @neighbors::jsonb WHERE \"Id\" = @id",
            conn
        )
    )
    {
        cmd.Parameters.AddWithValue("id", areaDbId);
        cmd.Parameters.AddWithValue("neighbors", JsonSerializer.Serialize(mergedNeighbours));
        cmd.ExecuteNonQuery();
    }
    areasUpdated++;
}
Console.WriteLine($"  {areasUpdated} areas updated with neighbours.");

// Also update cross-subcontinent neighbour back-links
foreach (var (a, b) in areaNeighbourPairs)
{
    // If one side is outside the filtered set, update that area's neighbours too
    var outsideName = !filteredAreaNames.Contains(a)
        ? a
        : (!filteredAreaNames.Contains(b) ? b : null);
    if (outsideName == null)
        continue;
    if (!areaDbIds.TryGetValue(outsideName, out var outsideId))
        continue;

    var insideName = outsideName == a ? b : a;

    string[] existingNb;
    using (var cmd = new NpgsqlCommand("SELECT \"Neighbors\" FROM areas WHERE \"Id\" = @id", conn))
    {
        cmd.Parameters.AddWithValue("id", outsideId);
        var json = (string?)cmd.ExecuteScalar();
        existingNb =
            json != null
                ? JsonSerializer.Deserialize<string[]>(json) ?? Array.Empty<string>()
                : Array.Empty<string>();
    }
    if (!existingNb.Contains(insideName, StringComparer.OrdinalIgnoreCase))
    {
        var updated = existingNb
            .Append(insideName)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        using var cmd = new NpgsqlCommand(
            "UPDATE areas SET \"Neighbors\" = @neighbors::jsonb WHERE \"Id\" = @id",
            conn
        );
        cmd.Parameters.AddWithValue("id", outsideId);
        cmd.Parameters.AddWithValue("neighbors", JsonSerializer.Serialize(updated));
        cmd.ExecuteNonQuery();
        Console.WriteLine($"  [LINK] {outsideName} ↔ {insideName}");
    }
}

conn.Close();
Console.WriteLine("\nAll done.");
