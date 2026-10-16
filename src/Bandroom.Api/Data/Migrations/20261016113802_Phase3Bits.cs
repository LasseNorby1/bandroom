using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace Bandroom.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase3Bits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Notice",
                table: "Bands",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "NoticeByMembershipId",
                table: "Bands",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Instant>(
                name: "NoticeUpdatedAt",
                table: "Bands",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecurringPracticeDay",
                table: "Bands",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Reactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BandId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    MembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Reactions_DemoVersions_VersionId",
                        column: x => x.VersionId,
                        principalTable: "DemoVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Reactions_Memberships_MembershipId",
                        column: x => x.MembershipId,
                        principalTable: "Memberships",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SongFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BandId = table.Column<Guid>(type: "uuid", nullable: false),
                    SongId = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("PK_SongFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SongFiles_Memberships_UploadedByMembershipId",
                        column: x => x.UploadedByMembershipId,
                        principalTable: "Memberships",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SongFiles_Songs_SongId",
                        column: x => x.SongId,
                        principalTable: "Songs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VersionAnalyses",
                columns: table => new
                {
                    VersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    BandId = table.Column<Guid>(type: "uuid", nullable: false),
                    ModelVersion = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Bpm = table.Column<double>(type: "double precision", nullable: true),
                    Key = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: true),
                    KeyConfidence = table.Column<double>(type: "double precision", nullable: true),
                    Error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VersionAnalyses", x => x.VersionId);
                    table.ForeignKey(
                        name: "FK_VersionAnalyses_DemoVersions_VersionId",
                        column: x => x.VersionId,
                        principalTable: "DemoVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Reactions_MembershipId",
                table: "Reactions",
                column: "MembershipId");

            migrationBuilder.CreateIndex(
                name: "IX_Reactions_VersionId_MembershipId",
                table: "Reactions",
                columns: new[] { "VersionId", "MembershipId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SongFiles_SongId",
                table: "SongFiles",
                column: "SongId");

            migrationBuilder.CreateIndex(
                name: "IX_SongFiles_UploadedByMembershipId",
                table: "SongFiles",
                column: "UploadedByMembershipId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Reactions");

            migrationBuilder.DropTable(
                name: "SongFiles");

            migrationBuilder.DropTable(
                name: "VersionAnalyses");

            migrationBuilder.DropColumn(
                name: "Notice",
                table: "Bands");

            migrationBuilder.DropColumn(
                name: "NoticeByMembershipId",
                table: "Bands");

            migrationBuilder.DropColumn(
                name: "NoticeUpdatedAt",
                table: "Bands");

            migrationBuilder.DropColumn(
                name: "RecurringPracticeDay",
                table: "Bands");
        }
    }
}
