namespace DeepArquitectura.Contracts;

/// <summary>Rutas de la API que usa la app de los albañiles (todas bajo /api/albanil).</summary>
public static class RutasAlbanil
{
    public const string Login = "/api/albanil/login";
    public const string Logout = "/api/albanil/logout";
    public const string Yo = "/api/albanil/yo";
    public static string Obra(int obraId) => $"/api/albanil/obras/{obraId}";
    public static string Avances(int obraId) => $"/api/albanil/obras/{obraId}/avances";
    public static string Medios(int obraId) => $"/api/albanil/obras/{obraId}/medios";
    public static string Entrada(int obraId) => $"/api/albanil/obras/{obraId}/jornada/entrada";
    public static string Salida(int obraId) => $"/api/albanil/obras/{obraId}/jornada/salida";
    public static string Pedidos(int obraId) => $"/api/albanil/obras/{obraId}/pedidos";
    public static string Problemas(int obraId) => $"/api/albanil/obras/{obraId}/problemas";
    public static string Partes(int obraId) => $"/api/albanil/obras/{obraId}/partes";
    public static string Recepciones(int obraId) => $"/api/albanil/obras/{obraId}/recepciones";
    public static string Controles(int obraId) => $"/api/albanil/obras/{obraId}/controles";
    public static string NotasPedido(int obraId) => $"/api/albanil/obras/{obraId}/notas-pedido";
    public static string Leido(int obraId) => $"/api/albanil/obras/{obraId}/libro/leido";
    /// <summary>Última foto de un punto fijo (para encuadrar igual).</summary>
    public static string UltimaFotoPunto(int obraId, int puntoId) => $"/api/albanil/obras/{obraId}/puntos/{puntoId}/ultima";

    public const int MaximoItemsPedido = 20;
    public const decimal MaximoHorasDia = 24;
}

/// <summary>Valores de texto que viajan en la API (mismos nombres que los enums del servidor).</summary>
public static class ValoresAlbanil
{
    public static readonly string[] TiposProblema = ["FaltaMaterial", "ErrorPlano", "Seguridad", "Accidente", "Clima", "Herramientas", "Otro"];
    public static readonly string[] Urgencias = ["Baja", "Media", "Alta"];
    /// <summary>Unidades sugeridas en la app para pedidos de material.</summary>
    public static readonly string[] UnidadesMaterial = ["bolsas", "u", "m³", "m²", "ml", "kg", "barras", "latas", "rollos"];
    /// <summary>Oficios del personal de obra (propio o subcontratado).</summary>
    public static readonly string[] Oficios = ["Oficial albañil", "Medio oficial", "Ayudante", "Electricista", "Plomero / sanitarista",
        "Gasista", "Pintor", "Durlock / yesero", "Herrero", "Carpintero", "Techista", "Otro"];
    /// <summary>Elementos de protección que se controlan al fichar la entrada. El arnés solo si trabaja en altura.</summary>
    public static readonly string[] Epp = ["Casco", "Calzado de seguridad", "Guantes", "Protección ocular"];
    public const string Arnes = "Arnés";

    public static string Etiqueta(string valor) => valor switch
    {
        "FaltaMaterial" => "Falta material",
        "ErrorPlano" => "Error o duda en plano",
        "Seguridad" => "Seguridad",
        "Accidente" => "Accidente",
        "Clima" => "Clima / lluvia",
        "Herramientas" => "Herramienta rota o faltante",
        "Otro" => "Otro",
        "EnCurso" => "En curso",
        "EnRevision" => "En revisión",
        _ => valor,
    };
}

public sealed record AlbanilDto(int Id, string Nombre, string Usuario, string? Oficio,
    string Capataz, string? CapatazTelefono, IReadOnlyList<ObraResumenDto> Obras, string? Empresa = null);

public sealed record LoginAlbanilResponse(string Token, AlbanilDto Albanil);

/// <summary>Todo lo que la app muestra de una obra (para el albañil conectado).</summary>
public sealed record ObraAlbanilDto(
    int Id, string Codigo, string Nombre, string? Direccion, string? Localidad, string Estado, int PorcentajeAvance,
    IReadOnlyList<TareaDto> Tareas,
    JornadaAbiertaDto? JornadaAbierta,
    IReadOnlyList<AvanceDto> MisAvances,
    IReadOnlyList<PedidoDto> Pedidos,
    IReadOnlyList<ProblemaDto> Problemas,
    ParteDto? ParteDeHoy,
    double? Latitud = null, double? Longitud = null, int RadioMetros = 150,
    IReadOnlyList<ControlDto>? Controles = null,
    IReadOnlyList<OrdenDto>? Ordenes = null,
    IReadOnlyList<PuntoFotoDto>? PuntosFoto = null,
    IReadOnlyList<ClimaDto>? Clima = null);

/// <summary>Control de una etapa en curso. Resultado: null (sin cargar), Ok u Observado.</summary>
public sealed record ControlDto(int Id, string Etapa, string Texto, bool RequiereFoto, string? Resultado, DateTime? FechaUtc, string? Nota);

/// <summary>Orden de servicio del estudio. <see cref="Leida"/>: este albañil ya confirmó que la leyó.</summary>
public sealed record OrdenDto(int Id, int Numero, DateTime FechaUtc, string Texto, bool Leida);

/// <summary>Punto fijo de foto. <see cref="Pendiente"/>: ya pasó la frecuencia desde la última foto.</summary>
public sealed record PuntoFotoDto(int Id, string Nombre, string? Indicacion, DateTime? UltimaUtc, bool Pendiente, bool TieneReferencia);

public sealed record ClimaDto(DateOnly Fecha, double? TempMax, double? TempMin, double? LluviaMm, double? VientoMaxKmh, string Descripcion);

/// <summary>
/// Unidad null = la tarea se mide en porcentaje. <see cref="Mia"/> false = tarea de la cuadrilla sin asignar.
/// Estado: Pendiente, EnCurso, Completada.
/// </summary>
public sealed record TareaDto(int Id, string Titulo, string? Descripcion, string? Etapa, string? Unidad,
    decimal? CantidadObjetivo, decimal CantidadHecha, int Porcentaje, string Estado, DateOnly? FechaLimite, bool Mia,
    DateOnly? FechaInicioPlan = null, bool Atrasada = false);

public sealed record JornadaAbiertaDto(Guid Ref, DateTime EntradaUtc);

/// <summary>Validacion: null (no requiere), Pendiente, Aprobado, Corregido o Rechazado (con el motivo).</summary>
public sealed record AvanceDto(int Id, DateTime FechaUtc, string Titulo, string? Tarea, decimal? Cantidad, string? Unidad, int Fotos, int Videos,
    string? Validacion = null, string? Motivo = null);

/// <summary>Estado: Pendiente, Aprobado, Rechazado, Entregado o EntregaParcial.</summary>
public sealed record PedidoDto(int Id, DateTime FechaUtc, string Estado, bool Urgente, string Detalle, string? Respuesta,
    IReadOnlyList<PedidoItemDto>? Items = null);

/// <summary>Renglón de un pedido con lo que ya llegó a la obra.</summary>
public sealed record PedidoItemDto(int Id, string Material, decimal Cantidad, string Unidad, decimal Recibido);

/// <summary>Estado: Abierto, EnRevision, Resuelto.</summary>
public sealed record ProblemaDto(int Id, DateTime FechaUtc, string Tipo, string Urgencia, string Descripcion, string Estado, string? Respuesta);

public sealed record ParteDto(DateOnly Fecha, string? Resumen, decimal HorasTotales, IReadOnlyList<ParteItemDto> Items);

/// <summary>
/// Avance cargado en el teléfono. Con tarea medida en cantidad: <see cref="Cantidad"/> es lo hecho en este reporte
/// (ej. 8 m² hoy). Con tarea medida en %: <see cref="Porcentaje"/> es el avance total de la tarea.
/// Sin tarea: fotos/videos de avance general de la obra. Las fotos se suben después con <see cref="CamposMedio"/>
/// usando este mismo <see cref="Ref"/>.
/// </summary>
public sealed record NuevoAvanceRequest(Guid Ref, DateTime FechaUtc, int? TareaId, decimal? Cantidad, int? Porcentaje,
    bool Terminada, string? Nota, int? PuntoFotoId = null);

public sealed record NuevoAvanceResponse(int Id, TareaDto? Tarea);

/// <summary>Entrada o salida. En la salida, <see cref="Ref"/> es el de la entrada que cierra.</summary>
/// <param name="EppFaltante">Solo en la entrada: lo que le falta (vacío = tiene todo; null = no se hizo el control).</param>
public sealed record FichadaRequest(Guid Ref, DateTime FechaUtc, double? Latitud, double? Longitud, double? PrecisionMetros,
    bool? TrabajoEnAltura = null, IReadOnlyList<string>? EppFaltante = null);

public sealed record PedidoItemRequest(string Material, decimal Cantidad, string Unidad);

public sealed record NuevoPedidoRequest(Guid Ref, DateTime FechaUtc, int? TareaId, bool Urgente, string? Nota, IReadOnlyList<PedidoItemRequest> Items);

/// <summary>Las fotos del problema se suben después con este mismo <see cref="Ref"/> como registroRef.</summary>
public sealed record NuevoProblemaRequest(Guid Ref, DateTime FechaUtc, int? TareaId, string Tipo, string Urgencia, string Descripcion);

public sealed record ParteItemDto(int? TareaId, decimal Horas, string? Detalle);

/// <summary>Parte del día. Mandarlo de nuevo para el mismo día lo reemplaza.</summary>
public sealed record ParteRequest(DateOnly Fecha, string? Resumen, IReadOnlyList<ParteItemDto> Items);

public sealed record IdResponse(int Id);

public sealed record RecepcionItemRequest(int? PedidoItemId, string Material, decimal Cantidad, string Unidad);

/// <summary>Material que llegó a la obra. La foto del remito se sube después con este mismo <see cref="Ref"/>.</summary>
public sealed record NuevaRecepcionRequest(Guid Ref, DateTime FechaUtc, int? PedidoId, string? Proveedor, string? NumeroRemito,
    string? Nota, IReadOnlyList<RecepcionItemRequest> Items);

/// <summary>Resultado de un control de etapa: Ok u Observado. Las fotos se suben con este mismo <see cref="Ref"/>.</summary>
public sealed record RespuestaControlRequest(Guid Ref, DateTime FechaUtc, int ControlId, string Resultado, string? Nota);

/// <summary>Nota de pedido de la obra al estudio (libro de obra). Fotos con este mismo <see cref="Ref"/>.</summary>
public sealed record NotaPedidoRequest(Guid Ref, DateTime FechaUtc, string Texto);

/// <summary>Órdenes de servicio que el albañil confirma haber leído.</summary>
public sealed record LeidoRequest(IReadOnlyList<int> Ordenes);
