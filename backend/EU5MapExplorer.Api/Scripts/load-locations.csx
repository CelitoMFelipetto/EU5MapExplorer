#r "nuget: Tamar.Clausewitz, 0.5.1"
#r "nuget: Npgsql, 8.0.6"
#r "..\bin\Debug\net10.0\EU5MapExplorer.Api.dll"
#nullable enable

using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using EU5MapExplorer.Api.Services;
using Npgsql;
using Tamar.Clausewitz;

var config = ProjectConfig.Instance;

// ── Step 1: Parse definitions.txt → ALL areas → provinces + locations ─────────

Console.WriteLine("Step 1: Parsing definitions.txt...");

var definitionsPath = Path.Combine(config.DataPath, "in_game", "map_data", "definitions.txt");

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

// areaAncestry: areaName → (continent, subContinent, region)
var areaAncestry = new Dictionary<
    string,
    (string? Continent, string? SubContinent, string? Region)
>(StringComparer.OrdinalIgnoreCase);

// The hierarchy is: continent → sub-continent → region → area → province → locations.
var bfsClauseQueue =
    new Queue<(dynamic Clause, string? Continent, string? SubContinent, string? Region)>();
foreach (var c in definitionsRoot.Clauses.Cast<dynamic>())
    bfsClauseQueue.Enqueue((c, null, null, null));

while (bfsClauseQueue.Count > 0)
{
    var (candidate, continent, subContinent, region) = bfsClauseQueue.Dequeue();
    if (string.IsNullOrEmpty((string?)candidate.Name))
        continue;

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

// ── Step 2: Parse named_locations/*.txt → location name → RGB hex ────────────

Console.WriteLine("\nStep 2: Parsing named_locations...");

var namedLocationsDir = Path.Combine(config.DataPath, "in_game", "map_data", "named_locations");
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

// ── Step 3: Parse location_templates.txt → per-location properties ───────────

Console.WriteLine("\nStep 3: Parsing location_templates.txt...");

var templatesPath = Path.Combine(config.DataPath, "in_game", "map_data", "location_templates.txt");
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

// ── Step 4: Load city positions ──────────────────────────────────────────────

Console.WriteLine("\nStep 4: Loading city positions...");

const int MAP_HEIGHT = 8192;

var cityLocatorsPath = Path.Combine(
    config.DataPath,
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
        cityPositions[id] = (gameX, MAP_HEIGHT - gameZ);
    }
    Console.WriteLine($"  → {cityPositions.Count} city positions loaded.");
}

// ── Step 4b: Load unit stack positions ───────────────────────────────────────

Console.WriteLine("\nStep 4b: Loading unit stack positions...");

var unitLocatorsPath = Path.Combine(
    config.DataPath,
    "in_game",
    "gfx",
    "map",
    "map_objects",
    "generated_map_object_locators_unit_stack.txt"
);

var unitPositions = new Dictionary<string, (double x, double y)>(StringComparer.OrdinalIgnoreCase);

if (!File.Exists(unitLocatorsPath))
{
    Console.WriteLine(
        "  [WARN] Unit stack locator file not found — unit position will be null for all locations."
    );
}
else
{
    var unitInstanceRx = new Regex(
        @"\{\s*id=(\w+)\s+position=\{\s*([\d.]+)\s+[\d.]+\s+([\d.]+)\s*\}",
        RegexOptions.Singleline
    );

    foreach (Match m in unitInstanceRx.Matches(File.ReadAllText(unitLocatorsPath)))
    {
        var id = m.Groups[1].Value;
        var gameX = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
        var gameZ = double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
        unitPositions[id] = (gameX, MAP_HEIGHT - gameZ);
    }
    Console.WriteLine($"  → {unitPositions.Count} unit stack positions loaded.");
}

// ── Step 5: Load location ranks ──────────────────────────────────────────────

Console.WriteLine("\nStep 5: Loading location ranks...");

var citiesPath = Path.Combine(
    config.DataPath,
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

// ── Step 5b: Load pops ───────────────────────────────────────────────────────

Console.WriteLine("\nStep 5b: Loading pops...");

var popsPath = Path.Combine(config.DataPath, "main_menu", "setup", "start", "06_pops.txt");

// locationName → JSON array string of {type,size,culture,religion} entries
var popsLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

if (!File.Exists(popsPath))
{
    Console.WriteLine("  [WARN] Pops file not found — pops will be null for all locations.");
}
else
{
    var popsRoot = Interpreter.InterpretText(File.ReadAllText(popsPath));
    var locationsClause = popsRoot.FindClauseDepthFirst("locations");
    if (locationsClause != null)
    {
        foreach (var locClause in locationsClause.Clauses)
        {
            var locName = (string?)locClause.Name;
            if (string.IsNullOrEmpty(locName)) continue;

            var pops = new List<object>();
            foreach (var popClause in locClause.Clauses)
            {
                if ((string?)popClause.Name != "define_pop") continue;
                string popType = "", sizeStr = "0", popCulture = "", popReligion = "";
                foreach (var b in popClause.Bindings)
                {
                    switch ((string?)b.Name)
                    {
                        case "type":     popType     = (string?)b.Value ?? ""; break;
                        case "size":     sizeStr     = (string?)b.Value ?? "0"; break;
                        case "culture":  popCulture  = (string?)b.Value ?? ""; break;
                        case "religion": popReligion = (string?)b.Value ?? ""; break;
                    }
                }
                double.TryParse(sizeStr, System.Globalization.NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var size);
                pops.Add(new { type = popType, size, culture = popCulture, religion = popReligion });
            }
            popsLookup[locName] = JsonSerializer.Serialize(pops);
        }
    }
    Console.WriteLine($"  → {popsLookup.Count} locations with pop data loaded.");
}

// ── Step 5c: Load markets ─────────────────────────────────────────────────────

Console.WriteLine("\nStep 5c: Loading markets...");

var marketsPath = Path.Combine(config.DataPath, "main_menu", "setup", "start", "03_markets.txt");

var marketLocations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

if (!File.Exists(marketsPath))
{
    Console.WriteLine("  [WARN] Markets file not found — HasMarket will be false for all locations.");
}
else
{
    var marketsRoot = Interpreter.InterpretText(File.ReadAllText(marketsPath));
    var managerClause = marketsRoot.FindClauseDepthFirst("market_manager");
    if (managerClause != null)
    {
        foreach (var binding in managerClause.Bindings.Cast<dynamic>())
        {
            if ((string?)binding.Name == "add_market")
                marketLocations.Add((string)binding.Value);
        }
    }
    Console.WriteLine($"  → {marketLocations.Count} market locations loaded.");
}

// ── Step 6: Compute content hash per area ────────────────────────────────────

Console.WriteLine("\nStep 6: Computing content hashes...");

string ComputeAreaHash(string areaName, Dictionary<string, List<string>> provinces)
{
    var canonical = new
    {
        provinces = provinces
            .OrderBy(kvp => kvp.Key, StringComparer.Ordinal)
            .Select(kvp => new
            {
                name = kvp.Key,
                locations = kvp
                    .Value.OrderBy(l => l, StringComparer.Ordinal)
                    .Select(l =>
                    {
                        colorLookup.TryGetValue(l, out var hex);
                        templateLookup.TryGetValue(l, out var tmpl);
                        var rank = rankLookup.TryGetValue(l, out var r) ? r : "rural_settlement";
                        double? cx = null, cy = null;
                        if (cityPositions.TryGetValue(l, out var cp))
                        {
                            cx = cp.x;
                            cy = cp.y;
                        }
                        double? ux = null, uy = null;
                        if (unitPositions.TryGetValue(l, out var up))
                        {
                            ux = up.x;
                            uy = up.y;
                        }
                        return new
                        {
                            Name = l,
                            Color = hex ?? "",
                            Topography = tmpl?.Topography ?? "unknown",
                            Climate = tmpl?.Climate ?? "unknown",
                            Vegetation = tmpl?.Vegetation,
                            RawMaterial = tmpl?.RawMaterial,
                            Rank = rank,
                            CityX = cx,
                            CityY = cy,
                            UnitX = ux,
                            UnitY = uy,
                            HasMarket = marketLocations.Contains(l),
                            Pops = popsLookup.TryGetValue(l, out var p) ? p : null,
                        };
                    })
                    .ToArray(),
            })
            .ToArray(),
    };
    var json = JsonSerializer.Serialize(canonical);
    var hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(json));
    return Convert.ToHexString(hash).ToLowerInvariant();
}

var areaHashes = allAreas.ToDictionary(
    kvp => kvp.Key,
    kvp => ComputeAreaHash(kvp.Key, kvp.Value),
    StringComparer.OrdinalIgnoreCase
);

Console.WriteLine($"  {areaHashes.Count} hashes computed.");

// ── Step 7: Write to database ────────────────────────────────────────────────

Console.WriteLine("\nStep 7: Writing to database...");

var conn = new NpgsqlConnection(config.ConnectionString);
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
    cmd.Parameters.AddWithValue("version", config.GameVersion);
    gameVersionId = (int)cmd.ExecuteScalar()!;
}
Console.WriteLine($"  game_version id={gameVersionId} ({config.GameVersion})");

// Clear existing game_version_areas links for all areas (full reload)
using (
    var cmd = new NpgsqlCommand(
        "DELETE FROM game_version_areas WHERE \"GameVersionId\" = @versionId",
        conn
    )
)
{
    cmd.Parameters.AddWithValue("versionId", gameVersionId);
    cmd.ExecuteNonQuery();
}

int areasInserted = 0,
    areasUpdated = 0,
    areasReused = 0;

foreach (var (aName, provMap) in allAreas)
{
    var hash = areaHashes[aName];
    var (areaCont, areaSub, areaReg) = areaAncestry.TryGetValue(aName, out var anc)
        ? anc
        : (null, null, null);

    using var tx = conn.BeginTransaction();
    try
    {
        // Look up existing area by name
        int? existingAreaId = null;
        string? existingHash = null;
        using (
            var cmd = new NpgsqlCommand(
                "SELECT \"Id\", \"ContentHash\" FROM areas WHERE \"Name\" = @name LIMIT 1",
                conn,
                tx
            )
        )
        {
            cmd.Parameters.AddWithValue("name", aName);
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                existingAreaId = reader.GetInt32(0);
                existingHash = reader.GetString(1);
            }
        }

        int areaId;
        if (existingAreaId.HasValue && existingHash == hash)
        {
            // Hash unchanged — skip entirely, preserve all existing data
            areaId = existingAreaId.Value;
            areasReused++;
            Console.WriteLine($"  [REUSE] {aName} (id={areaId})");
        }
        else if (existingAreaId.HasValue)
        {
            // Area exists but hash changed — update metadata, preserve Neighbors
            areaId = existingAreaId.Value;
            using (
                var cmd = new NpgsqlCommand(
                    "UPDATE areas SET \"ContentHash\" = @hash, \"Continent\" = @continent, "
                        + "\"SubContinent\" = @subContinent, \"Region\" = @region "
                        + "WHERE \"Id\" = @id",
                    conn,
                    tx
                )
            )
            {
                cmd.Parameters.AddWithValue("id", areaId);
                cmd.Parameters.AddWithValue("hash", hash);
                cmd.Parameters.AddWithValue("continent", (object?)areaCont ?? DBNull.Value);
                cmd.Parameters.AddWithValue("subContinent", (object?)areaSub ?? DBNull.Value);
                cmd.Parameters.AddWithValue("region", (object?)areaReg ?? DBNull.Value);
                cmd.ExecuteNonQuery();
            }

            // Upsert provinces + locations
            UpsertProvincesAndLocations(conn, tx, areaId, provMap);

            areasUpdated++;
            Console.WriteLine($"  [UPDATE] {aName} (id={areaId}, {provMap.Count} provinces)");
        }
        else
        {
            // Brand new area
            using (
                var cmd = new NpgsqlCommand(
                    "INSERT INTO areas (\"Name\", \"ContentHash\", \"Continent\", \"SubContinent\", \"Region\", \"Neighbors\") "
                        + "VALUES (@name, @hash, @continent, @subContinent, @region, '[]'::jsonb) "
                        + "RETURNING \"Id\"",
                    conn,
                    tx
                )
            )
            {
                cmd.Parameters.AddWithValue("name", aName);
                cmd.Parameters.AddWithValue("hash", hash);
                cmd.Parameters.AddWithValue("continent", (object?)areaCont ?? DBNull.Value);
                cmd.Parameters.AddWithValue("subContinent", (object?)areaSub ?? DBNull.Value);
                cmd.Parameters.AddWithValue("region", (object?)areaReg ?? DBNull.Value);
                areaId = (int)cmd.ExecuteScalar()!;
            }

            // Insert provinces + locations (fresh — no existing data to preserve)
            UpsertProvincesAndLocations(conn, tx, areaId, provMap);

            areasInserted++;
            Console.WriteLine($"  [NEW]   {aName} (id={areaId}, {provMap.Count} provinces)");
        }

        // Link area to game version
        using (
            var cmd = new NpgsqlCommand(
                "INSERT INTO game_version_areas (\"GameVersionId\", \"AreaId\") "
                    + "VALUES (@versionId, @areaId) ON CONFLICT DO NOTHING",
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
        Console.Error.WriteLine($"  [ERROR] {aName}: {ex.Message}");
        throw;
    }
}

conn.Close();

var totalProvinces = allAreas.Values.Sum(p => p.Count);
var totalLocations = allAreas.Values.SelectMany(p => p.Values).Sum(l => l.Count);
Console.WriteLine(
    $"\nDone. {areasInserted} inserted, {areasUpdated} updated, {areasReused} reused. "
        + $"{totalProvinces} provinces, {totalLocations} locations."
);

// ── Upsert helpers ───────────────────────────────────────────────────────────

void UpsertProvincesAndLocations(
    NpgsqlConnection db,
    NpgsqlTransaction transaction,
    int areaId,
    Dictionary<string, List<string>> provinces
)
{
    foreach (var (pName, locNames) in provinces)
    {
        // Try to find existing province — preserve BorderRings + bounds
        int provinceId;
        using (
            var cmd = new NpgsqlCommand(
                "SELECT \"Id\" FROM provinces WHERE \"AreaId\" = @areaId AND \"Name\" = @name LIMIT 1",
                db,
                transaction
            )
        )
        {
            cmd.Parameters.AddWithValue("areaId", areaId);
            cmd.Parameters.AddWithValue("name", pName);
            var result = cmd.ExecuteScalar();
            if (result != null)
            {
                provinceId = (int)result;
                // Province already exists — no metadata to update (BorderRings/bounds preserved)
            }
            else
            {
                provinceId = -1;
            }
        }

        if (provinceId < 0)
        {
            using var cmd = new NpgsqlCommand(
                "INSERT INTO provinces (\"AreaId\", \"Name\", \"BorderRings\", \"MaxN\", \"MaxS\", \"MaxE\", \"MaxW\") "
                    + "VALUES (@areaId, @name, '[]'::jsonb, 0, 0, 0, 0) "
                    + "RETURNING \"Id\"",
                db,
                transaction
            );
            cmd.Parameters.AddWithValue("areaId", areaId);
            cmd.Parameters.AddWithValue("name", pName);
            provinceId = (int)cmd.ExecuteScalar()!;
        }

        foreach (var locName in locNames)
        {
            colorLookup.TryGetValue(locName, out var hex);
            templateLookup.TryGetValue(locName, out var tmpl);
            var rank = rankLookup.TryGetValue(locName, out var r) ? r : "rural_settlement";
            double? cx = null, cy = null;
            if (cityPositions.TryGetValue(locName, out var cp))
            {
                cx = cp.x;
                cy = cp.y;
            }
            double? ux = null, uy = null;
            if (unitPositions.TryGetValue(locName, out var up))
            {
                ux = up.x;
                uy = up.y;
            }
            var pops = popsLookup.TryGetValue(locName, out var po) ? po : null;
            var hasMarket = marketLocations.Contains(locName);

            // Check if location exists — update metadata, preserve BorderRings
            int? existingLocId = null;
            using (
                var cmd = new NpgsqlCommand(
                    "SELECT \"Id\" FROM locations WHERE \"ProvinceId\" = @provinceId AND \"Name\" = @name LIMIT 1",
                    db,
                    transaction
                )
            )
            {
                cmd.Parameters.AddWithValue("provinceId", provinceId);
                cmd.Parameters.AddWithValue("name", locName);
                var result = cmd.ExecuteScalar();
                if (result != null)
                    existingLocId = (int)result;
            }

            if (existingLocId.HasValue)
            {
                // Update metadata only — BorderRings untouched
                using var cmd = new NpgsqlCommand(
                    "UPDATE locations SET "
                        + "\"Color\" = @color, \"Topography\" = @topography, \"Climate\" = @climate, "
                        + "\"Vegetation\" = @vegetation, \"RawMaterial\" = @rawMaterial, "
                        + "\"Rank\" = @rank, \"CityX\" = @cityX, \"CityY\" = @cityY, "
                        + "\"UnitX\" = @unitX, \"UnitY\" = @unitY, "
                        + "\"Pops\" = @pops::jsonb, \"HasMarket\" = @hasMarket "
                        + "WHERE \"Id\" = @id",
                    db,
                    transaction
                );
                cmd.Parameters.AddWithValue("id", existingLocId.Value);
                cmd.Parameters.AddWithValue("color", hex ?? "000000");
                cmd.Parameters.AddWithValue("topography", tmpl?.Topography ?? "unknown");
                cmd.Parameters.AddWithValue("climate", tmpl?.Climate ?? "unknown");
                cmd.Parameters.AddWithValue("vegetation", (object?)tmpl?.Vegetation ?? DBNull.Value);
                cmd.Parameters.AddWithValue("rawMaterial", (object?)tmpl?.RawMaterial ?? DBNull.Value);
                cmd.Parameters.AddWithValue("rank", rank);
                cmd.Parameters.AddWithValue("cityX", (object?)cx ?? DBNull.Value);
                cmd.Parameters.AddWithValue("cityY", (object?)cy ?? DBNull.Value);
                cmd.Parameters.AddWithValue("unitX", (object?)ux ?? DBNull.Value);
                cmd.Parameters.AddWithValue("unitY", (object?)uy ?? DBNull.Value);
                cmd.Parameters.AddWithValue("pops", (object?)pops ?? DBNull.Value);
                cmd.Parameters.AddWithValue("hasMarket", hasMarket);
                cmd.ExecuteNonQuery();
            }
            else
            {
                using var cmd = new NpgsqlCommand(
                    "INSERT INTO locations "
                        + "(\"ProvinceId\",\"Name\",\"Color\",\"Topography\",\"Climate\",\"Vegetation\","
                        + "\"RawMaterial\",\"Rank\",\"CityX\",\"CityY\",\"UnitX\",\"UnitY\","
                        + "\"Pops\",\"HasMarket\",\"BorderRings\") "
                        + "VALUES "
                        + "(@provinceId,@name,@color,@topography,@climate,@vegetation,"
                        + "@rawMaterial,@rank,@cityX,@cityY,@unitX,@unitY,"
                        + "@pops::jsonb,@hasMarket,'[]'::jsonb)",
                    db,
                    transaction
                );
                cmd.Parameters.AddWithValue("provinceId", provinceId);
                cmd.Parameters.AddWithValue("name", locName);
                cmd.Parameters.AddWithValue("color", hex ?? "000000");
                cmd.Parameters.AddWithValue("topography", tmpl?.Topography ?? "unknown");
                cmd.Parameters.AddWithValue("climate", tmpl?.Climate ?? "unknown");
                cmd.Parameters.AddWithValue("vegetation", (object?)tmpl?.Vegetation ?? DBNull.Value);
                cmd.Parameters.AddWithValue("rawMaterial", (object?)tmpl?.RawMaterial ?? DBNull.Value);
                cmd.Parameters.AddWithValue("rank", rank);
                cmd.Parameters.AddWithValue("cityX", (object?)cx ?? DBNull.Value);
                cmd.Parameters.AddWithValue("cityY", (object?)cy ?? DBNull.Value);
                cmd.Parameters.AddWithValue("unitX", (object?)ux ?? DBNull.Value);
                cmd.Parameters.AddWithValue("unitY", (object?)uy ?? DBNull.Value);
                cmd.Parameters.AddWithValue("pops", (object?)pops ?? DBNull.Value);
                cmd.Parameters.AddWithValue("hasMarket", hasMarket);
                cmd.ExecuteNonQuery();
            }
        }
    }
}

// ── Type declarations ────────────────────────────────────────────────────────

record LocationTemplate(string Topography, string Climate, string? Vegetation, string? RawMaterial);
