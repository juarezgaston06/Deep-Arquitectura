using DeepArquitectura.Web.Data.Entities;

namespace DeepArquitectura.Web.Services;

/// <summary>Etiquetas legibles para enums y formatos de fecha/moneda en es-AR.</summary>
public static class Textos
{
    private static readonly System.Globalization.CultureInfo EsAr = new("es-AR");

    private static readonly TimeZoneInfo ZonaArgentina =
        TimeZoneInfo.TryFindSystemTimeZoneById("America/Argentina/Cordoba", out var tz) ? tz
        : TimeZoneInfo.TryFindSystemTimeZoneById("Argentina Standard Time", out tz) ? tz
        : TimeZoneInfo.Utc;

    public static DateTime AHoraLocal(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), ZonaArgentina);

    public static DateOnly HoyLocal() => DateOnly.FromDateTime(AHoraLocal(DateTime.UtcNow));

    public static string Fecha(DateTime utc) => AHoraLocal(utc).ToString("dd/MM/yyyy HH:mm", EsAr);
    /// <summary>Solo el día (hora de Argentina), sin la hora.</summary>
    public static string Dia(DateTime utc) => AHoraLocal(utc).ToString("dd/MM/yyyy", EsAr);
    public static string Fecha(DateOnly? d) => d?.ToString("dd/MM/yyyy", EsAr) ?? "—";
    public static string Moneda(decimal? v) => v is null ? "—" : v.Value.ToString("C0", EsAr);
    public static string Numero(decimal v) => v.ToString("#,0.##", EsAr);

    public static string Etiqueta(Enum valor) => valor switch
    {
        TipoServicio.PlanosMunicipales => "Planos municipales",
        TipoServicio.DisenoDeObra => "Diseño de obra",
        EstadoObra.EnCurso => "En curso",
        EstadoObra.EnTramiteMunicipal => "En trámite municipal",
        EstadoEtapa.EnCurso => "En curso",
        PilarContenido.DetrasDeEscena => "Detrás de escena",
        PilarContenido.PruebaSocial => "Prueba social",
        FormatoPublicacion.VideoTikTok => "Video TikTok",
        EstadoTarea.EnCurso => "En curso",
        EstadoProblema.EnRevision => "En revisión",
        TipoProblema.FaltaMaterial => "Falta material",
        TipoProblema.ErrorPlano => "Error o duda en plano",
        TipoProblema.Clima => "Clima / lluvia",
        TipoProblema.Herramientas => "Herramientas",
        CategoriaDocumento.Plano => "Planos",
        CategoriaDocumento.Contrato => "Contrato",
        CategoriaDocumento.Presupuesto => "Presupuestos",
        CategoriaDocumento.Permiso => "Permisos y trámites",
        CategoriaDocumento.Factura => "Facturas y recibos",
        CategoriaDocumento.Otro => "Otros",
        EstadoAprobacion.Pendiente => "Por responder",
        EstadoCertificado.Emitido => "A pagar",
        EstadoCertificado.Cobrado => "Pagado",
        OrigenCliente.Web => "Sitio (directo)",
        OrigenCliente.Referido => "Recomendación",
        OrigenCliente.Buscador => "Google / buscador",
        OrigenCliente.CartelObra => "Cartel de obra / QR",
        MotivoPerdida.Precio => "Precio",
        MotivoPerdida.Plazo => "Plazo",
        MotivoPerdida.EligioOtro => "Eligió a otro profesional",
        MotivoPerdida.SinRespuesta => "No respondió",
        MotivoPerdida.NoHaceLaObra => "Ya no hace la obra",
        MotivoPerdida.FueraDeAlcance => "No es un trabajo nuestro",
        MotivoPerdida.Otro => "Otro",
        CategoriaGasto.Material => "Material (sin remito)",
        CategoriaGasto.ManoDeObra => "Mano de obra (fuera de fichadas)",
        CategoriaGasto.AlquilerEquipos => "Alquiler de equipos",
        CategoriaGasto.Tramites => "Trámites y tasas",
        CausaAtraso.Clima => "Clima",
        CausaAtraso.FaltaMaterial => "Falta de material",
        CausaAtraso.CambioCliente => "Cambio del cliente",
        CausaAtraso.FaltaPersonal => "Falta de personal",
        CausaAtraso.Retrabajo => "Retrabajo (hubo que rehacer)",
        CausaAtraso.DefinicionPendiente => "Falta una definición del estudio",
        CausaAtraso.Tramite => "Trámite / inspección",
        CausaAtraso.PagoCliente => "Atraso de pago del cliente",
        EstadoTramite.Preparando => "Preparando",
        EstadoTramite.Presentado => "Presentado",
        EstadoTramite.Observado => "Observado (hay que corregir)",
        DestinoSobrante.DevueltoProveedor => "Devuelto al proveedor",
        DestinoSobrante.OtraObra => "Llevado a otra obra",
        DestinoSobrante.Deposito => "Al depósito",
        DestinoSobrante.Desperdicio => "Desperdicio",
        EstadoActa.Borrador => "Borrador (el cliente no la ve)",
        EstadoActa.Abierta => "Publicada, falta la firma",
        EstadoActa.Firmada => "Firmada",
        EstadoReclamo.Nuevo => "Recibido",
        EstadoReclamo.EnRevision => "En revisión",
        EstadoReclamo.Programado => "Visita programada",
        EstadoReclamo.Resuelto => "Resuelto",
        EstadoReclamo.NoCorresponde => "No corresponde a garantía",
        CausaReclamo.ManoDeObra => "Mano de obra",
        CausaReclamo.Diseno => "Diseño / proyecto",
        CausaReclamo.UsoDelCliente => "Uso del cliente",
        CausaReclamo.DesgasteNormal => "Desgaste normal",
        MomentoEncuesta.FinDeEtapa => "Fin de etapa",
        EstadoPagoInformado.Pendiente => "A confirmar",
        _ => valor.ToString(),
    };

    /// <summary>Duración corta y legible: «35 min», «5 h», «3 días».</summary>
    public static string Demora(TimeSpan t) =>
        t.TotalMinutes < 60 ? $"{Math.Max(0, (int)t.TotalMinutes)} min"
        : t.TotalHours < 48 ? $"{(int)t.TotalHours} h"
        : $"{(int)t.TotalDays} días";

    /// <summary>Clase CSS para el "chip" de estado en el panel.</summary>
    public static string ClaseEstado(Enum valor) => valor switch
    {
        EstadoObra.EnCurso or EstadoObra.EnTramiteMunicipal or EstadoEtapa.EnCurso or EstadoConsulta.Contactada => "chip chip-activo",
        EstadoObra.Finalizada or EstadoEtapa.Completada or EstadoConsulta.Convertida or EstadoPublicacion.Publicada => "chip chip-ok",
        EstadoObra.Cancelada or EstadoConsulta.Descartada or EstadoPublicacion.Descartada or EstadoEtapa.Omitida => "chip chip-apagado",
        EstadoConsulta.Nueva or EstadoObra.Pausada => "chip chip-alerta",
        EstadoPresupuesto.Enviado => "chip chip-activo",
        EstadoPresupuesto.Aceptado => "chip chip-ok",
        EstadoPresupuesto.Rechazado => "chip chip-apagado",
        EstadoTarea.EnCurso or EstadoPedido.Aprobado or EstadoProblema.EnRevision => "chip chip-activo",
        EstadoTarea.Completada or EstadoPedido.Entregado or EstadoProblema.Resuelto => "chip chip-ok",
        EstadoTarea.Cancelada or EstadoPedido.Rechazado => "chip chip-apagado",
        EstadoPedido.Pendiente or EstadoProblema.Abierto or UrgenciaProblema.Alta => "chip chip-alerta",
        EstadoAprobacion.Pendiente => "chip chip-alerta",
        EstadoAprobacion.Aprobada => "chip chip-ok",
        EstadoAprobacion.Rechazada => "chip chip-activo",
        EstadoAprobacion.Anulada => "chip chip-apagado",
        EstadoTramite.Presentado => "chip chip-activo",
        EstadoTramite.Observado or EstadoTramite.Rechazado => "chip chip-alerta",
        EstadoTramite.Aprobado => "chip chip-ok",
        EstadoActa.Abierta => "chip chip-activo",
        EstadoActa.Firmada => "chip chip-ok",
        EstadoReclamo.Nuevo => "chip chip-alerta",
        EstadoReclamo.EnRevision or EstadoReclamo.Programado => "chip chip-activo",
        EstadoReclamo.Resuelto => "chip chip-ok",
        EstadoReclamo.NoCorresponde => "chip chip-apagado",
        EstadoPagoInformado.Pendiente => "chip chip-alerta",
        EstadoPagoInformado.Confirmado => "chip chip-ok",
        EstadoPagoInformado.Rechazado => "chip chip-apagado",
        _ => "chip",
    };
}
