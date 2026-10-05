namespace VigiShield.Application.Services;

/// <summary>
/// Videos de ejemplo para quien no puede provocar una escena en su casa.
///
/// Cada clave es un path de MediaMTX en la VM de IA (<c>ejemplo01</c>…) que
/// reproduce en bucle un clip de Pexels (licencia libre). Las zonas marcan la
/// entrada de cada escena: sin ellas el motor no sabe dónde está la puerta y
/// nunca pasa de «persona desconocida». Los clips y las zonas se probaron con
/// el detector de producción; las fuentes están en docs/videos-ejemplo.md.
/// </summary>
public static class SampleVideoCatalog
{
    public record SampleVideo(string Key, string Title, string TitleEn, string ZonesJson);

    private static string Zona(string type, string name, string polygon) =>
        $$"""{"version":1,"zones":[{"id":"z1","type":"{{type}}","name":"{{name}}","polygon":{{polygon}}}]}""";

    public static readonly IReadOnlyList<SampleVideo> All =
    [
        new("ejemplo01", "Persona encapuchada toca la puerta", "Hooded person knocks on the door",
            Zona("door", "Puerta", "[[0.3,0.55],[0.7,0.55],[0.74,0.97],[0.26,0.97]]")),
        new("ejemplo02", "Alguien se asoma a la puerta de noche", "Someone peeks through the door at night",
            Zona("door", "Puerta", "[[0.04,0.3],[1,0.3],[1,1],[0,1]]")),
        new("ejemplo03", "Persona con linterna junto a una ventana, de noche", "Person with a flashlight by a window at night",
            Zona("door", "Puerta", "[[0,0.45],[0.55,0.45],[0.55,1],[0,1]]")),
        new("ejemplo04", "Alguien recoge paquetes de la entrada", "Someone picks up packages at the entrance",
            Zona("door", "Puerta", "[[0.351,0.55],[0.713,0.55],[0.756,0.99],[0.319,0.99]]")),
        new("ejemplo05", "Visita desconocida con un paquete", "Unknown visitor with a package",
            Zona("door", "Entrada", "[[0.073,0.62],[0.874,0.62],[0.906,1],[0.052,1]]")),
        new("ejemplo06", "Repartidor toca la puerta", "Delivery person knocks on the door",
            Zona("door", "Puerta", "[[0,0.5],[1,0.5],[1,1],[0,1]]")),
        new("ejemplo07", "Alguien manipula una cámara de seguridad", "Someone tampers with a security camera",
            Zona("door", "Entrada", "[[0,0.5],[1,0.5],[1,1],[0,1]]")),
        new("ejemplo08", "Persona con faroles frente a una casa, de noche", "Person with lanterns outside a house at night",
            Zona("door", "Puerta", "[[0.15,0.45],[0.9,0.45],[0.95,1],[0.1,1]]")),
        new("ejemplo09", "Persona revisa un auto de noche", "Person checks a car at night",
            Zona("yard", "Cochera", "[[0,0.45],[1,0.45],[1,0.92],[0,0.92]]")),
        new("ejemplo10", "Desconocido toca el timbre (vista por la mirilla)", "Stranger rings the bell (peephole view)",
            Zona("door", "Puerta", "[[0.18,0.4],[0.82,0.4],[0.82,1],[0.18,1]]")),
    ];

    public static SampleVideo? Find(string? key) => All.FirstOrDefault(v => v.Key == key);
}
