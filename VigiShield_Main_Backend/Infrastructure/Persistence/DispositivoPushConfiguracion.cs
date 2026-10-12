using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VigiShield.Domain.Entities;

namespace VigiShield.Infrastructure.Persistence;

/// <summary>
/// Tabla de los teléfonos que reciben push. Va en su propia clase para no tocar
/// la configuración de las demás tablas: AppDbContext solo la aplica.
/// </summary>
public class DispositivoPushConfiguracion : IEntityTypeConfiguration<DispositivoPush>
{
    public void Configure(EntityTypeBuilder<DispositivoPush> e)
    {
        e.ToTable("DispositivosPush");
        e.HasKey(x => x.Id);
        e.Property(x => x.Plataforma).HasMaxLength(20);
        e.Property(x => x.Entorno).HasMaxLength(20);
        e.Property(x => x.Token).HasMaxLength(512);
        // Un token es de un solo teléfono: si otra cuenta entra en ese teléfono, pasa a ella.
        e.HasIndex(x => x.Token).IsUnique();
        e.HasIndex(x => x.UserId);
        e.HasOne(x => x.User)
         .WithMany()
         .HasForeignKey(x => x.UserId)
         .OnDelete(DeleteBehavior.Cascade);
    }
}
