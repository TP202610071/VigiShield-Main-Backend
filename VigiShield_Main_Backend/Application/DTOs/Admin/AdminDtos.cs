namespace VigiShield.Application.DTOs.Admin;

public record AdminResumenDto(
    int Usuarios, int Hogares, int UsuariosActivos7Dias, int UsuariosEnLinea,
    int Camaras, int CamarasActivas, int CamarasDelTelefono,
    int EventosTotal, int Eventos24h, int Eventos7Dias, int SesionesVideoEjemplo,
    Dictionary<string, int> EventosPorTipo7Dias);

public record AdminUsuarioDto(
    Guid Id, string Nombre, string Correo, string Rol, Guid HogarId, string? Direccion,
    DateTime CreadoEn, DateTime? UltimaActividad, DateTime? TerminosAceptados,
    string Estado, bool EnLinea, int Camaras, int CamarasActivas, int Eventos,
    DateTime? UltimoEvento, int SesionesVideoEjemplo, bool VigilanciaPausada);

public record AdminCamaraDto(
    Guid Id, string Nombre, Guid HogarId, string? Dueno, string? CorreoDueno,
    string Modo, bool Activa, bool Notificaciones, bool EsEjemplo, DateTime? EjemploHasta,
    string? StreamKey, int Zonas, DateTime CreadaEn);

public record AdminEventoDto(
    Guid Id, DateTime Fecha, Guid HogarId, string? Dueno, string? CorreoDueno,
    string? Camara, string Tipo, string TipoEtiqueta, string Riesgo, float? Confianza,
    bool Nocturno, string? Foto, string? Clip, bool EsEjemplo, bool AvisoEnviado);

public record AdminEventosPaginaDto(int Total, int Pagina, int Tamano, List<AdminEventoDto> Items);

public record AdminFiltroEventos(
    Guid? Hogar = null, string? Tipo = null, DateTime? Desde = null, DateTime? Hasta = null,
    bool? Ejemplos = null, int Pagina = 1, int Tamano = 50);

public record AdminCambiarActivaRequest(bool Activa);
