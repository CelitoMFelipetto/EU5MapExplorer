using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EU5MapExplorer.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLocationGameData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HasMarket",
                table: "locations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Pops",
                table: "locations",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "UnitX",
                table: "locations",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "UnitY",
                table: "locations",
                type: "double precision",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HasMarket",
                table: "locations");

            migrationBuilder.DropColumn(
                name: "Pops",
                table: "locations");

            migrationBuilder.DropColumn(
                name: "UnitX",
                table: "locations");

            migrationBuilder.DropColumn(
                name: "UnitY",
                table: "locations");
        }
    }
}
