using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace Bandroom.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class SongsAreTheContainer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Songs become the one container. Order matters: move the data while
            // SongIdeas still exists, then drop it, then rename and re-point the keys.
            migrationBuilder.DropForeignKey(name: "FK_DemoVersions_SongIdeas_SongIdeaId", table: "DemoVersions");
            migrationBuilder.DropForeignKey(name: "FK_PolishJobs_SongIdeas_SongIdeaId", table: "PolishJobs");
            migrationBuilder.DropForeignKey(name: "FK_Stems_SongIdeas_SongIdeaId", table: "Stems");

            migrationBuilder.AddColumn<Guid>(name: "CreatedByMembershipId", table: "Songs", type: "uuid", nullable: true);

            // 1. Status vocabulary: Active/Retired → Ready/Parked.
            migrationBuilder.Sql("""
                UPDATE "Songs" SET "Status" = CASE "Status" WHEN 'Active' THEN 'Ready' WHEN 'Retired' THEN 'Parked' ELSE "Status" END;
                """);

            // 2. Every idea that wasn't linked to a song becomes a song, keeping its id.
            migrationBuilder.Sql("""
                INSERT INTO "Songs" ("Id", "BandId", "Title", "Status", "CreatedAt", "CreatedByMembershipId", "Key", "Bpm", "Notes", "DurationSeconds", "Chords")
                SELECT i."Id", i."BandId", i."Title",
                       CASE i."Status" WHEN 'Idea' THEN 'Idea' WHEN 'InProgress' THEN 'Working' WHEN 'Finished' THEN 'Ready' ELSE 'Parked' END,
                       i."CreatedAt", i."CreatedByMembershipId", NULL, NULL, NULL, NULL, NULL
                FROM "SongIdeas" i
                WHERE i."SongId" IS NULL;
                """);

            // 3. Takes, stems and polish runs follow: to the linked song, else to the idea-turned-song.
            foreach (var table in new[] { "DemoVersions", "Stems", "PolishJobs" })
            {
                migrationBuilder.Sql($"""
                    UPDATE "{table}" t SET "SongIdeaId" = COALESCE(i."SongId", i."Id")
                    FROM "SongIdeas" i WHERE i."Id" = t."SongIdeaId";
                    """);
            }

            // 4. Two ideas merged into one song can collide on take numbers; renumber those songs by time.
            migrationBuilder.Sql("""
                UPDATE "DemoVersions" v SET "Number" = r.rn
                FROM (
                    SELECT "Id", ROW_NUMBER() OVER (PARTITION BY "SongIdeaId" ORDER BY "CreatedAt", "Number") AS rn
                    FROM "DemoVersions"
                    WHERE "SongIdeaId" IN (SELECT "SongIdeaId" FROM "DemoVersions" GROUP BY "SongIdeaId", "Number" HAVING COUNT(*) > 1)
                ) r WHERE r."Id" = v."Id";
                """);

            migrationBuilder.DropTable(name: "SongIdeas");

            migrationBuilder.RenameColumn(name: "SongIdeaId", table: "Stems", newName: "SongId");
            migrationBuilder.RenameIndex(name: "IX_Stems_SongIdeaId", table: "Stems", newName: "IX_Stems_SongId");
            migrationBuilder.RenameColumn(name: "SongIdeaId", table: "PolishJobs", newName: "SongId");
            migrationBuilder.RenameIndex(name: "IX_PolishJobs_SongIdeaId", table: "PolishJobs", newName: "IX_PolishJobs_SongId");
            migrationBuilder.RenameColumn(name: "SongIdeaId", table: "DemoVersions", newName: "SongId");
            migrationBuilder.RenameIndex(name: "IX_DemoVersions_SongIdeaId_Number", table: "DemoVersions", newName: "IX_DemoVersions_SongId_Number");

            migrationBuilder.CreateIndex(name: "IX_Songs_CreatedByMembershipId", table: "Songs", column: "CreatedByMembershipId");

            migrationBuilder.AddForeignKey(
                name: "FK_DemoVersions_Songs_SongId", table: "DemoVersions", column: "SongId",
                principalTable: "Songs", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
            migrationBuilder.AddForeignKey(
                name: "FK_PolishJobs_Songs_SongId", table: "PolishJobs", column: "SongId",
                principalTable: "Songs", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
            migrationBuilder.AddForeignKey(
                name: "FK_Songs_Memberships_CreatedByMembershipId", table: "Songs", column: "CreatedByMembershipId",
                principalTable: "Memberships", principalColumn: "Id", onDelete: ReferentialAction.SetNull);
            migrationBuilder.AddForeignKey(
                name: "FK_Stems_Songs_SongId", table: "Stems", column: "SongId",
                principalTable: "Songs", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Song ideas were folded into songs; restore from backup instead.");
        }
    }
}
