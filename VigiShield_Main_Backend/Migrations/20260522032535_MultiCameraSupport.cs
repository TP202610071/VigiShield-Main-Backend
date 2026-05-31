using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VigiShield.Migrations
{
    /// <inheritdoc />
    public partial class MultiCameraSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CameraConfigs_HouseholdId",
                table: "CameraConfigs");

            migrationBuilder.AddColumn<bool>(
                name: "IsDefault",
                table: "CameraConfigs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "CameraConfigs",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_CameraConfigs_HouseholdId",
                table: "CameraConfigs",
                column: "HouseholdId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CameraConfigs_HouseholdId",
                table: "CameraConfigs");

            migrationBuilder.DropColumn(
                name: "IsDefault",
                table: "CameraConfigs");

            migrationBuilder.DropColumn(
                name: "Name",
                table: "CameraConfigs");

            migrationBuilder.CreateIndex(
                name: "IX_CameraConfigs_HouseholdId",
                table: "CameraConfigs",
                column: "HouseholdId",
                unique: true);
        }
    }
}
