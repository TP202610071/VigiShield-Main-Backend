using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VigiShield.Migrations
{
    /// <inheritdoc />
    public partial class AddBloqueosAdmin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BloqueosAdmin",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Tipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Valor = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Motivo = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    CreadoPor = table.Column<Guid>(type: "uuid", nullable: true),
                    CreadoEn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BloqueosAdmin", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DispositivosVistos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Ip = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Plataforma = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    PrimeraVez = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UltimaVez = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DispositivosVistos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DispositivosVistos_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BloqueosAdmin_Tipo_Valor",
                table: "BloqueosAdmin",
                columns: new[] { "Tipo", "Valor" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DispositivosVistos_DeviceId",
                table: "DispositivosVistos",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_DispositivosVistos_Ip",
                table: "DispositivosVistos",
                column: "Ip");

            migrationBuilder.CreateIndex(
                name: "IX_DispositivosVistos_UserId_DeviceId",
                table: "DispositivosVistos",
                columns: new[] { "UserId", "DeviceId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BloqueosAdmin");

            migrationBuilder.DropTable(
                name: "DispositivosVistos");
        }
    }
}
