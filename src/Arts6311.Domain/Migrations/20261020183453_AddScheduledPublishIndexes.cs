using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arts6311.Domain.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduledPublishIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Pages_Status_ScheduledFor",
                table: "Pages",
                columns: new[] { "Status", "ScheduledFor" });

            migrationBuilder.CreateIndex(
                name: "IX_Exhibitions_Status_ScheduledFor",
                table: "Exhibitions",
                columns: new[] { "Status", "ScheduledFor" });

            migrationBuilder.CreateIndex(
                name: "IX_Artists_Status_ScheduledFor",
                table: "Artists",
                columns: new[] { "Status", "ScheduledFor" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Pages_Status_ScheduledFor",
                table: "Pages");

            migrationBuilder.DropIndex(
                name: "IX_Exhibitions_Status_ScheduledFor",
                table: "Exhibitions");

            migrationBuilder.DropIndex(
                name: "IX_Artists_Status_ScheduledFor",
                table: "Artists");
        }
    }
}
