using System.Diagnostics;

namespace VigiShield.Infrastructure.Services;

/// <summary>
/// CPU, RAM y disco de la máquina donde corre el backend (la VM de Oracle),
/// leídos de /proc. El % de CPU se mide entre una llamada y la anterior, así
/// que el panel, que pregunta cada pocos segundos, ve el uso de ese intervalo.
/// Fuera de Linux (pruebas en Windows) devuelve lo que pueda sin fallar.
/// </summary>
public class HostMetrics
{
    private readonly object _lock = new();
    private (long ocupado, long total)? _anterior;

    public object Leer()
    {
        var cpu = Cpu();
        var (totalKb, disponibleKb) = Memoria();
        var raiz = DriveInfo.GetDrives().FirstOrDefault(d => d.IsReady && (d.Name == "/" || d.Name.StartsWith("C:")));
        double[] carga = [];
        try
        {
            carga = File.ReadAllText("/proc/loadavg").Split(' ').Take(3)
                .Select(x => double.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        }
        catch { /* sin /proc */ }
        double? encendidaHace = null;
        try
        {
            encendidaHace = double.Parse(File.ReadAllText("/proc/uptime").Split(' ')[0],
                System.Globalization.CultureInfo.InvariantCulture);
        }
        catch { /* sin /proc */ }

        return new
        {
            ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0,
            nucleos = Environment.ProcessorCount,
            cpu,
            carga,
            ramTotalMb = totalKb / 1024,
            ramUsadaMb = (totalKb - disponibleKb) / 1024,
            ram = totalKb > 0 ? Math.Round(100.0 * (totalKb - disponibleKb) / totalKb, 1) : 0,
            discoTotalGb = raiz is null ? 0 : Math.Round(raiz.TotalSize / 1073741824.0, 1),
            discoUsadoGb = raiz is null ? 0 : Math.Round((raiz.TotalSize - raiz.AvailableFreeSpace) / 1073741824.0, 1),
            encendidaHaceSeg = encendidaHace,
            backendRamMb = Process.GetCurrentProcess().WorkingSet64 / 1048576,
        };
    }

    private double Cpu()
    {
        try
        {
            // cpu  user nice system idle iowait irq softirq steal ...
            var campos = File.ReadLines("/proc/stat").First().Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Skip(1).Select(long.Parse).ToArray();
            var inactivo = campos[3] + (campos.Length > 4 ? campos[4] : 0);
            var total = campos.Sum();
            var actual = (ocupado: total - inactivo, total);
            lock (_lock)
            {
                var previo = _anterior;
                _anterior = actual;
                if (previo is null || actual.total <= previo.Value.total) return 0;
                return Math.Round(100.0 * (actual.ocupado - previo.Value.ocupado) / (actual.total - previo.Value.total), 1);
            }
        }
        catch { return 0; }
    }

    private static (long totalKb, long disponibleKb) Memoria()
    {
        try
        {
            long total = 0, disponible = 0;
            foreach (var linea in File.ReadLines("/proc/meminfo"))
            {
                if (linea.StartsWith("MemTotal:")) total = long.Parse(linea.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1]);
                else if (linea.StartsWith("MemAvailable:")) disponible = long.Parse(linea.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1]);
            }
            return (total, disponible);
        }
        catch { return (0, 0); }
    }
}
