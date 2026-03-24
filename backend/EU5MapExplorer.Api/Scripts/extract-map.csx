#r "nuget: SixLabors.ImageSharp, 3.1.5"
#r "nuget: Tamar.Clausewitz, 0.5.1"
#nullable enable

using System.Collections.Concurrent;
using System.Text.Json;
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

// ── Read EU5:DataPath from appsettings ───────────────────────────────────────

string? dataPath = null;
foreach (var filename in new[] { "appsettings.Development.json", "appsettings.json" })
{
    var fullPath = Path.Combine(projectDir, filename);
    if (!File.Exists(fullPath))
        continue;
    using var doc = JsonDocument.Parse(File.ReadAllText(fullPath));
    if (
        doc.RootElement.TryGetProperty("EU5", out var eu5)
        && eu5.TryGetProperty("DataPath", out var dp)
        && !string.IsNullOrWhiteSpace(dp.GetString())
    )
    {
        dataPath = dp.GetString();
        break;
    }
}

if (dataPath == null)
{
    Console.Error.WriteLine(
        "Error: EU5:DataPath is not configured in appsettings.Development.json."
    );
    return;
}

// ── Step 1: Parse definitions.txt → svealand_area provinces + locations ───────

Console.WriteLine("Step 1: Parsing definitions.txt...");

var definitionsPath = Path.Combine(dataPath, "in_game", "map_data", "definitions.txt");
if (!File.Exists(definitionsPath))
{
    Console.Error.WriteLine($"Error: File not found: {definitionsPath}");
    return;
}

var definitionsRoot = Interpreter.InterpretText(File.ReadAllText(definitionsPath));
var svealandClause = definitionsRoot.FindClauseDepthFirst("svealand_area");
if (svealandClause == null)
{
    Console.Error.WriteLine("Error: svealand_area not found in definitions.txt.");
    return;
}

// provinceName → ordered list of location names
// Province blocks contain bare identifiers: uppland_province = { stockholm norrtalje ... }
// Tamar.Clausewitz: sub-blocks are in .Clauses, bare scalar tokens are in .Tokens
var svealandProvinces = new Dictionary<string, List<string>>();
foreach (var provinceClause in svealandClause.Clauses)
{
    var provinceName = provinceClause.Name;
    if (string.IsNullOrEmpty(provinceName))
        continue;

    var locations = provinceClause
        .Tokens.Select(t => t.Value)
        .Where(v => !string.IsNullOrEmpty(v))
        .ToList();

    svealandProvinces[provinceName] = locations!;
}

Console.WriteLine($"  → {svealandProvinces.Count} provinces:");
foreach (var (prov, locs) in svealandProvinces)
    Console.WriteLine($"    {prov}: {locs.Count} locations");

// ── Step 2: Parse named_locations/*.txt → location name → RGB hex ─────────────

Console.WriteLine("\nStep 2: Parsing named_locations...");

var namedLocationsDir = Path.Combine(dataPath, "in_game", "map_data", "named_locations");
if (!Directory.Exists(namedLocationsDir))
{
    Console.Error.WriteLine($"Error: Directory not found: {namedLocationsDir}");
    return;
}

// location_name → normalized 6-char hex string (leading zeros restored)
var colorLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

foreach (var file in Directory.GetFiles(namedLocationsDir, "*.txt").OrderBy(f => f))
{
    var locRoot = Interpreter.InterpretText(File.ReadAllText(file));
    // Each line is a Binding: stockholm = dda910
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

// location name → { topography, climate, vegetation?, raw_material? }
record LocationTemplate(string Topography, string Climate, string? Vegetation, string? RawMaterial);

var templateLookup = new Dictionary<string, LocationTemplate>(StringComparer.OrdinalIgnoreCase);

var templatesRoot = Interpreter.InterpretText(File.ReadAllText(templatesPath));
foreach (var locClause in templatesRoot.Clauses)
{
    if (string.IsNullOrEmpty(locClause.Name))
        continue;

    string? topography = null;
    string? climate = null;
    string? vegetation = null;
    string? rawMaterial = null;

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

// ── Step 4: Match svealand locations to their colors ─────────────────────────

// Flat list keeping province context: (province, location, r, g, b, hex)
var locationColors =
    new List<(string province, string location, byte r, byte g, byte b, string hex)>();

foreach (var (province, locations) in svealandProvinces)
{
    foreach (var loc in locations)
    {
        if (!colorLookup.TryGetValue(loc, out var hex))
        {
            Console.WriteLine($"  [WARN] No color found for location '{loc}' — skipping.");
            continue;
        }
        var r = Convert.ToByte(hex.Substring(0, 2), 16);
        var g = Convert.ToByte(hex.Substring(2, 2), 16);
        var b = Convert.ToByte(hex.Substring(4, 2), 16);
        locationColors.Add((province, loc, r, g, b, hex));
    }
}

Console.WriteLine($"\n  → {locationColors.Count} locations with colors.");

// Build province → set of location indices (used later for province path tracing).
// Lakes are excluded so the province outline hugs only land pixels.
var provinceIndexSets = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);
for (int ci = 0; ci < locationColors.Count; ci++)
{
    var (prov, locName, _, _, _, _) = locationColors[ci];
    if (templateLookup.TryGetValue(locName, out var tmpl) && tmpl.Topography == "lakes")
        continue;
    if (!provinceIndexSets.ContainsKey(prov))
        provinceIndexSets[prov] = new HashSet<int>();
    provinceIndexSets[prov].Add(ci);
}

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

// Build fast (R,G,B) → location index dictionary
var colorIndex = new Dictionary<(byte, byte, byte), int>();
for (int i = 0; i < locationColors.Count; i++)
    colorIndex.TryAdd((locationColors[i].r, locationColors[i].g, locationColors[i].b), i);

// Single-pass scan
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

// ── Shared tracing helpers ────────────────────────────────────────────────────

// Right-hand-rule turn order (clockwise winding) — read-only, safe to share across threads
var cwOrder = new Dictionary<(int, int), (int, int)[]>
{
    [(0, -1)] = new[] { (1, 0), (0, -1), (-1, 0) }, // arrived N → try E, N, W
    [(1,  0)] = new[] { (0, 1), (1,  0), (0, -1) }, // arrived E → try S, E, N
    [(0,  1)] = new[] { (-1, 0), (0, 1), (1,  0) }, // arrived S → try W, S, E
    [(-1, 0)] = new[] { (0, -1), (-1, 0), (0,  1) }, // arrived W → try N, W, S
};

// Shared edge-graph tracer: isMember(x,y) returns true when the pixel at (x,y)
// belongs to the shape being traced.
int[][][] TracePaths(Func<int, int, bool> isMember)
{
    var adj = new Dictionary<(int, int), HashSet<(int, int)>>();
    void Link((int, int) a, (int, int) b)
    {
        if (!adj.ContainsKey(a)) adj[a] = new HashSet<(int, int)>();
        if (!adj.ContainsKey(b)) adj[b] = new HashSet<(int, int)>();
        adj[a].Add(b);
        adj[b].Add(a);
    }

    for (int py = 0; py < height; py++)
    for (int px = 0; px < width; px++)
    {
        if (!isMember(px, py)) continue;
        if (py == 0          || !isMember(px, py - 1)) Link((px, py),     (px + 1, py));
        if (py == height - 1 || !isMember(px, py + 1)) Link((px, py + 1), (px + 1, py + 1));
        if (px == 0          || !isMember(px - 1, py)) Link((px, py),     (px, py + 1));
        if (px == width  - 1 || !isMember(px + 1, py)) Link((px + 1, py), (px + 1, py + 1));
    }

    var remaining = adj.ToDictionary(kvp => kvp.Key, kvp => new HashSet<(int, int)>(kvp.Value));
    void UseEdge((int, int) a, (int, int) b)
    {
        if (remaining.TryGetValue(a, out var sa)) { sa.Remove(b); if (sa.Count == 0) remaining.Remove(a); }
        if (remaining.TryGetValue(b, out var sb)) { sb.Remove(a); if (sb.Count == 0) remaining.Remove(b); }
    }

    var tracedPaths = new List<int[][]>();
    while (remaining.Count > 0)
    {
        var start     = remaining.Keys.OrderBy(v => v.Item2).ThenBy(v => v.Item1).First();
        var firstNext = remaining[start].OrderBy(v => v.Item2).ThenBy(v => v.Item1).First();
        UseEdge(start, firstNext);

        var pts  = new List<(int, int)> { start };
        var prev = start;
        var curr = firstNext;

        while (curr != start)
        {
            pts.Add(curr);
            if (!remaining.ContainsKey(curr)) break;

            var arrDir = (curr.Item1 - prev.Item1, curr.Item2 - prev.Item2);
            (int, int) next = default;
            bool found = false;
            foreach (var tryDir in cwOrder[arrDir])
            {
                var cand = (curr.Item1 + tryDir.Item1, curr.Item2 + tryDir.Item2);
                if (remaining.TryGetValue(curr, out var nb) && nb.Contains(cand))
                    { next = cand; found = true; break; }
            }
            if (!found) break;
            UseEdge(curr, next);
            prev = curr;
            curr = next;
        }

        var simplified = new List<int[]>();
        for (int i = 0; i < pts.Count; i++)
        {
            if (i == 0 || i == pts.Count - 1) { simplified.Add(new[] { pts[i].Item1, pts[i].Item2 }); continue; }
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

// ── Step 6: Trace boundary paths for every location (parallelised) ────────────

Console.WriteLine(
    $"\nStep 6: Tracing location paths ({locationColors.Count} locations, {Environment.ProcessorCount} logical cores)..."
);

// Pre-allocated results array — each slot written by exactly one thread, no contention
var locationResults = new int[locationColors.Count][][][];

Parallel.For(0, locationColors.Count, ci =>
{
    locationResults[ci] = TracePaths((px, py) => colorMap[px, py] == ci);
    Console.WriteLine(
        $"  [{ci + 1}/{locationColors.Count}] {locationColors[ci].location} ({locationColors[ci].hex}) → {locationResults[ci].Length} path(s)"
    );
});

// ── Step 7: Trace boundary paths for every province (parallelised) ────────────

var provinceNames = svealandProvinces.Keys.ToArray();

Console.WriteLine(
    $"\nStep 7: Tracing province paths ({provinceNames.Length} provinces, {Environment.ProcessorCount} logical cores)..."
);

// Pre-allocated results array — each slot written by exactly one thread, no contention
var provinceResults = new int[provinceNames.Length][][][];

Parallel.For(0, provinceNames.Length, pi =>
{
    var indices = provinceIndexSets[provinceNames[pi]];
    provinceResults[pi] = TracePaths((px, py) => indices.Contains(colorMap[px, py]));
    Console.WriteLine(
        $"  [{pi + 1}/{provinceNames.Length}] {provinceNames[pi]} → {provinceResults[pi].Length} path(s)"
    );
});

// ── Collect results into provinceMap (sequential — no contention) ─────────────
var provinceMap = new Dictionary<string, List<object>>();

for (int ci = 0; ci < locationColors.Count; ci++)
{
    var (province, locName, _, _, _, hex) = locationColors[ci];
    if (!provinceMap.ContainsKey(province))
        provinceMap[province] = new List<object>();

    templateLookup.TryGetValue(locName, out var tmpl);

    provinceMap[province].Add(new
    {
        name         = locName,
        color        = hex,
        topography   = tmpl?.Topography,
        climate      = tmpl?.Climate,
        vegetation   = tmpl?.Vegetation,
        raw_material = tmpl?.RawMaterial,
        paths        = locationResults[ci],
    });
}

// ── Step 8: Write JSON ────────────────────────────────────────────────────────

Console.WriteLine("\nStep 8: Writing JSON...");

var output = new
{
    area = "svealand_area",
    provinces = provinceNames
        .Select((pName, pi) => new
        {
            name      = pName,
            paths     = provinceResults[pi],
            locations = provinceMap.TryGetValue(pName, out var locs) ? locs : new List<object>(),
        })
        .ToList(),
};

var jsonOutputPath = Path.Combine(projectDir, "Scripts", "svealand_area.json");
File.WriteAllText(
    jsonOutputPath,
    JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true })
);

Console.WriteLine($"\nAll done. JSON saved to: {jsonOutputPath}");
