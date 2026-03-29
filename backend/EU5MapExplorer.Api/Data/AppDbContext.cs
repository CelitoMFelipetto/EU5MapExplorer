using EU5MapExplorer.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using System.Text.Json;

namespace EU5MapExplorer.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<GameVersion> GameVersions => Set<GameVersion>();
    public DbSet<Area> Areas => Set<Area>();
    public DbSet<GameVersionArea> GameVersionAreas => Set<GameVersionArea>();
    public DbSet<Province> Provinces => Set<Province>();
    public DbSet<Location> Locations => Set<Location>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<GameVersion>(e =>
        {
            e.ToTable("game_versions");
            e.HasKey(x => x.Id);
            e.Property(x => x.Version).HasMaxLength(32).IsRequired();
            e.HasIndex(x => x.Version).IsUnique();
        });

        mb.Entity<Area>(e =>
        {
            e.ToTable("areas");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(128).IsRequired();
            e.Property(x => x.ContentHash).HasMaxLength(64).IsRequired();
            e.Property(x => x.Continent).HasMaxLength(128);
            e.Property(x => x.SubContinent).HasMaxLength(128);
            e.Property(x => x.Region).HasMaxLength(128);
            e.HasIndex(x => new { x.Name, x.ContentHash }).IsUnique();
            e.Property(x => x.Neighbors)
             .HasColumnType("jsonb")
             .HasConversion(
                 v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                 s => JsonSerializer.Deserialize<string[]>(s, (JsonSerializerOptions?)null) ?? Array.Empty<string>())
             .Metadata.SetValueComparer(new ValueComparer<string[]>(
                 (a, b) => a != null && b != null && a.SequenceEqual(b),
                 v => v.Aggregate(0, (h, s) => HashCode.Combine(h, s.GetHashCode())),
                 v => v.ToArray()));
        });

        mb.Entity<GameVersionArea>(e =>
        {
            e.ToTable("game_version_areas");
            e.HasKey(x => new { x.GameVersionId, x.AreaId });
            e.HasOne(x => x.GameVersion)
             .WithMany()
             .HasForeignKey(x => x.GameVersionId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Area)
             .WithMany(a => a.GameVersionAreas)
             .HasForeignKey(x => x.AreaId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        mb.Entity<Province>(e =>
        {
            e.ToTable("provinces");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(128).IsRequired();
            e.HasIndex(x => new { x.AreaId, x.Name }).IsUnique();
            e.Property(x => x.Paths)
             .HasColumnType("jsonb")
             .HasConversion(
                 v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                 s => JsonSerializer.Deserialize<int[][][]>(s, (JsonSerializerOptions?)null) ?? Array.Empty<int[][]>())
             .Metadata.SetValueComparer(new ValueComparer<int[][][]>(
                 (a, b) => JsonSerializer.Serialize(a, (JsonSerializerOptions?)null) == JsonSerializer.Serialize(b, (JsonSerializerOptions?)null),
                 v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null).GetHashCode(),
                 v => JsonSerializer.Deserialize<int[][][]>(JsonSerializer.Serialize(v, (JsonSerializerOptions?)null), (JsonSerializerOptions?)null)!));
            e.HasOne(x => x.Area)
             .WithMany(a => a.Provinces)
             .HasForeignKey(x => x.AreaId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        mb.Entity<Location>(e =>
        {
            e.ToTable("locations");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(128).IsRequired();
            e.HasIndex(x => new { x.ProvinceId, x.Name }).IsUnique();
            e.Property(x => x.Color).HasMaxLength(6).IsRequired();
            e.Property(x => x.Topography).HasMaxLength(64).IsRequired();
            e.Property(x => x.Climate).HasMaxLength(64).IsRequired();
            e.Property(x => x.Vegetation).HasMaxLength(64);
            e.Property(x => x.RawMaterial).HasMaxLength(64);
            e.Property(x => x.Rank).HasMaxLength(32).IsRequired();
            e.Property(x => x.Paths)
             .HasColumnType("jsonb")
             .HasConversion(
                 v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                 s => JsonSerializer.Deserialize<int[][][]>(s, (JsonSerializerOptions?)null) ?? Array.Empty<int[][]>())
             .Metadata.SetValueComparer(new ValueComparer<int[][][]>(
                 (a, b) => JsonSerializer.Serialize(a, (JsonSerializerOptions?)null) == JsonSerializer.Serialize(b, (JsonSerializerOptions?)null),
                 v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null).GetHashCode(),
                 v => JsonSerializer.Deserialize<int[][][]>(JsonSerializer.Serialize(v, (JsonSerializerOptions?)null), (JsonSerializerOptions?)null)!));
            e.HasOne(x => x.Province)
             .WithMany(p => p.Locations)
             .HasForeignKey(x => x.ProvinceId)
             .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
