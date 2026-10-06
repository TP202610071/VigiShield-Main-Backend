using System.IO.Compression;
using System.Net;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using VigiShield.Application.DTOs.Admin;
using VigiShield.Application.Services;
using VigiShield.Common.Exceptions;
using VigiShield.Common.Security;
using VigiShield.Domain.Entities;
using VigiShield.Domain.Enums;
using Xunit;

namespace VigiShield.Tests;

public class AdminTests
{
    private static (Household hogar, User usuario) Hogar(TestDb f, string correo, DateTime? visto)
    {
        var hogar = new Household { Id = Guid.NewGuid(), Address = "Av. Prueba 1" };
        var usuario = new User { Id = Guid.NewGuid(), Email = correo, Name = correo, Household = hogar,
            Role = UserRole.Primary, LastSeenAt = visto, CreatedAt = DateTime.UtcNow.AddDays(-30) };
        hogar.PrimaryUserId = usuario.Id;
        f.Db.AddRange(hogar, usuario);
        return (hogar, usuario);
    }

    [Fact]
    public async Task Users_are_active_or_inactive_by_their_last_activity()
    {
        await using var f = new TestDb();
        var (h1, _) = Hogar(f, "activa@x.com", DateTime.UtcNow.AddMinutes(-2));
        Hogar(f, "dormida@x.com", DateTime.UtcNow.AddDays(-20));
        f.Db.AddRange(
            new CameraConfig { Household = h1, Name = "Puerta", IsActive = true, UpdatedAt = DateTime.UtcNow.AddDays(-30) },
            new CameraConfig { Household = h1, Name = "Video de ejemplo", IsSample = true, UpdatedAt = DateTime.UtcNow.AddDays(-30) });
        await f.Db.SaveChangesAsync();

        var usuarios = await new AdminService(f.Db).UsuariosAsync();

        var activa = usuarios.Single(u => u.Correo == "activa@x.com");
        Assert.Equal("activo", activa.Estado);
        Assert.True(activa.EnLinea);
        Assert.Equal(1, activa.Camaras);   // el video de ejemplo no cuenta
        Assert.Equal("inactivo", usuarios.Single(u => u.Correo == "dormida@x.com").Estado);
    }

    [Fact]
    public async Task Admin_can_switch_off_a_camera_of_any_household_but_not_the_sample_one()
    {
        await using var f = new TestDb();
        var (h, _) = Hogar(f, "tester@x.com", null);
        var cam = new CameraConfig { Household = h, Name = "Puerta", IsActive = true };
        var ejemplo = new CameraConfig { Household = h, Name = "Video de ejemplo", IsSample = true, SampleUntil = DateTime.UtcNow.AddMinutes(2) };
        f.Db.AddRange(cam, ejemplo);
        await f.Db.SaveChangesAsync();
        var admin = new AdminService(f.Db);

        var resultado = await admin.CambiarActivaAsync(cam.Id, false);

        Assert.False(resultado.Activa);
        Assert.Equal("tester@x.com", resultado.CorreoDueno);
        await Assert.ThrowsAsync<AppException>(() => admin.CambiarActivaAsync(ejemplo.Id, false));
    }

    [Fact]
    public async Task Events_are_filtered_paged_and_export_as_zip_with_csv_and_media()
    {
        await using var f = new TestDb();
        var (h, _) = Hogar(f, "tester@x.com", null);
        var ahora = DateTime.UtcNow;
        f.Db.AddRange(
            new SecurityEvent { Household = h, CameraName = "Puerta", EventType = EventType.UnknownFace, RiskLevel = RiskLevel.Medium,
                CreatedAt = ahora.AddMinutes(-3), ImageCapturePath = "https://bucket.test/events/a.jpg", VideoClipPath = "https://bucket.test/clips/a.mp4" },
            new SecurityEvent { Household = h, CameraName = "Video de ejemplo", EventType = EventType.SuspiciousIntent, RiskLevel = RiskLevel.High,
                CreatedAt = ahora.AddMinutes(-2), NotificationsEnabled = false },
            new SecurityEvent { Household = h, CameraName = "Puerta", EventType = EventType.Tailgating, RiskLevel = RiskLevel.Medium,
                CreatedAt = ahora.AddMinutes(-1) });
        await f.Db.SaveChangesAsync();
        var admin = new AdminService(f.Db);

        var desconocidas = await admin.EventosAsync(new AdminFiltroEventos(Tipo: "UnknownFace"));
        Assert.Equal(1, desconocidas.Total);
        Assert.Equal("Persona desconocida", desconocidas.Items[0].TipoEtiqueta);
        Assert.Equal("tester@x.com", desconocidas.Items[0].CorreoDueno);
        var ejemplos = await admin.EventosAsync(new AdminFiltroEventos(Ejemplos: true));
        Assert.True(Assert.Single(ejemplos.Items).EsEjemplo);
        var pagina = await admin.EventosAsync(new AdminFiltroEventos(Tamano: 2, Pagina: 2));
        Assert.Equal(3, pagina.Total);
        Assert.Single(pagina.Items);

        using var http = new HttpClient(new Archivos());
        using var zipBytes = new MemoryStream();
        await admin.ExportarAsync(new AdminFiltroEventos(), zipBytes, http, CancellationToken.None);
        zipBytes.Position = 0;
        using var zip = new ZipArchive(zipBytes, ZipArchiveMode.Read);
        var nombres = zip.Entries.Select(e => e.FullName).ToList();
        Assert.Contains(nombres, n => n.StartsWith("fotos/") && n.EndsWith(".jpg"));
        Assert.Contains(nombres, n => n.StartsWith("clips/") && n.EndsWith(".mp4"));
        using var csv = new StreamReader(zip.GetEntry("eventos.csv")!.Open(), Encoding.UTF8);
        var lineas = (await csv.ReadToEndAsync()).Trim().Split('\n');
        Assert.Equal(4, lineas.Length);   // cabecera + 3 eventos
        Assert.Contains("Riesgo de intrusión", lineas[1] + lineas[2] + lineas[3]);
    }

    [Fact]
    public void Only_admins_pass_the_admin_filter()
    {
        static AuthorizationFilterContext Contexto(string rol)
        {
            var http = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, rol)], "test")),
            };
            return new AuthorizationFilterContext(
                new ActionContext(http, new RouteData(), new ActionDescriptor()), []);
        }
        var filtro = new SoloAdminAttribute();
        var tester = Contexto("Primary");
        filtro.OnAuthorization(tester);
        Assert.IsType<ForbidResult>(tester.Result);
        var admin = Contexto("Admin");
        filtro.OnAuthorization(admin);
        Assert.Null(admin.Result);
    }

    private sealed class Archivos : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Encoding.ASCII.GetBytes("contenido " + request.RequestUri)),
            });
    }
}
