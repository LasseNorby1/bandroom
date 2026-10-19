using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bandroom.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class BandLyricsLanguage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LyricsLanguage",
                table: "Bands",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LyricsLanguage",
                table: "Bands");
        }
    }
}
