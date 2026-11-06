using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using VigiShield.Infrastructure.Persistence;
using VigiShield.Infrastructure.Services;

namespace VigiShield.Application.Services;

/// <summary>
/// Cuando se registra un evento de riesgo alto o crítico (con avisos encendidos),
/// llama con voz sintetizada a cada integrante del hogar cuyo número esté
/// verificado en Twilio. Corre en segundo plano: nunca demora ni hace fallar el
/// registro del evento. A un mismo número se le llama como mucho cada 5 min.
/// Los demás usuarios siguen con la alerta de emergencia de la app, como antes.
/// </summary>
public class LlamadasEmergenciaService(IServiceScopeFactory scopes, TwilioVozService twilio,
    ILogger<LlamadasEmergenciaService> log) : BackgroundService
{
    private sealed record Aviso(Guid HogarId, string Tipo, string Camara, string Hora);

    private static readonly Channel<Aviso> Cola = Channel.CreateBounded<Aviso>(
        new BoundedChannelOptions(100) { FullMode = BoundedChannelFullMode.DropOldest });
    private static readonly TimeSpan Espacio = TimeSpan.FromMinutes(5);
    private readonly ConcurrentDictionary<string, DateTime> _ultima = new();

    /// <summary>Lo llama EventService al registrar un evento de riesgo alto.</summary>
    public static void Encolar(Guid hogarId, string tipo, string camara, string hora) =>
        Cola.Writer.TryWrite(new Aviso(hogarId, tipo, camara, hora));

    public static string Texto(string tipo, string camara, string hora) =>
        $"Alerta de VigiShield. Se detectó {tipo.ToLowerInvariant()} en la cámara {camara}, a las {hora}. Revisa la aplicación ahora.";

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        await foreach (var a in Cola.Reader.ReadAllAsync(stop))
        {
            try
            {
                if (!await twilio.DisponibleAsync()) continue;
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var numeros = await db.Users.AsNoTracking()
                    .Where(u => u.HouseholdId == a.HogarId && u.WhatsAppNumber != null && u.WhatsAppNumber != "")
                    .Select(u => u.WhatsAppNumber!).ToListAsync(stop);
                foreach (var n in numeros.Select(TwilioVozService.E164).OfType<string>().Distinct())
                {
                    if (!await twilio.EsVerificadoAsync(n)) continue;
                    var ahora = DateTime.UtcNow;
                    if (_ultima.TryGetValue(n, out var previa) && ahora - previa < Espacio) continue;
                    _ultima[n] = ahora;
                    var error = await twilio.LlamarAsync(n, Texto(a.Tipo, a.Camara, a.Hora));
                    if (error is null) log.LogInformation("Llamada de emergencia a {Numero}", n[..^4] + "****");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogWarning(ex, "No se pudo hacer la llamada de emergencia");
            }
        }
    }
}
