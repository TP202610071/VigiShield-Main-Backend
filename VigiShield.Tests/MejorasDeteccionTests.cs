using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using VigiShield.Common.Media;
using VigiShield.Infrastructure.Services;
using Xunit;

namespace VigiShield.Tests;

/// <summary>Mejoras de detección (2026-10-10): rostros junto a la foto del evento y
/// alerta de WhatsApp con la foto.</summary>
public class MejorasDeteccionTests
{
    private const string Foto = "https://bucket.vigishield.app/events/20261010_abc.jpg";

    [Fact]
    public void Con_la_foto_se_borran_tambien_sus_rostros()
    {
        var todas = MediosDelEvento.ConDerivados([Foto, "https://bucket.vigishield.app/events/clip.mp4"]).ToList();

        Assert.Contains(Foto, todas);
        Assert.Contains("https://bucket.vigishield.app/events/20261010_abc_reconocido.jpg", todas);
        Assert.Contains("https://bucket.vigishield.app/events/20261010_abc_desconocido_3.jpg", todas);
        Assert.Equal(1 + MediosDelEvento.Sufijos.Length + 1, todas.Count);   // el clip no tiene derivados
    }

    [Fact]
    public void Un_derivado_no_se_vuelve_a_derivar()
    {
        var url = "https://bucket.vigishield.app/events/20261010_abc_reconocido.jpg";
        Assert.Equal([url], MediosDelEvento.ConDerivados([url]).ToList());
    }

    private sealed class Captura : HttpMessageHandler
    {
        public readonly List<string> Cuerpos = [];
        public Func<string, HttpStatusCode> Respuesta = _ => HttpStatusCode.OK;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var cuerpo = await r.Content!.ReadAsStringAsync(ct);
            Cuerpos.Add(cuerpo);
            return new HttpResponseMessage(Respuesta(cuerpo)) { Content = new StringContent("{}") };
        }
    }

    private sealed class Fabrica(HttpMessageHandler h) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(h, disposeHandler: false);
    }

    private static WhatsAppService Servicio(Captura h, string? plantillaFoto)
    {
        var datos = new Dictionary<string, string?>
        {
            ["WhatsApp:AccessToken"] = "token-de-prueba",
            ["WhatsApp:PhoneNumberId"] = "123",
            ["WhatsApp:EventTemplate"] = "vigishield_general_alert",
            ["WhatsApp:EventTemplateLang"] = "en",
            ["WhatsApp:EventTemplateHasButton"] = "false",
            ["WhatsApp:EventImageTemplate"] = plantillaFoto,
        };
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(datos).Build();
        return new WhatsAppService(new Fabrica(h), cfg, NullLogger<WhatsAppService>.Instance);
    }

    [Fact]
    public async Task Sin_plantilla_de_foto_envia_la_de_texto_de_siempre()
    {
        var h = new Captura();
        await Servicio(h, null).SendEventAlertWithPhotoAsync(["+51 999 888 777"], "ev1", "Merodeo", "Puerta", "10/10/2026", "07:00 PM", Foto);
        var cuerpo = Assert.Single(h.Cuerpos);
        Assert.Contains("vigishield_general_alert", cuerpo);
        Assert.DoesNotContain("header", cuerpo);
    }

    [Fact]
    public async Task Con_plantilla_de_foto_manda_la_foto_en_el_encabezado()
    {
        var h = new Captura();
        await Servicio(h, "vigishield_alerta_foto").SendEventAlertWithPhotoAsync(["+51 999 888 777"], "ev1", "Merodeo", "Puerta", "10/10/2026", "07:00 PM", Foto);
        var cuerpo = Assert.Single(h.Cuerpos);
        Assert.Contains("vigishield_alerta_foto", cuerpo);
        Assert.Contains("\"header\"", cuerpo);
        Assert.Contains(Foto, cuerpo);
    }

    [Fact]
    public async Task Si_Meta_rechaza_la_de_foto_no_se_pierde_la_alerta()
    {
        var h = new Captura { Respuesta = c => c.Contains("\"header\"") ? HttpStatusCode.BadRequest : HttpStatusCode.OK };
        await Servicio(h, "vigishield_alerta_foto").SendEventAlertWithPhotoAsync(["+51 999 888 777"], "ev1", "Merodeo", "Puerta", "10/10/2026", "07:00 PM", Foto);
        Assert.Equal(2, h.Cuerpos.Count);
        Assert.Contains("vigishield_general_alert", h.Cuerpos[1]);
    }

    [Fact]
    public async Task Sin_foto_publica_envia_la_de_texto()
    {
        var h = new Captura();
        await Servicio(h, "vigishield_alerta_foto").SendEventAlertWithPhotoAsync(["+51 999 888 777"], "ev1", "Merodeo", "Puerta", "10/10/2026", "07:00 PM", null);
        Assert.Contains("vigishield_general_alert", Assert.Single(h.Cuerpos));
    }
}
