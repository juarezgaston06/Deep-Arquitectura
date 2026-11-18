namespace DeepArquitectura.Web.Data.Entities;

/// <summary>Persona o empresa para la que trabaja el estudio.</summary>
public class Cliente
{
    public int Id { get; set; }
    public string Nombre { get; set; } = "";
    public string? Email { get; set; }
    public string? Telefono { get; set; }
    /// <summary>DNI o CUIT, para presupuestos y facturación.</summary>
    public string? DniCuit { get; set; }
    public string? Direccion { get; set; }
    public string? Localidad { get; set; }
    public OrigenCliente Origen { get; set; } = OrigenCliente.Web;
    public string? Notas { get; set; }
    public DateTime CreadoEn { get; set; }
    public DateTime ActualizadoEn { get; set; }

    public List<Obra> Obras { get; set; } = [];
    public List<Consulta> Consultas { get; set; } = [];
}

/// <summary>Mensaje que deja un visitante en el formulario del sitio (lead).</summary>
public class Consulta
{
    public int Id { get; set; }
    public string Nombre { get; set; } = "";
    public string Email { get; set; } = "";
    public string? Telefono { get; set; }
    public string Mensaje { get; set; } = "";
    public AreaConsulta Area { get; set; }
    public EstadoConsulta Estado { get; set; } = EstadoConsulta.Nueva;
    public DateTime CreadoEn { get; set; }

    // ---- De dónde vino (primer contacto con el sitio, lo guarda main.js) ----
    /// <summary>Canal deducido de los utm o del sitio de origen (lo puede corregir el estudio).</summary>
    public OrigenCliente Canal { get; set; } = OrigenCliente.Web;
    public string? UtmFuente { get; set; }
    public string? UtmMedio { get; set; }
    public string? UtmCampania { get; set; }
    /// <summary>Sitio desde el que llegó (ej. l.instagram.com), sin la ruta.</summary>
    public string? Referente { get; set; }
    /// <summary>Primera página del sitio que vio.</summary>
    public string? PaginaEntrada { get; set; }
    /// <summary>Publicación del agente de redes que la trajo (utm_campaign = pub-{id}).</summary>
    public int? PublicacionId { get; set; }
    public Publicacion? Publicacion { get; set; }
    /// <summary>Primera vez que el estudio la marcó como contactada (tiempo de respuesta).</summary>
    public DateTime? ContactadaEn { get; set; }
    public MotivoPerdida? MotivoDescarte { get; set; }

    public int? ClienteId { get; set; }
    public Cliente? Cliente { get; set; }
}

/// <summary>Trabajo concreto para un cliente (un plano municipal, una instalación de gas, etc.).</summary>
public class Obra
{
    public int Id { get; set; }
    /// <summary>Código legible, ej. OB-2026-007.</summary>
    public string Codigo { get; set; } = "";
    public string Nombre { get; set; } = "";
    public TipoServicio Servicio { get; set; }
    public EstadoObra Estado { get; set; } = EstadoObra.Presupuestada;
    public string? Direccion { get; set; }
    public string? Localidad { get; set; }
    public string? ExpedienteMunicipal { get; set; }
    public string? Descripcion { get; set; }
    public DateOnly? FechaInicio { get; set; }
    public DateOnly? FechaEntregaEstimada { get; set; }
    public DateOnly? FechaFinalizacion { get; set; }
    public decimal? MontoPresupuesto { get; set; }
    public DateTime CreadoEn { get; set; }
    public DateTime ActualizadoEn { get; set; }

    /// <summary>Ubicación de la obra: para avisar fichadas lejos de la obra y traer el clima.</summary>
    public double? Latitud { get; set; }
    public double? Longitud { get; set; }
    /// <summary>Distancia aceptada para fichar (metros).</summary>
    public int RadioMetros { get; set; } = 150;
    /// <summary>Link privado de solo lectura para el cliente (null = portal apagado).</summary>
    public string? TokenPortal { get; set; }

    public int ClienteId { get; set; }
    public Cliente Cliente { get; set; } = null!;

    public List<EtapaObra> Etapas { get; set; } = [];
    public List<RegistroAvance> Registros { get; set; } = [];
    public List<Pago> Pagos { get; set; } = [];

    /// <summary>Porcentaje de etapas cerradas (completadas u omitidas) sobre el total.</summary>
    public int PorcentajeAvance => CalcularAvance(Etapas);

    public static int CalcularAvance(IReadOnlyCollection<EtapaObra> etapas)
    {
        var relevantes = etapas.Count(e => e.Estado != EstadoEtapa.Omitida);
        if (relevantes == 0) return etapas.Count > 0 ? 100 : 0;
        var completadas = etapas.Count(e => e.Estado == EstadoEtapa.Completada);
        return (int)Math.Round(completadas * 100.0 / relevantes);
    }
}

public class EtapaObra
{
    public int Id { get; set; }
    public int ObraId { get; set; }
    public Obra Obra { get; set; } = null!;
    public int Orden { get; set; }
    public string Nombre { get; set; } = "";
    public EstadoEtapa Estado { get; set; } = EstadoEtapa.Pendiente;
    public DateOnly? FechaInicio { get; set; }
    public DateOnly? FechaFin { get; set; }
    public string? Notas { get; set; }
}

/// <summary>Entrada de la bitácora de obra. Si <see cref="UsableEnRedes"/> es true, el agente CM puede usarla como materia prima.</summary>
public class RegistroAvance
{
    public int Id { get; set; }
    public int ObraId { get; set; }
    public Obra Obra { get; set; } = null!;
    public int? EtapaObraId { get; set; }
    public EtapaObra? Etapa { get; set; }
    public DateTime Fecha { get; set; }
    public string Titulo { get; set; } = "";
    public string? Descripcion { get; set; }
    public bool UsableEnRedes { get; set; }

    /// <summary>
    /// Capataz que la mandó desde su app, o capataz del albañil que la mandó (null = cargada desde el panel).
    /// </summary>
    public int? CapatazId { get; set; }
    public Capataz? Capataz { get; set; }
    /// <summary>Albañil que la mandó desde su app (si no, la mandó el capataz o el estudio).</summary>
    public int? AlbanilId { get; set; }
    public Albanil? Albanil { get; set; }
    /// <summary>Tarea sobre la que informa el avance.</summary>
    public int? TareaObraId { get; set; }
    public TareaObra? Tarea { get; set; }
    /// <summary>Cantidad hecha en este reporte, en la unidad de la tarea (ej. 8 m²).</summary>
    public decimal? Cantidad { get; set; }
    /// <summary>% de la tarea informado en este reporte (tareas que se miden en porcentaje).</summary>
    public int? PorcentajeTarea { get; set; }

    /// <summary>Solo avances de albañiles: null = no requiere validación (capataz, estudio, problemas…).</summary>
    public EstadoValidacion? Validacion { get; set; }
    /// <summary>Lo que informó el albañil, si el capataz lo corrigió.</summary>
    public decimal? CantidadInformada { get; set; }
    public int? PorcentajeInformado { get; set; }
    /// <summary>Capataz que validó (null con validación hecha = la hizo el estudio desde el panel).</summary>
    public int? ValidadoPorCapatazId { get; set; }
    public Capataz? ValidadoPorCapataz { get; set; }
    public DateTime? ValidadoEn { get; set; }
    public string? MotivoValidacion { get; set; }

    /// <summary>El estudio la eligió para mostrarla en el portal del cliente.</summary>
    public bool VisibleCliente { get; set; }
    /// <summary>Foto tomada desde un punto fijo de la obra (para comparar en el tiempo).</summary>
    public int? PuntoFotoId { get; set; }
    public PuntoFoto? PuntoFoto { get; set; }
    /// <summary>Id generado por la app: evita duplicados cuando el teléfono reintenta el envío.</summary>
    public Guid? Ref { get; set; }
    public List<MedioRegistro> Medios { get; set; } = [];
}

/// <summary>Foto o video adjunto a una novedad de obra.</summary>
public class MedioRegistro
{
    public int Id { get; set; }
    public int RegistroAvanceId { get; set; }
    public RegistroAvance Registro { get; set; } = null!;
    public TipoMedio Tipo { get; set; }
    /// <summary>Nombre en el almacenamiento (disco local o Azure Blob), ej. obra-3/2026/11/abc.jpg.</summary>
    public string Ruta { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long Tamano { get; set; }
    public string? NombreOriginal { get; set; }
    /// <summary>Id generado por la app para no duplicar en reintentos.</summary>
    public Guid Ref { get; set; }
    public DateTime CreadoEn { get; set; }
}

/// <summary>Responsable en obra que usa la app Android. Solo ve las obras que tiene asignadas.</summary>
public class Capataz
{
    public int Id { get; set; }
    public string Nombre { get; set; } = "";
    /// <summary>Usuario para la app (único, en minúsculas).</summary>
    public string Usuario { get; set; } = "";
    public string? Telefono { get; set; }
    public string PinHash { get; set; } = "";
    public bool Activo { get; set; } = true;
    public DateTime CreadoEn { get; set; }
    public DateTime? UltimoAcceso { get; set; }

    public List<CapatazObra> Obras { get; set; } = [];
    public List<SesionCapataz> Sesiones { get; set; } = [];
}

public class CapatazObra
{
    public int CapatazId { get; set; }
    public Capataz Capataz { get; set; } = null!;
    public int ObraId { get; set; }
    public Obra Obra { get; set; } = null!;
    public DateTime AsignadoEn { get; set; }
}

/// <summary>Sesión de un teléfono. Se guarda solo el hash del token; se revoca borrándola.</summary>
public class SesionCapataz
{
    public int Id { get; set; }
    public int CapatazId { get; set; }
    public Capataz Capataz { get; set; } = null!;
    public string TokenHash { get; set; } = "";
    public string? Dispositivo { get; set; }
    public DateTime CreadaEn { get; set; }
    public DateTime UltimoUso { get; set; }
    public DateTime ExpiraEn { get; set; }
}

/// <summary>
/// Albañil u oficial de la cuadrilla de un capataz. Usa la app de albañiles y solo ve las obras que tiene asignadas
/// y que además son de su capataz.
/// </summary>
public class Albanil
{
    public int Id { get; set; }
    public string Nombre { get; set; } = "";
    /// <summary>Usuario para la app (único, en minúsculas).</summary>
    public string Usuario { get; set; } = "";
    public string? Telefono { get; set; }
    /// <summary>Oficial albañil, ayudante, electricista, plomero… (ver <c>Oficios.Lista</c>).</summary>
    public string? Oficio { get; set; }
    /// <summary>Empresa del subcontratista (null = personal propio).</summary>
    public string? Empresa { get; set; }
    /// <summary>Costo por hora para el estudio (jornal / horas). Se copia en cada fichada para que un aumento no cambie lo ya trabajado.</summary>
    public decimal? ValorHora { get; set; }
    public string PinHash { get; set; } = "";
    public bool Activo { get; set; } = true;
    public DateTime CreadoEn { get; set; }
    public DateTime? UltimoAcceso { get; set; }

    public int CapatazId { get; set; }
    public Capataz Capataz { get; set; } = null!;

    public List<AlbanilObra> Obras { get; set; } = [];
    public List<SesionAlbanil> Sesiones { get; set; } = [];
}

public class AlbanilObra
{
    public int AlbanilId { get; set; }
    public Albanil Albanil { get; set; } = null!;
    public int ObraId { get; set; }
    public Obra Obra { get; set; } = null!;
    public DateTime AsignadoEn { get; set; }
}

/// <summary>Sesión del teléfono de un albañil (solo el hash del token).</summary>
public class SesionAlbanil
{
    public int Id { get; set; }
    public int AlbanilId { get; set; }
    public Albanil Albanil { get; set; } = null!;
    public string TokenHash { get; set; } = "";
    public string? Dispositivo { get; set; }
    public DateTime CreadaEn { get; set; }
    public DateTime UltimoUso { get; set; }
    public DateTime ExpiraEn { get; set; }
}

/// <summary>
/// Trabajo concreto que el estudio le encarga a la cuadrilla (ej. «Mampostería planta baja, 40 m²»).
/// Si tiene <see cref="Unidad"/>, el avance se mide en cantidad; si no, en porcentaje.
/// </summary>
public class TareaObra
{
    public int Id { get; set; }
    public int ObraId { get; set; }
    public Obra Obra { get; set; } = null!;
    public int? EtapaObraId { get; set; }
    public EtapaObra? Etapa { get; set; }
    /// <summary>Albañil a cargo (null = cualquiera de la cuadrilla asignado a la obra).</summary>
    public int? AlbanilId { get; set; }
    public Albanil? Albanil { get; set; }
    /// <summary>Sin albañil asignado: solo la ve el personal de este oficio (null = todos).</summary>
    public string? Oficio { get; set; }
    /// <summary>Tipo de trabajo (Mampostería, Revoque grueso…): agrupa rendimientos entre obras.</summary>
    public string? Rubro { get; set; }
    public string Titulo { get; set; } = "";
    public string? Descripcion { get; set; }
    /// <summary>m², m³, ml, u… (null = se mide en %).</summary>
    public string? Unidad { get; set; }
    public decimal? CantidadObjetivo { get; set; }
    /// <summary>Suma de lo informado en los avances no rechazados (lo recalcula el servidor).</summary>
    public decimal CantidadHecha { get; set; }
    public int Porcentaje { get; set; }
    /// <summary>Solo lo validado por el capataz o el estudio: es lo que cuenta para rendimientos y certificados.</summary>
    public decimal CantidadAprobada { get; set; }
    public int PorcentajeAprobado { get; set; }
    public EstadoTarea Estado { get; set; } = EstadoTarea.Pendiente;
    /// <summary>Plan: inicio previsto.</summary>
    public DateOnly? FechaInicioPlan { get; set; }
    /// <summary>Plan: fin previsto.</summary>
    public DateOnly? FechaLimite { get; set; }
    /// <summary>Incidencia en el avance de la obra (para la curva y los certificados). Sin cargar = todas pesan igual.</summary>
    public decimal? Peso { get; set; }
    /// <summary>Por qué se atrasó (lo anota el capataz en su app o lo carga el estudio).</summary>
    public CausaAtraso? CausaAtraso { get; set; }
    public string? DetalleAtraso { get; set; }
    public DateTime? AtrasoInformadoEn { get; set; }
    public DateTime? CompletadaEn { get; set; }
    public DateTime CreadoEn { get; set; }
    public DateTime ActualizadoEn { get; set; }
}

/// <summary>Entrada y salida del albañil en una obra, con la ubicación del teléfono.</summary>
public class JornadaAlbanil
{
    public int Id { get; set; }
    public int AlbanilId { get; set; }
    public Albanil Albanil { get; set; } = null!;
    public int ObraId { get; set; }
    public Obra Obra { get; set; } = null!;
    /// <summary>Capataz del albañil al momento de fichar.</summary>
    public int? CapatazId { get; set; }
    public Capataz? Capataz { get; set; }
    /// <summary>Id de la entrada generado por el teléfono (la salida lo referencia).</summary>
    public Guid Ref { get; set; }
    public DateTime Entrada { get; set; }
    public double? EntradaLatitud { get; set; }
    public double? EntradaLongitud { get; set; }
    public double? EntradaPrecision { get; set; }
    public DateTime? Salida { get; set; }
    public double? SalidaLatitud { get; set; }
    public double? SalidaLongitud { get; set; }
    public double? SalidaPrecision { get; set; }
    /// <summary>Distancia a la obra al fichar (si la obra tiene ubicación cargada).</summary>
    public int? EntradaDistanciaMetros { get; set; }
    public int? SalidaDistanciaMetros { get; set; }

    /// <summary>Control de elementos de protección al entrar.</summary>
    public bool? TrabajoEnAltura { get; set; }
    /// <summary>Lo que le faltaba (separado por coma). Null = no se hizo el control; vacío = tenía todo.</summary>
    public string? EppFaltante { get; set; }

    /// <summary>Valor hora del albañil al fichar la entrada (null = no tenía cargado; se usa el actual).</summary>
    public decimal? ValorHora { get; set; }

    public double? Horas => Salida is null ? null : Math.Round((Salida.Value - Entrada).TotalHours, 2);
}

/// <summary>Llegada de material a la obra (con o sin pedido previo). La foto del remito va en su entrada de bitácora.</summary>
public class RecepcionMaterial
{
    public int Id { get; set; }
    public Guid Ref { get; set; }
    public int ObraId { get; set; }
    public Obra Obra { get; set; } = null!;
    public int? PedidoMaterialId { get; set; }
    public PedidoMaterial? Pedido { get; set; }
    public int? AlbanilId { get; set; }
    public Albanil? Albanil { get; set; }
    public int? CapatazId { get; set; }
    public Capataz? Capataz { get; set; }
    public int? RegistroAvanceId { get; set; }
    public RegistroAvance? Registro { get; set; }
    public DateTime Fecha { get; set; }
    public string? Proveedor { get; set; }
    public string? NumeroRemito { get; set; }
    public string? Nota { get; set; }
    /// <summary>Factura del proveedor (la carga el estudio con los precios).</summary>
    public string? NumeroFactura { get; set; }
    public List<RecepcionItem> Items { get; set; } = [];
}

public class RecepcionItem
{
    public int Id { get; set; }
    public int RecepcionMaterialId { get; set; }
    public RecepcionMaterial Recepcion { get; set; } = null!;
    /// <summary>Renglón del pedido al que corresponde (null = material que no estaba pedido).</summary>
    public int? PedidoMaterialItemId { get; set; }
    public PedidoMaterialItem? PedidoItem { get; set; }
    public string Material { get; set; } = "";
    public decimal Cantidad { get; set; }
    public string Unidad { get; set; } = "";
    /// <summary>Precio por unidad con IVA, de la factura (null = todavía no se cargó).</summary>
    public decimal? PrecioUnitario { get; set; }
}

/// <summary>Punto a controlar en una etapa antes de seguir o tapar (ej. «Armadura según plano»).</summary>
public class ControlEtapa
{
    public int Id { get; set; }
    public int EtapaObraId { get; set; }
    public EtapaObra Etapa { get; set; } = null!;
    public int Orden { get; set; }
    public string Texto { get; set; } = "";
    public bool RequiereFoto { get; set; } = true;
    public List<RespuestaControl> Respuestas { get; set; } = [];
}

/// <summary>Resultado de un control cargado desde la obra. Vale el más reciente.</summary>
public class RespuestaControl
{
    public int Id { get; set; }
    public Guid Ref { get; set; }
    public int ControlEtapaId { get; set; }
    public ControlEtapa Control { get; set; } = null!;
    public int? AlbanilId { get; set; }
    public Albanil? Albanil { get; set; }
    public int? CapatazId { get; set; }
    public Capataz? Capataz { get; set; }
    public int? RegistroAvanceId { get; set; }
    public RegistroAvance? Registro { get; set; }
    public DateTime Fecha { get; set; }
    public ResultadoControl Resultado { get; set; }
    public string? Nota { get; set; }
}

/// <summary>Libro de obra digital: órdenes de servicio del estudio y notas de pedido de la obra, numeradas por obra.</summary>
public class AsientoLibro
{
    public int Id { get; set; }
    public int ObraId { get; set; }
    public Obra Obra { get; set; } = null!;
    public TipoAsiento Tipo { get; set; }
    /// <summary>Correlativo por obra y tipo (Orden de servicio N° 3).</summary>
    public int Numero { get; set; }
    public DateTime Fecha { get; set; }
    public string Texto { get; set; } = "";
    /// <summary>Autor de una nota de pedido (capataz o albañil). En órdenes de servicio es el estudio.</summary>
    public int? CapatazId { get; set; }
    public Capataz? Capataz { get; set; }
    public int? AlbanilId { get; set; }
    public Albanil? Albanil { get; set; }
    /// <summary>Id generado en el teléfono (notas de pedido de la app).</summary>
    public Guid? Ref { get; set; }
    /// <summary>Fotos de la nota de pedido (entrada de bitácora con el mismo Ref).</summary>
    public int? RegistroAvanceId { get; set; }
    public RegistroAvance? Registro { get; set; }
    /// <summary>Orden de servicio que contesta una nota de pedido.</summary>
    public int? RespondeAId { get; set; }
    public AsientoLibro? RespondeA { get; set; }
    public List<LecturaAsiento> Lecturas { get; set; } = [];
}

/// <summary>Quién leyó una orden de servicio (y cuándo).</summary>
public class LecturaAsiento
{
    public int Id { get; set; }
    public int AsientoLibroId { get; set; }
    public AsientoLibro Asiento { get; set; } = null!;
    public int? CapatazId { get; set; }
    public Capataz? Capataz { get; set; }
    public int? AlbanilId { get; set; }
    public Albanil? Albanil { get; set; }
    public DateTime LeidoEn { get; set; }
}

/// <summary>Clima de un día en la obra (Open-Meteo), para explicar atrasos.</summary>
public class ClimaDiario
{
    public int Id { get; set; }
    public int ObraId { get; set; }
    public Obra Obra { get; set; } = null!;
    public DateOnly Fecha { get; set; }
    public double? TempMax { get; set; }
    public double? TempMin { get; set; }
    public double? LluviaMm { get; set; }
    public double? VientoMaxKmh { get; set; }
    public int? CodigoTiempo { get; set; }
    public DateTime ActualizadoEn { get; set; }

    /// <summary>Día perdido o complicado para trabajar afuera.</summary>
    public bool Desfavorable => LluviaMm >= 5 || VientoMaxKmh >= 50 || CodigoTiempo is >= 95;
}

/// <summary>Encuadre fijo de la obra que se fotografía cada tanto (siempre igual) para ver la evolución.</summary>
public class PuntoFoto
{
    public int Id { get; set; }
    public int ObraId { get; set; }
    public Obra Obra { get; set; } = null!;
    public int Orden { get; set; }
    public string Nombre { get; set; } = "";
    /// <summary>Cómo pararse para sacarla (ej. «desde la vereda de enfrente, mirando la fachada»).</summary>
    public string? Indicacion { get; set; }
    public int FrecuenciaDias { get; set; } = 7;
    public bool Activo { get; set; } = true;
    /// <summary>Sus fotos se muestran en el portal del cliente.</summary>
    public bool VisibleCliente { get; set; } = true;
}

/// <summary>Certificado de avance de obra: % físico aprobado × monto de la obra, para cobrar por etapas.</summary>
public class Certificado
{
    public int Id { get; set; }
    public int ObraId { get; set; }
    public Obra Obra { get; set; } = null!;
    public int Numero { get; set; }
    public DateOnly Fecha { get; set; }
    public decimal PorcentajeAnterior { get; set; }
    public decimal PorcentajeAcumulado { get; set; }
    public decimal MontoObra { get; set; }
    public decimal Monto { get; set; }
    public EstadoCertificado Estado { get; set; } = EstadoCertificado.Borrador;
    /// <summary>JSON con el detalle por tarea tal como se emitió (no cambia después).</summary>
    public string Detalle { get; set; } = "[]";
    public string? Notas { get; set; }
    public int? PagoId { get; set; }
    public Pago? Pago { get; set; }
    public DateTime CreadoEn { get; set; }
}

/// <summary>Pedido de materiales hecho desde la obra.</summary>
public class PedidoMaterial
{
    public int Id { get; set; }
    public Guid Ref { get; set; }
    public int ObraId { get; set; }
    public Obra Obra { get; set; } = null!;
    public int AlbanilId { get; set; }
    public Albanil Albanil { get; set; } = null!;
    public int? CapatazId { get; set; }
    public Capataz? Capataz { get; set; }
    public int? TareaObraId { get; set; }
    public TareaObra? Tarea { get; set; }
    public DateTime Fecha { get; set; }
    public bool Urgente { get; set; }
    public string? Nota { get; set; }
    public EstadoPedido Estado { get; set; } = EstadoPedido.Pendiente;
    /// <summary>Lo que contesta el estudio (ej. «llega el jueves»).</summary>
    public string? Respuesta { get; set; }
    public DateTime ActualizadoEn { get; set; }
    public List<PedidoMaterialItem> Items { get; set; } = [];
}

public class PedidoMaterialItem
{
    public int Id { get; set; }
    public int PedidoMaterialId { get; set; }
    public PedidoMaterial Pedido { get; set; } = null!;
    public string Material { get; set; } = "";
    public decimal Cantidad { get; set; }
    public string Unidad { get; set; } = "";
}

/// <summary>Problema o incidente avisado desde la obra. Las fotos van en su entrada de bitácora.</summary>
public class ProblemaObra
{
    public int Id { get; set; }
    public Guid Ref { get; set; }
    public int ObraId { get; set; }
    public Obra Obra { get; set; } = null!;
    public int AlbanilId { get; set; }
    public Albanil Albanil { get; set; } = null!;
    public int? CapatazId { get; set; }
    public Capataz? Capataz { get; set; }
    public int? TareaObraId { get; set; }
    public TareaObra? Tarea { get; set; }
    /// <summary>Entrada de bitácora (mismo Ref) donde quedan las fotos y videos del problema.</summary>
    public int? RegistroAvanceId { get; set; }
    public RegistroAvance? Registro { get; set; }
    public TipoProblema Tipo { get; set; }
    public UrgenciaProblema Urgencia { get; set; }
    public string Descripcion { get; set; } = "";
    public EstadoProblema Estado { get; set; } = EstadoProblema.Abierto;
    public string? Respuesta { get; set; }
    public DateTime Fecha { get; set; }
    public DateTime? ResueltoEn { get; set; }
}

/// <summary>Resumen del día de un albañil en una obra: qué hizo y cuántas horas por tarea. Uno por día y obra.</summary>
public class ParteDiario
{
    public int Id { get; set; }
    public int AlbanilId { get; set; }
    public Albanil Albanil { get; set; } = null!;
    public int ObraId { get; set; }
    public Obra Obra { get; set; } = null!;
    public int? CapatazId { get; set; }
    public Capataz? Capataz { get; set; }
    /// <summary>Día (hora de Argentina).</summary>
    public DateOnly Fecha { get; set; }
    public string? Resumen { get; set; }
    public decimal HorasTotales { get; set; }
    public DateTime CreadoEn { get; set; }
    public DateTime ActualizadoEn { get; set; }
    public List<ParteDiarioItem> Items { get; set; } = [];
}

public class ParteDiarioItem
{
    public int Id { get; set; }
    public int ParteDiarioId { get; set; }
    public ParteDiario Parte { get; set; } = null!;
    public int? TareaObraId { get; set; }
    public TareaObra? Tarea { get; set; }
    public decimal Horas { get; set; }
    public string? Detalle { get; set; }
}

public class Pago
{
    public int Id { get; set; }
    public int ObraId { get; set; }
    public Obra Obra { get; set; } = null!;
    public DateOnly Fecha { get; set; }
    public decimal Monto { get; set; }
    public string? Concepto { get; set; }
    public MedioPago Medio { get; set; }
}

/// <summary>
/// Una versión de la escala arancelaria del Colegio (cambia cada semestre). El detalle completo
/// (escalas, tabla de uso y destino, valores fijos) va como JSON en <see cref="Contenido"/>.
/// </summary>
public class ArancelVersion
{
    public int Id { get; set; }
    public string Nombre { get; set; } = "";
    public DateOnly VigenteDesde { get; set; }
    public DateOnly VigenteHasta { get; set; }
    public decimal ValorBasico { get; set; }
    /// <summary>JSON de <c>Presupuestos.Arancel</c>.</summary>
    public string Contenido { get; set; } = "";
    public string? Notas { get; set; }
    public DateTime CreadoEn { get; set; }
    public DateTime ActualizadoEn { get; set; }
}

/// <summary>Presupuesto de honorarios emitido a un cliente (o a un interesado que todavía no es cliente).</summary>
public class Presupuesto
{
    public int Id { get; set; }
    /// <summary>Número legible, ej. PR-2026-004.</summary>
    public string Numero { get; set; } = "";
    public string Titulo { get; set; } = "";
    public TipoServicio Servicio { get; set; }
    public EstadoPresupuesto Estado { get; set; } = EstadoPresupuesto.Borrador;
    public DateOnly Fecha { get; set; }
    public int ValidezDias { get; set; } = 15;
    /// <summary>Obligatorio al marcarlo rechazado: por qué se perdió.</summary>
    public MotivoPerdida? MotivoPerdida { get; set; }
    public string? DetallePerdida { get; set; }
    /// <summary>Cuándo pasó a aceptado o rechazado (tiempo de decisión del cliente).</summary>
    public DateTime? DecididoEn { get; set; }

    public int? ClienteId { get; set; }
    public Cliente? Cliente { get; set; }
    /// <summary>Nombre a quien va dirigido cuando todavía no hay cliente cargado.</summary>
    public string? Destinatario { get; set; }
    public string? Ubicacion { get; set; }

    public int ArancelVersionId { get; set; }
    public ArancelVersion ArancelVersion { get; set; } = null!;

    public decimal SuperficieTotal { get; set; }
    public decimal MontoObra { get; set; }
    public decimal Total { get; set; }
    /// <summary>JSON de <c>Presupuestos.PresupuestoEntrada</c> para poder reabrirlo y recalcularlo.</summary>
    public string Parametros { get; set; } = "";
    public string? Notas { get; set; }
    public DateTime CreadoEn { get; set; }
    public DateTime ActualizadoEn { get; set; }

    /// <summary>Obra creada a partir de este presupuesto (al aceptarse).</summary>
    public int? ObraId { get; set; }
    public Obra? Obra { get; set; }

    /// <summary>Renglones tal como se emitieron (no cambian si después se edita el arancel).</summary>
    public List<PresupuestoItem> Items { get; set; } = [];

    public DateOnly VenceEl => Fecha.AddDays(ValidezDias);
    public string NombreDestinatario => Cliente?.Nombre ?? Destinatario ?? "—";
}

public class PresupuestoItem
{
    public int Id { get; set; }
    public int PresupuestoId { get; set; }
    public Presupuesto Presupuesto { get; set; } = null!;
    public int Orden { get; set; }
    public string Seccion { get; set; } = "";
    public string Concepto { get; set; } = "";
    public string? Detalle { get; set; }
    public decimal Importe { get; set; }
}

/// <summary>Pieza de contenido para redes (la genera el agente CM o se carga a mano).</summary>
public class Publicacion
{
    public int Id { get; set; }
    public string Titulo { get; set; } = "";
    public RedSocial Red { get; set; }
    public FormatoPublicacion Formato { get; set; }
    public PilarContenido Pilar { get; set; }
    /// <summary>Guion, slides, captions y hashtags en Markdown.</summary>
    public string Contenido { get; set; } = "";
    public EstadoPublicacion Estado { get; set; } = EstadoPublicacion.Borrador;
    public DateOnly? FechaProgramada { get; set; }
    public bool GeneradaPorIA { get; set; }
    public DateTime CreadoEn { get; set; }

    public int? ObraId { get; set; }
    public Obra? Obra { get; set; }
}

/// <summary>Credencial del administrador cambiada desde el panel. Si existe, reemplaza a Admin:PasswordHash de la configuración.</summary>
public class CredencialAdmin
{
    public int Id { get; set; }
    public string Usuario { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    /// <summary>Cambia con cada contraseña nueva: invalida las sesiones abiertas con la anterior.</summary>
    public string Sello { get; set; } = "";
    public DateTime ActualizadoEn { get; set; }
}

// ---- App del cliente (/mi-obra) ----

/// <summary>Acceso del cliente a la app de su obra: código que manda el estudio + PIN. Uno por obra.</summary>
public class AccesoCliente
{
    public int Id { get; set; }
    public int ObraId { get; set; }
    public Obra Obra { get; set; } = null!;
    /// <summary>Código de obra para el cliente, ej. DEEP-7K3M-Q9TX (sin letras confusas).</summary>
    public string Codigo { get; set; } = "";
    public string PinHash { get; set; } = "";
    /// <summary>Cambia al regenerar el PIN o desactivar: las sesiones abiertas dejan de valer.</summary>
    public string Sello { get; set; } = "";
    public bool Activo { get; set; } = true;
    public int IntentosFallidos { get; set; }
    public DateTime? BloqueadoHasta { get; set; }
    public DateTime? UltimoIngreso { get; set; }
    public DateTime CreadoEn { get; set; }
}

/// <summary>Plano, contrato, presupuesto u otro archivo de la obra que el estudio comparte con el cliente.</summary>
public class DocumentoObra
{
    public int Id { get; set; }
    public int ObraId { get; set; }
    public Obra Obra { get; set; } = null!;
    public string Titulo { get; set; } = "";
    public CategoriaDocumento Categoria { get; set; }
    public string Ruta { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long Tamano { get; set; }
    public string? NombreOriginal { get; set; }
    public bool VisibleCliente { get; set; } = true;
    public DateTime CreadoEn { get; set; }
}

/// <summary>Mensaje entre el cliente y el estudio sobre una obra.</summary>
public class MensajeObra
{
    public int Id { get; set; }
    public int ObraId { get; set; }
    public Obra Obra { get; set; } = null!;
    public bool DelCliente { get; set; }
    /// <summary>Nombre que pone el cliente (pueden entrar varias personas de la familia) o del estudio.</summary>
    public string? Autor { get; set; }
    public string Texto { get; set; } = "";
    public DateTime Fecha { get; set; }
    /// <summary>Cuándo lo leyó la otra parte.</summary>
    public DateTime? LeidoEn { get; set; }
    /// <summary>Id generado en el formulario: si el cliente toca dos veces «Enviar» no se duplica.</summary>
    public Guid? Ref { get; set; }
}

/// <summary>Decisión que el estudio le pide al cliente (elegir una terminación, aprobar un adicional).</summary>
public class AprobacionCliente
{
    public int Id { get; set; }
    public int ObraId { get; set; }
    public Obra Obra { get; set; } = null!;
    public string Titulo { get; set; } = "";
    public string? Descripcion { get; set; }
    /// <summary>Opciones para elegir, una por línea. Vacío = aprobar o rechazar.</summary>
    public string? Opciones { get; set; }
    /// <summary>Costo adicional que implica (si corresponde).</summary>
    public decimal? Monto { get; set; }
    public DateOnly? FechaLimite { get; set; }
    public int? DocumentoObraId { get; set; }
    public DocumentoObra? Documento { get; set; }
    public EstadoAprobacion Estado { get; set; } = EstadoAprobacion.Pendiente;
    public string? OpcionElegida { get; set; }
    public string? Comentario { get; set; }
    /// <summary>Nombre que escribió quien respondió (queda como constancia).</summary>
    public string? RespondidoPor { get; set; }
    public DateTime? RespondidoEn { get; set; }
    public string? RespondidoDesdeIp { get; set; }
    /// <summary>El estudio ya vio la respuesta.</summary>
    public bool RespuestaVista { get; set; }
    public DateTime CreadoEn { get; set; }

    public IReadOnlyList<string> ListaOpciones =>
        (Opciones ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}