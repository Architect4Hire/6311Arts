using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arts6311.Domain.Migrations
{
    /// <inheritdoc />
    public partial class AddShopItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ShopItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Slug = table.Column<string>(type: "varchar(120)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", maxLength: 100000, nullable: true),
                    ArtistId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Sku = table.Column<string>(type: "varchar(64)", nullable: false),
                    ItemNumber = table.Column<int>(type: "int", nullable: false),
                    OneOfAKind = table.Column<bool>(type: "bit", nullable: false),
                    StartingPriceCents = table.Column<long>(type: "bigint", nullable: true),
                    StartingStockQuantity = table.Column<int>(type: "int", nullable: true),
                    PriceCents = table.Column<long>(type: "bigint", nullable: true),
                    StockOnHand = table.Column<int>(type: "int", nullable: true),
                    SoldOut = table.Column<bool>(type: "bit", nullable: false),
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
                    table.PrimaryKey("PK_ShopItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShopItems_Artists_ArtistId",
                        column: x => x.ArtistId,
                        principalTable: "Artists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ShopItemCategories",
                columns: table => new
                {
                    ShopItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Category = table.Column<string>(type: "varchar(32)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShopItemCategories", x => new { x.ShopItemId, x.Category });
                    table.ForeignKey(
                        name: "FK_ShopItemCategories_ShopItems_ShopItemId",
                        column: x => x.ShopItemId,
                        principalTable: "ShopItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ShopItemImages",
                columns: table => new
                {
                    ShopItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MediaAssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Position = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShopItemImages", x => new { x.ShopItemId, x.MediaAssetId });
                    table.ForeignKey(
                        name: "FK_ShopItemImages_MediaAssets_MediaAssetId",
                        column: x => x.MediaAssetId,
                        principalTable: "MediaAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShopItemImages_ShopItems_ShopItemId",
                        column: x => x.ShopItemId,
                        principalTable: "ShopItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ShopItemMediaReferences",
                columns: table => new
                {
                    ShopItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MediaAssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShopItemMediaReferences", x => new { x.ShopItemId, x.MediaAssetId });
                    table.ForeignKey(
                        name: "FK_ShopItemMediaReferences_MediaAssets_MediaAssetId",
                        column: x => x.MediaAssetId,
                        principalTable: "MediaAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShopItemMediaReferences_ShopItems_ShopItemId",
                        column: x => x.ShopItemId,
                        principalTable: "ShopItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ShopItemCategories_Category",
                table: "ShopItemCategories",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_ShopItemImages_MediaAssetId",
                table: "ShopItemImages",
                column: "MediaAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_ShopItemMediaReferences_MediaAssetId",
                table: "ShopItemMediaReferences",
                column: "MediaAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_ShopItems_ArtistId",
                table: "ShopItems",
                column: "ArtistId");

            migrationBuilder.CreateIndex(
                name: "IX_ShopItems_ItemNumber",
                table: "ShopItems",
                column: "ItemNumber",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_ShopItems_LegacyWordPressId",
                table: "ShopItems",
                column: "LegacyWordPressId",
                unique: true,
                filter: "[LegacyWordPressId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ShopItems_Sku",
                table: "ShopItems",
                column: "Sku");

            migrationBuilder.CreateIndex(
                name: "IX_ShopItems_Slug",
                table: "ShopItems",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShopItems_Status_ScheduledFor",
                table: "ShopItems",
                columns: new[] { "Status", "ScheduledFor" });

            migrationBuilder.CreateIndex(
                name: "IX_ShopItems_Status_Title_Id",
                table: "ShopItems",
                columns: new[] { "Status", "Title", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ShopItems_Title_Id",
                table: "ShopItems",
                columns: new[] { "Title", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ShopItemCategories");

            migrationBuilder.DropTable(
                name: "ShopItemImages");

            migrationBuilder.DropTable(
                name: "ShopItemMediaReferences");

            migrationBuilder.DropTable(
                name: "ShopItems");
        }
    }
}
