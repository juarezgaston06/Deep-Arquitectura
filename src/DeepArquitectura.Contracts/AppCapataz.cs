namespace DeepArquitectura.Contracts;

/// <summary>Rutas de la API que usa la app del capataz (todas bajo /api/app).</summary>
public static class RutasApp
{
    public const string Login = "/api/app/login";
    public const string Logout = "/api/app/logout";
    public const string Yo = "/api/app/yo";
    public static string Obra(int obraId) => $"/api/app/obras/{obraId}";
    public static string Registros(int obraId) => $"/api/app/obras/{obraId}/registros";
    public static string Medios(int obraId) => $"/api/app/obras/{obraId}/medios";
    public static string Etapa(int obraId, int etapaId) => $"/api/app/obras/{obraId}/etapas/{etapaId}";
    public static string Validaciones(int obraId) => $"/api/app/obras/{obraId}/validaciones";
    public static string Atrasos(int obraId) => $"/api/app/obras/{obraId}/atrasos";
    /// <summary>Multipart: campo <see cref="CamposGasto.Datos"/> (JSON) + hasta 5 archivos en <see cref="CamposGasto.Archivo"/>.</summary>
    public static string Gastos(int obraId) => $"/api/app/obras/{obraId}/gastos";
    public static string Sobrantes(int obraId) => $"/api/app/obras/{obraId}/sobrantes";
    public static string NotasPedido(int obraId) => $"/api/app/obras/{obraId}/notas-pedido";
    public static string Leido(int obraId) => $"/api/app/obras/{obraId}/libro/leido";
    /// <summary>Foto de un avance de la cuadrilla (para verla antes de validar).</summary>
    public static string Foto(int medioId) => $"/api/app/medios/{medioId}";

    /// <summary>Tamaño máximo de un archivo (videos de obra en buena calidad).</summary>
    public const long TamanoMaximoMedio = 500L * 1024 * 1024;
    /// <summary>Comprobantes de gasto: fotos o PDF de hasta 15 MB, hasta 5 por gasto.</summary>
    public const long TamanoMaximoComprobante = 15L * 1024 * 1024;
    public const int MaximoComprobantes = 5;
}

public sealed record LoginRequest(string Usuario, string Pin, string? Dispositivo);

public sealed record LoginResponse(string Token, CapatazDto Capataz);

public sealed record CapatazDto(int Id, string Nombre, string Usuario, IReadOnlyList<ObraResumenDto> Obras);

public sealed record ObraResumenDto(int Id, string Codigo, string Nombre, string? Direccion, string? Localidad, string Estado, int PorcentajeAvance);

/// <param name="PorValidar">Avances de la cuadrilla esperando que el capataz los apruebe, corrija o rechace.</param>
/// <param name="Ordenes">Órdenes de servicio del estudio que este capataz todavía no confirmó haber leído.</param>
/// <param name="Atrasadas">Tareas de la obra pasadas de fecha (con la causa si ya se anotó).</param>
public sealed record ObraDetalleDto(
    int Id, string Codigo, string Nombre, string? Direccion, string? Localidad, string Estado, int PorcentajeAvance,
    IReadOnlyList<EtapaDto> Etapas, IReadOnlyList<RegistroDto> Registros,
    IReadOnlyList<AvancePorValidarDto>? PorValidar = null,
    IReadOnlyList<OrdenDto>? Ordenes = null,
    IReadOnlyList<TareaAtrasadaDto>? Atrasadas = null);

/// <summary>Estado: Pendiente, EnCurso, Completada u Omitida.</summary>
public sealed record EtapaDto(int Id, int Orden, string Nombre, string Estado, DateOnly? FechaInicio, DateOnly? FechaFin);

public sealed record RegistroDto(int Id, DateTime FechaUtc, string Titulo, string? Descripcion, string? Etapa, string? Autor, int Fotos, int Videos);

/// <summary>
/// Avance de un albañil por validar. Con <see cref="Unidad"/>: <see cref="Cantidad"/> es lo informado en este reporte;
/// sin unidad: <see cref="Porcentaje"/> es el avance total de la tarea. <see cref="FotoIds"/> se abren con <see cref="RutasApp.Foto"/>.
/// </summary>
public sealed record AvancePorValidarDto(int RegistroId, string Albanil, int TareaId, string Tarea, string? Unidad,
    decimal? Cantidad, int? Porcentaje, bool Terminada, DateTime FechaUtc, string? Nota, int Fotos, int Videos,
    decimal? CantidadObjetivo, decimal CantidadAprobada, IReadOnlyList<int> FotoIds);

/// <param name="Causa">Valor de <see cref="ValoresCapataz.CausasAtraso"/> ya anotado, o null.</param>
/// <param name="CausaSugerida">Lo que sugiere el sistema (lluvia en el período, avisos de falta de material), o null.</param>
public sealed record TareaAtrasadaDto(int Id, string Titulo, string? Etapa, DateOnly? FechaLimite, int Porcentaje,
    string? Causa, string? Detalle, string? CausaSugerida, string? MotivoSugerido);

/// <summary>
/// Novedad de obra creada en el teléfono. <see cref="Ref"/> lo genera la app: si el envío se repite
/// (sin señal, reintento), el servidor no la duplica.
/// </summary>
public sealed record NuevoRegistroRequest(Guid Ref, DateTime FechaUtc, string Titulo, string? Descripcion, int? EtapaId);

public sealed record NuevoRegistroResponse(int Id);

/// <summary>Campos del formulario multipart para subir una foto o video (más el archivo en "archivo").</summary>
public static class CamposMedio
{
    public const string RegistroRef = "registroRef";
    public const string MedioRef = "medioRef";
    public const string Archivo = "archivo";
}

/// <summary>Campos del formulario multipart de un gasto: el JSON de <see cref="NuevoGastoRequest"/> y los comprobantes.</summary>
public static class CamposGasto
{
    public const string Datos = "datos";
    public const string Archivo = "archivo";
}

public sealed record CambiarEtapaRequest(string Estado);

/// <summary>Decisión sobre un avance: aprobar, corregir (con la cantidad o el % correcto) o rechazar (con motivo).</summary>
public sealed record ValidacionItem(int RegistroId, string Decision, decimal? Cantidad = null, int? Porcentaje = null, string? Motivo = null);

/// <summary>Una o varias decisiones juntas («aprobar todos»). Validar de nuevo lo mismo deja lo mismo.</summary>
public sealed record ValidacionesRequest(IReadOnlyList<ValidacionItem> Items);

public sealed record AtrasoRequest(int TareaId, string Causa, string? Detalle);

/// <summary>Gasto que pagó el capataz. Va con sus comprobantes en el mismo envío (multipart): o llega todo o nada.</summary>
public sealed record NuevoGastoRequest(Guid Ref, DateTime FechaUtc, string Descripcion, decimal Monto, string Categoria,
    string? Proveedor, string? NumeroComprobante, int? EtapaId);

/// <param name="ObraDestinoId">Solo con destino OtraObra: una de las obras del capataz.</param>
public sealed record NuevoSobranteRequest(Guid Ref, DateTime FechaUtc, string Material, decimal Cantidad, string Unidad,
    string Destino, int? ObraDestinoId, string? Nota);

public sealed record ErrorApp(string Mensaje);

/// <summary>Valores de texto que viajan en la API del capataz (mismos nombres que los enums del servidor) y su etiqueta.</summary>
public static class ValoresCapataz
{
    public const string Aprobar = "aprobar";
    public const string Corregir = "corregir";
    public const string Rechazar = "rechazar";

    public static readonly (string Valor, string Etiqueta)[] CausasAtraso =
    [
        ("Clima", "Clima / lluvia"),
        ("FaltaMaterial", "Falta de material"),
        ("FaltaPersonal", "Falta de personal"),
        ("CambioCliente", "Cambio del cliente"),
        ("DefinicionPendiente", "Falta una definición del estudio"),
        ("Retrabajo", "Retrabajo (hubo que rehacer)"),
        ("Tramite", "Trámite / inspección"),
        ("PagoCliente", "Atraso de pago del cliente"),
        ("Otro", "Otro"),
    ];

    public static readonly (string Valor, string Etiqueta)[] CategoriasGasto =
    [
        ("Flete", "Flete"),
        ("Material", "Material (ferretería, corralón al paso)"),
        ("ManoDeObra", "Changa / jornal fuera de la app"),
        ("Subcontrato", "Subcontrato"),
        ("AlquilerEquipos", "Alquiler de equipos (volquete, andamio…)"),
        ("Herramientas", "Herramientas"),
        ("Tramites", "Trámites y tasas"),
        ("Otro", "Otro"),
    ];

    public static readonly (string Valor, string Etiqueta)[] DestinosSobrante =
    [
        ("Deposito", "Al depósito"),
        ("DevueltoProveedor", "Devuelto al proveedor"),
        ("OtraObra", "Llevado a otra obra"),
        ("Desperdicio", "Desperdicio (se tiró)"),
    ];

    public static string Etiqueta((string Valor, string Etiqueta)[] lista, string? valor) =>
        lista.FirstOrDefault(x => x.Valor == valor).Etiqueta ?? valor ?? "";
}
