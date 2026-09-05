using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bandroom.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class VersionPeaks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<float[]>(
                name: "Peaks",
                table: "DemoVersions",
                type: "real[]",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Peaks",
                table: "DemoVersions");
        }
    }
}
