#r "nuget: SixLabors.ImageSharp, 3.1.5"
#nullable enable

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Text.Json;

// ── Find project dir by walking up from CWD ──────────────────────────────────

var projectDir = Directory.GetCurrentDirectory();
while (projectDir != null && !File.Exists(Path.Combine(projectDir, "EU5MapExplorer.Api.csproj")))
    projectDir = Directory.GetParent(projectDir)?.FullName;

if (projectDir == null)
{
    Console.Error.WriteLine("Error: Could not locate EU5MapExplorer.Api.csproj. Run from inside the project directory.");
    return;
}

// ── Read EU5:DataPath from appsettings ───────────────────────────────────────

string? dataPath = null;
foreach (var filename in new[] { "appsettings.Development.json", "appsettings.json" })
{
    var fullPath = Path.Combine(projectDir, filename);
    if (!File.Exists(fullPath)) continue;
    using var doc = JsonDocument.Parse(File.ReadAllText(fullPath));
    if (doc.RootElement.TryGetProperty("EU5", out var eu5) &&
        eu5.TryGetProperty("DataPath", out var dp) &&
        !string.IsNullOrWhiteSpace(dp.GetString()))
    {
        dataPath = dp.GetString();
        break;
    }
}

if (dataPath == null)
{
    Console.Error.WriteLine("Error: EU5:DataPath is not configured in appsettings.Development.json.");
    return;
}

// ── Define target colours ─────────────────────────────────────────────────────

var colours = new (byte R, byte G, byte B, string Hex)[]
{
    (0xDD, 0xA9, 0x10, "#dda910"),
    (0x54, 0x06, 0x01, "#540601"),
    (0xA8, 0x2D, 0xB1, "#a82db1"),
    (0x54, 0x15, 0x83, "#541583"),
    (0x00, 0x1E, 0x05, "#001e05"),
    (0xA8, 0x39, 0x89, "#a83989"),
    (0x00, 0x3C, 0x0A, "#003c0a"),
};

// ── Load image ───────────────────────────────────────────────────────────────

var imagePath = Path.Combine(dataPath, "in_game", "map_data", "locations.png");

if (!File.Exists(imagePath))
{
    Console.Error.WriteLine($"Error: File not found: {imagePath}");
    return;
}

Console.WriteLine($"Reading: {imagePath}");

var image = Image.Load<Rgba32>(imagePath);
int width = image.Width, height = image.Height;
Console.WriteLine($"Size: {width} x {height} px");

// ── Pass 1: single scan — map every pixel to a colour index (or -1) ──────────
// One pass over all pixels; each matched pixel records which colour it belongs to.

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
            for (int ci = 0; ci < colours.Length; ci++)
            {
                if (px.R != colours[ci].R || px.G != colours[ci].G || px.B != colours[ci].B) continue;
                colorMap[x, y] = ci;
                break;
            }
        }
    }
});

image.Dispose();

// ── Compute global bounding box across all matched pixels ─────────────────────

int globalMinX = int.MaxValue, globalMaxX = int.MinValue;
int globalMinY = int.MaxValue, globalMaxY = int.MinValue;

for (int y = 0; y < height; y++)
{
    for (int x = 0; x < width; x++)
    {
        if (colorMap[x, y] < 0) continue;
        if (x < globalMinX) globalMinX = x;
        if (x > globalMaxX) globalMaxX = x;
        if (y < globalMinY) globalMinY = y;
        if (y > globalMaxY) globalMaxY = y;
    }
}

if (globalMinX == int.MaxValue)
{
    Console.Error.WriteLine("Error: None of the target colours were found in the image.");
    return;
}

int svgWidth  = globalMaxX - globalMinX + 1;
int svgHeight = globalMaxY - globalMinY + 1;
Console.WriteLine($"Global bounding box: {svgWidth} x {svgHeight} px");

// ── Right-hand-rule turn order (shared across all colours) ────────────────────

var cwOrder = new Dictionary<(int, int), (int, int)[]>
{
    [(0, -1)] = new[] { (1, 0), (0, -1), (-1, 0) },  // arrived N → try E, N, W
    [(1,  0)] = new[] { (0, 1), (1,  0), (0, -1) },  // arrived E → try S, E, N
    [(0,  1)] = new[] { (-1,0), (0,  1), (1,  0) },  // arrived S → try W, S, E
    [(-1, 0)] = new[] { (0,-1), (-1, 0), (0,  1) },  // arrived W → try N, W, S
};

// ── Passes 2-4: per-colour — edge graph → closed path tracing ────────────────

var allColourPaths = new List<(string hex, List<List<(int, int)>> paths)>();

for (int ci = 0; ci < colours.Length; ci++)
{
    var hex = colours[ci].Hex;
    Console.WriteLine($"Processing {hex}...");

    // Build boundary edge adjacency graph for this colour.
    // A boundary edge exists wherever this colour pixel meets a different/absent pixel.
    var adj = new Dictionary<(int, int), HashSet<(int, int)>>();
    Action<(int, int), (int, int)> link = (a, b) =>
    {
        if (!adj.ContainsKey(a)) adj[a] = new HashSet<(int, int)>();
        if (!adj.ContainsKey(b)) adj[b] = new HashSet<(int, int)>();
        adj[a].Add(b);
        adj[b].Add(a);
    };

    for (int py = 0; py < height; py++)
    {
        for (int px = 0; px < width; px++)
        {
            if (colorMap[px, py] != ci) continue;
            if (py == 0          || colorMap[px, py - 1] != ci) link((px, py),     (px + 1, py));
            if (py == height - 1 || colorMap[px, py + 1] != ci) link((px, py + 1), (px + 1, py + 1));
            if (px == 0          || colorMap[px - 1, py] != ci) link((px, py),     (px, py + 1));
            if (px == width  - 1 || colorMap[px + 1, py] != ci) link((px + 1, py), (px + 1, py + 1));
        }
    }

    // Trace closed paths by walking the graph with the right-hand rule.
    var remaining = adj.ToDictionary(kvp => kvp.Key, kvp => new HashSet<(int, int)>(kvp.Value));
    Action<(int, int), (int, int)> useEdge = (a, b) =>
    {
        if (remaining.ContainsKey(a)) { remaining[a].Remove(b); if (remaining[a].Count == 0) remaining.Remove(a); }
        if (remaining.ContainsKey(b)) { remaining[b].Remove(a); if (remaining[b].Count == 0) remaining.Remove(b); }
    };

    var tracedPaths = new List<List<(int, int)>>();

    while (remaining.Count > 0)
    {
        var start = remaining.Keys.OrderBy(v => v.Item2).ThenBy(v => v.Item1).First();
        var firstNext = remaining[start].OrderBy(v => v.Item2).ThenBy(v => v.Item1).First();
        useEdge(start, firstNext);

        var path = new List<(int, int)> { start };
        var prev = start;
        var curr = firstNext;

        while (curr != start)
        {
            path.Add(curr);
            if (!remaining.ContainsKey(curr)) break;

            var arrDir = (curr.Item1 - prev.Item1, curr.Item2 - prev.Item2);
            (int, int) next = default;
            bool found = false;

            foreach (var tryDir in cwOrder[arrDir])
            {
                var candidate = (curr.Item1 + tryDir.Item1, curr.Item2 + tryDir.Item2);
                if (remaining.ContainsKey(curr) && remaining[curr].Contains(candidate))
                {
                    next = candidate;
                    found = true;
                    break;
                }
            }

            if (!found) break;

            useEdge(curr, next);
            prev = curr;
            curr = next;
        }

        tracedPaths.Add(path);
    }

    Console.WriteLine($"  → {tracedPaths.Count} path(s) traced.");
    allColourPaths.Add((hex, tracedPaths));
}

// ── Write SVG ─────────────────────────────────────────────────────────────────
// Each colour's closed loops are filled with that colour.
// Coordinates are offset to the global bounding box origin, plus padding.

const int pad = 2;
int totalWidth  = svgWidth  + pad * 2;
int totalHeight = svgHeight + pad * 2;

var svgOutputPath = Path.Combine(projectDir, "Scripts", "extract-map.svg");

using (var svgWriter = new StreamWriter(svgOutputPath, append: false, encoding: System.Text.Encoding.UTF8))
{
    svgWriter.WriteLine($"""<svg xmlns="http://www.w3.org/2000/svg" width="{totalWidth}" height="{totalHeight}" viewBox="0 0 {totalWidth} {totalHeight}">""");
    svgWriter.WriteLine($"""  <g transform="translate({pad},{pad})">""");

    foreach (var (hex, tracedPaths) in allColourPaths)
    {
        svgWriter.WriteLine($"    <!-- {hex} -->");

        foreach (var path in tracedPaths)
        {
            if (path.Count < 2) continue;

            // Strip collinear intermediate points, then emit as a filled path.
            var sb = new System.Text.StringBuilder();
            int lx = path[0].Item1, ly = path[0].Item2;
            sb.Append($"M {lx - globalMinX},{ly - globalMinY}");
            for (int i = 1; i < path.Count; i++)
            {
                int cx = path[i].Item1, cy = path[i].Item2;
                if (i < path.Count - 1)
                {
                    int nx = path[i + 1].Item1, ny = path[i + 1].Item2;
                    if ((cx - lx) * (ny - cy) - (cy - ly) * (nx - cx) == 0) continue;
                }
                sb.Append($" L {cx - globalMinX},{cy - globalMinY}");
                lx = cx; ly = cy;
            }
            sb.Append(" Z");
            svgWriter.WriteLine($"""    <path d="{sb}" fill="{hex}"/>""");
        }
    }

    svgWriter.WriteLine("  </g>");
    svgWriter.WriteLine("</svg>");
}

Console.WriteLine();
Console.WriteLine($"SVG saved to: {svgOutputPath}");
