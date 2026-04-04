using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace EU5MapExplorer.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class BorderBasedGeometry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Paths",
                table: "provinces",
                newName: "BorderRings");

            migrationBuilder.RenameColumn(
                name: "Paths",
                table: "locations",
                newName: "BorderRings");

            migrationBuilder.AddColumn<int>(
                name: "MaxE",
                table: "provinces",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MaxN",
                table: "provinces",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MaxS",
                table: "provinces",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MaxW",
                table: "provinces",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "borders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Key = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    LocationA = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    LocationB = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Paths = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_borders", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_borders_Key",
                table: "borders",
                column: "Key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "borders");

            migrationBuilder.DropColumn(
                name: "MaxE",
                table: "provinces");

            migrationBuilder.DropColumn(
                name: "MaxN",
                table: "provinces");

            migrationBuilder.DropColumn(
                name: "MaxS",
                table: "provinces");

            migrationBuilder.DropColumn(
                name: "MaxW",
                table: "provinces");

            migrationBuilder.RenameColumn(
                name: "BorderRings",
                table: "provinces",
                newName: "Paths");

            migrationBuilder.RenameColumn(
                name: "BorderRings",
                table: "locations",
                newName: "Paths");
        }
    }
}
