using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arts6311.Domain.Migrations
{
    /// <inheritdoc />
    public partial class AddRedirects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Redirects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FromPath = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    ToPath = table.Column<string>(type: "nvarchar(800)", maxLength: 800, nullable: false),
                    TargetPath = table.Column<string>(type: "nvarchar(800)", maxLength: 800, nullable: true),
                    StatusCode = table.Column<int>(type: "int", nullable: false),
                    Source = table.Column<int>(type: "int", nullable: false),
                    EffectiveAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LegacyWordPressId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    RowVersion = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Redirects", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Redirects_FromPath",
                table: "Redirects",
                column: "FromPath",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Redirects_LegacyWordPressId",
                table: "Redirects",
                column: "LegacyWordPressId",
                unique: true,
                filter: "[LegacyWordPressId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Redirects_TargetPath",
                table: "Redirects",
                column: "TargetPath");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Redirects");
        }
    }
}
