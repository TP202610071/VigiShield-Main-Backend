namespace VigiShield.Common.Media;

/// <summary>
/// Archivos que la IA sube junto a la foto de un evento, con el mismo nombre y
/// un sufijo: la comparación del rostro de un acceso reconocido
/// (<c>_reconocido.jpg</c>) y el rostro de una persona desconocida
/// (<c>_desconocido.jpg</c> y sus fotos para registrarla,
/// <c>_desconocido_1.jpg</c>…). No se guardan en la base: se derivan de la URL
/// de la foto. Al borrar los medios de un evento hay que borrar también estos.
/// </summary>
public static class MediosDelEvento
{
    public static readonly string[] Sufijos =
        ["_reconocido.jpg", "_desconocido.jpg", "_desconocido_1.jpg", "_desconocido_2.jpg", "_desconocido_3.jpg"];

    /// <summary>Las URLs dadas más, por cada foto .jpg, las de sus archivos derivados.</summary>
    public static IEnumerable<string> ConDerivados(IEnumerable<string> urls)
    {
        foreach (var url in urls)
        {
            yield return url;
            var sinConsulta = url.Split('?')[0];
            if (!sinConsulta.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)) continue;
            var raiz = sinConsulta[..^4];
            if (Sufijos.Any(s => sinConsulta.EndsWith(s, StringComparison.OrdinalIgnoreCase))) continue;
            foreach (var s in Sufijos) yield return raiz + s;
        }
    }
}
