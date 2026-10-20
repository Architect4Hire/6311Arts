using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arts6311.Domain.Migrations
{
    /// <inheritdoc />
    public partial class AddClasses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CatalogSkus",
                columns: table => new
                {
                    Sku = table.Column<string>(type: "varchar(64)", nullable: false),
                    OwnerType = table.Column<string>(type: "varchar(32)", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogSkus", x => x.Sku);
                });

            migrationBuilder.CreateTable(
                name: "ClassOfferings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Slug = table.Column<string>(type: "varchar(120)", nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Body = table.Column<string>(type: "nvarchar(max)", maxLength: 100000, nullable: true),
                    ClassType = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    Level = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    Instructor = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Cadence = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    StartingPriceCents = table.Column<long>(type: "bigint", nullable: true),
                    OfferingNumber = table.Column<int>(type: "int", nullable: false),
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
                    table.PrimaryKey("PK_ClassOfferings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClassOfferings_MediaAssets_HeroMediaId",
                        column: x => x.HeroMediaId,
                        principalTable: "MediaAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ClassOfferingMediaReferences",
                columns: table => new
                {
                    ClassOfferingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MediaAssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClassOfferingMediaReferences", x => new { x.ClassOfferingId, x.MediaAssetId });
                    table.ForeignKey(
                        name: "FK_ClassOfferingMediaReferences_ClassOfferings_ClassOfferingId",
                        column: x => x.ClassOfferingId,
                        principalTable: "ClassOfferings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ClassOfferingMediaReferences_MediaAssets_MediaAssetId",
                        column: x => x.MediaAssetId,
                        principalTable: "MediaAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ClassSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClassOfferingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartsAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EndsAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DisplayTimeZone = table.Column<string>(type: "varchar(64)", nullable: false),
                    Sku = table.Column<string>(type: "varchar(64)", nullable: false),
                    Capacity = table.Column<int>(type: "int", nullable: false),
                    PriceCents = table.Column<long>(type: "bigint", nullable: true),
                    SeatsRemaining = table.Column<int>(type: "int", nullable: true),
                    SoldOut = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClassSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClassSessions_ClassOfferings_ClassOfferingId",
                        column: x => x.ClassOfferingId,
                        principalTable: "ClassOfferings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CatalogSkus_OwnerType_OwnerId",
                table: "CatalogSkus",
                columns: new[] { "OwnerType", "OwnerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClassOfferingMediaReferences_MediaAssetId",
                table: "ClassOfferingMediaReferences",
                column: "MediaAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_ClassOfferings_HeroMediaId",
                table: "ClassOfferings",
                column: "HeroMediaId");

            migrationBuilder.CreateIndex(
                name: "IX_ClassOfferings_LegacyWordPressId",
                table: "ClassOfferings",
                column: "LegacyWordPressId",
                unique: true,
                filter: "[LegacyWordPressId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ClassOfferings_OfferingNumber",
                table: "ClassOfferings",
                column: "OfferingNumber",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_ClassOfferings_Slug",
                table: "ClassOfferings",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClassOfferings_Status_ScheduledFor",
                table: "ClassOfferings",
                columns: new[] { "Status", "ScheduledFor" });

            migrationBuilder.CreateIndex(
                name: "IX_ClassOfferings_Status_Title_Id",
                table: "ClassOfferings",
                columns: new[] { "Status", "Title", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ClassOfferings_Title_Id",
                table: "ClassOfferings",
                columns: new[] { "Title", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ClassSessions_ClassOfferingId_StartsAt",
                table: "ClassSessions",
                columns: new[] { "ClassOfferingId", "StartsAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ClassSessions_Sku",
                table: "ClassSessions",
                column: "Sku");

            migrationBuilder.CreateIndex(
                name: "IX_ClassSessions_StartsAt",
                table: "ClassSessions",
                column: "StartsAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CatalogSkus");

            migrationBuilder.DropTable(
                name: "ClassOfferingMediaReferences");

            migrationBuilder.DropTable(
                name: "ClassSessions");

            migrationBuilder.DropTable(
                name: "ClassOfferings");
        }
    }
}
