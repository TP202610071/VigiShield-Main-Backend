using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using VigiShield.Application.DTOs.Auth;
using VigiShield.Application.Services;
using VigiShield.Common.Exceptions;
using VigiShield.Domain.Entities;
using VigiShield.Domain.Enums;
using VigiShield.Infrastructure.Services;
using Xunit;

namespace VigiShield.Tests;

/// <summary>Iniciar sesión con Apple: verificar el token, vincular o crear la cuenta y borrarla.</summary>
public class CuentaExternaTests
{
    private const string Bundle = "com.vigishield.app";

    // Llave propia que hace de Apple: los tokens de prueba se firman con ella.
    private static readonly ECDsa LlaveApple = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private static readonly ECDsaSecurityKey ClaveApple = new(LlaveApple) { KeyId = "prueba" };

    private sealed class Http : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private sealed class Entorno(string raiz) : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = Path.Combine(raiz, "wwwroot");
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "pruebas";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = raiz;
        public string EnvironmentName { get; set; } = Environments.Development;
    }

    private static IConfiguration Config() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Jwt:Secret"] = "clave-de-pruebas-de-al-menos-32-caracteres!!",
        ["Jwt:Issuer"] = "VigiShield",
        ["Jwt:Audience"] = "VigiShieldApp",
    }).Build();

    private static (LoginExternoService servicio, CuentaService cuentas) Servicio(TestDb f)
    {
        var cfg = Config();
        var apple = new AppleSignInService(cfg, new Http(), NullLogger<AppleSignInService>.Instance)
        {
            ObtenerClaves = _ => Task.FromResult<ICollection<SecurityKey>>([ClaveApple]),
        };
        var jwt = new JwtService(cfg);
        var auth = new AuthService(f.Db, jwt, NullLogger<AuthService>.Instance,
            new WhatsAppService(new Http(), cfg, NullLogger<WhatsAppService>.Instance),
            new EmailService(cfg, NullLogger<EmailService>.Instance), cfg);
        var cuentas = new CuentaService(f.Db, new R2Service(new ConfigurationBuilder().Build(), NullLogger<R2Service>.Instance),
            new Entorno(Directory.CreateTempSubdirectory().FullName), NullLogger<CuentaService>.Instance);
        return (new LoginExternoService(f.Db, apple, jwt, auth, cuentas, NullLogger<LoginExternoService>.Instance), cuentas);
    }

    private static string Token(string sub, string? email = null, bool verificado = true, string aud = Bundle,
        string? nonce = null, SecurityKey? llave = null)
    {
        var claims = new Dictionary<string, object> { ["sub"] = sub, ["email_verified"] = verificado ? "true" : "false" };
        if (email is not null) claims["email"] = email;
        if (nonce is not null) claims["nonce"] = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(nonce)));
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = AppleSignInService.Emisor,
            Audience = aud,
            Expires = DateTime.UtcNow.AddMinutes(10),
            Claims = claims,
            SigningCredentials = new SigningCredentials(llave ?? ClaveApple, SecurityAlgorithms.EcdsaSha256),
        });
    }

    private static AppleLoginRequest Pedido(string token, string? nombre = null, string? nonce = null) =>
        new(token, null, nonce, nombre, "ios");

    [Fact]
    public async Task First_apple_sign_in_creates_a_primary_account_with_its_own_household()
    {
        await using var f = new TestDb();
        var (s, _) = Servicio(f);

        var r = await s.ConAppleAsync(Pedido(Token("sub-1", "ana@privaterelay.appleid.com"), "Ana Pérez"));

        Assert.False(string.IsNullOrEmpty(r.Token));
        Assert.Equal("Ana Pérez", r.User.Name);
        var user = await f.Db.Users.Include(u => u.Household).SingleAsync();
        Assert.Equal(UserRole.Primary, user.Role);
        Assert.Equal(user.Id, user.Household.PrimaryUserId);
        Assert.Null(user.TermsAcceptedAt); // los acepta en la app, como cualquier cuenta nueva
        Assert.True(await f.Db.AlertConfigs.AnyAsync(a => a.HouseholdId == user.HouseholdId));
        var vinculo = await f.Db.Set<CuentaExterna>().SingleAsync();
        Assert.True(vinculo.CreoLaCuenta);
        Assert.Equal(new CuentasExternasDto(true, false, false), await s.EstadoAsync(user.Id));
    }

    [Fact]
    public async Task Signing_in_again_reuses_the_same_account()
    {
        await using var f = new TestDb();
        var (s, _) = Servicio(f);
        var primera = await s.ConAppleAsync(Pedido(Token("sub-1", "ana@x.com"), "Ana"));
        // Apple solo manda el nombre la primera vez.
        var segunda = await s.ConAppleAsync(Pedido(Token("sub-1", "ana@x.com")));

        Assert.Equal(primera.User.Id, segunda.User.Id);
        Assert.Equal(1, await f.Db.Users.CountAsync());
        Assert.Equal(1, await f.Db.Set<CuentaExterna>().CountAsync());
    }

    [Fact]
    public async Task A_verified_email_links_to_the_existing_account_but_an_unverified_one_does_not()
    {
        await using var f = new TestDb();
        var (s, _) = Servicio(f);
        var hogar = new Household { Id = Guid.NewGuid(), Address = "Av. 1" };
        var existente = new User { Id = Guid.NewGuid(), Email = "luis@x.com", Name = "Luis", Household = hogar,
            Role = UserRole.Primary, PasswordHash = BCrypt.Net.BCrypt.HashPassword("Clave.2026") };
        hogar.PrimaryUserId = existente.Id;
        f.Db.AddRange(hogar, existente);
        await f.Db.SaveChangesAsync();

        var otra = await s.ConAppleAsync(Pedido(Token("sub-falso", "luis@x.com", verificado: false)));
        Assert.NotEqual(existente.Id, otra.User.Id);

        var vinculada = await s.ConAppleAsync(Pedido(Token("sub-luis", "Luis@X.com")));
        Assert.Equal(existente.Id, vinculada.User.Id);
        Assert.Equal(new CuentasExternasDto(true, false, true), await s.EstadoAsync(existente.Id));
    }

    [Fact]
    public async Task Tokens_for_another_app_signed_by_someone_else_or_with_a_wrong_nonce_are_rejected()
    {
        await using var f = new TestDb();
        var (s, _) = Servicio(f);
        var ajena = new ECDsaSecurityKey(ECDsa.Create(ECCurve.NamedCurves.nistP256)) { KeyId = "prueba" };

        await Assert.ThrowsAsync<AppException>(() => s.ConAppleAsync(Pedido(Token("a", aud: "com.otra.app"))));
        await Assert.ThrowsAsync<AppException>(() => s.ConAppleAsync(Pedido(Token("a", llave: ajena))));
        await Assert.ThrowsAsync<AppException>(() => s.ConAppleAsync(Pedido(Token("a", nonce: "uno"), nonce: "otro")));
        var ok = await s.ConAppleAsync(Pedido(Token("a", "a@x.com", nonce: "uno"), nonce: "uno"));
        Assert.NotNull(ok.Token);
        Assert.Equal(1, await f.Db.Users.CountAsync());
    }

    [Fact]
    public async Task Deleting_with_apple_removes_the_household_like_the_password_path()
    {
        await using var f = new TestDb();
        var (s, _) = Servicio(f);
        var r = await s.ConAppleAsync(Pedido(Token("sub-1", "ana@x.com"), "Ana"));
        var hogarId = (await f.Db.Users.SingleAsync()).HouseholdId;
        f.Db.Add(new CameraConfig { HouseholdId = hogarId, Name = "Puerta" });
        await f.Db.SaveChangesAsync();

        // Con la cuenta de Apple de otra persona no se puede.
        await Assert.ThrowsAsync<AppException>(() =>
            s.EliminarConAppleAsync(r.User.Id, new EliminarConAppleRequest(Token("sub-otro"), null)));

        var borrado = await s.EliminarConAppleAsync(r.User.Id, new EliminarConAppleRequest(Token("sub-1"), null));
        Assert.True(borrado.HogarEliminado);
        f.Db.ChangeTracker.Clear();
        Assert.False(await f.Db.Households.AnyAsync());
        Assert.False(await f.Db.Users.AnyAsync());
        Assert.False(await f.Db.CameraConfigs.AnyAsync());
        Assert.False(await f.Db.Set<CuentaExterna>().AnyAsync());
    }

    [Fact]
    public async Task Confirmed_deletion_matches_password_deletion_for_a_guest()
    {
        await using var f = new TestDb();
        var (_, cuentas) = Servicio(f);
        var hogar = new Household { Id = Guid.NewGuid(), Address = "Av. 1" };
        var dueno = new User { Id = Guid.NewGuid(), Email = "d@x.com", Name = "D", Household = hogar, Role = UserRole.Primary };
        var invitado = new User { Id = Guid.NewGuid(), Email = "i@x.com", Name = "I", Household = hogar, Role = UserRole.Secondary };
        hogar.PrimaryUserId = dueno.Id;
        f.Db.AddRange(hogar, dueno, invitado);
        await f.Db.SaveChangesAsync();

        var r = await cuentas.EliminarConfirmadoAsync(invitado.Id);

        Assert.False(r.HogarEliminado);
        Assert.Equal(1, r.UsuariosEliminados);
        f.Db.ChangeTracker.Clear();
        Assert.True(await f.Db.Users.AnyAsync(u => u.Id == dueno.Id));
        Assert.False(await f.Db.Users.AnyAsync(u => u.Id == invitado.Id));
    }

    [Fact]
    public async Task Tokens_to_revoke_cover_the_whole_household_only_for_the_owner()
    {
        await using var f = new TestDb();
        var (s, _) = Servicio(f);
        var hogar = new Household { Id = Guid.NewGuid(), Address = "Av. 1" };
        var dueno = new User { Id = Guid.NewGuid(), Email = "d@x.com", Name = "D", Household = hogar, Role = UserRole.Primary };
        var invitado = new User { Id = Guid.NewGuid(), Email = "i@x.com", Name = "I", Household = hogar, Role = UserRole.Secondary };
        hogar.PrimaryUserId = dueno.Id;
        f.Db.AddRange(hogar, dueno, invitado,
            new CuentaExterna { Id = Guid.NewGuid(), User = dueno, Proveedor = "apple", Sujeto = "d", RefreshToken = "rd" },
            new CuentaExterna { Id = Guid.NewGuid(), User = invitado, Proveedor = "apple", Sujeto = "i", RefreshToken = "ri" });
        await f.Db.SaveChangesAsync();

        Assert.Equal(["rd", "ri"], (await s.TokensARevocarAsync(dueno.Id)).Select(t => t.Token).Order());
        Assert.Equal(["ri"], (await s.TokensARevocarAsync(invitado.Id)).Select(t => t.Token));
    }
}
