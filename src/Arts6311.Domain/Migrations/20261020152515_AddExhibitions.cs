using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arts6311.Domain.Migrations
{
    /// <inheritdoc />
    public partial class AddExhibitions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Exhibitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Slug = table.Column<string>(type: "varchar(120)", nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Body = table.Column<string>(type: "nvarchar(max)", maxLength: 10000, nullable: true),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ReceptionStartsAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReceptionEndsAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReceptionNote = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    HeroMediaId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
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
                    table.PrimaryKey("PK_Exhibitions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Exhibitions_MediaAssets_HeroMediaId",
                        column: x => x.HeroMediaId,
                        principalTable: "MediaAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExhibitionArtists",
                columns: table => new
                {
                    ExhibitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ArtistId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Position = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExhibitionArtists", x => new { x.ExhibitionId, x.ArtistId });
                    table.ForeignKey(
                        name: "FK_ExhibitionArtists_Artists_ArtistId",
                        column: x => x.ArtistId,
                        principalTable: "Artists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExhibitionArtists_Exhibitions_ExhibitionId",
                        column: x => x.ExhibitionId,
                        principalTable: "Exhibitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExhibitionArtists_ArtistId",
                table: "ExhibitionArtists",
                column: "ArtistId");

            migrationBuilder.CreateIndex(
                name: "IX_Exhibitions_HeroMediaId",
                table: "Exhibitions",
                column: "HeroMediaId");

            migrationBuilder.CreateIndex(
                name: "IX_Exhibitions_LegacyWordPressId",
                table: "Exhibitions",
                column: "LegacyWordPressId",
                unique: true,
                filter: "[LegacyWordPressId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Exhibitions_Slug",
                table: "Exhibitions",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Exhibitions_StartDate_Id",
                table: "Exhibitions",
                columns: new[] { "StartDate", "Id" },
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_Exhibitions_Status_StartDate_Id",
                table: "Exhibitions",
                columns: new[] { "Status", "StartDate", "Id" },
                descending: new[] { false, true, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExhibitionArtists");

            migrationBuilder.DropTable(
                name: "Exhibitions");
        }
    }
}
