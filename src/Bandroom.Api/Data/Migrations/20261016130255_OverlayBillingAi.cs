using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace Bandroom.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class OverlayBillingAi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CalendarImportError",
                table: "Memberships",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CalendarImportUrl",
                table: "Memberships",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Instant>(
                name: "CalendarImportedAt",
                table: "Memberships",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Instant>(
                name: "LastSeenAt",
                table: "Memberships",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AiEnabled",
                table: "Bands",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "BillingCustomerId",
                table: "Bands",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BillingPortalUrl",
                table: "Bands",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BillingSubscriptionId",
                table: "Bands",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ImportedBusyDays",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BandId = table.Column<Guid>(type: "uuid", nullable: false),
                    MembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<LocalDate>(type: "date", nullable: false),
                    Slot = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Summary = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportedBusyDays", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImportedBusyDays_Memberships_MembershipId",
                        column: x => x.MembershipId,
                        principalTable: "Memberships",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ImportedBusyDays_MembershipId_Date",
                table: "ImportedBusyDays",
                columns: new[] { "MembershipId", "Date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ImportedBusyDays");

            migrationBuilder.DropColumn(
                name: "CalendarImportError",
                table: "Memberships");

            migrationBuilder.DropColumn(
                name: "CalendarImportUrl",
                table: "Memberships");

            migrationBuilder.DropColumn(
                name: "CalendarImportedAt",
                table: "Memberships");

            migrationBuilder.DropColumn(
                name: "LastSeenAt",
                table: "Memberships");

            migrationBuilder.DropColumn(
                name: "AiEnabled",
                table: "Bands");

            migrationBuilder.DropColumn(
                name: "BillingCustomerId",
                table: "Bands");

            migrationBuilder.DropColumn(
                name: "BillingPortalUrl",
                table: "Bands");

            migrationBuilder.DropColumn(
                name: "BillingSubscriptionId",
                table: "Bands");
        }
    }
}
