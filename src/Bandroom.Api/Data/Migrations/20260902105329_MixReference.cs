using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bandroom.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class MixReference : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "MixReferenceTrackId",
                table: "PolishJobs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "MixReferenceVersionId",
                table: "PolishJobs",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MixReferenceTrackId",
                table: "PolishJobs");

            migrationBuilder.DropColumn(
                name: "MixReferenceVersionId",
                table: "PolishJobs");
        }
    }
}
