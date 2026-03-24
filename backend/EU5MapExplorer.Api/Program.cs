var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

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

app.MapGet("/api/map", () =>
{
    var repoRoot  = GetRepoRoot(app.Environment.ContentRootPath);
    var jsonPath  = Path.Combine(repoRoot, "backend", "EU5MapExplorer.Api", "Scripts", "svealand_area.json");

    if (!File.Exists(jsonPath))
        return Results.NotFound(new { error = "svealand_area.json not found. Run the extract-map script first." });

    var json = File.ReadAllText(jsonPath);
    return Results.Content(json, "application/json");
})
.WithName("GetMap")
.WithOpenApi();

app.MapGet("/api/saves/header", (string? path) =>
{
    var repoRoot = GetRepoRoot(app.Environment.ContentRootPath);
    var relativeOrAbsolute = string.IsNullOrWhiteSpace(path)
        ? Path.Combine(repoRoot, "SP_NAP_1337_04_01_204f0002-e9cc-495b-b4c0-2b84f7eb5e56.eu5")
        : path;

    var fullPath = Path.GetFullPath(Path.IsPathRooted(relativeOrAbsolute)
        ? relativeOrAbsolute
        : Path.Combine(repoRoot, relativeOrAbsolute));

    if (!fullPath.StartsWith(repoRoot, StringComparison.OrdinalIgnoreCase))
    {
        return Results.BadRequest(new { error = "Path must be inside the repository root." });
    }

    if (!File.Exists(fullPath))
    {
        return Results.NotFound(new { error = "File not found.", fullPath });
    }

    if (!string.Equals(Path.GetExtension(fullPath), ".eu5", StringComparison.OrdinalIgnoreCase))
    {
        return Results.BadRequest(new { error = "Only .eu5 files are supported for this endpoint." });
    }

    var headerLine = EU5MapExplorer.Api.Eu5SaveInspector.ReadHeaderLine(fullPath);
    var header = EU5MapExplorer.Api.Eu5SaveInspector.ParseHeader(headerLine);

    return Results.Ok(new
    {
        file = new { fullPath },
        header
    });
})
.WithName("GetEu5SaveHeader")
.WithOpenApi();

app.MapGet("/api/saves/inspect", (string? path, int? previewBytes, bool? includeZipEntryPreview, bool? includeGamestateTree, int? maxNodes, int? maxDepth) =>
{
    var repoRoot = GetRepoRoot(app.Environment.ContentRootPath);
    var relativeOrAbsolute = string.IsNullOrWhiteSpace(path)
        ? Path.Combine(repoRoot, "SP_NAP_1337_04_01_204f0002-e9cc-495b-b4c0-2b84f7eb5e56.eu5")
        : path;

    var fullPath = Path.GetFullPath(Path.IsPathRooted(relativeOrAbsolute)
        ? relativeOrAbsolute
        : Path.Combine(repoRoot, relativeOrAbsolute));

    if (!fullPath.StartsWith(repoRoot, StringComparison.OrdinalIgnoreCase))
    {
        return Results.BadRequest(new { error = "Path must be inside the repository root." });
    }

    if (!File.Exists(fullPath))
    {
        return Results.NotFound(new { error = "File not found.", fullPath });
    }

    if (!string.Equals(Path.GetExtension(fullPath), ".eu5", StringComparison.OrdinalIgnoreCase))
    {
        return Results.BadRequest(new { error = "Only .eu5 files are supported for this endpoint." });
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
        maxGamestateDepth: depthLimit);

    return Results.Ok(new
    {
        file = new { fullPath, length = new FileInfo(fullPath).Length },
        header,
        afterHeader = inspection.AfterHeader,
        embeddedZip = inspection.EmbeddedZip,
        gamestate = inspection.Gamestate
    });
})
.WithName("InspectEu5Save")
.WithOpenApi();

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", () =>
{
    var forecast =  Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
})
.WithName("GetWeatherForecast")
.WithOpenApi();

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
