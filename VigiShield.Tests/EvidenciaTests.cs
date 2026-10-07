using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using VigiShield.Application.Services;
using VigiShield.Common.Exceptions;
using VigiShield.Domain.Entities;
using VigiShield.Domain.Enums;
using Xunit;

namespace VigiShield.Tests;

public class EvidenciaTests
{
    private static readonly DateTime Politica = new(2026, 10, 7, 1, 0, 0, DateTimeKind.Utc);

    private static EvidenciaService Servicio(TestDb f) => new(f.Db, new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Evidencia:PoliticaDesde"] = "2026-10-07T01:00:00Z" })
        .Build());

    private static Household Hogar(TestDb f, string nombre, DateTime registrado, DateTime? terminos)
    {
        var hogar = new Household { Id = Guid.NewGuid(), Address = "Av. " + nombre };
        var dueno = new User { Id = Guid.NewGuid(), Email = $"{nombre}@x.com", Name = nombre, Household = hogar,
            Role = UserRole.Primary, CreatedAt = registrado, TermsAcceptedAt = terminos, TermsVersion = "2026-10-05" };
        hogar.PrimaryUserId = dueno.Id;
        f.Db.AddRange(hogar, dueno,
            new CameraConfig { Household = hogar, Name = "Celular", StreamMode = StreamMode.MobileWebRtc });
        return hogar;
    }

    [Fact]
    public async Task Por_defecto_solo_se_graba_a_quien_se_registro_con_la_politica_nueva()
    {
        await using var f = new TestDb();
        var nuevo = Hogar(f, "nuevo", Politica.AddHours(2), Politica.AddHours(2));
        var antiguo = Hogar(f, "antiguo", Politica.AddDays(-3), Politica.AddDays(-3));
        // Cuenta nueva que aceptó después, desde la pantalla de la app (que dice que no se graba).
        var tardio = Hogar(f, "tardio", Politica.AddHours(1), Politica.AddDays(1));
        var sinTerminos = Hogar(f, "sin", Politica.AddHours(1), null);
        await f.Db.SaveChangesAsync();

        var estado = await Servicio(f).ListarAsync();

        var grabar = estado.Hogares.ToDictionary(h => h.HogarId, h => h.Grabar);
        Assert.True(grabar[nuevo.Id]);
        Assert.False(grabar[antiguo.Id]);
        Assert.False(grabar[tardio.Id]);
        Assert.False(grabar[sinTerminos.Id]);
        Assert.All(estado.Hogares, h => Assert.False(h.Manual));
        Assert.Equal(1, estado.Hogares.First(h => h.HogarId == nuevo.Id).CamarasCelular);
    }

    [Fact]
    public async Task El_interruptor_manda_sobre_la_regla_y_el_grabador_lo_ve()
    {
        await using var f = new TestDb();
        var nuevo = Hogar(f, "nuevo", Politica.AddHours(2), Politica.AddHours(2));
        var antiguo = Hogar(f, "antiguo", Politica.AddDays(-3), Politica.AddDays(-3));
        await f.Db.SaveChangesAsync();
        var s = Servicio(f);

        var a = await s.CambiarAsync(antiguo.Id, true);    // aceptó por mensaje
        var n = await s.CambiarAsync(nuevo.Id, false);     // pidió que no se grabe

        Assert.True(a.Grabar && a.Manual);
        Assert.False(n.Grabar);
        var (grabar, existentes) = await s.ParaGrabadorAsync();
        Assert.Equal([antiguo.Id], grabar);
        Assert.Equal(2, existentes.Count);
        await Assert.ThrowsAsync<AppException>(() => s.CambiarAsync(Guid.NewGuid(), true));
    }

    [Fact]
    public async Task Al_borrar_el_hogar_se_borra_su_ajuste()
    {
        await using var f = new TestDb();
        var hogar = Hogar(f, "ana", Politica.AddDays(-3), Politica.AddDays(-3));
        await f.Db.SaveChangesAsync();
        await Servicio(f).CambiarAsync(hogar.Id, true);

        await f.Db.Households.Where(h => h.Id == hogar.Id).ExecuteDeleteAsync();

        Assert.False(await f.Db.Set<EvidenciaGrabacion>().AnyAsync());
    }
}
