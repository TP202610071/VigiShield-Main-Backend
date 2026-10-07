using System.Globalization;
using Microsoft.EntityFrameworkCore;
using VigiShield.Common.Exceptions;
using VigiShield.Domain.Entities;
using VigiShield.Domain.Enums;
using VigiShield.Infrastructure.Persistence;

namespace VigiShield.Application.Services;

/// <summary>
/// Interruptor «Grabar evidencia» de la validación: si se graba el primer
/// minuto de cada transmisión desde las cámaras de celular de un hogar. Lo
/// graba el servicio vigishield-grabaciones de la VM de IA, que pregunta aquí.
///
/// Por defecto se graba solo a quien se registró DESPUÉS de que la política
/// de privacidad empezó a decirlo (<see cref="PoliticaDesde"/>) y aceptó los
/// términos al registrarse. Las cuentas anteriores aceptaron un texto que dice
/// que el video no se graba, así que empiezan apagadas: se encienden a mano
/// desde el panel cuando el tester lo acepta.
/// </summary>
public class EvidenciaService(AppDbContext db, IConfiguration config)
{
    /// <summary>Publicación en vigishield.app/privacidad de la versión que lo menciona (UTC).</summary>
    public const string PoliticaDesdePorDefecto = "2026-10-07T01:19:53Z";

    public record Hogar(Guid HogarId, string Dueno, string Correo, DateTime? Registrado,
        DateTime? TerminosAceptados, int CamarasCelular, bool Grabar, bool Manual);

    public record Estado(DateTime PoliticaDesde, List<Hogar> Hogares);

    public DateTime PoliticaDesde =>
        DateTime.Parse(config["Evidencia:PoliticaDesde"] ?? PoliticaDesdePorDefecto,
            CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);

    /// <summary>Regla por defecto: se registró después de la política y aceptó al registrarse.</summary>
    public bool GrabarPorDefecto(DateTime? registrado, DateTime? terminosAceptados) =>
        registrado is { } r && terminosAceptados is { } t
        && r >= PoliticaDesde && t - r < TimeSpan.FromMinutes(5);

    public async Task<Estado> ListarAsync(CancellationToken ct = default)
    {
        var duenos = await db.Households
            .Select(h => new { h.Id, h.PrimaryUserId })
            .Join(db.Users, h => h.PrimaryUserId, u => u.Id,
                (h, u) => new { h.Id, u.Name, u.Email, u.CreatedAt, u.TermsAcceptedAt })
            .ToListAsync(ct);
        var celulares = await db.CameraConfigs
            .Where(c => c.StreamMode == StreamMode.MobileWebRtc && !c.IsSample)
            .GroupBy(c => c.HouseholdId)
            .Select(g => new { g.Key, N = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.N, ct);
        var ajustes = await db.Set<EvidenciaGrabacion>().ToDictionaryAsync(a => a.HouseholdId, a => a.Grabar, ct);

        var hogares = duenos.Select(d => new Hogar(d.Id, d.Name, d.Email, d.CreatedAt, d.TermsAcceptedAt,
                celulares.GetValueOrDefault(d.Id),
                ajustes.TryGetValue(d.Id, out var manual) ? manual : GrabarPorDefecto(d.CreatedAt, d.TermsAcceptedAt),
                ajustes.ContainsKey(d.Id)))
            .OrderByDescending(h => h.Registrado)
            .ToList();
        return new Estado(PoliticaDesde, hogares);
    }

    public async Task<Hogar> CambiarAsync(Guid hogarId, bool grabar, CancellationToken ct = default)
    {
        if (!await db.Households.AnyAsync(h => h.Id == hogarId, ct))
            throw AppException.NotFound("Hogar no encontrado");
        var ajuste = await db.Set<EvidenciaGrabacion>().FirstOrDefaultAsync(a => a.HouseholdId == hogarId, ct);
        if (ajuste is null)
            db.Add(new EvidenciaGrabacion { HouseholdId = hogarId, Grabar = grabar });
        else
        {
            ajuste.Grabar = grabar;
            ajuste.ActualizadoEn = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(ct);
        return (await ListarAsync(ct)).Hogares.First(h => h.HogarId == hogarId);
    }

    /// <summary>
    /// Para el grabador: los hogares que se graban y los que existen (para que
    /// borre las grabaciones de cuentas eliminadas).
    /// </summary>
    public async Task<(List<Guid> Grabar, List<Guid> Existentes)> ParaGrabadorAsync(CancellationToken ct = default)
    {
        var estado = await ListarAsync(ct);
        return (estado.Hogares.Where(h => h.Grabar).Select(h => h.HogarId).ToList(),
                estado.Hogares.Select(h => h.HogarId).ToList());
    }
}
