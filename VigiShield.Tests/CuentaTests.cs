using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using VigiShield.Application.Services;
using VigiShield.Common.Exceptions;
using VigiShield.Domain.Entities;
using VigiShield.Domain.Enums;
using VigiShield.Infrastructure.Services;
using Xunit;

namespace VigiShield.Tests;

public class CuentaTests
{
    private const string Clave = "Clave.2026";

    private sealed class Entorno(string raiz) : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = Path.Combine(raiz, "wwwroot");
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "pruebas";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = raiz;
        public string EnvironmentName { get; set; } = Environments.Development;
    }

    private static CuentaService Servicio(TestDb f, string raiz) => new(f.Db,
        new R2Service(new ConfigurationBuilder().Build(), NullLogger<R2Service>.Instance),
        new Entorno(raiz), NullLogger<CuentaService>.Instance);

    private static (Household hogar, User dueno, User invitado) Hogar(TestDb f, string nombre)
    {
        var hogar = new Household { Id = Guid.NewGuid(), Address = "Av. " + nombre };
        var dueno = new User { Id = Guid.NewGuid(), Email = $"{nombre}@x.com", Name = nombre, Household = hogar,
            Role = UserRole.Primary, PasswordHash = BCrypt.Net.BCrypt.HashPassword(Clave) };
        var invitado = new User { Id = Guid.NewGuid(), Email = $"{nombre}.inv@x.com", Name = nombre + " inv", Household = hogar,
            Role = UserRole.Secondary, PasswordHash = BCrypt.Net.BCrypt.HashPassword(Clave) };
        hogar.PrimaryUserId = dueno.Id;
        var cam = new CameraConfig { Household = hogar, Name = "Puerta" };
        var ev = new SecurityEvent { Household = hogar, Camera = cam, CameraName = "Puerta", EventType = EventType.UnknownFace };
        f.Db.AddRange(hogar, dueno, invitado, cam, ev,
            new AuthorizedFace { Household = hogar, PersonName = "Mamá" },
            new SampleVideoSession { Household = hogar, VideoKey = "ejemplo01" },
            new NotificationLog { Household = hogar, SecurityEvent = ev, Recipient = invitado, Message = "Alerta" },
            new NotificationLog { Household = hogar, SecurityEvent = ev, Recipient = dueno, Message = "Alerta" });
        return (hogar, dueno, invitado);
    }

    [Fact]
    public async Task Owner_deletes_the_whole_household_and_its_files_but_nothing_else()
    {
        await using var f = new TestDb();
        var (hogar, dueno, invitado) = Hogar(f, "ana");
        var (otro, _, _) = Hogar(f, "luis");
        await f.Db.SaveChangesAsync();
        var raiz = Directory.CreateTempSubdirectory().FullName;
        var rostros = Directory.CreateDirectory(Path.Combine(raiz, "wwwroot", "uploads", "faces", hogar.Id.ToString())).FullName;
        await File.WriteAllTextAsync(Path.Combine(rostros, "1.jpg"), "x");
        var avatares = Directory.CreateDirectory(Path.Combine(raiz, "wwwroot", "avatars")).FullName;
        await File.WriteAllTextAsync(Path.Combine(avatares, $"{invitado.Id}.jpg"), "x");

        var r = await Servicio(f, raiz).EliminarAsync(dueno.Id, Clave);

        Assert.True(r.HogarEliminado);
        Assert.Equal(2, r.UsuariosEliminados);
        f.Db.ChangeTracker.Clear();
        Assert.False(await f.Db.Households.AnyAsync(h => h.Id == hogar.Id));
        Assert.False(await f.Db.Users.AnyAsync(u => u.HouseholdId == hogar.Id));
        Assert.False(await f.Db.Events.AnyAsync(e => e.HouseholdId == hogar.Id));
        Assert.False(await f.Db.CameraConfigs.AnyAsync(c => c.HouseholdId == hogar.Id));
        Assert.False(await f.Db.AuthorizedFaces.AnyAsync(x => x.HouseholdId == hogar.Id));
        Assert.False(await f.Db.NotificationLogs.AnyAsync(n => n.HouseholdId == hogar.Id));
        Assert.False(await f.Db.SampleVideoSessions.AnyAsync(s => s.HouseholdId == hogar.Id));
        Assert.False(Directory.Exists(rostros));
        Assert.False(File.Exists(Path.Combine(avatares, $"{invitado.Id}.jpg")));
        // El otro hogar sigue intacto.
        Assert.Equal(2, await f.Db.Users.CountAsync(u => u.HouseholdId == otro.Id));
        Assert.Equal(1, await f.Db.Events.CountAsync(e => e.HouseholdId == otro.Id));
    }

    [Fact]
    public async Task Invited_member_deletes_only_their_own_account()
    {
        await using var f = new TestDb();
        var (hogar, dueno, invitado) = Hogar(f, "ana");
        await f.Db.SaveChangesAsync();

        var r = await Servicio(f, Directory.CreateTempSubdirectory().FullName).EliminarAsync(invitado.Id, Clave);

        Assert.False(r.HogarEliminado);
        f.Db.ChangeTracker.Clear();
        Assert.False(await f.Db.Users.AnyAsync(u => u.Id == invitado.Id));
        Assert.True(await f.Db.Users.AnyAsync(u => u.Id == dueno.Id));
        Assert.True(await f.Db.Events.AnyAsync(e => e.HouseholdId == hogar.Id));
        Assert.Equal(1, await f.Db.NotificationLogs.CountAsync());   // solo se fue el aviso del invitado
    }

    [Fact]
    public async Task Wrong_password_deletes_nothing()
    {
        await using var f = new TestDb();
        var (_, dueno, _) = Hogar(f, "ana");
        await f.Db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<AppException>(() =>
            Servicio(f, Directory.CreateTempSubdirectory().FullName).EliminarAsync(dueno.Id, "otra"));

        Assert.Equal(400, error.StatusCode);
        Assert.Equal(2, await f.Db.Users.CountAsync());
    }
}
