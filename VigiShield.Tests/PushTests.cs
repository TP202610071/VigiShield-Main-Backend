using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using VigiShield.Application.DTOs.Push;
using VigiShield.Application.Services;
using VigiShield.Common.Exceptions;
using VigiShield.Domain.Entities;
using VigiShield.Domain.Enums;
using VigiShield.Infrastructure.Persistence;
using VigiShield.Infrastructure.Services;
using Xunit;

namespace VigiShield.Tests;

/// <summary>Push: APNs directo para iPhone, registro de teléfonos y limpieza de tokens muertos.</summary>
public class PushTests
{
    private static readonly ECDsa Llave = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    /// <summary>Hace de APNs: responde según el token y guarda lo que recibió.</summary>
    private sealed class ApnsFalso : HttpMessageHandler
    {
        public readonly List<(string Url, Dictionary<string, string> Cabeceras, string Cuerpo)> Pedidos = [];
        public Func<string, bool, (HttpStatusCode, string?)> Responder { get; set; } = (_, _) => (HttpStatusCode.OK, null);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var cuerpo = r.Content is null ? "" : await r.Content.ReadAsStringAsync(ct);
            lock (Pedidos)
                Pedidos.Add((r.RequestUri!.ToString(),
                    r.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase), cuerpo));
            var token = r.RequestUri!.Segments[^1];
            var (estado, razon) = Responder(token, r.RequestUri.Host.Contains("sandbox"));
            return new HttpResponseMessage(estado)
            {
                Content = new StringContent(razon is null ? "" : JsonSerializer.Serialize(new { reason = razon })),
            };
        }
    }

    private static IConfiguration Config(bool conLlave = true) => new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?>
        {
            ["Apns:TeamId"] = conLlave ? "ZUXS3B846T" : null,
            ["Apns:KeyId"] = conLlave ? "ABC123DEFG" : null,
            ["Apns:PrivateKey"] = conLlave ? Llave.ExportPkcs8PrivateKeyPem() : null,
        }).Build();

    private static (PushService push, ApnsFalso apns) Servicio(TestDb f, bool conLlave = true)
    {
        var cfg = Config(conLlave);
        var falso = new ApnsFalso();
        var apns = new ApnsService(cfg, NullLogger<ApnsService>.Instance) { Cliente = new HttpClient(falso) };
        var fcm = new FcmService(new Fabrica(), cfg, NullLogger<FcmService>.Instance);
        var servicios = new ServiceCollection().AddSingleton(f.Db).BuildServiceProvider();
        return (new PushService(f.Db, apns, fcm, servicios.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<PushService>.Instance), falso);
    }

    private sealed class Fabrica : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private static async Task<(User ana, User beto)> HogarAsync(TestDb f)
    {
        var hogar = new Household { Id = Guid.NewGuid(), Address = "Av. 1" };
        var ana = new User { Id = Guid.NewGuid(), Email = "ana@x.com", Name = "Ana", Household = hogar, Role = UserRole.Primary };
        var beto = new User { Id = Guid.NewGuid(), Email = "beto@x.com", Name = "Beto", Household = hogar, Role = UserRole.Secondary };
        hogar.PrimaryUserId = ana.Id;
        f.Db.AddRange(hogar, ana, beto);
        await f.Db.SaveChangesAsync();
        return (ana, beto);
    }

    [Fact]
    public async Task Apns_request_has_the_headers_payload_and_a_valid_es256_token()
    {
        var falso = new ApnsFalso();
        var apns = new ApnsService(Config(), NullLogger<ApnsService>.Instance) { Cliente = new HttpClient(falso) };

        var (r, _) = await apns.EnviarAsync("aabbcc", sandbox: false, "Persona desconocida", "Puerta · 14:32",
            new Dictionary<string, string> { ["eventId"] = "e1" });

        Assert.Equal(ResultadoPush.Enviado, r);
        var (url, cab, cuerpo) = Assert.Single(falso.Pedidos);
        Assert.Equal("https://api.push.apple.com/3/device/aabbcc", url);
        Assert.Equal("com.vigishield.app", cab["apns-topic"]);
        Assert.Equal("alert", cab["apns-push-type"]);
        Assert.Equal("10", cab["apns-priority"]);
        using var json = JsonDocument.Parse(cuerpo);
        Assert.Equal("Persona desconocida", json.RootElement.GetProperty("aps").GetProperty("alert").GetProperty("title").GetString());
        Assert.Equal("time-sensitive", json.RootElement.GetProperty("aps").GetProperty("interruption-level").GetString());
        Assert.Equal("e1", json.RootElement.GetProperty("eventId").GetString());

        // El JWT va firmado con la llave .p8 y lleva el Team ID y el Key ID.
        var jwt = cab["authorization"].Replace("bearer ", "").Split('.');
        static byte[] B64(string s) => Convert.FromBase64String(s.Replace('-', '+').Replace('_', '/').PadRight((s.Length + 3) / 4 * 4, '='));
        using var cabecera = JsonDocument.Parse(B64(jwt[0]));
        using var claims = JsonDocument.Parse(B64(jwt[1]));
        Assert.Equal("ES256", cabecera.RootElement.GetProperty("alg").GetString());
        Assert.Equal("ABC123DEFG", cabecera.RootElement.GetProperty("kid").GetString());
        Assert.Equal("ZUXS3B846T", claims.RootElement.GetProperty("iss").GetString());
        Assert.True(Llave.VerifyData(Encoding.ASCII.GetBytes($"{jwt[0]}.{jwt[1]}"), B64(jwt[2]), HashAlgorithmName.SHA256));
    }

    [Fact]
    public async Task Without_the_key_nothing_is_sent()
    {
        var falso = new ApnsFalso();
        var apns = new ApnsService(Config(conLlave: false), NullLogger<ApnsService>.Instance) { Cliente = new HttpClient(falso) };
        var (r, _) = await apns.EnviarAsync("aa", false, "t", "c", new Dictionary<string, string>());
        Assert.Equal(ResultadoPush.NoConfigurado, r);
        Assert.Empty(falso.Pedidos);
    }

    [Fact]
    public async Task A_token_moves_to_the_account_that_signs_in_on_that_phone()
    {
        await using var f = new TestDb();
        var (push, _) = Servicio(f);
        var (ana, beto) = await HogarAsync(f);

        await push.RegistrarAsync(ana.Id, new RegistrarDispositivoRequest("tok1", "ios", "sandbox"));
        await push.RegistrarAsync(beto.Id, new RegistrarDispositivoRequest("tok1", "ios", null));

        var d = await f.Db.Set<DispositivoPush>().SingleAsync();
        Assert.Equal(beto.Id, d.UserId);
        Assert.Equal("produccion", d.Entorno);
        Assert.Equal(1, await push.DarDeBajaAsync("tok1"));
        Assert.False(await f.Db.Set<DispositivoPush>().AnyAsync());
    }

    [Fact]
    public async Task Test_push_removes_dead_tokens_and_fixes_the_environment()
    {
        await using var f = new TestDb();
        var (push, apns) = Servicio(f);
        var (ana, _) = await HogarAsync(f);
        await push.RegistrarAsync(ana.Id, new RegistrarDispositivoRequest("vivo", "ios", "produccion"));
        await push.RegistrarAsync(ana.Id, new RegistrarDispositivoRequest("muerto", "ios", "produccion"));
        await push.RegistrarAsync(ana.Id, new RegistrarDispositivoRequest("desarrollo", "ios", "produccion"));
        apns.Responder = (token, sandbox) => token switch
        {
            "muerto" => (HttpStatusCode.Gone, "Unregistered"),
            "desarrollo" when !sandbox => (HttpStatusCode.BadRequest, "BadDeviceToken"),
            _ => (HttpStatusCode.OK, null),
        };

        var r = await push.EnviarPruebaAsync(ana.Id);

        Assert.Equal(3, r.Dispositivos);
        Assert.Equal(2, r.Enviados);
        Assert.Equal(1, r.Invalidos);
        f.Db.ChangeTracker.Clear();
        var quedan = await f.Db.Set<DispositivoPush>().OrderBy(d => d.Token).ToListAsync();
        Assert.Equal(["desarrollo", "vivo"], quedan.Select(d => d.Token));
        Assert.Equal("sandbox", quedan[0].Entorno);
    }

    [Fact]
    public async Task An_event_reaches_every_phone_of_the_household_and_no_other()
    {
        await using var f = new TestDb();
        var (push, apns) = Servicio(f);
        var (ana, beto) = await HogarAsync(f);
        var otroHogar = new Household { Id = Guid.NewGuid(), Address = "Av. 2" };
        var luis = new User { Id = Guid.NewGuid(), Email = "luis@x.com", Name = "Luis", Household = otroHogar, Role = UserRole.Primary };
        f.Db.AddRange(otroHogar, luis);
        await f.Db.SaveChangesAsync();
        await push.RegistrarAsync(ana.Id, new RegistrarDispositivoRequest("ana", "ios", null));
        await push.RegistrarAsync(beto.Id, new RegistrarDispositivoRequest("beto", "ios", null));
        await push.RegistrarAsync(luis.Id, new RegistrarDispositivoRequest("luis", "ios", null));

        var ev = new SecurityEvent { Id = Guid.NewGuid(), HouseholdId = ana.HouseholdId, EventType = EventType.UnknownFace };
        await push.EnviarEventoAsync(ev, "Puerta", "14:32");

        // El envío sigue en segundo plano.
        for (var i = 0; i < 50 && apns.Pedidos.Count < 2; i++) await Task.Delay(20);
        await Task.Delay(50);
        var tokens = apns.Pedidos.Select(p => p.Url.Split('/')[^1]).Order().ToList();
        Assert.Equal(["ana", "beto"], tokens);
        Assert.Contains("Persona desconocida", apns.Pedidos[0].Cuerpo);
        Assert.Contains(ev.Id.ToString(), apns.Pedidos[0].Cuerpo);
    }

    [Fact]
    public async Task Deleting_a_user_deletes_their_phones()
    {
        await using var f = new TestDb();
        var (push, _) = Servicio(f);
        var (_, beto) = await HogarAsync(f);
        await push.RegistrarAsync(beto.Id, new RegistrarDispositivoRequest("beto", "android", null));
        await f.Db.Users.Where(u => u.Id == beto.Id).ExecuteDeleteAsync();
        Assert.False(await f.Db.Set<DispositivoPush>().AnyAsync());
    }

    private static MensajePush Mensaje(string nivel = "normal", bool sonido = true, string? imagen = null, int? insignia = null) =>
        new("Hola 👋", "Aviso del equipo", "Mañana probamos la cámara 📷", imagen, nivel, sonido, insignia,
            new Dictionary<string, string> { ["eventId"] = "/history", ["type"] = "Admin" });

    [Fact]
    public async Task Panel_push_to_a_household_reaches_every_member_and_to_an_account_only_theirs()
    {
        await using var f = new TestDb();
        var (push, apns) = Servicio(f);
        var (ana, beto) = await HogarAsync(f);
        await push.RegistrarAsync(ana.Id, new RegistrarDispositivoRequest("ana", "ios", null));
        await push.RegistrarAsync(beto.Id, new RegistrarDispositivoRequest("beto", "ios", null));

        var hogar = await push.EnviarAdminAsync("hogar", ana.HouseholdId, Mensaje());
        Assert.Equal(2, hogar.Enviados);
        Assert.Equal(2, hogar.Ios);

        apns.Pedidos.Clear();
        var cuenta = await push.EnviarAdminAsync("usuario", beto.Id, Mensaje());
        Assert.Equal(1, cuenta.Enviados);
        Assert.EndsWith("/beto", Assert.Single(apns.Pedidos).Url);

        await Assert.ThrowsAsync<AppException>(() => push.EnviarAdminAsync("todos", ana.Id, Mensaje()));
    }

    [Fact]
    public async Task Panel_push_carries_emojis_subtitle_badge_level_sound_and_image()
    {
        await using var f = new TestDb();
        var (push, apns) = Servicio(f);
        var (ana, _) = await HogarAsync(f);
        await push.RegistrarAsync(ana.Id, new RegistrarDispositivoRequest("ana", "ios", null));

        await push.EnviarAdminAsync("usuario", ana.Id,
            Mensaje(nivel: "silencioso", sonido: false, imagen: "https://bucket.vigishield.app/foto.jpg", insignia: 3));

        var (_, cab, cuerpo) = Assert.Single(apns.Pedidos);
        Assert.Equal("5", cab["apns-priority"]);
        using var json = JsonDocument.Parse(cuerpo);
        var aps = json.RootElement.GetProperty("aps");
        Assert.Equal("Hola 👋", aps.GetProperty("alert").GetProperty("title").GetString());
        Assert.Equal("Aviso del equipo", aps.GetProperty("alert").GetProperty("subtitle").GetString());
        Assert.Equal("Mañana probamos la cámara 📷", aps.GetProperty("alert").GetProperty("body").GetString());
        Assert.Equal("passive", aps.GetProperty("interruption-level").GetString());
        Assert.Equal(3, aps.GetProperty("badge").GetInt32());
        Assert.Equal(1, aps.GetProperty("mutable-content").GetInt32());
        Assert.False(aps.TryGetProperty("sound", out _));
        Assert.Equal("https://bucket.vigishield.app/foto.jpg", json.RootElement.GetProperty("imagen").GetString());
        Assert.Equal("/history", json.RootElement.GetProperty("eventId").GetString());
    }

    [Theory]
    [InlineData("urgente", true, "alertas", "time-sensitive")]
    [InlineData("normal", true, "avisos", "active")]
    [InlineData("normal", false, "silenciosas", "active")]
    [InlineData("silencioso", true, "silenciosas", "passive")]
    public void Each_level_maps_to_its_android_channel_and_ios_interruption_level(
        string nivel, bool sonido, string canal, string ios)
    {
        var m = Mensaje(nivel, sonido);
        Assert.Equal(canal, m.CanalAndroid);
        Assert.Equal(ios, m.NivelIos);
    }
}
