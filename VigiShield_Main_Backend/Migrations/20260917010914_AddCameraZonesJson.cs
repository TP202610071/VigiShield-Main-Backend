using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VigiShield.Migrations
{
    /// <inheritdoc />
    public partial class AddCameraZonesJson : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Idempotente a propósito: la BD de producción pudo haber recibido
            // "AvatarPath" fuera de una migración (drift del feature de avatar),
            // y "ZonesJson" es nuevo. "ADD COLUMN IF NOT EXISTS" evita fallar si
            // alguna ya existe — no destruye ni sobreescribe datos.
            migrationBuilder.Sql(@"ALTER TABLE ""Users"" ADD COLUMN IF NOT EXISTS ""AvatarPath"" text;");
            migrationBuilder.Sql(@"ALTER TABLE ""CameraConfigs"" ADD COLUMN IF NOT EXISTS ""ZonesJson"" text;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"ALTER TABLE ""CameraConfigs"" DROP COLUMN IF EXISTS ""ZonesJson"";");
            migrationBuilder.Sql(@"ALTER TABLE ""Users"" DROP COLUMN IF EXISTS ""AvatarPath"";");
        }
    }
}
