using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VigiShield.Domain.Entities;

namespace VigiShield.Infrastructure.Persistence;

/// <summary>
/// Tabla de las cuentas de Apple/Google. Va en su propia clase para no tocar
/// la configuración de las demás tablas: AppDbContext solo la aplica.
/// </summary>
public class CuentaExternaConfiguracion : IEntityTypeConfiguration<CuentaExterna>
{
    public void Configure(EntityTypeBuilder<CuentaExterna> e)
    {
        e.ToTable("CuentasExternas");
        e.HasKey(x => x.Id);
        e.Property(x => x.Proveedor).HasMaxLength(20);
        e.Property(x => x.Sujeto).HasMaxLength(255);
        // Una cuenta de Apple pertenece a un solo usuario de VigiShield.
        e.HasIndex(x => new { x.Proveedor, x.Sujeto }).IsUnique();
        e.HasIndex(x => x.UserId);
        // Al borrar el usuario (o su hogar) se va también el vínculo.
        e.HasOne(x => x.User)
         .WithMany()
         .HasForeignKey(x => x.UserId)
         .OnDelete(DeleteBehavior.Cascade);
    }
}
