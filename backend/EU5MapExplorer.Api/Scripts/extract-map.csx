#r "nuget: SixLabors.ImageSharp, 3.1.5"
#r "nuget: Tamar.Clausewitz, 0.5.1"
#r "nuget: Npgsql, 8.0.6"
#nullable enable

using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Npgsql;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Tamar.Clausewitz;

// ── Find project dir by walking up from CWD ──────────────────────────────────

var projectDir = Directory.GetCurrentDirectory();
while (projectDir != null && !File.Exists(Path.Combine(projectDir, "EU5MapExplorer.Api.csproj")))
    projectDir = Directory.GetParent(projectDir)?.FullName;

if (projectDir == null)
{
    Console.Error.WriteLine(
        "Error: Could not locate EU5MapExplorer.Api.csproj. Run from inside the project directory."
    );
    return;
}

// ── Read EU5:DataPath, EU5:Version and ConnectionStrings:Default ──────────────

string? dataPath = null;
string? gameVersion = null;
string? connectionString = null;

foreach (var filename in new[] { "appsettings.Development.json", "appsettings.json" })
{
    var fullPath = Path.Combine(projectDir, filename);
    if (!File.Exists(fullPath))
        continue;
    using var doc = JsonDocument.Parse(File.ReadAllText(fullPath));

    if (
        dataPath == null
        && doc.RootElement.TryGetProperty("EU5", out var eu5)
        && eu5.TryGetProperty("DataPath", out var dp)
        && !string.IsNullOrWhiteSpace(dp.GetString())
    )
    {
        dataPath = dp.GetString();
    }

    if (
        gameVersion == null
        && doc.RootElement.TryGetProperty("EU5", out var eu5v)
        && eu5v.TryGetProperty("Version", out var ver)
        && !string.IsNullOrWhiteSpace(ver.GetString())
    )
    {
        gameVersion = ver.GetString();
    }

    if (
        connectionString == null
        && doc.RootElement.TryGetProperty("ConnectionStrings", out var cs)
        && cs.TryGetProperty("Default", out var csDefault)
        && !string.IsNullOrWhiteSpace(csDefault.GetString())
    )
    {
        connectionString = csDefault.GetString();
    }
}

if (dataPath == null)
{
    Console.Error.WriteLine(
        "Error: EU5:DataPath is not configured in appsettings.Development.json."
    );
    return;
}

gameVersion ??= "1.1.10";
Console.WriteLine($"Game version: {gameVersion}");

if (connectionString == null)
{
    Console.Error.WriteLine("Error: ConnectionStrings:Default is not configured.");
    return;
}

// ── Parse --subcontinent argument ─────────────────────────────────────────────

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

// ── Step 1: Parse definitions.txt → ALL areas → provinces + locations ─────────

Console.WriteLine("\nStep 1: Parsing definitions.txt...");

var definitionsPath = Path.Combine(dataPath, "in_game", "map_data", "definitions.txt");
if (!File.Exists(definitionsPath))
{
    Console.Error.WriteLine($"Error: File not found: {definitionsPath}");
    return;
}

var definitionsRoot = Interpreter.InterpretText(File.ReadAllText(definitionsPath));

// allAreas: areaName → { provinceName → [locationNames] }
var allAreas = new Dictionary<string, Dictionary<string, List<string>>>(
    StringComparer.OrdinalIgnoreCase
);

// areaAncestry: areaName → (continent, subContinent, region) — filled while descending
var areaAncestry = new Dictionary<
    string,
    (string? Continent, string? SubContinent, string? Region)
>(StringComparer.OrdinalIgnoreCase);

// The hierarchy is: continent → sub-continent → region → area → province → locations.
// We queue (clause, continent, subContinent, region) and fill ancestors as we descend.
var bfsClauseQueue =
    new Queue<(dynamic Clause, string? Continent, string? SubContinent, string? Region)>();
foreach (var c in definitionsRoot.Clauses.Cast<dynamic>())
    bfsClauseQueue.Enqueue((c, null, null, null));

while (bfsClauseQueue.Count > 0)
{
    var (candidate, continent, subContinent, region) = bfsClauseQueue.Dequeue();
    if (string.IsNullOrEmpty((string?)candidate.Name))
        continue;

    // Check if any direct child has tokens — if so, candidate is an area clause
    bool isAreaClause = false;
    foreach (var child in candidate.Clauses)
    {
        var childTokens = child.Tokens;
        bool hasTokens = false;
        foreach (var _ in childTokens)
        {
            hasTokens = true;
            break;
        }
        if (hasTokens)
        {
            isAreaClause = true;
            break;
        }
    }

    if (isAreaClause)
    {
        var provinces = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var provinceClause in candidate.Clauses)
        {
            var provinceName = (string?)provinceClause.Name;
            if (string.IsNullOrEmpty(provinceName))
                continue;

            var locations = ((IEnumerable<dynamic>)provinceClause.Tokens)
                .Select(t => (string?)t.Value)
                .Where(v => !string.IsNullOrEmpty(v))
                .Cast<string>()
                .ToList();

            provinces[provinceName] = locations;
        }
        var areaName = (string)candidate.Name;
        allAreas[areaName] = provinces;
        areaAncestry[areaName] = (continent, subContinent, region);
    }
    else
    {
        // Not an area yet — shift the current clause name into the next ancestry slot
        var name = (string)candidate.Name;
        string? newCont = continent,
            newSub = subContinent,
            newReg = region;
        if (continent == null)
            newCont = name;
        else if (subContinent == null)
            newSub = name;
        else
            newReg = name;

        foreach (var child in candidate.Clauses)
            bfsClauseQueue.Enqueue((child, newCont, newSub, newReg));
    }
}

Console.WriteLine($"  → {allAreas.Count} areas found.");

// Keep an unfiltered copy so Step 4 can build a complete colour map for all 803 areas.
// This ensures cross-sub-continent borders are visible during neighbour detection.
var allAreasGlobal = new Dictionary<string, Dictionary<string, List<string>>>(
    allAreas,
    StringComparer.OrdinalIgnoreCase
);

if (subcontinentFilter != null)
{
    var before = allAreas.Count;
    var toRemove = allAreas
        .Keys.Where(k =>
            !areaAncestry.TryGetValue(k, out var anc)
            || !string.Equals(
                anc.SubContinent,
                subcontinentFilter,
                StringComparison.OrdinalIgnoreCase
            )
        )
        .ToList();
    foreach (var k in toRemove)
    {
        allAreas.Remove(k);
        areaAncestry.Remove(k);
    }
    Console.WriteLine($"  → {allAreas.Count} areas kept after filtering (was {before}).");
}

foreach (var (aName, provs) in allAreas)
    Console.WriteLine($"    {aName}: {provs.Count} provinces");

// ── Step 2: Parse named_locations/*.txt → location name → RGB hex ─────────────

Console.WriteLine("\nStep 2: Parsing named_locations...");

var namedLocationsDir = Path.Combine(dataPath, "in_game", "map_data", "named_locations");
if (!Directory.Exists(namedLocationsDir))
{
    Console.Error.WriteLine($"Error: Directory not found: {namedLocationsDir}");
    return;
}

var colorLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

foreach (var file in Directory.GetFiles(namedLocationsDir, "*.txt").OrderBy(f => f))
{
    var locRoot = Interpreter.InterpretText(File.ReadAllText(file));
    foreach (var binding in locRoot.Bindings)
    {
        if (!string.IsNullOrEmpty(binding.Name) && !string.IsNullOrEmpty(binding.Value))
            colorLookup[binding.Name] = binding.Value.PadLeft(6, '0');
    }
}

Console.WriteLine($"  → {colorLookup.Count} color entries loaded.");

// ── Step 3: Parse location_templates.txt → per-location properties ────────────

Console.WriteLine("\nStep 3: Parsing location_templates.txt...");

var templatesPath = Path.Combine(dataPath, "in_game", "map_data", "location_templates.txt");
if (!File.Exists(templatesPath))
{
    Console.Error.WriteLine($"Error: File not found: {templatesPath}");
    return;
}

var templateLookup = new Dictionary<string, LocationTemplate>(StringComparer.OrdinalIgnoreCase);
var templatesRoot = Interpreter.InterpretText(File.ReadAllText(templatesPath));

foreach (var locClause in templatesRoot.Clauses)
{
    if (string.IsNullOrEmpty(locClause.Name))
        continue;

    string? topography = null,
        climate = null,
        vegetation = null,
        rawMaterial = null;
    foreach (var b in locClause.Bindings)
    {
        switch (b.Name)
        {
            case "topography":
                topography = b.Value;
                break;
            case "climate":
                climate = b.Value;
                break;
            case "vegetation":
                vegetation = b.Value;
                break;
            case "raw_material":
                rawMaterial = b.Value;
                break;
        }
    }
    if (topography != null && climate != null)
        templateLookup[locClause.Name] = new LocationTemplate(
            topography,
            climate,
            vegetation,
            rawMaterial
        );
}

Console.WriteLine($"  → {templateLookup.Count} location templates loaded.");

// ── Step 4: Build global location color list (all areas) ──────────────────────

Console.WriteLine("\nStep 4: Building global location color list...");

// (areaName, provinceName, locationName, r, g, b, hex)
var locationColors =
    new List<(string area, string province, string location, byte r, byte g, byte b, string hex)>();

foreach (var (aName, provinces) in allAreasGlobal)
{
    foreach (var (pName, locs) in provinces)
    {
        foreach (var loc in locs)
        {
            if (!colorLookup.TryGetValue(loc, out var hex))
            {
                Console.WriteLine($"  [WARN] No color for '{loc}' in {aName}/{pName} — skipping.");
                continue;
            }
            var r = Convert.ToByte(hex.Substring(0, 2), 16);
            var g = Convert.ToByte(hex.Substring(2, 2), 16);
            var b = Convert.ToByte(hex.Substring(4, 2), 16);
            locationColors.Add((aName, pName, loc, r, g, b, hex));
        }
    }
}

Console.WriteLine($"  → {locationColors.Count} locations total.");

// Build fast (R,G,B) → location index dictionary
var colorIndex = new Dictionary<(byte, byte, byte), int>();
for (int i = 0; i < locationColors.Count; i++)
    colorIndex.TryAdd((locationColors[i].r, locationColors[i].g, locationColors[i].b), i);

// ── Step 5: Load locations.png and map every pixel to a location index ────────

Console.WriteLine("\nStep 5: Scanning image...");

var imagePath = Path.Combine(dataPath, "in_game", "map_data", "locations.png");
if (!File.Exists(imagePath))
{
    Console.Error.WriteLine($"Error: File not found: {imagePath}");
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

// ── Step 5b: Detect area neighbours ──────────────────────────────────────────

Console.WriteLine("\nStep 5b: Detecting area neighbours...");

var areaNeighbourPairs = new HashSet<(string, string)>();

for (int y = 0; y < height; y++)
for (int x = 0; x < width - 1; x++)
{
    var i = colorMap[x, y];
    var j = colorMap[x + 1, y];
    if (i < 0 || j < 0 || i == j)
        continue;
    var aI = locationColors[i].area;
    var aJ = locationColors[j].area;
    if (aI == aJ)
        continue;
    var pair = string.Compare(aI, aJ, StringComparison.OrdinalIgnoreCase) < 0 ? (aI, aJ) : (aJ, aI);
    areaNeighbourPairs.Add(pair);
}

for (int y = 0; y < height - 1; y++)
for (int x = 0; x < width; x++)
{
    var i = colorMap[x, y];
    var j = colorMap[x, y + 1];
    if (i < 0 || j < 0 || i == j)
        continue;
    var aI = locationColors[i].area;
    var aJ = locationColors[j].area;
    if (aI == aJ)
        continue;
    var pair = string.Compare(aI, aJ, StringComparison.OrdinalIgnoreCase) < 0 ? (aI, aJ) : (aJ, aI);
    areaNeighbourPairs.Add(pair);
}

// areaName → sorted list of neighbour names
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

Console.WriteLine($"  → {areaNeighbourPairs.Count} neighbour pairs found.");

// ── Shared tracing helpers ────────────────────────────────────────────────────

var cwOrder = new Dictionary<(int, int), (int, int)[]>
{
    [(0, -1)] = new[] { (1, 0), (0, -1), (-1, 0) },
    [(1, 0)] = new[] { (0, 1), (1, 0), (0, -1) },
    [(0, 1)] = new[] { (-1, 0), (0, 1), (1, 0) },
    [(-1, 0)] = new[] { (0, -1), (-1, 0), (0, 1) },
};

int[][][] TracePaths(Func<int, int, bool> isMember)
{
    var adj = new Dictionary<(int, int), HashSet<(int, int)>>();
    void Link((int, int) a, (int, int) b)
    {
        if (!adj.ContainsKey(a))
            adj[a] = new HashSet<(int, int)>();
        if (!adj.ContainsKey(b))
            adj[b] = new HashSet<(int, int)>();
        adj[a].Add(b);
        adj[b].Add(a);
    }

    for (int py = 0; py < height; py++)
    for (int px = 0; px < width; px++)
    {
        if (!isMember(px, py))
            continue;
        if (py == 0 || !isMember(px, py - 1))
            Link((px, py), (px + 1, py));
        if (py == height - 1 || !isMember(px, py + 1))
            Link((px, py + 1), (px + 1, py + 1));
        if (px == 0 || !isMember(px - 1, py))
            Link((px, py), (px, py + 1));
        if (px == width - 1 || !isMember(px + 1, py))
            Link((px + 1, py), (px + 1, py + 1));
    }

    var remaining = adj.ToDictionary(kvp => kvp.Key, kvp => new HashSet<(int, int)>(kvp.Value));
    void UseEdge((int, int) a, (int, int) b)
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

    var tracedPaths = new List<int[][]>();
    while (remaining.Count > 0)
    {
        var start = remaining.Keys.OrderBy(v => v.Item2).ThenBy(v => v.Item1).First();
        var firstNext = remaining[start].OrderBy(v => v.Item2).ThenBy(v => v.Item1).First();
        UseEdge(start, firstNext);

        var pts = new List<(int, int)> { start };
        var prev = start;
        var curr = firstNext;

        while (curr != start)
        {
            pts.Add(curr);
            if (!remaining.ContainsKey(curr))
                break;
            var arrDir = (curr.Item1 - prev.Item1, curr.Item2 - prev.Item2);
            (int, int) next = default;
            bool found = false;
            foreach (var tryDir in cwOrder[arrDir])
            {
                var cand = (curr.Item1 + tryDir.Item1, curr.Item2 + tryDir.Item2);
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

        var simplified = new List<int[]>();
        for (int i = 0; i < pts.Count; i++)
        {
            if (i == 0 || i == pts.Count - 1)
            {
                simplified.Add(new[] { pts[i].Item1, pts[i].Item2 });
                continue;
            }
            var (ax, ay) = pts[i - 1];
            var (bx, by) = pts[i];
            var (cx, cy) = pts[i + 1];
            if ((bx - ax) * (cy - by) - (by - ay) * (cx - bx) != 0)
                simplified.Add(new[] { bx, by });
        }
        tracedPaths.Add(simplified.ToArray());
    }
    return tracedPaths.ToArray();
}

// ── Load city positions ───────────────────────────────────────────────────────

Console.WriteLine("\nLoading city positions...");

var cityLocatorsPath = Path.Combine(
    dataPath,
    "in_game",
    "gfx",
    "map",
    "map_objects",
    "generated_map_object_locators_city.txt"
);

var cityPositions = new Dictionary<string, (double x, double y)>(StringComparer.OrdinalIgnoreCase);

if (!File.Exists(cityLocatorsPath))
{
    Console.WriteLine(
        "  [WARN] City locator file not found — city_position will be null for all locations."
    );
}
else
{
    var instanceRx = new Regex(
        @"\{\s*id=(\w+)\s+position=\{\s*([\d.]+)\s+[\d.]+\s+([\d.]+)\s*\}",
        RegexOptions.Singleline
    );

    foreach (Match m in instanceRx.Matches(File.ReadAllText(cityLocatorsPath)))
    {
        var id = m.Groups[1].Value;
        var gameX = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
        var gameZ = double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
        cityPositions[id] = (gameX, height - gameZ);
    }
    Console.WriteLine($"  → {cityPositions.Count} city positions loaded.");
}

// ── Load location ranks ───────────────────────────────────────────────────────

Console.WriteLine("\nLoading location ranks...");

var citiesPath = Path.Combine(
    dataPath,
    "main_menu",
    "setup",
    "start",
    "07_cities_and_buildings.txt"
);

var rankLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

if (!File.Exists(citiesPath))
{
    Console.WriteLine(
        "  [WARN] Cities file not found — all ranks will default to 'rural_settlement'."
    );
}
else
{
    var citiesRoot = Interpreter.InterpretText(File.ReadAllText(citiesPath));
    var locationsClause = citiesRoot.FindClauseDepthFirst("locations");
    if (locationsClause != null)
    {
        foreach (var locClause in locationsClause.Clauses)
        {
            if (string.IsNullOrEmpty(locClause.Name))
                continue;
            var rankBinding = locClause.Bindings.FirstOrDefault(b => b.Name == "rank");
            if (!string.IsNullOrEmpty(rankBinding?.Value))
                rankLookup[locClause.Name] = rankBinding.Value;
        }
    }
    Console.WriteLine($"  → {rankLookup.Count} location ranks loaded.");
}

// ── Process each area: trace paths → compute hash ─────────────────────────────

Console.WriteLine($"\nStep 6: Tracing paths for all areas ({allAreas.Count} areas)...");

// Per-location index sets per area (for province path tracing), excluding lakes
var areaProvinceSets = new Dictionary<string, Dictionary<string, HashSet<int>>>(
    StringComparer.OrdinalIgnoreCase
);
for (int ci = 0; ci < locationColors.Count; ci++)
{
    var (aName, pName, locName, _, _, _, _) = locationColors[ci];
    if (templateLookup.TryGetValue(locName, out var tmpl) && tmpl.Topography == "lakes")
        continue;
    if (!areaProvinceSets.TryGetValue(aName, out var provSets))
        areaProvinceSets[aName] = provSets = new(StringComparer.OrdinalIgnoreCase);
    if (!provSets.TryGetValue(pName, out var idxSet))
        provSets[pName] = idxSet = new HashSet<int>();
    idxSet.Add(ci);
}

// Only trace locations that belong to the filtered areas (not the global set).
// colorMap still covers all 803 areas so neighbour detection is unaffected.
var relevantIndices = new HashSet<int>();
for (int ci = 0; ci < locationColors.Count; ci++)
{
    var (aName, _, _, _, _, _, _) = locationColors[ci];
    if (allAreas.ContainsKey(aName))
        relevantIndices.Add(ci);
}

Console.WriteLine($"  Tracing {relevantIndices.Count} location paths (filtered from {locationColors.Count} global)...");
var locationResults = new int[locationColors.Count][][][];
Parallel.For(
    0,
    locationColors.Count,
    ci =>
    {
        if (relevantIndices.Contains(ci))
            locationResults[ci] = TracePaths((px, py) => colorMap[px, py] == ci);
    }
);
Console.WriteLine("  Location paths done.");

// Build per-area location index sets for province tracing (need area → province → indices)
var processedAreas = new List<AreaData>();

foreach (var (aName, provMap) in allAreas)
{
    var provinceNames = provMap.Keys.ToArray();
    Console.WriteLine($"  Tracing {provinceNames.Length} province paths for {aName}...");

    var provinceResults = new int[provinceNames.Length][][][];
    var provSets = areaProvinceSets.TryGetValue(aName, out var ps) ? ps : new();

    Parallel.For(
        0,
        provinceNames.Length,
        pi =>
        {
            var pName = provinceNames[pi];
            var indices = provSets.TryGetValue(pName, out var idxSet) ? idxSet : new HashSet<int>();
            provinceResults[pi] = TracePaths((px, py) => indices.Contains(colorMap[px, py]));
        }
    );

    // Build province data
    var provincesData = new Dictionary<
        string,
        (int[][][] ProvincePaths, List<LocationData> Locations)
    >(StringComparer.OrdinalIgnoreCase);

    for (int pi = 0; pi < provinceNames.Length; pi++)
    {
        var pName = provinceNames[pi];
        var locationDataList = new List<LocationData>();

        foreach (var locName in provMap[pName])
        {
            // Find location index
            var ci = locationColors.FindIndex(l =>
                string.Equals(l.area, aName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(l.location, locName, StringComparison.OrdinalIgnoreCase)
            );

            if (ci < 0)
                continue; // was skipped (no color)

            var hex = locationColors[ci].hex;
            templateLookup.TryGetValue(locName, out var tmpl);
            var rank = rankLookup.TryGetValue(locName, out var r) ? r : "rural_settlement";
            double? cx = null,
                cy = null;
            if (cityPositions.TryGetValue(locName, out var cp))
            {
                cx = cp.x;
                cy = cp.y;
            }

            locationDataList.Add(
                new LocationData(
                    locName,
                    hex,
                    tmpl?.Topography ?? "unknown",
                    tmpl?.Climate ?? "unknown",
                    tmpl?.Vegetation,
                    tmpl?.RawMaterial,
                    rank,
                    cx,
                    cy,
                    locationResults[ci]
                )
            );
        }

        provincesData[pName] = (provinceResults[pi], locationDataList);
    }

    var neighbours = areaNeighboursMap.TryGetValue(aName, out var nb)
        ? nb.ToArray()
        : Array.Empty<string>();
    var (areaCont, areaSub, areaReg) = areaAncestry.TryGetValue(aName, out var anc)
        ? anc
        : (null, null, null);
    processedAreas.Add(new AreaData(aName, areaCont, areaSub, areaReg, neighbours, provincesData));
}

Console.WriteLine("  Path tracing complete.");

// ── Step 7: Compute content hash per area ─────────────────────────────────────

Console.WriteLine("\nStep 7: Computing content hashes...");

string ComputeAreaHash(AreaData area)
{
    // Hash = SHA-256 over the deterministic JSON of sorted provinces + sorted locations.
    // Neighbours are excluded: they are topology that grows incrementally as sub-continents
    // are extracted, and should not cause a new area row to be created on each run.
    var canonical = new
    {
        provinces = area
            .Provinces.OrderBy(kvp => kvp.Key, StringComparer.Ordinal)
            .Select(kvp => new
            {
                name = kvp.Key,
                paths = kvp.Value.ProvincePaths,
                locations = kvp
                    .Value.Locations.OrderBy(l => l.Name, StringComparer.Ordinal)
                    .Select(l => new
                    {
                        l.Name,
                        l.Color,
                        l.Topography,
                        l.Climate,
                        l.Vegetation,
                        l.RawMaterial,
                        l.Rank,
                        l.CityX,
                        l.CityY,
                        l.Paths,
                    })
                    .ToArray(),
            })
            .ToArray(),
    };

    var json = JsonSerializer.Serialize(canonical);
    var hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(json));
    return Convert.ToHexString(hash).ToLowerInvariant();
}

var areaHashes = processedAreas.ToDictionary(
    a => a.Name,
    ComputeAreaHash,
    StringComparer.OrdinalIgnoreCase
);
Console.WriteLine($"  → {areaHashes.Count} hashes computed.");

// ── Step 8: Write to DB ───────────────────────────────────────────────────────

Console.WriteLine("\nStep 8: Writing to database...");

var conn = new NpgsqlConnection(connectionString);
conn.Open();

// Upsert game_version
int gameVersionId;
using (
    var cmd = new NpgsqlCommand(
        "INSERT INTO game_versions (\"Version\", \"ExtractedAt\") "
            + "VALUES (@version, NOW() AT TIME ZONE 'UTC') "
            + "ON CONFLICT (\"Version\") DO UPDATE SET \"ExtractedAt\" = EXCLUDED.\"ExtractedAt\" "
            + "RETURNING \"Id\"",
        conn
    )
)
{
    cmd.Parameters.AddWithValue("version", gameVersion);
    gameVersionId = (int)cmd.ExecuteScalar()!;
}
Console.WriteLine($"  game_version id={gameVersionId} ({gameVersion})");

// Remove existing game_version_areas links only for the areas in the current run.
// This allows re-runs of a single sub-continent without unlinking other sub-continents.
var currentAreaNames = processedAreas.Select(a => a.Name).ToArray();
using (
    var cmd = new NpgsqlCommand(
        "DELETE FROM game_version_areas gva "
            + "USING areas a "
            + "WHERE gva.\"AreaId\" = a.\"Id\" "
            + "  AND gva.\"GameVersionId\" = @versionId "
            + "  AND a.\"Name\" = ANY(@names)",
        conn
    )
)
{
    cmd.Parameters.AddWithValue("versionId", gameVersionId);
    cmd.Parameters.AddWithValue("names", currentAreaNames);
    cmd.ExecuteNonQuery();
}

int areasInserted = 0,
    areasReused = 0;

foreach (var area in processedAreas)
{
    var hash = areaHashes[area.Name];

    // All neighbours detected for this area (local + cross-sub-continent).
    var allDetectedNeighbours = areaNeighboursMap.TryGetValue(area.Name, out var nbList)
        ? nbList.ToArray()
        : Array.Empty<string>();

    // Neighbours NOT in this run's filter — may or may not be in DB yet.
    var crossNeighbourNames = allDetectedNeighbours.Where(n => !allAreas.ContainsKey(n)).ToArray();

    using var tx = conn.BeginTransaction();

    try
    {
        // Resolve which cross-sub-continent neighbours already exist in DB.
        var knownCrossNeighbours = new List<(int Id, string Name)>();
        foreach (var crossName in crossNeighbourNames)
        {
            using var cmd = new NpgsqlCommand(
                "SELECT \"Id\" FROM areas WHERE \"Name\" = @name",
                conn,
                tx
            );
            cmd.Parameters.AddWithValue("name", crossName);
            var result = cmd.ExecuteScalar();
            if (result != null)
                knownCrossNeighbours.Add(((int)result, crossName));
        }

        // Full neighbours for this area = local (in-run) + cross ones already in DB.
        var fullNeighbours = allDetectedNeighbours
            .Where(n => allAreas.ContainsKey(n))
            .Concat(knownCrossNeighbours.Select(c => c.Name))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        // Check for existing area row. Priority:
        // 1. Already linked to this game version by name → always reuse (handles hash changes on re-run)
        // 2. Same name + same content hash across any version → reuse for cross-version dedup
        int? existingAreaId = null;
        using (
            var cmd = new NpgsqlCommand(
                "SELECT a.\"Id\" FROM areas a "
                    + "JOIN game_version_areas gva ON gva.\"AreaId\" = a.\"Id\" "
                    + "WHERE a.\"Name\" = @name AND gva.\"GameVersionId\" = @versionId "
                    + "LIMIT 1",
                conn,
                tx
            )
        )
        {
            cmd.Parameters.AddWithValue("name", area.Name);
            cmd.Parameters.AddWithValue("versionId", gameVersionId);
            var result = cmd.ExecuteScalar();
            if (result != null)
                existingAreaId = (int)result;
        }
        if (existingAreaId == null)
        {
            using var cmd = new NpgsqlCommand(
                "SELECT \"Id\" FROM areas WHERE \"Name\" = @name AND \"ContentHash\" = @hash LIMIT 1",
                conn,
                tx
            );
            cmd.Parameters.AddWithValue("name", area.Name);
            cmd.Parameters.AddWithValue("hash", hash);
            var result = cmd.ExecuteScalar();
            if (result != null)
                existingAreaId = (int)result;
        }

        int areaId;
        if (existingAreaId.HasValue)
        {
            areaId = existingAreaId.Value;
            areasReused++;

            // Merge newly detected neighbours into existing row (preserves any already stored).
            string[] existingNeighbours;
            using (
                var cmd = new NpgsqlCommand(
                    "SELECT \"Neighbors\" FROM areas WHERE \"Id\" = @id",
                    conn,
                    tx
                )
            )
            {
                cmd.Parameters.AddWithValue("id", areaId);
                var json = (string?)cmd.ExecuteScalar();
                existingNeighbours =
                    json != null
                        ? JsonSerializer.Deserialize<string[]>(json) ?? Array.Empty<string>()
                        : Array.Empty<string>();
            }
            var mergedNeighbours = existingNeighbours
                .Concat(fullNeighbours)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            using (
                var cmd = new NpgsqlCommand(
                    "UPDATE areas SET \"Neighbors\" = @neighbors::jsonb, \"ContentHash\" = @hash WHERE \"Id\" = @id",
                    conn,
                    tx
                )
            )
            {
                cmd.Parameters.AddWithValue("id", areaId);
                cmd.Parameters.AddWithValue("hash", hash);
                cmd.Parameters.AddWithValue(
                    "neighbors",
                    JsonSerializer.Serialize(mergedNeighbours)
                );
                cmd.ExecuteNonQuery();
            }

            Console.WriteLine($"  [REUSE] {area.Name} (id={areaId})");
        }
        else
        {
            // Insert new area row with full neighbour list.
            using (
                var cmd = new NpgsqlCommand(
                    "INSERT INTO areas (\"Name\", \"ContentHash\", \"Continent\", \"SubContinent\", \"Region\", \"Neighbors\") "
                        + "VALUES (@name, @hash, @continent, @subContinent, @region, @neighbors::jsonb) "
                        + "RETURNING \"Id\"",
                    conn,
                    tx
                )
            )
            {
                cmd.Parameters.AddWithValue("name", area.Name);
                cmd.Parameters.AddWithValue("hash", hash);
                cmd.Parameters.AddWithValue("continent", (object?)area.Continent ?? DBNull.Value);
                cmd.Parameters.AddWithValue(
                    "subContinent",
                    (object?)area.SubContinent ?? DBNull.Value
                );
                cmd.Parameters.AddWithValue("region", (object?)area.Region ?? DBNull.Value);
                cmd.Parameters.AddWithValue("neighbors", JsonSerializer.Serialize(fullNeighbours));
                areaId = (int)cmd.ExecuteScalar()!;
            }

            // Insert provinces and locations.
            foreach (var (provinceName, (provincePaths, locationDataList)) in area.Provinces)
            {
                int provinceId;
                using (
                    var cmd = new NpgsqlCommand(
                        "INSERT INTO provinces (\"AreaId\", \"Name\", \"Paths\") "
                            + "VALUES (@areaId, @name, @paths::jsonb) "
                            + "RETURNING \"Id\"",
                        conn,
                        tx
                    )
                )
                {
                    cmd.Parameters.AddWithValue("areaId", areaId);
                    cmd.Parameters.AddWithValue("name", provinceName);
                    cmd.Parameters.AddWithValue("paths", JsonSerializer.Serialize(provincePaths));
                    provinceId = (int)cmd.ExecuteScalar()!;
                }

                foreach (var loc in locationDataList)
                {
                    using var cmd = new NpgsqlCommand(
                        "INSERT INTO locations "
                            + "(\"ProvinceId\",\"Name\",\"Color\",\"Topography\",\"Climate\",\"Vegetation\","
                            + "\"RawMaterial\",\"Rank\",\"CityX\",\"CityY\",\"Paths\") "
                            + "VALUES "
                            + "(@provinceId,@name,@color,@topography,@climate,@vegetation,"
                            + "@rawMaterial,@rank,@cityX,@cityY,@paths::jsonb)",
                        conn,
                        tx
                    );

                    cmd.Parameters.AddWithValue("provinceId", provinceId);
                    cmd.Parameters.AddWithValue("name", loc.Name);
                    cmd.Parameters.AddWithValue("color", loc.Color);
                    cmd.Parameters.AddWithValue("topography", loc.Topography);
                    cmd.Parameters.AddWithValue("climate", loc.Climate);
                    cmd.Parameters.AddWithValue(
                        "vegetation",
                        (object?)loc.Vegetation ?? DBNull.Value
                    );
                    cmd.Parameters.AddWithValue(
                        "rawMaterial",
                        (object?)loc.RawMaterial ?? DBNull.Value
                    );
                    cmd.Parameters.AddWithValue("rank", loc.Rank);
                    cmd.Parameters.AddWithValue("cityX", (object?)loc.CityX ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("cityY", (object?)loc.CityY ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("paths", JsonSerializer.Serialize(loc.Paths));
                    cmd.ExecuteNonQuery();
                }
            }

            areasInserted++;
            Console.WriteLine(
                $"  [NEW]   {area.Name} (id={areaId}, {area.Provinces.Count} provinces)"
            );
        }

        // Add back-links: for each cross-sub-continent neighbour already in DB,
        // merge this area's name into their Neighbors list.
        foreach (var (crossId, crossName) in knownCrossNeighbours)
        {
            string[] crossExisting;
            using (
                var cmd = new NpgsqlCommand(
                    "SELECT \"Neighbors\" FROM areas WHERE \"Id\" = @id",
                    conn,
                    tx
                )
            )
            {
                cmd.Parameters.AddWithValue("id", crossId);
                var json = (string?)cmd.ExecuteScalar();
                crossExisting =
                    json != null
                        ? JsonSerializer.Deserialize<string[]>(json) ?? Array.Empty<string>()
                        : Array.Empty<string>();
            }
            if (!crossExisting.Contains(area.Name, StringComparer.OrdinalIgnoreCase))
            {
                var updated = crossExisting
                    .Append(area.Name)
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                using var cmd = new NpgsqlCommand(
                    "UPDATE areas SET \"Neighbors\" = @neighbors::jsonb WHERE \"Id\" = @id",
                    conn,
                    tx
                );
                cmd.Parameters.AddWithValue("id", crossId);
                cmd.Parameters.AddWithValue("neighbors", JsonSerializer.Serialize(updated));
                cmd.ExecuteNonQuery();
                Console.WriteLine($"  [LINK]  {crossName} ↔ {area.Name}");
            }
        }

        // Link this area to the game version.
        using (
            var cmd = new NpgsqlCommand(
                "INSERT INTO game_version_areas (\"GameVersionId\", \"AreaId\") "
                    + "VALUES (@versionId, @areaId) "
                    + "ON CONFLICT DO NOTHING",
                conn,
                tx
            )
        )
        {
            cmd.Parameters.AddWithValue("versionId", gameVersionId);
            cmd.Parameters.AddWithValue("areaId", areaId);
            cmd.ExecuteNonQuery();
        }

        tx.Commit();
    }
    catch (Exception ex)
    {
        tx.Rollback();
        Console.Error.WriteLine($"  [ERROR] {area.Name}: {ex.Message}");
        throw;
    }
}

Console.WriteLine($"\nAll done. {areasInserted} areas inserted, {areasReused} areas reused.");

// ── Type declarations (must appear after all top-level statements in .csx) ────

record AreaData(
    string Name,
    string? Continent,
    string? SubContinent,
    string? Region,
    string[] Neighbours,
    Dictionary<string, (int[][][] ProvincePaths, List<LocationData> Locations)> Provinces
);

record LocationData(
    string Name,
    string Color,
    string Topography,
    string Climate,
    string? Vegetation,
    string? RawMaterial,
    string Rank,
    double? CityX,
    double? CityY,
    int[][][] Paths
);

record LocationTemplate(string Topography, string Climate, string? Vegetation, string? RawMaterial);
