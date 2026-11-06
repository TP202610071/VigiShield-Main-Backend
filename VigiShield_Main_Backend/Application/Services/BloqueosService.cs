using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using VigiShield.Domain.Entities;
using VigiShield.Infrastructure.Persistence;

namespace VigiShield.Application.Services;

/// <summary>Lo bloqueado en este momento, ya resuelto (cuentas ligadas a teléfonos o IPs bloqueadas, cámaras de esos hogares).</summary>
public sealed record InstantaneaBloqueos(
    HashSet<Guid> Camaras,          // todas las cámaras que la IA no debe analizar
    HashSet<Guid> CamarasDirectas,  // las bloqueadas una por una
    HashSet<Guid> Cuentas,
    HashSet<Guid> Hogares,
    HashSet<string> Dispositivos,
    HashSet<string> Ips);

/// <summary>
/// Bloqueos del administrador (cámara, cuenta, teléfono o IP) y registro de los
/// teléfonos desde los que entra cada cuenta. Singleton: guarda una instantánea
/// de 20 s para que el middleware y la IA no consulten la BD en cada petición.
/// </summary>
public class BloqueosService(IServiceScopeFactory scopes, ILogger<BloqueosService> logger)
{
    public static readonly string[] Tipos = ["camara", "cuenta", "dispositivo", "ip"];
    private static readonly TimeSpan Vigencia = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan CadaRegistro = TimeSpan.FromMinutes(10);

    private readonly SemaphoreSlim _cargando = new(1, 1);
    private readonly ConcurrentDictionary<string, DateTime> _registrados = new();
    private InstantaneaBloqueos? _inst;
    private DateTime _instEn = DateTime.MinValue;

    public void Invalidar() => _instEn = DateTime.MinValue;

    public async Task<InstantaneaBloqueos> InstantaneaAsync()
    {
        if (_inst is not null && DateTime.UtcNow - _instEn < Vigencia) return _inst;
        await _cargando.WaitAsync();
        try
        {
            if (_inst is not null && DateTime.UtcNow - _instEn < Vigencia) return _inst;
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var todos = await db.Set<BloqueoAdmin>().AsNoTracking().ToListAsync();
            HashSet<string> De(string tipo) => todos.Where(b => b.Tipo == tipo).Select(b => b.Valor).ToHashSet();
            var dispositivos = De("dispositivo");
            var ips = De("ip");
            var cuentas = De("cuenta").Select(v => Guid.TryParse(v, out var g) ? g : Guid.Empty)
                .Where(g => g != Guid.Empty).ToHashSet();
            var camarasDirectas = De("camara").Select(v => Guid.TryParse(v, out var g) ? g : Guid.Empty)
                .Where(g => g != Guid.Empty).ToHashSet();
            if (dispositivos.Count > 0 || ips.Count > 0)
            {
                var ligadas = await db.Set<DispositivoVisto>().AsNoTracking()
                    .Where(d => dispositivos.Contains(d.DeviceId) || (d.Ip != null && ips.Contains(d.Ip)))
                    .Select(d => d.UserId).Distinct().ToListAsync();
                cuentas.UnionWith(ligadas);
            }
            var hogares = cuentas.Count == 0 ? [] : (await db.Users.AsNoTracking()
                .Where(u => cuentas.Contains(u.Id)).Select(u => u.HouseholdId).ToListAsync()).ToHashSet();
            var camaras = new HashSet<Guid>(camarasDirectas);
            if (hogares.Count > 0)
                camaras.UnionWith(await db.CameraConfigs.AsNoTracking()
                    .Where(c => hogares.Contains(c.HouseholdId)).Select(c => c.Id).ToListAsync());
            _inst = new InstantaneaBloqueos(camaras, camarasDirectas, cuentas, hogares, dispositivos, ips);
            _instEn = DateTime.UtcNow;
            return _inst;
        }
        finally { _cargando.Release(); }
    }

    /// <summary>Guarda (como mucho cada 10 min) desde qué teléfono e IP entra la cuenta. Nunca falla la petición.</summary>
    public void Registrar(Guid userId, string deviceId, string? ip, string? plataforma)
    {
        var clave = $"{userId}|{deviceId}|{ip}";
        var ahora = DateTime.UtcNow;
        if (_registrados.TryGetValue(clave, out var previo) && ahora - previo < CadaRegistro) return;
        _registrados[clave] = ahora;
        _ = Task.Run(async () =>
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var d = await db.Set<DispositivoVisto>()
                    .FirstOrDefaultAsync(x => x.UserId == userId && x.DeviceId == deviceId);
                if (d is null)
                    db.Add(new DispositivoVisto { UserId = userId, DeviceId = deviceId, Ip = ip, Plataforma = plataforma });
                else
                {
                    d.Ip = ip;
                    d.UltimaVez = ahora;
                    if (plataforma is not null) d.Plataforma = plataforma;
                }
                await db.SaveChangesAsync();
                // Un teléfono bloqueado con una cuenta nueva: la instantánea debe incluirla ya.
                if (d is null && _inst is not null && _inst.Dispositivos.Contains(deviceId)) Invalidar();
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "No se pudo registrar el dispositivo de {Usuario}", userId);
            }
        });
    }

    public async Task<BloqueoAdmin> CrearAsync(string tipo, string valor, string? motivo, Guid? adminId)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var existente = await db.Set<BloqueoAdmin>().FirstOrDefaultAsync(b => b.Tipo == tipo && b.Valor == valor);
        if (existente is not null) return existente;
        var b = new BloqueoAdmin { Tipo = tipo, Valor = valor, Motivo = motivo, CreadoPor = adminId };
        db.Add(b);
        await db.SaveChangesAsync();
        Invalidar();
        return b;
    }

    public async Task<bool> QuitarAsync(Guid id)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var n = await db.Set<BloqueoAdmin>().Where(b => b.Id == id).ExecuteDeleteAsync();
        Invalidar();
        return n > 0;
    }

    /// <summary>Bloqueos con una descripción legible para el panel.</summary>
    public async Task<List<object>> ListarAsync()
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var lista = await db.Set<BloqueoAdmin>().AsNoTracking().OrderByDescending(b => b.CreadoEn).ToListAsync();
        var ids = lista.Where(b => b.Tipo is "cuenta" or "camara")
            .Select(b => Guid.TryParse(b.Valor, out var g) ? g : Guid.Empty).ToHashSet();
        var correos = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Email);
        var camaras = await db.CameraConfigs.AsNoTracking().Where(c => ids.Contains(c.Id))
            .Select(c => new { c.Id, c.Name, Correo = db.Users.Where(u => u.HouseholdId == c.HouseholdId)
                .OrderBy(u => u.CreatedAt).Select(u => u.Email).FirstOrDefault() })
            .ToDictionaryAsync(c => c.Id);
        var valoresDisp = lista.Where(b => b.Tipo is "dispositivo" or "ip").Select(b => b.Valor).ToHashSet();
        var vistos = await db.Set<DispositivoVisto>().AsNoTracking()
            .Where(d => valoresDisp.Contains(d.DeviceId) || (d.Ip != null && valoresDisp.Contains(d.Ip)))
            .Select(d => new { d.DeviceId, d.Ip, d.User.Email }).ToListAsync();
        return lista.Select(b =>
        {
            Guid.TryParse(b.Valor, out var g);
            var detalle = b.Tipo switch
            {
                "cuenta" => correos.GetValueOrDefault(g) ?? "(cuenta borrada)",
                "camara" => camaras.TryGetValue(g, out var c) ? $"{c.Name} · {c.Correo}" : "(cámara borrada)",
                "dispositivo" => string.Join(", ", vistos.Where(v => v.DeviceId == b.Valor).Select(v => v.Email).Distinct()),
                _ => string.Join(", ", vistos.Where(v => v.Ip == b.Valor).Select(v => v.Email).Distinct()),
            };
            return (object)new { b.Id, b.Tipo, b.Valor, b.Motivo, b.CreadoEn, detalle };
        }).ToList();
    }

    /// <summary>Teléfonos vistos, los compartidos por más cuentas primero (para detectar multicuentas).</summary>
    public async Task<List<object>> DispositivosAsync(string? buscar)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var q = db.Set<DispositivoVisto>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(buscar))
        {
            var t = buscar.Trim().ToLower();
            var coinciden = q.Where(d => d.User.Email.ToLower().Contains(t) || d.DeviceId.ToLower().Contains(t)
                || (d.Ip != null && d.Ip.Contains(t))).Select(d => d.DeviceId);
            q = q.Where(d => coinciden.Contains(d.DeviceId));
        }
        var filas = await q.Select(d => new { d.DeviceId, d.Ip, d.Plataforma, d.UltimaVez, d.UserId, d.User.Email })
            .OrderByDescending(d => d.UltimaVez).Take(2000).ToListAsync();
        return filas.GroupBy(f => f.DeviceId)
            .Select(g => new
            {
                deviceId = g.Key,
                plataforma = g.First().Plataforma,
                ultimaVez = g.Max(x => x.UltimaVez),
                ips = g.Select(x => x.Ip).Where(x => x != null).Distinct().ToList(),
                cuentas = g.Select(x => new { usuarioId = x.UserId, correo = x.Email }).DistinctBy(x => x.usuarioId).ToList(),
            })
            .OrderByDescending(x => x.cuentas.Count).ThenByDescending(x => x.ultimaVez)
            .Take(200).Cast<object>().ToList();
    }
}
