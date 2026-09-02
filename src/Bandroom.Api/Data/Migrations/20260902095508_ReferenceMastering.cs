using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace Bandroom.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReferenceMastering : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ReferenceTrackId",
                table: "PolishJobs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReferenceVersionId",
                table: "PolishJobs",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ReferenceTracks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BandId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    FileKey = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    FileName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    UploadedByMembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReferenceTracks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReferenceTracks_Bands_BandId",
                        column: x => x.BandId,
                        principalTable: "Bands",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReferenceTracks_Memberships_UploadedByMembershipId",
                        column: x => x.UploadedByMembershipId,
                        principalTable: "Memberships",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReferenceTracks_BandId",
                table: "ReferenceTracks",
                column: "BandId");

            migrationBuilder.CreateIndex(
                name: "IX_ReferenceTracks_UploadedByMembershipId",
                table: "ReferenceTracks",
                column: "UploadedByMembershipId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReferenceTracks");

            migrationBuilder.DropColumn(
                name: "ReferenceTrackId",
                table: "PolishJobs");

            migrationBuilder.DropColumn(
                name: "ReferenceVersionId",
                table: "PolishJobs");
        }
    }
}
