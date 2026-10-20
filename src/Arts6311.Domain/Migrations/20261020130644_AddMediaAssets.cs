using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arts6311.Domain.Migrations
{
    /// <inheritdoc />
    public partial class AddMediaAssets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MediaAssets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    ParentAssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RenditionName = table.Column<string>(type: "varchar(16)", nullable: true),
                    FileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    MimeType = table.Column<string>(type: "varchar(32)", nullable: false),
                    Bytes = table.Column<long>(type: "bigint", nullable: false),
                    UploadedBytes = table.Column<long>(type: "bigint", nullable: true),
                    Width = table.Column<int>(type: "int", nullable: false),
                    Height = table.Column<int>(type: "int", nullable: false),
                    Checksum = table.Column<string>(type: "varchar(64)", nullable: false),
                    BlobKey = table.Column<string>(type: "varchar(200)", nullable: false),
                    ProfileHash = table.Column<string>(type: "varchar(16)", nullable: true),
                    ProcessingStatus = table.Column<int>(type: "int", nullable: false),
                    Visibility = table.Column<int>(type: "int", nullable: false),
                    CompressionPreset = table.Column<int>(type: "int", nullable: false),
                    AltText = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Credit = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    UploadedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    UploadedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaAssets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MediaAssets_MediaAssets_ParentAssetId",
                        column: x => x.ParentAssetId,
                        principalTable: "MediaAssets",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_MediaAssets_Checksum_Kind",
                table: "MediaAssets",
                columns: new[] { "Checksum", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_MediaAssets_Kind_UploadedAt_Id",
                table: "MediaAssets",
                columns: new[] { "Kind", "UploadedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_MediaAssets_ParentAssetId_RenditionName",
                table: "MediaAssets",
                columns: new[] { "ParentAssetId", "RenditionName" },
                unique: true,
                filter: "[ParentAssetId] IS NOT NULL AND [RenditionName] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MediaAssets");
        }
    }
}
