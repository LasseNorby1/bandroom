using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace Bandroom.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class EventReminders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Instant>(
                name: "ReminderSentAt",
                table: "Events",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReminderSentAt",
                table: "Events");
        }
    }
}
