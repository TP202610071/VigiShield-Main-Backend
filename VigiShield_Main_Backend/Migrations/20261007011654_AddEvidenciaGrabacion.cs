using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VigiShield.Migrations
{
    /// <inheritdoc />
    public partial class AddEvidenciaGrabacion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EvidenciaGrabaciones",
                columns: table => new
                {
                    HouseholdId = table.Column<Guid>(type: "uuid", nullable: false),
                    Grabar = table.Column<bool>(type: "boolean", nullable: false),
                    ActualizadoEn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvidenciaGrabaciones", x => x.HouseholdId);
                    table.ForeignKey(
                        name: "FK_EvidenciaGrabaciones_Households_HouseholdId",
                        column: x => x.HouseholdId,
                        principalTable: "Households",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EvidenciaGrabaciones");
        }
    }
}
