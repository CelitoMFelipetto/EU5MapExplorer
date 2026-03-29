using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EU5MapExplorer.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAreaHierarchy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Continent",
                table: "areas",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Region",
                table: "areas",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubContinent",
                table: "areas",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Continent",
                table: "areas");

            migrationBuilder.DropColumn(
                name: "Region",
                table: "areas");

            migrationBuilder.DropColumn(
                name: "SubContinent",
                table: "areas");
        }
    }
}
