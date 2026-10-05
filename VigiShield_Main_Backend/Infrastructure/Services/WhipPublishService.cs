using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using VigiShield.Application.DTOs.Stream;
using VigiShield.Common.Exceptions;
using VigiShield.Domain.Enums;
using VigiShield.Infrastructure.Persistence;

namespace VigiShield.Infrastructure.Services;

// One registry per API process. Deploy with one API replica; gateway must also reject
// replacing an existing publisher (overridePublisher: false) across process restarts.
public sealed class WhipPublishService(IServiceScopeFactory scopes, IHttpClientFactory http,
    IConfiguration config, ILogger<WhipPublishService> logger) : BackgroundService
{
    /// <summary>
    /// Rechaza la publicacion dejando constancia del motivo.
    ///
    /// Un 400 en el telefono solo dice "no se pudo"; sin esta traza hay que
    /// adivinar cual de las validaciones salto. Del SDP se registra SOLO la
    /// primera linea y las lineas m=, que es lo que determina el rechazo: el
    /// resto lleva credenciales ICE y huellas DTLS y no debe ir al registro.
    /// </summary>
    private AppException Rechazar(Guid cameraId, string motivo, string? sdp = null)
    {
        if (sdp is null) logger.LogWarning("Publicacion rechazada ({Camara}): {Motivo}", cameraId, motivo);
        else
        {
            var lineas = sdp.Replace("\r\n", "\n").Split('\n');
            var primera = lineas.Length > 0 ? lineas[0] : "";
            var medios = string.Join(" | ", lineas.Where(l => l.StartsWith("m=")));
            logger.LogWarning(
                "Publicacion rechazada ({Camara}): {Motivo}. bytes={Bytes} primera='{Primera}' medios='{Medios}'",
                cameraId, motivo, Encoding.UTF8.GetByteCount(sdp), primera, medios);
        }
        return new AppException(motivo);
    }

    private sealed record Session(Guid Id, Guid Household, Guid Camera, string StreamKey, Uri Location, string GatewayKey, DateTimeOffset Expires);
    private readonly Dictionary<Guid, Session> sessions = [];
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<PublishResponse> PublishAsync(Guid householdId, Guid cameraId, string sdp, CancellationToken ct = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        ct = timeout.Token;
        await gate.WaitAsync(ct);
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var camera = await db.CameraConfigs.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cameraId && c.HouseholdId == householdId, ct)
                ?? throw AppException.NotFound("Cámara no encontrada");
            if (camera.StreamMode != StreamMode.MobileWebRtc || !camera.IsConfigured)
                throw Rechazar(cameraId, "La cámara no es una fuente móvil configurada.");
            if (sessions.Values.Any(s => s.Camera == cameraId || s.StreamKey == camera.StreamKey))
                throw AppException.Conflict("La cámara ya tiene una publicación activa.");
            var key = config["MediaMtx:WhipGatewayKey"];
            if (!Uri.TryCreate(config["MediaMtx:WhipBaseUrl"], UriKind.Absolute, out var baseUrl)
                || baseUrl.Scheme != "https" || baseUrl.UserInfo.Length != 0
                || baseUrl.Query.Length != 0 || baseUrl.Fragment.Length != 0
                || !baseUrl.AbsolutePath.EndsWith('/') || string.IsNullOrWhiteSpace(key)
                || key.Any(char.IsControl))
                throw new AppException("Publicación móvil no configurada.", 503);
            if (string.IsNullOrWhiteSpace(sdp) || Encoding.UTF8.GetByteCount(sdp) > 65536 || !sdp.StartsWith("v=0\r\n") && !sdp.StartsWith("v=0\n"))
                throw Rechazar(cameraId, "SDP inválido.", sdp);
            var media = sdp.Replace("\r\n", "\n").Split('\n').Where(l => l.StartsWith("m=")).ToArray();
            if (media.Length != 1 || !media[0].StartsWith("m=video ") || sdp.Contains('\0'))
                throw Rechazar(cameraId, "Solo se permite video.", sdp);
            if (sessions.Count >= 256) throw new AppException("Capacidad de publicación alcanzada.", 503);
            if (string.IsNullOrEmpty(camera.StreamKey) || camera.StreamKey.Length > 64 || !camera.StreamKey.All(char.IsAsciiLetterOrDigit))
                throw Rechazar(cameraId, "Clave de cámara inválida.");
            var endpoint = new Uri(baseUrl, camera.StreamKey + "/whip");
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Add("X-VigiShield-Publish-Key", key);
            request.Content = new StringContent(sdp, Encoding.UTF8, "application/sdp");
            using var client = http.CreateClient("whip");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (response.StatusCode != HttpStatusCode.Created) throw new AppException("Publicación rechazada por el servidor de medios.", 502);
            var rawLocation = response.Headers.Location;
            if (rawLocation == null || rawLocation.OriginalString.Contains('%') || rawLocation.OriginalString.Contains('\\')
                || !Uri.TryCreate(endpoint, rawLocation, out var location)
                || location.Scheme != baseUrl.Scheme || location.Host != baseUrl.Host || location.Port != baseUrl.Port
                || location.UserInfo.Length != 0 || location.Query.Length != 0 || location.Fragment.Length != 0
                || !location.AbsolutePath.StartsWith(endpoint.AbsolutePath + "/", StringComparison.Ordinal))
                throw new AppException("Respuesta de publicación inválida.", 502);
            var session = new Session(Guid.NewGuid(), householdId, cameraId, camera.StreamKey!, location, key, DateTimeOffset.UtcNow.AddHours(2));
            sessions.Add(session.Id, session);
            try
            {
                if (response.Content.Headers.ContentType?.MediaType != "application/sdp")
                    throw new AppException("Respuesta SDP inválida.", 502);
                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                var bytes = new byte[65537];
                var count = 0;
                while (count < bytes.Length)
                {
                    var read = await stream.ReadAsync(bytes.AsMemory(count), ct);
                    if (read == 0) break;
                    count += read;
                }
                var answer = Encoding.UTF8.GetString(bytes, 0, count);
                if (count > 65536 || !answer.StartsWith("v=0\r\n") && !answer.StartsWith("v=0\n"))
                    throw new AppException("Respuesta SDP inválida.", 502);
                return new(session.Id, answer);
            }
            catch
            {
                // Keep failed cleanup in the registry for the reaper to retry.
                sessions[session.Id] = session with { Expires = DateTimeOffset.MinValue };
                try { await DeleteUpstreamAsync(session, CancellationToken.None); sessions.Remove(session.Id); }
                catch (Exception ex) when (ex is AppException or HttpRequestException or OperationCanceledException) { }
                throw;
            }
        }
        catch (HttpRequestException) { throw new AppException("Servidor de publicación no disponible.", 502); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new AppException("Publicación agotó el tiempo de espera.", 504); }
        finally { gate.Release(); }
    }

    public async Task DeleteAsync(Guid householdId, Guid cameraId, Guid sessionId, CancellationToken ct = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        ct = timeout.Token;
        await gate.WaitAsync(ct);
        try
        {
            if (!sessions.TryGetValue(sessionId, out var session) || session.Household != householdId || session.Camera != cameraId)
                throw AppException.NotFound("Publicación no encontrada");
            await DeleteUpstreamAsync(session, ct);
            sessions.Remove(sessionId);
        }
        catch (HttpRequestException) { throw new AppException("Servidor de publicación no disponible.", 502); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new AppException("Publicación agotó el tiempo de espera.", 504); }
        finally { gate.Release(); }
    }

    private async Task DeleteUpstreamAsync(Session session, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        ct = timeout.Token;
        using var request = new HttpRequestMessage(HttpMethod.Delete, session.Location);
        request.Headers.Add("X-VigiShield-Publish-Key", session.GatewayKey);
        using var client = http.CreateClient("whip");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode && response.StatusCode is not HttpStatusCode.NotFound and not HttpStatusCode.Gone)
            throw new AppException("No se pudo cerrar la publicación.", 502);
    }

    public async Task CleanupAsync(CancellationToken ct = default, bool all = false)
    {
        await gate.WaitAsync(ct);
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            foreach (var session in sessions.Values.ToArray())
            {
                if (!all && session.Expires > DateTimeOffset.UtcNow && await db.CameraConfigs.AnyAsync(c =>
                    c.Id == session.Camera && c.HouseholdId == session.Household && c.StreamKey == session.StreamKey
                    && c.StreamMode == StreamMode.MobileWebRtc && c.IsConfigured, ct)) continue;
                try { await DeleteUpstreamAsync(session, ct); sessions.Remove(session.Id); }
                catch (Exception ex) when (ex is AppException or HttpRequestException or OperationCanceledException) { }
            }
        }
        finally { gate.Release(); }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try { await CleanupAsync(stoppingToken); }
                catch (Exception) when (!stoppingToken.IsCancellationRequested) { /* Retry next tick; never log secrets. */ }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        await CleanupAsync(cancellationToken, all: true);
    }
}
