using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arts6311.Domain.Migrations
{
    /// <inheritdoc />
    public partial class AddMembershipPlans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MembershipPlans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Slug = table.Column<string>(type: "varchar(120)", nullable: false),
                    DiscountPercent = table.Column<int>(type: "int", nullable: false),
                    Featured = table.Column<bool>(type: "bit", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    StartingPriceCents = table.Column<long>(type: "bigint", nullable: true),
                    StartingCadence = table.Column<int>(type: "int", nullable: false),
                    PriceCents = table.Column<long>(type: "bigint", nullable: true),
                    Cadence = table.Column<string>(type: "varchar(32)", nullable: true),
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
                    table.PrimaryKey("PK_MembershipPlans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MembershipPlanPerks",
                columns: table => new
                {
                    MembershipPlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Position = table.Column<int>(type: "int", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MembershipPlanPerks", x => new { x.MembershipPlanId, x.Position });
                    table.ForeignKey(
                        name: "FK_MembershipPlanPerks_MembershipPlans_MembershipPlanId",
                        column: x => x.MembershipPlanId,
                        principalTable: "MembershipPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MembershipPlans_DisplayOrder_Id",
                table: "MembershipPlans",
                columns: new[] { "DisplayOrder", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_MembershipPlans_LegacyWordPressId",
                table: "MembershipPlans",
                column: "LegacyWordPressId",
                unique: true,
                filter: "[LegacyWordPressId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MembershipPlans_Slug",
                table: "MembershipPlans",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MembershipPlans_Status_DisplayOrder_Id",
                table: "MembershipPlans",
                columns: new[] { "Status", "DisplayOrder", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_MembershipPlans_Status_ScheduledFor",
                table: "MembershipPlans",
                columns: new[] { "Status", "ScheduledFor" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MembershipPlanPerks");

            migrationBuilder.DropTable(
                name: "MembershipPlans");
        }
    }
}
