using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VigiShield.Migrations
{
    /// <inheritdoc />
    public partial class AddCameraIdToEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CameraId",
                table: "Events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CameraName",
                table: "Events",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Events_CameraId",
                table: "Events",
                column: "CameraId");

            migrationBuilder.AddForeignKey(
                name: "FK_Events_CameraConfigs_CameraId",
                table: "Events",
                column: "CameraId",
                principalTable: "CameraConfigs",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Events_CameraConfigs_CameraId",
                table: "Events");

            migrationBuilder.DropIndex(
                name: "IX_Events_CameraId",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "CameraId",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "CameraName",
                table: "Events");
        }
    }
}
