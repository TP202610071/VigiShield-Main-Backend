using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VigiShield.Migrations
{
    /// <inheritdoc />
    public partial class AddCameraIsActive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // defaultValue TRUE, no el false que genera EF por ser el valor por
            // defecto de bool en C#: con false, aplicar esta migracion apagaria
            // de golpe TODAS las camaras ya existentes y la IA dejaria de
            // procesarlas sin que nadie lo hubiera pedido.
            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "CameraConfigs",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "DisabledEventTypes",
                table: "AlertConfigs",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "CameraConfigs");

            migrationBuilder.DropColumn(
                name: "DisabledEventTypes",
                table: "AlertConfigs");
        }
    }
}
