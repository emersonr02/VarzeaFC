using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Varzea.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Players",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Players", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CareerSlots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    SlotIndex = table.Column<int>(type: "integer", nullable: false),
                    Archived = table.Column<bool>(type: "boolean", nullable: false),
                    Seed = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    Country = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DraftPicks = table.Column<int[]>(type: "integer[]", nullable: false),
                    Position = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    TransferChoices = table.Column<bool[]>(type: "boolean[]", nullable: false),
                    RulesetVersion = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Score = table.Column<double>(type: "double precision", nullable: false),
                    TitlesScore = table.Column<double>(type: "double precision", nullable: false),
                    AwardsScore = table.Column<double>(type: "double precision", nullable: false),
                    ProductionScore = table.Column<double>(type: "double precision", nullable: false),
                    PeakScore = table.Column<double>(type: "double precision", nullable: false),
                    SavedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CareerSlots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CareerSlots_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Achievements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    CareerSlotId = table.Column<Guid>(type: "uuid", nullable: false),
                    PeriodType = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    PeriodKey = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Tier = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Score = table.Column<double>(type: "double precision", nullable: false),
                    AwardedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Achievements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Achievements_CareerSlots_CareerSlotId",
                        column: x => x.CareerSlotId,
                        principalTable: "CareerSlots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Achievements_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Achievements_CareerSlotId",
                table: "Achievements",
                column: "CareerSlotId");

            migrationBuilder.CreateIndex(
                name: "IX_Achievements_PlayerId_PeriodType_PeriodKey",
                table: "Achievements",
                columns: new[] { "PlayerId", "PeriodType", "PeriodKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CareerSlots_PlayerId_SlotIndex",
                table: "CareerSlots",
                columns: new[] { "PlayerId", "SlotIndex" },
                unique: true,
                filter: "NOT \"Archived\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Achievements");

            migrationBuilder.DropTable(
                name: "CareerSlots");

            migrationBuilder.DropTable(
                name: "Players");
        }
    }
}
