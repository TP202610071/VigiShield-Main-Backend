using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VigiShield.Domain.Entities;

namespace VigiShield.Infrastructure.Persistence;

/// <summary>Tablas de bloqueos del administrador. AppDbContext solo las aplica.</summary>
public class BloqueoAdminConfiguracion : IEntityTypeConfiguration<BloqueoAdmin>
{
    public void Configure(EntityTypeBuilder<BloqueoAdmin> e)
    {
        e.ToTable("BloqueosAdmin");
        e.HasKey(x => x.Id);
        e.Property(x => x.Tipo).HasMaxLength(20);
        e.Property(x => x.Valor).HasMaxLength(128);
        e.Property(x => x.Motivo).HasMaxLength(300);
        e.HasIndex(x => new { x.Tipo, x.Valor }).IsUnique();
    }
}

public class DispositivoVistoConfiguracion : IEntityTypeConfiguration<DispositivoVisto>
{
    public void Configure(EntityTypeBuilder<DispositivoVisto> e)
    {
        e.ToTable("DispositivosVistos");
        e.HasKey(x => x.Id);
        e.Property(x => x.DeviceId).HasMaxLength(128);
        e.Property(x => x.Ip).HasMaxLength(64);
        e.Property(x => x.Plataforma).HasMaxLength(20);
        e.HasIndex(x => new { x.UserId, x.DeviceId }).IsUnique();
        e.HasIndex(x => x.DeviceId);
        e.HasIndex(x => x.Ip);
        e.HasOne(x => x.User)
         .WithMany()
         .HasForeignKey(x => x.UserId)
         .OnDelete(DeleteBehavior.Cascade);
    }
}
