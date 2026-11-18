namespace DeepArquitectura.Web.Data.Entities;

public enum OrigenCliente
{
    Web = 0,
    WhatsApp = 1,
    Instagram = 2,
    Facebook = 3,
    TikTok = 4,
    Referido = 5,
    /// <summary>Buscador (Google, Bing…).</summary>
    Buscador = 6,
    /// <summary>Cartel de obra, volante, código QR.</summary>
    CartelObra = 7,
    Otro = 99,
}

/// <summary>Página del sitio desde la que llegó la consulta.</summary>
public enum AreaConsulta
{
    General = 0,
    Electricidad = 1,
    Gas = 2,
}

public enum EstadoConsulta
{
    Nueva = 0,
    Contactada = 1,
    Convertida = 2,
    Descartada = 3,
}

public enum TipoServicio
{
    PlanosMunicipales = 0,
    Relevamiento = 1,
    DisenoDeObra = 2,
    Electricidad = 3,
    Gas = 4,
}

public enum EstadoObra
{
    Presupuestada = 0,
    EnCurso = 1,
    EnTramiteMunicipal = 2,
    Pausada = 3,
    Finalizada = 4,
    Cancelada = 5,
}

public enum EstadoEtapa
{
    Pendiente = 0,
    EnCurso = 1,
    Completada = 2,
    Omitida = 3,
}

public enum MedioPago
{
    Transferencia = 0,
    Efectivo = 1,
    Otro = 99,
}

public enum RedSocial
{
    Instagram = 0,
    TikTok = 1,
    Facebook = 2,
    Multired = 3,
}

public enum FormatoPublicacion
{
    Reel = 0,
    Carrusel = 1,
    Post = 2,
    Historia = 3,
    VideoTikTok = 4,
}

public enum PilarContenido
{
    Educativo = 0,
    Obra = 1,
    Seguridad = 2,
    DetrasDeEscena = 3,
    PruebaSocial = 4,
}

public enum EstadoPresupuesto
{
    Borrador = 0,
    Enviado = 1,
    Aceptado = 2,
    Rechazado = 3,
}

public enum EstadoPublicacion
{
    Borrador = 0,
    Aprobada = 1,
    Publicada = 2,
    Descartada = 3,
}

public enum TipoMedio
{
    Foto = 0,
    Video = 1,
}

public enum EstadoTarea
{
    Pendiente = 0,
    EnCurso = 1,
    Completada = 2,
    Cancelada = 3,
}

public enum EstadoPedido
{
    Pendiente = 0,
    Aprobado = 1,
    Rechazado = 2,
    Entregado = 3,
    /// <summary>Llegó una parte: falta material (se avisó como problema).</summary>
    EntregaParcial = 4,
}

/// <summary>Control del capataz (o del estudio) sobre un avance informado por un albañil.</summary>
public enum EstadoValidacion
{
    Pendiente = 0,
    Aprobado = 1,
    /// <summary>Aprobado con otra cantidad o porcentaje.</summary>
    Corregido = 2,
    Rechazado = 3,
}

public enum ResultadoControl
{
    Ok = 0,
    /// <summary>No está bien: hay que corregir antes de tapar / seguir.</summary>
    Observado = 1,
}

public enum TipoAsiento
{
    /// <summary>Del estudio (dirección de obra) a la obra.</summary>
    OrdenServicio = 0,
    /// <summary>De la obra (capataz o cuadrilla) al estudio.</summary>
    NotaPedido = 1,
}

public enum EstadoCertificado
{
    Borrador = 0,
    Emitido = 1,
    Cobrado = 2,
}

public enum TipoProblema
{
    FaltaMaterial = 0,
    ErrorPlano = 1,
    Seguridad = 2,
    Accidente = 3,
    Clima = 4,
    Herramientas = 5,
    Otro = 99,
}

public enum UrgenciaProblema
{
    Baja = 0,
    Media = 1,
    Alta = 2,
}

public enum EstadoProblema
{
    Abierto = 0,
    EnRevision = 1,
    Resuelto = 2,
}

public enum CategoriaDocumento
{
    Plano = 0,
    Contrato = 1,
    Presupuesto = 2,
    Permiso = 3,
    Factura = 4,
    Otro = 99,
}

public enum EstadoAprobacion
{
    Pendiente = 0,
    Aprobada = 1,
    Rechazada = 2,
    /// <summary>El estudio la retiró antes de que el cliente respondiera.</summary>
    Anulada = 3,
}

// ---------------- Datos del proceso (comercial, costos, cierre y postventa) ----------------

/// <summary>Por qué no avanzó un presupuesto o una consulta.</summary>
public enum MotivoPerdida
{
    Precio = 0,
    Plazo = 1,
    EligioOtro = 2,
    SinRespuesta = 3,
    NoHaceLaObra = 4,
    FueraDeAlcance = 5,
    Otro = 99,
}

public enum CategoriaGasto
{
    /// <summary>Compras sin remito en el sistema (ferretería, corralón al paso). Lo que entra con remito va con precio en la recepción.</summary>
    Material = 0,
    Subcontrato = 1,
    /// <summary>Jornales pagados por fuera de las fichadas (changas, personal sin app).</summary>
    ManoDeObra = 2,
    Flete = 3,
    AlquilerEquipos = 4,
    Herramientas = 5,
    Tramites = 6,
    Otro = 99,
}

public enum CausaAtraso
{
    Clima = 0,
    FaltaMaterial = 1,
    CambioCliente = 2,
    FaltaPersonal = 3,
    Retrabajo = 4,
    /// <summary>Falta una definición del estudio (plano, detalle, elección).</summary>
    DefinicionPendiente = 5,
    Tramite = 6,
    PagoCliente = 7,
    Otro = 99,
}

public enum EstadoTramite
{
    Preparando = 0,
    Presentado = 1,
    Observado = 2,
    Aprobado = 3,
    Rechazado = 4,
}

public enum DestinoSobrante
{
    DevueltoProveedor = 0,
    OtraObra = 1,
    Deposito = 2,
    Desperdicio = 3,
}

public enum EstadoActa
{
    /// <summary>El estudio está cargando las observaciones de la recorrida: el cliente no la ve.</summary>
    Borrador = 0,
    /// <summary>Publicada: el cliente ve las observaciones y puede firmar.</summary>
    Abierta = 1,
    Firmada = 2,
}

public enum EstadoReclamo
{
    Nuevo = 0,
    EnRevision = 1,
    Programado = 2,
    Resuelto = 3,
    NoCorresponde = 4,
}

/// <summary>Causa raíz de un reclamo (la decide el estudio al revisarlo).</summary>
public enum CausaReclamo
{
    ManoDeObra = 0,
    Material = 1,
    Diseno = 2,
    UsoDelCliente = 3,
    DesgasteNormal = 4,
    Otro = 99,
}

public enum MomentoEncuesta
{
    FinDeEtapa = 0,
    Entrega = 1,
}

public enum EstadoPagoInformado
{
    Pendiente = 0,
    Confirmado = 1,
    Rechazado = 2,
}