using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arts6311.Domain.Migrations
{
    /// <inheritdoc />
    public partial class AddClassRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ClassSessions_ClassOfferings_ClassOfferingId",
                table: "ClassSessions");

            migrationBuilder.DropIndex(
                name: "IX_ClassSessions_Sku",
                table: "ClassSessions");

            migrationBuilder.DropColumn(
                name: "Capacity",
                table: "ClassSessions");

            migrationBuilder.DropColumn(
                name: "DisplayTimeZone",
                table: "ClassSessions");

            migrationBuilder.DropColumn(
                name: "PriceCents",
                table: "ClassSessions");

            migrationBuilder.DropColumn(
                name: "SeatsRemaining",
                table: "ClassSessions");

            migrationBuilder.DropColumn(
                name: "Sku",
                table: "ClassSessions");

            migrationBuilder.DropColumn(
                name: "SoldOut",
                table: "ClassSessions");

            migrationBuilder.DropColumn(
                name: "Cadence",
                table: "ClassOfferings");

            migrationBuilder.DropColumn(
                name: "Instructor",
                table: "ClassOfferings");

            migrationBuilder.DropColumn(
                name: "StartingPriceCents",
                table: "ClassOfferings");

            migrationBuilder.RenameColumn(
                name: "ClassOfferingId",
                table: "ClassSessions",
                newName: "ClassRunId");

            migrationBuilder.RenameIndex(
                name: "IX_ClassSessions_ClassOfferingId_StartsAt",
                table: "ClassSessions",
                newName: "IX_ClassSessions_ClassRunId_StartsAt");

            migrationBuilder.CreateTable(
                name: "ClassRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClassOfferingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Label = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    InstructorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Cadence = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DisplayTimeZone = table.Column<string>(type: "varchar(64)", nullable: false),
                    Sku = table.Column<string>(type: "varchar(64)", nullable: false),
                    Capacity = table.Column<int>(type: "int", nullable: false),
                    StartingPriceCents = table.Column<long>(type: "bigint", nullable: true),
                    PriceCents = table.Column<long>(type: "bigint", nullable: true),
                    SeatsRemaining = table.Column<int>(type: "int", nullable: true),
                    SoldOut = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClassRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClassRuns_ClassOfferings_ClassOfferingId",
                        column: x => x.ClassOfferingId,
                        principalTable: "ClassOfferings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ClassRuns_Instructors_InstructorId",
                        column: x => x.InstructorId,
                        principalTable: "Instructors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClassRuns_ClassOfferingId",
                table: "ClassRuns",
                column: "ClassOfferingId");

            migrationBuilder.CreateIndex(
                name: "IX_ClassRuns_InstructorId",
                table: "ClassRuns",
                column: "InstructorId");

            migrationBuilder.CreateIndex(
                name: "IX_ClassRuns_Sku",
                table: "ClassRuns",
                column: "Sku");

            migrationBuilder.AddForeignKey(
                name: "FK_ClassSessions_ClassRuns_ClassRunId",
                table: "ClassSessions",
                column: "ClassRunId",
                principalTable: "ClassRuns",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ClassSessions_ClassRuns_ClassRunId",
                table: "ClassSessions");

            migrationBuilder.DropTable(
                name: "ClassRuns");

            migrationBuilder.RenameColumn(
                name: "ClassRunId",
                table: "ClassSessions",
                newName: "ClassOfferingId");

            migrationBuilder.RenameIndex(
                name: "IX_ClassSessions_ClassRunId_StartsAt",
                table: "ClassSessions",
                newName: "IX_ClassSessions_ClassOfferingId_StartsAt");

            migrationBuilder.AddColumn<int>(
                name: "Capacity",
                table: "ClassSessions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "DisplayTimeZone",
                table: "ClassSessions",
                type: "varchar(64)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "PriceCents",
                table: "ClassSessions",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SeatsRemaining",
                table: "ClassSessions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Sku",
                table: "ClassSessions",
                type: "varchar(64)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "SoldOut",
                table: "ClassSessions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Cadence",
                table: "ClassOfferings",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Instructor",
                table: "ClassOfferings",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "StartingPriceCents",
                table: "ClassOfferings",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClassSessions_Sku",
                table: "ClassSessions",
                column: "Sku");

            migrationBuilder.AddForeignKey(
                name: "FK_ClassSessions_ClassOfferings_ClassOfferingId",
                table: "ClassSessions",
                column: "ClassOfferingId",
                principalTable: "ClassOfferings",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
