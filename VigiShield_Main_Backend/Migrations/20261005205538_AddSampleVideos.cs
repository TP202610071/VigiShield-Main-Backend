using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VigiShield.Migrations
{
    /// <inheritdoc />
    public partial class AddSampleVideos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsSample",
                table: "CameraConfigs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "SampleUntil",
                table: "CameraConfigs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SampleVideoSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HouseholdId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    VideoKey = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SampleVideoSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SampleVideoSessions_Households_HouseholdId",
                        column: x => x.HouseholdId,
                        principalTable: "Households",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SampleVideoSessions_HouseholdId",
                table: "SampleVideoSessions",
                column: "HouseholdId");

            migrationBuilder.CreateIndex(
                name: "IX_SampleVideoSessions_VideoKey",
                table: "SampleVideoSessions",
                column: "VideoKey");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SampleVideoSessions");

            migrationBuilder.DropColumn(
                name: "IsSample",
                table: "CameraConfigs");

            migrationBuilder.DropColumn(
                name: "SampleUntil",
                table: "CameraConfigs");
        }
    }
}
