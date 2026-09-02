using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace Bandroom.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Availability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IcsToken",
                table: "Memberships",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Instant>(
                name: "PatternUpdatedAt",
                table: "Memberships",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WeeklyPattern",
                table: "Memberships",
                type: "jsonb",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "AvailabilityExceptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BandId = table.Column<Guid>(type: "uuid", nullable: false),
                    MembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    From = table.Column<LocalDate>(type: "date", nullable: false),
                    To = table.Column<LocalDate>(type: "date", nullable: false),
                    Note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AvailabilityExceptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AvailabilityExceptions_Memberships_MembershipId",
                        column: x => x.MembershipId,
                        principalTable: "Memberships",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Memberships_IcsToken",
                table: "Memberships",
                column: "IcsToken",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AvailabilityExceptions_MembershipId_To",
                table: "AvailabilityExceptions",
                columns: new[] { "MembershipId", "To" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AvailabilityExceptions");

            migrationBuilder.DropIndex(
                name: "IX_Memberships_IcsToken",
                table: "Memberships");

            migrationBuilder.DropColumn(
                name: "IcsToken",
                table: "Memberships");

            migrationBuilder.DropColumn(
                name: "PatternUpdatedAt",
                table: "Memberships");

            migrationBuilder.DropColumn(
                name: "WeeklyPattern",
                table: "Memberships");
        }
    }
}
