using System.Collections.Concurrent;

namespace VigiShield.Infrastructure.Services;

/// <summary>Una orden pendiente para el agente de una vivienda.</summary>
public record CameraAgentCommand(
    Guid Id,
    Guid CameraId,
    string Ip,
    int HttpPort,
    string? Username,
    string? Password,
    string Kind,                        // "read" | "apply"
    Dictionary<string, string> Settings // vacío en "read"
);

/// <summary>Lo que el agente devuelve tras ejecutar la orden.</summary>
public record CameraAgentResult(
    bool Ok,
    Dictionary<string, string>? Values,
    string? Error
);

/// <summary>
/// Puente entre el backend en la nube y un agente que corre en la casa.
///
/// El backend no puede hablar con una cámara de IP privada: desde la VM,
/// 192.168.1.82 se va por el gateway a internet y muere en un timeout. Y la app
/// solo la alcanza si el teléfono está en ese wifi. Para poder cambiar los
/// ajustes desde fuera de casa hace falta algo DENTRO de la casa: este broker
/// deja una orden en memoria, el agente (que ya convive con el relay de FFmpeg)
/// la recoge, la ejecuta contra la cámara y devuelve el resultado.
///
/// A propósito NO se persiste en base de datos: una orden vive segundos. Si el
/// backend se reinicia a mitad, la petición de la app caduca y se reintenta,
/// que es más simple y más honesto que arrastrar una cola en disco.
/// </summary>
public class CameraAgentBroker
{
    /// <summary>Cuánto espera la app a que el agente conteste.</summary>
    public static readonly TimeSpan EsperaResultado = TimeSpan.FromSeconds(25);

    /// <summary>Cuánto se mantiene abierta la consulta larga del agente.</summary>
    public static readonly TimeSpan EsperaAgente = TimeSpan.FromSeconds(20);

    /// <summary>Tras cuánto silencio se considera que el agente se cayó.</summary>
    private static readonly TimeSpan _vidaLatido = TimeSpan.FromSeconds(75);

    private readonly ConcurrentDictionary<Guid, Channel> _porHogar = new();
    private readonly ILogger<CameraAgentBroker> _logger;

    public CameraAgentBroker(ILogger<CameraAgentBroker> logger) => _logger = logger;

    private sealed class Channel
    {
        public readonly ConcurrentQueue<CameraAgentCommand> Pendientes = new();
        public readonly ConcurrentDictionary<Guid, TaskCompletionSource<CameraAgentResult>> Esperando = new();
        public readonly SemaphoreSlim Aviso = new(0);
        public DateTime UltimoLatido = DateTime.MinValue;
    }

    private Channel Canal(Guid householdId) =>
        _porHogar.GetOrAdd(householdId, _ => new Channel());

    /// <summary>¿Hay un agente vivo en esa vivienda ahora mismo?</summary>
    public bool AgenteConectado(Guid householdId) =>
        _porHogar.TryGetValue(householdId, out var c)
        && DateTime.UtcNow - c.UltimoLatido < _vidaLatido;

    // ── Lado app: encolar y esperar ───────────────────────────────────────────

    /// <summary>
    /// Deja la orden para el agente y espera su resultado. Devuelve null si el
    /// agente no contestó a tiempo.
    /// </summary>
    public async Task<CameraAgentResult?> EjecutarAsync(
        Guid householdId, CameraAgentCommand cmd, CancellationToken ct = default)
    {
        var canal = Canal(householdId);
        var espera = new TaskCompletionSource<CameraAgentResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        canal.Esperando[cmd.Id] = espera;
        canal.Pendientes.Enqueue(cmd);
        canal.Aviso.Release();

        using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limite.CancelAfter(EsperaResultado);
        try
        {
            return await espera.Task.WaitAsync(limite.Token);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("El agente de {Household} no contestó a la orden {Cmd}",
                householdId, cmd.Id);
            return null;
        }
        finally
        {
            canal.Esperando.TryRemove(cmd.Id, out _);
        }
    }

    // ── Lado agente: recoger y responder ──────────────────────────────────────

    /// <summary>
    /// Consulta larga: devuelve las órdenes pendientes, esperando hasta
    /// <see cref="EsperaAgente"/> si no hay ninguna. Así el agente reacciona al
    /// instante sin machacar el servidor a peticiones.
    /// </summary>
    public async Task<List<CameraAgentCommand>> RecogerAsync(
        Guid householdId, CancellationToken ct = default)
    {
        var canal = Canal(householdId);
        canal.UltimoLatido = DateTime.UtcNow;

        if (canal.Pendientes.IsEmpty)
        {
            using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limite.CancelAfter(EsperaAgente);
            try { await canal.Aviso.WaitAsync(limite.Token); }
            catch (OperationCanceledException) { /* sin novedades */ }
        }

        var lote = new List<CameraAgentCommand>();
        while (canal.Pendientes.TryDequeue(out var cmd)) lote.Add(cmd);
        canal.UltimoLatido = DateTime.UtcNow;
        return lote;
    }

    /// <summary>Entrega el resultado de una orden. Falso si ya había caducado.</summary>
    public bool Responder(Guid householdId, Guid commandId, CameraAgentResult resultado)
    {
        var canal = Canal(householdId);
        canal.UltimoLatido = DateTime.UtcNow;
        return canal.Esperando.TryRemove(commandId, out var espera)
               && espera.TrySetResult(resultado);
    }
}
