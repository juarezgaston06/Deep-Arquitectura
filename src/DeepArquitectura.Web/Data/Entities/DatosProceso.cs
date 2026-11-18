namespace DeepArquitectura.Web.Data.Entities;

// Datos del proceso completo: relevamiento, costos reales, trámites, sobrantes, entrega, garantía,
// encuestas y pagos que informa el cliente. Ver docs/ARQUITECTURA.md «Datos del proceso».

/// <summary>
/// Archivo (foto o PDF) adjunto a un relevamiento, gasto, factura, observación de entrega, reclamo o pago informado.
/// Va colgado de uno solo de ellos. Se guarda en <c>IAlmacenMedios</c> y nunca se sirve en forma pública.
/// </summary>
public class Adjunto
{
    public int Id { get; set; }
    /// <summary>Obra a la que pertenece (null solo en relevamientos de alguien que todavía no tiene obra).</summary>
    public int? ObraId { get; set; }
    public Obra? Obra { get; set; }
    public int? RelevamientoId { get; set; }
    public Relevamiento? Relevamiento { get; set; }
    public int? GastoObraId { get; set; }
    public GastoObra? Gasto { get; set; }
    public int? RecepcionMaterialId { get; set; }
    public RecepcionMaterial? Recepcion { get; set; }
    public int? ObservacionEntregaId { get; set; }
    public ObservacionEntrega? Observacion { get; set; }
    public int? ReclamoGarantiaId { get; set; }
    public ReclamoGarantia? Reclamo { get; set; }
    public int? PagoInformadoId { get; set; }
    public PagoInformado? PagoInformado { get; set; }
    /// <summary>Foto del «después» (observación o reclamo resuelto).</summary>
    public bool EsResolucion { get; set; }
    public string Ruta { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long Tamano { get; set; }
    public string? NombreOriginal { get; set; }
    public DateTime CreadoEn { get; set; }
}

/// <summary>Primera visita al terreno o a la casa: medidas, servicios, estado y lo que quiere el cliente.</summary>
public class Relevamiento
{
    public int Id { get; set; }
    public int? ClienteId { get; set; }
    public Cliente? Cliente { get; set; }
    public int? ConsultaId { get; set; }
    public Consulta? Consulta { get; set; }
    public int? ObraId { get; set; }
    public Obra? Obra { get; set; }
    /// <summary>Nombre del interesado si todavía no es cliente.</summary>
    public string? Contacto { get; set; }
    public DateOnly Fecha { get; set; }
    public string? Direccion { get; set; }
    public string? Localidad { get; set; }
    public double? Latitud { get; set; }
    public double? Longitud { get; set; }

    // Terreno y construcción existente
    public decimal? FrenteM { get; set; }
    public decimal? FondoM { get; set; }
    public decimal? SuperficieTerrenoM2 { get; set; }
    public decimal? SuperficieCubiertaM2 { get; set; }
    public int? Plantas { get; set; }
    public int? AntiguedadAnios { get; set; }
    /// <summary>Bueno / Regular / Malo / Terreno baldío (ver <c>Relevamientos.Estados</c>).</summary>
    public string? EstadoGeneral { get; set; }

    // Servicios (null = no se verificó)
    public bool? Agua { get; set; }
    public bool? Luz { get; set; }
    public bool? Gas { get; set; }
    public bool? Cloaca { get; set; }

    /// <summary>Lo encontrado, de una lista (humedad, fisuras…), separado por coma.</summary>
    public string? Hallazgos { get; set; }
    /// <summary>Qué quiere hacer el cliente.</summary>
    public string? Pedido { get; set; }
    public decimal? PresupuestoCliente { get; set; }
    public string? PlazoDeseado { get; set; }
    public string? Notas { get; set; }
    public DateTime CreadoEn { get; set; }
    public DateTime ActualizadoEn { get; set; }
    public List<Adjunto> Adjuntos { get; set; } = [];
}

/// <summary>Plata que sale de la obra y no entra por una recepción con precio: subcontratos, fletes, alquileres…</summary>
public class GastoObra
{
    public int Id { get; set; }
    public int ObraId { get; set; }
    public Obra Obra { get; set; } = null!;
    public int? EtapaObraId { get; set; }
    public EtapaObra? Etapa { get; set; }
    public DateOnly Fecha { get; set; }
    public CategoriaGasto Categoria { get; set; }
    public string Descripcion { get; set; } = "";
    public string? Proveedor { get; set; }
    public string? NumeroComprobante { get; set; }
    public decimal Monto { get; set; }
    /// <summary>Lo cargó el capataz desde su app (null = lo cargó el estudio).</summary>
    public int? CapatazId { get; set; }
    public Capataz? Capataz { get; set; }
    /// <summary>Id del envío del teléfono: si la app reintenta, no se duplica.</summary>
    public Guid? Ref { get; set; }
    public DateTime CreadoEn { get; set; }
    public List<Adjunto> Adjuntos { get; set; } = [];
}

/// <summary>Material que sobró al cerrar una etapa y qué se hizo con él.</summary>
public class MaterialSobrante
{
    public int Id { get; set; }
    public int ObraId { get; set; }
    public Obra Obra { get; set; } = null!;
    public int? EtapaObraId { get; set; }
    public EtapaObra? Etapa { get; set; }
    public DateOnly Fecha { get; set; }
    public string Material { get; set; } = "";
    public decimal Cantidad { get; set; }
    public string Unidad { get; set; } = "";
    public DestinoSobrante Destino { get; set; }
    /// <summary>Obra a la que se llevó (destino «otra obra»).</summary>
    public int? ObraDestinoId { get; set; }
    public Obra? ObraDestino { get; set; }
    /// <summary>Lo que se recuperó (nota de crédito, valor para la otra obra).</summary>
    public decimal? ValorRecuperado { get; set; }
    public string? Nota { get; set; }
    public int? CapatazId { get; set; }
    public Capataz? Capataz { get; set; }
    /// <summary>Id del envío del teléfono: si la app reintenta, no se duplica.</summary>
    public Guid? Ref { get; set; }
    public DateTime CreadoEn { get; set; }
}

/// <summary>Trámite de la obra ante un organismo (municipalidad, Ecogas, EPEC, Colegio…).</summary>
public class TramiteObra
{
    public int Id { get; set; }
    public int ObraId { get; set; }
    public Obra Obra { get; set; } = null!;
    public int Orden { get; set; }
    public string Nombre { get; set; } = "";
    public string? Organismo { get; set; }
    public string? Expediente { get; set; }
    public EstadoTramite Estado { get; set; } = EstadoTramite.Preparando;
    public DateOnly? FechaPresentado { get; set; }
    public DateOnly? FechaAprobado { get; set; }
    /// <summary>Próximo vencimiento (plazo para contestar una observación, turno de inspección…).</summary>
    public DateOnly? Vence { get; set; }
    public bool VisibleCliente { get; set; } = true;
    public DateTime CreadoEn { get; set; }
    public List<MovimientoTramite> Movimientos { get; set; } = [];
}

/// <summary>Cada cambio de estado o novedad de un trámite (queda la historia con fechas).</summary>
public class MovimientoTramite
{
    public int Id { get; set; }
    public int TramiteObraId { get; set; }
    public TramiteObra Tramite { get; set; } = null!;
    public DateOnly Fecha { get; set; }
    public EstadoTramite Estado { get; set; }
    public string? Nota { get; set; }
    /// <summary>Respuesta del organismo, subida como documento de la obra.</summary>
    public int? DocumentoObraId { get; set; }
    public DocumentoObra? Documento { get; set; }
    public DateTime CreadoEn { get; set; }
}

/// <summary>Acta de entrega: recorrida final con el cliente, observaciones a resolver y su firma.</summary>
public class ActaEntrega
{
    public int Id { get; set; }
    public int ObraId { get; set; }
    public Obra Obra { get; set; } = null!;
    public EstadoActa Estado { get; set; } = EstadoActa.Borrador;
    public DateOnly? FechaRecorrida { get; set; }
    public DateTime? FirmadaEn { get; set; }
    public string? FirmadaPor { get; set; }
    public string? FirmadaDesdeIp { get; set; }
    /// <summary>Firmó con observaciones pendientes («recibo con reservas»).</summary>
    public bool ConReservas { get; set; }
    public string? ComentarioCliente { get; set; }
    /// <summary>Meses de garantía desde la firma.</summary>
    public int MesesGarantia { get; set; } = 12;
    public DateTime CreadoEn { get; set; }
    public List<ObservacionEntrega> Observaciones { get; set; } = [];
}

public class ObservacionEntrega
{
    public int Id { get; set; }
    public int ActaEntregaId { get; set; }
    public ActaEntrega Acta { get; set; } = null!;
    public string Descripcion { get; set; } = "";
    /// <summary>Ambiente o lugar (ej. «Baño planta alta»).</summary>
    public string? Ubicacion { get; set; }
    public string? Rubro { get; set; }
    /// <summary>Quién la tiene que resolver (oficio, empresa o persona).</summary>
    public string? Responsable { get; set; }
    public bool Resuelta { get; set; }
    public DateTime? ResueltaEn { get; set; }
    public string? NotaResolucion { get; set; }
    public DateTime CreadoEn { get; set; }
    public List<Adjunto> Adjuntos { get; set; } = [];
}

/// <summary>Problema que el cliente reporta desde su app (garantía / postventa).</summary>
public class ReclamoGarantia
{
    public int Id { get; set; }
    public Guid Ref { get; set; }
    public int ObraId { get; set; }
    public Obra Obra { get; set; } = null!;
    public DateTime Fecha { get; set; }
    public string Rubro { get; set; } = "";
    public string? Ubicacion { get; set; }
    public string Descripcion { get; set; } = "";
    public bool Urgente { get; set; }
    public string? ReportadoPor { get; set; }
    public EstadoReclamo Estado { get; set; } = EstadoReclamo.Nuevo;
    /// <summary>Lo que ve el cliente.</summary>
    public string? Respuesta { get; set; }
    public DateOnly? FechaVisita { get; set; }
    public DateTime? ResueltoEn { get; set; }
    /// <summary>El estudio ya lo vio (para la bandeja).</summary>
    public bool Visto { get; set; }

    // Lo que decide el estudio al revisarlo (no lo ve el cliente)
    public CausaReclamo? Causa { get; set; }
    public int? EtapaObraId { get; set; }
    public EtapaObra? Etapa { get; set; }
    public string? Responsable { get; set; }
    public decimal? CostoReparacion { get; set; }
    public string? NotaInterna { get; set; }
    public List<Adjunto> Adjuntos { get; set; } = [];
}

/// <summary>Encuesta corta (0 a 10 + comentario) al terminar una etapa o al entregar la obra.</summary>
public class EncuestaCliente
{
    public int Id { get; set; }
    public int ObraId { get; set; }
    public Obra Obra { get; set; } = null!;
    public MomentoEncuesta Momento { get; set; }
    public int? EtapaObraId { get; set; }
    public EtapaObra? Etapa { get; set; }
    public string Titulo { get; set; } = "";
    public DateTime CreadaEn { get; set; }
    public DateTime? RespondidaEn { get; set; }
    /// <summary>¿Cuánto nos recomendarías? 0 a 10.</summary>
    public int? Puntaje { get; set; }
    public string? Comentario { get; set; }
    public bool Vista { get; set; }
}

/// <summary>Qué autorizó el cliente para mostrar en redes y en el sitio. Uno por obra; lo puede cambiar cuando quiera.</summary>
public class PermisoDifusion
{
    public int Id { get; set; }
    public int ObraId { get; set; }
    public Obra Obra { get; set; } = null!;
    public bool UsarFotos { get; set; }
    /// <summary>Mostrar su nombre junto al testimonio (si no, va anónimo: «cliente de Villa Allende»).</summary>
    public bool MostrarNombre { get; set; }
    public string? Testimonio { get; set; }
    public string? Nombre { get; set; }
    public string? DesdeIp { get; set; }
    public DateTime ActualizadoEn { get; set; }
}

/// <summary>Pago que el cliente avisa desde su app (con el comprobante). Se suma a los pagos cuando el estudio lo confirma.</summary>
public class PagoInformado
{
    public int Id { get; set; }
    public Guid Ref { get; set; }
    public int ObraId { get; set; }
    public Obra Obra { get; set; } = null!;
    public DateOnly Fecha { get; set; }
    public decimal Monto { get; set; }
    public MedioPago Medio { get; set; }
    /// <summary>Certificado que dice estar pagando (opcional).</summary>
    public int? CertificadoId { get; set; }
    public Certificado? Certificado { get; set; }
    public string? Nota { get; set; }
    public string? InformadoPor { get; set; }
    public DateTime InformadoEn { get; set; }
    public EstadoPagoInformado Estado { get; set; } = EstadoPagoInformado.Pendiente;
    public string? MotivoRechazo { get; set; }
    public DateTime? RevisadoEn { get; set; }
    /// <summary>Pago creado al confirmarlo.</summary>
    public int? PagoId { get; set; }
    public Pago? Pago { get; set; }
    public List<Adjunto> Adjuntos { get; set; } = [];
}
