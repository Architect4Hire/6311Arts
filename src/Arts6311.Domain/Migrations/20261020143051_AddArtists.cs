using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arts6311.Domain.Migrations
{
    /// <inheritdoc />
    public partial class AddArtists : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Artists",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Slug = table.Column<string>(type: "varchar(120)", nullable: false),
                    Medium = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Bio = table.Column<string>(type: "nvarchar(max)", maxLength: 5000, nullable: true),
                    Active = table.Column<bool>(type: "bit", nullable: false),
                    PortraitMediaId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LegacyWordPressId = table.Column<long>(type: "bigint", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ScheduledFor = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    RowVersion = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Artists", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Artists_MediaAssets_PortraitMediaId",
                        column: x => x.PortraitMediaId,
                        principalTable: "MediaAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Artists_LegacyWordPressId",
                table: "Artists",
                column: "LegacyWordPressId",
                unique: true,
                filter: "[LegacyWordPressId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Artists_Name_Id",
                table: "Artists",
                columns: new[] { "Name", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Artists_PortraitMediaId",
                table: "Artists",
                column: "PortraitMediaId");

            migrationBuilder.CreateIndex(
                name: "IX_Artists_Slug",
                table: "Artists",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Artists_Status_Name_Id",
                table: "Artists",
                columns: new[] { "Status", "Name", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Artists");
        }
    }
}
