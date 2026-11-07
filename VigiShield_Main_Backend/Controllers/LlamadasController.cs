using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VigiShield.Application.Services;
using VigiShield.Infrastructure.Persistence;
using VigiShield.Infrastructure.Services;

namespace VigiShield.Controllers;

/// <summary>
/// Llamada de voz de la alerta de emergencia (Twilio). Usa el teléfono del
/// perfil del usuario: si está verificado en Twilio, el servidor lo llama
/// ante un evento de riesgo alto aunque el celular esté bloqueado.
/// </summary>
[ApiController]
[Route("api/llamadas")]
[Authorize]
public class LlamadasController(TwilioVozService twilio, AppDbContext db) : ControllerBase
{
    private static readonly ConcurrentDictionary<Guid, DateTime> UltimaAccion = new();

    private async Task<(Guid Id, string Nombre, string? Numero)?> UsuarioAsync()
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)) return null;
        var u = await db.Users.AsNoTracking().Where(x => x.Id == id)
            .Select(x => new { x.Name, x.WhatsAppNumber }).FirstOrDefaultAsync();
        return u is null ? null : (id, u.Name, TwilioVozService.E164(u.WhatsAppNumber));
    }

    /// <summary>Una acción que llama por teléfono, como mucho cada 60 s por usuario.</summary>
    private static bool Frenar(Guid id)
    {
        var ahora = DateTime.UtcNow;
        if (UltimaAccion.TryGetValue(id, out var previa) && ahora - previa < TimeSpan.FromSeconds(60)) return true;
        UltimaAccion[id] = ahora;
        return false;
    }

    [HttpGet("estado")]
    public async Task<IActionResult> Estado()
    {
        if (await UsuarioAsync() is not { } u) return Unauthorized();
        var disponible = await twilio.DisponibleAsync();
        return Ok(new
        {
            disponible,
            tieneNumero = u.Numero is not null,
            numero = u.Numero is { Length: > 8 } n ? n[..4] + "•••" + n[^4..] : u.Numero,
            verificado = disponible && await twilio.EsVerificadoAsync(u.Numero),
        });
    }

    /// <summary>Twilio llama al número del perfil; el usuario marca el código que se devuelve aquí.</summary>
    [HttpPost("verificar")]
    public async Task<IActionResult> Verificar()
    {
        if (await UsuarioAsync() is not { } u) return Unauthorized();
        if (u.Numero is null) return BadRequest(new { error = "Agrega tu número de teléfono en tu perfil." });
        if (Frenar(u.Id)) return StatusCode(429, new { error = "Espera un minuto antes de intentarlo otra vez." });
        var (codigo, error) = await twilio.SolicitarVerificacionAsync(u.Numero, u.Nombre);
        return codigo is not null ? Ok(new { codigo }) : StatusCode(502, new { error });
    }

    [HttpDelete("verificar")]
    public async Task<IActionResult> Quitar()
    {
        if (await UsuarioAsync() is not { } u) return Unauthorized();
        return u.Numero is not null && await twilio.QuitarVerificacionAsync(u.Numero) ? NoContent() : NotFound();
    }

    /// <summary>Twilio pide aquí qué decir en la llamada (token de un solo uso, 10 min).</summary>
    [AllowAnonymous]
    [HttpGet("twiml/{token}")]
    [HttpPost("twiml/{token}")]
    public IActionResult Twiml(string token) =>
        token == "prueba"
            ? Content("<Response><Pause length=\"1\"/><Say language=\"es-MX\" voice=\"Polly.Mia\">"
                + "Esta es una llamada de prueba de VigiShield. Así te avisaremos cuando una cámara detecte un riesgo alto."
                + "</Say></Response>", "text/xml")
            : TwilioVozService.TomarTwiml(token) is { } twiml
            ? Content(twiml, "text/xml")
            : Content("<Response><Hangup/></Response>", "text/xml");

    /// <summary>Llamada de prueba al propio número verificado.</summary>
    [HttpPost("prueba")]
    public async Task<IActionResult> Prueba()
    {
        if (await UsuarioAsync() is not { } u) return Unauthorized();
        if (u.Numero is null) return BadRequest(new { error = "Agrega tu número de teléfono en tu perfil." });
        if (Frenar(u.Id)) return StatusCode(429, new { error = "Espera un minuto antes de intentarlo otra vez." });
        var hora = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("America/Lima")).ToString("HH:mm");
        var error = await twilio.LlamarAsync(u.Numero,
            "Esta es una llamada de prueba de VigiShield. Así te avisaremos cuando una cámara detecte un riesgo alto. "
            + LlamadasEmergenciaService.Texto("una persona sospechosa", "de prueba", hora));
        return error is null ? NoContent() : StatusCode(502, new { error });
    }
}
