using EU5MapExplorer.Api.Data;
using EU5MapExplorer.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddDbContext<AppDbContext>(opts =>
    opts.UseNpgsql(builder.Configuration.GetConnectionString("Default"))
);

var app = builder.Build();

static string GetRepoRoot(string contentRootPath)
{
    // contentRootPath points to ...\backend\EU5MapExplorer.Api
    var root = Path.GetFullPath(Path.Combine(contentRootPath, "..", ".."));
    return root;
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.MapGet(
        "/api/map",
        async (string? area, string? version, AppDbContext db, IConfiguration config) =>
        {
            var areaName = area ?? "svealand_area";
            var gameVersion = version ?? config["EU5:Version"] ?? "1.1.10";

            var areaEntity = await db
                .Areas.Include(a => a.Provinces)
                    .ThenInclude(p => p.Locations)
                .Where(a =>
                    a.Name == areaName
                    && a.GameVersionAreas.Any(gva => gva.GameVersion.Version == gameVersion)
                )
                .FirstOrDefaultAsync();

            if (areaEntity is null)
                return Results.NotFound(
                    new
                    {
                        error = $"Area '{areaName}' not found for version '{gameVersion}'. Run the extract-map script first.",
                    }
                );

            // Collect all border keys referenced by locations and provinces in this area.
            var borderKeys = new HashSet<string>();
            foreach (var p in areaEntity.Provinces)
            {
                foreach (var ring in p.BorderRings)
                foreach (var br in ring.Borders)
                    borderKeys.Add(br.Key);
                foreach (var loc in p.Locations)
                foreach (var ring in loc.BorderRings)
                foreach (var br in ring.Borders)
                    borderKeys.Add(br.Key);
            }

            // Batch-load referenced borders.
            var borderEntities = await db
                .Borders.Where(b => borderKeys.Contains(b.Key))
                .ToListAsync();
            var bordersDict = borderEntities.ToDictionary(b => b.Key, b => b.Paths);

            var response = new
            {
                area = areaEntity.Name,
                neighbors = areaEntity.Neighbors,
                borders = bordersDict,
                provinces = areaEntity
                    .Provinces.Select(p => new
                    {
                        name = p.Name,
                        borderRings = p.BorderRings,
                        bounds = new
                        {
                            maxN = p.MaxN,
                            maxS = p.MaxS,
                            maxE = p.MaxE,
                            maxW = p.MaxW,
                        },
                        locations = p
                            .Locations.Select(loc => new
                            {
                                name = loc.Name,
                                color = loc.Color,
                                topography = loc.Topography,
                                climate = loc.Climate,
                                vegetation = loc.Vegetation,
                                raw_material = loc.RawMaterial,
                                rank = loc.Rank,
                                city_position = (loc.CityX.HasValue && loc.CityY.HasValue)
                                    ? new { x = loc.CityX.Value, y = loc.CityY.Value }
                                    : null as object,
                                unit_position = (loc.UnitX.HasValue && loc.UnitY.HasValue)
                                    ? new { x = loc.UnitX.Value, y = loc.UnitY.Value }
                                    : null as object,
                                borderRings = loc.BorderRings,
                            })
                            .ToArray(),
                    })
                    .ToArray(),
            };

            return Results.Ok(response);
        }
    )
    .WithName("GetMap")
    .WithOpenApi();

app.MapGet(
        "/api/saves/header",
        (string? path) =>
        {
            var repoRoot = GetRepoRoot(app.Environment.ContentRootPath);
            var relativeOrAbsolute = string.IsNullOrWhiteSpace(path)
                ? Path.Combine(
                    repoRoot,
                    "SP_NAP_1337_04_01_204f0002-e9cc-495b-b4c0-2b84f7eb5e56.eu5"
                )
                : path;

            var fullPath = Path.GetFullPath(
                Path.IsPathRooted(relativeOrAbsolute)
                    ? relativeOrAbsolute
                    : Path.Combine(repoRoot, relativeOrAbsolute)
            );

            if (!fullPath.StartsWith(repoRoot, StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest(
                    new { error = "Path must be inside the repository root." }
                );
            }

            if (!File.Exists(fullPath))
            {
                return Results.NotFound(new { error = "File not found.", fullPath });
            }

            if (
                !string.Equals(
                    Path.GetExtension(fullPath),
                    ".eu5",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return Results.BadRequest(
                    new { error = "Only .eu5 files are supported for this endpoint." }
                );
            }

            var headerLine = EU5MapExplorer.Api.Eu5SaveInspector.ReadHeaderLine(fullPath);
            var header = EU5MapExplorer.Api.Eu5SaveInspector.ParseHeader(headerLine);

            return Results.Ok(new { file = new { fullPath }, header });
        }
    )
    .WithName("GetEu5SaveHeader")
    .WithOpenApi();

app.MapGet(
        "/api/saves/inspect",
        (
            string? path,
            int? previewBytes,
            bool? includeZipEntryPreview,
            bool? includeGamestateTree,
            int? maxNodes,
            int? maxDepth
        ) =>
        {
            var repoRoot = GetRepoRoot(app.Environment.ContentRootPath);
            var relativeOrAbsolute = string.IsNullOrWhiteSpace(path)
                ? Path.Combine(
                    repoRoot,
                    "SP_NAP_1337_04_01_204f0002-e9cc-495b-b4c0-2b84f7eb5e56.eu5"
                )
                : path;

            var fullPath = Path.GetFullPath(
                Path.IsPathRooted(relativeOrAbsolute)
                    ? relativeOrAbsolute
                    : Path.Combine(repoRoot, relativeOrAbsolute)
            );

            if (!fullPath.StartsWith(repoRoot, StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest(
                    new { error = "Path must be inside the repository root." }
                );
            }

            if (!File.Exists(fullPath))
            {
                return Results.NotFound(new { error = "File not found.", fullPath });
            }

            if (
                !string.Equals(
                    Path.GetExtension(fullPath),
                    ".eu5",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return Results.BadRequest(
                    new { error = "Only .eu5 files are supported for this endpoint." }
                );
            }

            var headerLine = EU5MapExplorer.Api.Eu5SaveInspector.ReadHeaderLine(fullPath);
            var header = EU5MapExplorer.Api.Eu5SaveInspector.ParseHeader(headerLine);

            var previewLen = Math.Clamp(previewBytes ?? 256, 0, 4096);
            var nodeLimit = Math.Clamp(maxNodes ?? 500, 50, 20_000);
            var depthLimit = Math.Clamp(maxDepth ?? 4, 1, 50);
            var inspection = EU5MapExplorer.Api.Eu5SaveInspector.Inspect(
                fullPath,
                afterHeaderPreviewBytes: previewLen,
                includeZipEntryPreview: includeZipEntryPreview ?? false,
                includeGamestateTree: includeGamestateTree ?? false,
                maxGamestateNodes: nodeLimit,
                maxGamestateDepth: depthLimit
            );

            return Results.Ok(
                new
                {
                    file = new { fullPath, length = new FileInfo(fullPath).Length },
                    header,
                    afterHeader = inspection.AfterHeader,
                    embeddedZip = inspection.EmbeddedZip,
                    gamestate = inspection.Gamestate,
                }
            );
        }
    )
    .WithName("InspectEu5Save")
    .WithOpenApi();

app.Run();
