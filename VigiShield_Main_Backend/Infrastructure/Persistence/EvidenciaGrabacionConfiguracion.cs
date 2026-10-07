using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VigiShield.Domain.Entities;

namespace VigiShield.Infrastructure.Persistence;

/// <summary>
/// Tabla del interruptor «Grabar evidencia». Va en su propia clase para no
/// tocar la configuración de las demás tablas: AppDbContext solo la aplica.
/// </summary>
public class EvidenciaGrabacionConfiguracion : IEntityTypeConfiguration<EvidenciaGrabacion>
{
    public void Configure(EntityTypeBuilder<EvidenciaGrabacion> e)
    {
        e.ToTable("EvidenciaGrabaciones");
        e.HasKey(x => x.HouseholdId);
        // Al borrar el hogar (o la cuenta del dueño) se va también su ajuste.
        e.HasOne(x => x.Household)
         .WithMany()
         .HasForeignKey(x => x.HouseholdId)
         .OnDelete(DeleteBehavior.Cascade);
    }
}
