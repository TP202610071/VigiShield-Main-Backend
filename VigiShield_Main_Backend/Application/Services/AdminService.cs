using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VigiShield.Application.DTOs.Admin;
using VigiShield.Common.Exceptions;
using VigiShield.Domain.Entities;
using VigiShield.Domain.Enums;
using VigiShield.Infrastructure.Persistence;

namespace VigiShield.Application.Services;

/// <summary>
/// Consultas y acciones del panel de administración (vigishield.app/admin).
/// Ve todos los hogares, así que solo lo usa AdminController, que exige rol
/// Admin. Son métodos nuevos: no cambia nada de lo que usa la app.
/// </summary>
public class AdminService(AppDbContext db)
{
    public const int MaxExportar = 300;
    private static readonly TimeSpan Activo = TimeSpan.FromDays(7);
    private static readonly TimeSpan EnLinea = TimeSpan.FromMinutes(10);

    // ── Resumen ───────────────────────────────────────────────────────────────

    public async Task<AdminResumenDto> ResumenAsync()
    {
        var ahora = DateTime.UtcNow;
        var usuarios = await UsuariosAsync();
        var camaras = await db.CameraConfigs.Where(c => !c.IsSample).ToListAsync();
        var hace7 = ahora.AddDays(-7);
        var porTipo = (await db.Events.Where(e => e.CreatedAt >= hace7)
                .GroupBy(e => e.EventType).Select(g => new { g.Key, N = g.Count() }).ToListAsync())
            .ToDictionary(x => EventService.SpanishLabel(x.Key), x => x.N);
        return new AdminResumenDto(
            usuarios.Count,
            await db.Households.CountAsync(),
            usuarios.Count(u => u.Estado == "activo"),
            usuarios.Count(u => u.EnLinea),
            camaras.Count,
            camaras.Count(c => c.IsActive),
            camaras.Count(c => c.StreamMode == StreamMode.MobileWebRtc),
            await db.Events.CountAsync(),
            await db.Events.CountAsync(e => e.CreatedAt >= ahora.AddDays(-1)),
            await db.Events.CountAsync(e => e.CreatedAt >= hace7),
            await db.SampleVideoSessions.CountAsync(),
            porTipo);
    }

    // ── Usuarios ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Todos los usuarios con la actividad de su hogar. «Activo» = alguna
    /// actividad en los últimos 7 días: su última petición a la API o, para
    /// cuentas de antes de que existiera ese dato, lo último que pasó en su
    /// hogar (un evento, una cámara modificada, un video de ejemplo).
    /// </summary>
    public async Task<List<AdminUsuarioDto>> UsuariosAsync()
    {
        var ahora = DateTime.UtcNow;
        var usuarios = await db.Users.Include(u => u.Household).AsNoTracking().ToListAsync();
        var camaras = (await db.CameraConfigs.Where(c => !c.IsSample)
                .Select(c => new { c.HouseholdId, c.IsActive, c.UpdatedAt }).ToListAsync())
            .GroupBy(c => c.HouseholdId).ToDictionary(g => g.Key, g => g.ToList());
        var eventos = (await db.Events.GroupBy(e => e.HouseholdId)
                .Select(g => new { g.Key, N = g.Count(), Ultimo = g.Max(e => e.CreatedAt) }).ToListAsync())
            .ToDictionary(x => x.Key);
        var ejemplos = (await db.SampleVideoSessions.GroupBy(s => s.HouseholdId)
                .Select(g => new { g.Key, N = g.Count(), Ultima = g.Max(s => s.StartedAt) }).ToListAsync())
            .ToDictionary(x => x.Key);

        return usuarios.Select(u =>
        {
            var cams = camaras.GetValueOrDefault(u.HouseholdId) ?? [];
            var ev = eventos.GetValueOrDefault(u.HouseholdId);
            var ej = ejemplos.GetValueOrDefault(u.HouseholdId);
            DateTime?[] marcas = [u.LastSeenAt, ev?.Ultimo, ej?.Ultima, cams.Count > 0 ? cams.Max(c => c.UpdatedAt) : null, u.CreatedAt];
            var ultima = marcas.Where(m => m.HasValue).Max();
            return new AdminUsuarioDto(
                u.Id, u.Name, u.Email, u.Role.ToString(), u.HouseholdId, u.Household?.Address,
                u.CreatedAt, ultima, u.TermsAcceptedAt,
                ultima.HasValue && ahora - ultima.Value <= Activo ? "activo" : "inactivo",
                u.LastSeenAt.HasValue && ahora - u.LastSeenAt.Value <= EnLinea,
                cams.Count, cams.Count(c => c.IsActive), ev?.N ?? 0, ev?.Ultimo, ej?.N ?? 0,
                u.Household?.IsMonitoringPaused ?? false);
        }).OrderByDescending(u => u.UltimaActividad).ToList();
    }

    // ── Cámaras ───────────────────────────────────────────────────────────────

    public async Task<List<AdminCamaraDto>> CamarasAsync()
    {
        var ahora = DateTime.UtcNow;
        var camaras = await db.CameraConfigs.AsNoTracking()
            .Where(c => !c.IsSample || c.SampleUntil > ahora)
            .OrderBy(c => c.CreatedAt).ToListAsync();
        var duenos = await Duenos();
        return camaras.Select(c =>
        {
            var d = duenos.GetValueOrDefault(c.HouseholdId);
            return new AdminCamaraDto(c.Id, c.Name, c.HouseholdId, d?.Name, d?.Email,
                c.StreamMode.ToString(), c.IsActive, c.NotificationsEnabled, c.IsSample,
                c.IsSample ? c.SampleUntil : null, c.StreamKey, ContarZonas(c.ZonesJson), c.CreatedAt);
        }).ToList();
    }

    /// <summary>Activa o desactiva el análisis de IA de la cámara de cualquier hogar.</summary>
    public async Task<AdminCamaraDto> CambiarActivaAsync(Guid camaraId, bool activa)
    {
        var cam = await db.CameraConfigs.FirstOrDefaultAsync(c => c.Id == camaraId)
            ?? throw AppException.NotFound("Cámara no encontrada");
        if (cam.IsSample) throw new AppException("El video de ejemplo se apaga solo al terminar su sesión.");
        cam.IsActive = activa;
        cam.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return (await CamarasAsync()).First(c => c.Id == camaraId);
    }

    // ── Eventos ───────────────────────────────────────────────────────────────

    public async Task<AdminEventosPaginaDto> EventosAsync(AdminFiltroEventos filtro)
    {
        var tamano = Math.Clamp(filtro.Tamano, 1, 200);
        var pagina = Math.Max(1, filtro.Pagina);
        var consulta = Filtrar(filtro);
        var total = await consulta.CountAsync();
        var eventos = await consulta.OrderByDescending(e => e.CreatedAt)
            .Skip((pagina - 1) * tamano).Take(tamano).ToListAsync();
        var duenos = await Duenos();
        return new AdminEventosPaginaDto(total, pagina, tamano, eventos.Select(e => ADto(e, duenos)).ToList());
    }

    /// <summary>
    /// ZIP con eventos.csv y la foto y el clip de cada evento (como mucho
    /// <see cref="MaxExportar"/>, los más recientes del filtro). Se escribe a
    /// medida que se descarga cada archivo: no se arma entero en memoria.
    /// </summary>
    public async Task ExportarAsync(AdminFiltroEventos filtro, Stream destino, HttpClient http, CancellationToken ct)
    {
        var eventos = await Filtrar(filtro).OrderByDescending(e => e.CreatedAt).Take(MaxExportar).ToListAsync(ct);
        var duenos = await Duenos();
        var filas = new StringBuilder();
        filas.AppendLine("id,fecha_lima,usuario,correo,camara,tipo,riesgo,confianza,nocturno,video_de_ejemplo,aviso_enviado,foto,clip,url_foto,url_clip");

        using var zip = new ZipArchive(destino, ZipArchiveMode.Create, leaveOpen: true);
        foreach (var e in eventos)
        {
            ct.ThrowIfCancellationRequested();
            var d = ADto(e, duenos);
            var baseNombre = $"{Lima(e.CreatedAt):yyyy-MM-dd_HHmmss}_{Slug(d.TipoEtiqueta)}_{e.Id.ToString()[..8]}";
            var foto = await Agregar(zip, http, d.Foto, $"fotos/{baseNombre}", ".jpg", ct);
            var clip = await Agregar(zip, http, d.Clip, $"clips/{baseNombre}", ".mp4", ct);
            filas.AppendLine(string.Join(',', new[]
            {
                e.Id.ToString(), Lima(e.CreatedAt).ToString("yyyy-MM-dd HH:mm:ss"), d.Dueno, d.CorreoDueno, d.Camara,
                d.TipoEtiqueta, d.Riesgo, d.Confianza?.ToString("0.00", CultureInfo.InvariantCulture),
                d.Nocturno ? "si" : "no", d.EsEjemplo ? "si" : "no", d.AvisoEnviado ? "si" : "no",
                foto, clip, d.Foto, d.Clip,
            }.Select(Csv)));
        }

        var csv = zip.CreateEntry("eventos.csv", CompressionLevel.Optimal);
        await using (var s = csv.Open())
        {
            var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(filas.ToString())).ToArray();
            await s.WriteAsync(bytes, ct);
        }
    }

    // ── Auxiliares ────────────────────────────────────────────────────────────

    private IQueryable<SecurityEvent> Filtrar(AdminFiltroEventos f)
    {
        var q = db.Events.Include(e => e.Camera).AsNoTracking().AsQueryable();
        if (f.Hogar.HasValue) q = q.Where(e => e.HouseholdId == f.Hogar.Value);
        if (!string.IsNullOrWhiteSpace(f.Tipo) && Enum.TryParse<EventType>(f.Tipo, true, out var tipo))
            q = q.Where(e => e.EventType == tipo);
        if (f.Desde.HasValue) q = q.Where(e => e.CreatedAt >= f.Desde.Value.ToUniversalTime());
        if (f.Hasta.HasValue) q = q.Where(e => e.CreatedAt < f.Hasta.Value.ToUniversalTime());
        if (f.Ejemplos == true) q = q.Where(e => (e.Camera != null && e.Camera.IsSample) || e.CameraName == "Video de ejemplo");
        if (f.Ejemplos == false) q = q.Where(e => !(e.Camera != null && e.Camera.IsSample) && e.CameraName != "Video de ejemplo");
        return q;
    }

    private async Task<Dictionary<Guid, User>> Duenos()
    {
        var hogares = await db.Households.AsNoTracking().Select(h => new { h.Id, h.PrimaryUserId }).ToListAsync();
        var usuarios = await db.Users.AsNoTracking().ToDictionaryAsync(u => u.Id);
        var resultado = new Dictionary<Guid, User>();
        foreach (var h in hogares)
        {
            var u = usuarios.GetValueOrDefault(h.PrimaryUserId)
                    ?? usuarios.Values.FirstOrDefault(x => x.HouseholdId == h.Id);
            if (u is not null) resultado[h.Id] = u;
        }
        return resultado;
    }

    private static AdminEventoDto ADto(SecurityEvent e, Dictionary<Guid, User> duenos)
    {
        var d = duenos.GetValueOrDefault(e.HouseholdId);
        var ejemplo = (e.Camera?.IsSample ?? false) || e.CameraName == "Video de ejemplo";
        return new AdminEventoDto(e.Id, e.CreatedAt, e.HouseholdId, d?.Name, d?.Email, e.CameraName,
            e.EventType.ToString(), EventService.SpanishLabel(e.EventType), e.RiskLevel.ToString(),
            e.ConfidenceScore, e.IsNighttime, e.ImageCapturePath, e.VideoClipPath, ejemplo,
            e.NotificationsEnabled && e.RiskLevel >= RiskLevel.Medium);
    }

    /// <summary>Descarga un archivo al ZIP. Devuelve su nombre dentro del ZIP,
    /// o vacío si no había URL o no se pudo bajar (el CSV conserva la URL).</summary>
    private static async Task<string> Agregar(ZipArchive zip, HttpClient http, string? url, string baseNombre,
        string extension, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("https" or "http"))
            return "";
        var ext = Path.GetExtension(uri.AbsolutePath);
        var nombre = baseNombre + (string.IsNullOrEmpty(ext) ? extension : ext.ToLowerInvariant());
        try
        {
            using var resp = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!resp.IsSuccessStatusCode) return "";
            var entrada = zip.CreateEntry(nombre, CompressionLevel.NoCompression);
            await using var destino = entrada.Open();
            await using var origen = await resp.Content.ReadAsStreamAsync(ct);
            await origen.CopyToAsync(destino, ct);
            return nombre;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return "";
        }
    }

    private static int ContarZonas(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return 0;
        try
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var p in doc.RootElement.EnumerateObject())
                if (p.Name.Equals("zones", StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.Array)
                    return p.Value.GetArrayLength();
        }
        catch (JsonException) { }
        return 0;
    }

    /// <summary>Perú no tiene horario de verano: UTC−5 todo el año.</summary>
    private static DateTime Lima(DateTime utc) => utc.AddHours(-5);

    private static string Slug(string texto)
    {
        var normal = texto.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var ch in normal)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.IsLetterOrDigit(ch) ? ch : '-');
        }
        return sb.ToString().Trim('-');
    }

    private static string Csv(string? valor)
    {
        if (string.IsNullOrEmpty(valor)) return "";
        // Excel interpreta =, +, - y @ al inicio como fórmula.
        if ("=+-@".Contains(valor[0])) valor = "'" + valor;
        return valor.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{valor.Replace("\"", "\"\"")}\"" : valor;
    }
}
