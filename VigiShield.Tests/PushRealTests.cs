using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using VigiShield.Infrastructure.Services;
using Xunit;

namespace VigiShield.Tests;

/// <summary>
/// Envío REAL a un teléfono de pruebas, para comprobar el formato contra FCM y
/// APNs. No hace nada salvo que se den las variables de entorno:
///   VIGI_FCM_CUENTA (ruta de la cuenta de servicio), VIGI_FCM_PROYECTO y VIGI_FCM_TOKEN.
/// </summary>
public class PushRealTests
{
    private sealed class Fabrica : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    [Fact]
    public async Task Android_receives_a_full_panel_message_when_configured()
    {
        var cuenta = Environment.GetEnvironmentVariable("VIGI_FCM_CUENTA");
        var token = Environment.GetEnvironmentVariable("VIGI_FCM_TOKEN");
        if (string.IsNullOrEmpty(cuenta) || string.IsNullOrEmpty(token)) return;

        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Fcm:ProjectId"] = Environment.GetEnvironmentVariable("VIGI_FCM_PROYECTO"),
            ["Fcm:ServiceAccountPath"] = cuenta,
        }).Build();
        var fcm = new FcmService(new Fabrica(), cfg, NullLogger<FcmService>.Instance);
        var m = new MensajePush("Prueba del panel 🔔", "Aviso del equipo", "Así se ve un aviso con imagen 📷✨",
            "https://images.pexels.com/photos/19227220/pexels-photo-19227220.jpeg?auto=compress&cs=tinysrgb&w=800",
            MensajePush.Normal, true, null,
            new Dictionary<string, string> { ["eventId"] = "/history", ["type"] = "Admin" });

        var (r, razon) = await fcm.EnviarAndroidMensajeAsync(token, m);

        Assert.True(r == ResultadoPush.Enviado, $"FCM respondió {r} {razon}");
    }
}
