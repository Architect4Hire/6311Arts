using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arts6311.Domain.Migrations
{
    /// <inheritdoc />
    public partial class ContentSoftDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ShopItems_Slug",
                table: "ShopItems");

            migrationBuilder.DropIndex(
                name: "IX_MembershipPlans_Slug",
                table: "MembershipPlans");

            migrationBuilder.DropIndex(
                name: "IX_Instructors_Slug",
                table: "Instructors");

            migrationBuilder.DropIndex(
                name: "IX_Exhibitions_Slug",
                table: "Exhibitions");

            migrationBuilder.DropIndex(
                name: "IX_ClassOfferings_Slug",
                table: "ClassOfferings");

            migrationBuilder.DropIndex(
                name: "IX_Artists_Slug",
                table: "Artists");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeletedAt",
                table: "ShopItems",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedBy",
                table: "ShopItems",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeletedAt",
                table: "MembershipPlans",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedBy",
                table: "MembershipPlans",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeletedAt",
                table: "Instructors",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedBy",
                table: "Instructors",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeletedAt",
                table: "Exhibitions",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedBy",
                table: "Exhibitions",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeletedAt",
                table: "ClassOfferings",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedBy",
                table: "ClassOfferings",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeletedAt",
                table: "Artists",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedBy",
                table: "Artists",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShopItems_DeletedAt_Id",
                table: "ShopItems",
                columns: new[] { "DeletedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ShopItems_Slug",
                table: "ShopItems",
                column: "Slug",
                unique: true,
                filter: "[DeletedAt] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MembershipPlans_DeletedAt_Id",
                table: "MembershipPlans",
                columns: new[] { "DeletedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_MembershipPlans_Slug",
                table: "MembershipPlans",
                column: "Slug",
                unique: true,
                filter: "[DeletedAt] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Instructors_DeletedAt_Id",
                table: "Instructors",
                columns: new[] { "DeletedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Instructors_Slug",
                table: "Instructors",
                column: "Slug",
                unique: true,
                filter: "[DeletedAt] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Exhibitions_DeletedAt_Id",
                table: "Exhibitions",
                columns: new[] { "DeletedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Exhibitions_Slug",
                table: "Exhibitions",
                column: "Slug",
                unique: true,
                filter: "[DeletedAt] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ClassOfferings_DeletedAt_Id",
                table: "ClassOfferings",
                columns: new[] { "DeletedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ClassOfferings_Slug",
                table: "ClassOfferings",
                column: "Slug",
                unique: true,
                filter: "[DeletedAt] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Artists_DeletedAt_Id",
                table: "Artists",
                columns: new[] { "DeletedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Artists_Slug",
                table: "Artists",
                column: "Slug",
                unique: true,
                filter: "[DeletedAt] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ShopItems_DeletedAt_Id",
                table: "ShopItems");

            migrationBuilder.DropIndex(
                name: "IX_ShopItems_Slug",
                table: "ShopItems");

            migrationBuilder.DropIndex(
                name: "IX_MembershipPlans_DeletedAt_Id",
                table: "MembershipPlans");

            migrationBuilder.DropIndex(
                name: "IX_MembershipPlans_Slug",
                table: "MembershipPlans");

            migrationBuilder.DropIndex(
                name: "IX_Instructors_DeletedAt_Id",
                table: "Instructors");

            migrationBuilder.DropIndex(
                name: "IX_Instructors_Slug",
                table: "Instructors");

            migrationBuilder.DropIndex(
                name: "IX_Exhibitions_DeletedAt_Id",
                table: "Exhibitions");

            migrationBuilder.DropIndex(
                name: "IX_Exhibitions_Slug",
                table: "Exhibitions");

            migrationBuilder.DropIndex(
                name: "IX_ClassOfferings_DeletedAt_Id",
                table: "ClassOfferings");

            migrationBuilder.DropIndex(
                name: "IX_ClassOfferings_Slug",
                table: "ClassOfferings");

            migrationBuilder.DropIndex(
                name: "IX_Artists_DeletedAt_Id",
                table: "Artists");

            migrationBuilder.DropIndex(
                name: "IX_Artists_Slug",
                table: "Artists");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "ShopItems");

            migrationBuilder.DropColumn(
                name: "DeletedBy",
                table: "ShopItems");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "MembershipPlans");

            migrationBuilder.DropColumn(
                name: "DeletedBy",
                table: "MembershipPlans");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "Instructors");

            migrationBuilder.DropColumn(
                name: "DeletedBy",
                table: "Instructors");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "Exhibitions");

            migrationBuilder.DropColumn(
                name: "DeletedBy",
                table: "Exhibitions");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "ClassOfferings");

            migrationBuilder.DropColumn(
                name: "DeletedBy",
                table: "ClassOfferings");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "Artists");

            migrationBuilder.DropColumn(
                name: "DeletedBy",
                table: "Artists");

            migrationBuilder.CreateIndex(
                name: "IX_ShopItems_Slug",
                table: "ShopItems",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MembershipPlans_Slug",
                table: "MembershipPlans",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Instructors_Slug",
                table: "Instructors",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Exhibitions_Slug",
                table: "Exhibitions",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClassOfferings_Slug",
                table: "ClassOfferings",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Artists_Slug",
                table: "Artists",
                column: "Slug",
                unique: true);
        }
    }
}
