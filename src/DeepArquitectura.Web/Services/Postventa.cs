using DeepArquitectura.Web.Data;
using DeepArquitectura.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace DeepArquitectura.Web.Services;

/// <summary>
/// Cierre y después de la entrega: acta de entrega con observaciones y firma del cliente, reclamos de garantía,
/// encuestas, permiso de difusión y pagos que informa el cliente. Lo usan el panel y la app del cliente.
/// </summary>
public class Postventa(IDbContextFactory<AppDbContext> dbFactory, Adjuntos adjuntos, TimeProvider? reloj = null)
{
    private readonly TimeProvider _reloj = reloj ?? TimeProvider.System;
    private DateTime Ahora => _reloj.GetUtcNow().UtcDateTime;
    private DateOnly Hoy => DateOnly.FromDateTime(Textos.AHoraLocal(Ahora));

    public static readonly string[] Rubros =
    [
        "Humedad / filtraciones", "Fisuras", "Instalación eléctrica", "Instalación de gas", "Instalación sanitaria",
        "Aberturas / carpintería", "Pisos y revestimientos", "Pintura", "Techo / cubierta", "Otro",
    ];

    // ================================================================== 10. Acta de entrega

    public async Task<ActaEntrega?> ActaAsync(int obraId, bool paraCliente = false, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var acta = await db.Actas.AsNoTracking().Include(a => a.Observaciones).ThenInclude(o => o.Adjuntos)
            .SingleOrDefaultAsync(a => a.ObraId == obraId, ct);
        if (acta is not null) acta.Observaciones = acta.Observaciones.OrderBy(o => o.Resuelta).ThenBy(o => o.Id).ToList();
        return paraCliente && acta?.Estado == EstadoActa.Borrador ? null : acta;
    }

    public async Task<(int? Id, string? Error)> CrearActaAsync(int obraId, DateOnly? fechaRecorrida, int mesesGarantia = 12, CancellationToken ct = default)
    {
        if (mesesGarantia is < 0 or > 120) return (null, "Meses de garantía inválidos.");
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await db.Obras.AnyAsync(o => o.Id == obraId, ct)) return (null, "La obra no existe.");
        if (await db.Actas.Where(a => a.ObraId == obraId).Select(a => (int?)a.Id).SingleOrDefaultAsync(ct) is int existente) return (existente, null);
        var acta = new ActaEntrega { ObraId = obraId, FechaRecorrida = fechaRecorrida ?? Hoy, MesesGarantia = mesesGarantia };
        db.Actas.Add(acta);
        await db.SaveChangesAsync(ct);
        return (acta.Id, null);
    }

    public sealed class DatosObservacion
    {
        public string? Descripcion { get; set; }
        public string? Ubicacion { get; set; }
        public string? Rubro { get; set; }
        public string? Responsable { get; set; }
    }

    public async Task<string?> AgregarObservacionAsync(int actaId, DatosObservacion d, IReadOnlyCollection<Adjuntos.Archivo>? fotos = null, CancellationToken ct = default)
    {
        var desc = Relevamientos.Texto(d.Descripcion, 1000);
        if (desc is null) return "Describí la observación.";
        if (fotos is { Count: > 0 } && Adjuntos.Validar(fotos) is { } e) return e;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var acta = await db.Actas.SingleOrDefaultAsync(a => a.Id == actaId, ct);
        if (acta is null) return "El acta no existe.";
        if (acta.Estado == EstadoActa.Firmada) return "El acta ya está firmada.";
        var o = new ObservacionEntrega
        {
            ActaEntregaId = actaId, Descripcion = desc, Ubicacion = Relevamientos.Texto(d.Ubicacion, 150),
            Rubro = Relevamientos.Texto(d.Rubro, 80), Responsable = Relevamientos.Texto(d.Responsable, 150),
        };
        db.ObservacionesEntrega.Add(o);
        if (fotos is { Count: > 0 }) await adjuntos.GuardarAsync(db, acta.ObraId, "entrega", fotos, a => a.Observacion = o, ct: ct);
        await db.SaveChangesAsync(ct);
        return null;
    }

    /// <summary>Marca resuelta (con la foto del «después», opcional) o la vuelve a abrir.</summary>
    public async Task<string?> ResolverObservacionAsync(int observacionId, bool resuelta, string? nota, IReadOnlyCollection<Adjuntos.Archivo>? fotos = null, CancellationToken ct = default)
    {
        if (fotos is { Count: > 0 } && Adjuntos.Validar(fotos) is { } e) return e;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var o = await db.ObservacionesEntrega.Include(x => x.Acta).SingleOrDefaultAsync(x => x.Id == observacionId, ct);
        if (o is null) return "La observación no existe.";
        if (o.Acta.Estado == EstadoActa.Firmada && !resuelta) return "El acta ya está firmada.";
        o.Resuelta = resuelta;
        o.ResueltaEn = resuelta ? Ahora : null;
        o.NotaResolucion = resuelta ? Relevamientos.Texto(nota, 1000) : null;
        if (fotos is { Count: > 0 }) await adjuntos.GuardarAsync(db, o.Acta.ObraId, "entrega", fotos, a => a.ObservacionEntregaId = o.Id, esResolucion: true, ct);
        await db.SaveChangesAsync(ct);
        return null;
    }

    public async Task<string?> BorrarObservacionAsync(int observacionId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var o = await db.ObservacionesEntrega.Include(x => x.Acta).Include(x => x.Adjuntos).SingleOrDefaultAsync(x => x.Id == observacionId, ct);
        if (o is null) return null;
        if (o.Acta.Estado == EstadoActa.Firmada) return "El acta ya está firmada: queda como constancia.";
        var rutas = o.Adjuntos.Select(a => a.Ruta).ToList();
        db.Adjuntos.RemoveRange(o.Adjuntos);
        db.ObservacionesEntrega.Remove(o);
        await db.SaveChangesAsync(ct);
        await adjuntos.BorrarArchivosAsync(rutas, ct);
        return null;
    }

    /// <summary>El cliente pasa a ver el acta y puede firmarla.</summary>
    public async Task<string?> PublicarActaAsync(int actaId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var n = await db.Actas.Where(a => a.Id == actaId && a.Estado == EstadoActa.Borrador)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.Estado, EstadoActa.Abierta), ct);
        return n == 1 ? null : "El acta ya estaba publicada.";
    }

    /// <summary>
    /// Firma del cliente desde su app. Con observaciones pendientes solo puede firmar «con reservas» y explicando por qué.
    /// Una sola vez (atómico). Al firmar arranca la garantía y se le pide la encuesta de entrega.
    /// </summary>
    public async Task<string?> FirmarActaAsync(int obraId, string? nombre, bool conReservas, string? comentario, string? ip, CancellationToken ct = default)
    {
        nombre = Relevamientos.Texto(nombre, 120);
        comentario = Relevamientos.Texto(comentario, 2000);
        if (nombre is null) return "Escribí tu nombre completo para firmar.";
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var acta = await db.Actas.AsNoTracking().SingleOrDefaultAsync(a => a.ObraId == obraId, ct);
        if (acta is null || acta.Estado == EstadoActa.Borrador) return "El acta todavía no está lista.";
        if (acta.Estado == EstadoActa.Firmada) return "El acta ya está firmada.";
        var pendientes = await db.ObservacionesEntrega.CountAsync(o => o.ActaEntregaId == acta.Id && !o.Resuelta, ct);
        if (pendientes > 0 && !conReservas) return $"Quedan {pendientes} observación(es) sin resolver: podés esperar a que las resolvamos o firmar «con reservas».";
        if (conReservas && comentario is null) return "Contanos qué queda pendiente o qué no te conforma.";

        var ahora = Ahora;
        var reservas = conReservas;
        var n = await db.Actas.Where(a => a.Id == acta.Id && a.Estado == EstadoActa.Abierta)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.Estado, EstadoActa.Firmada).SetProperty(a => a.FirmadaEn, ahora)
                .SetProperty(a => a.FirmadaPor, nombre).SetProperty(a => a.FirmadaDesdeIp, Relevamientos.Texto(ip, 60))
                .SetProperty(a => a.ConReservas, reservas).SetProperty(a => a.ComentarioCliente, comentario), ct);
        if (n == 0) return "El acta ya está firmada.";
        await AsegurarEncuestaAsync(db, obraId, MomentoEncuesta.Entrega, null, "¿Cómo fue tu experiencia con la obra?", ct, exigirAcceso: false);
        return null;
    }

    /// <summary>Fecha hasta la que corre la garantía (null si no se firmó el acta).</summary>
    public static DateOnly? GarantiaHasta(ActaEntrega? a) =>
        a is { Estado: EstadoActa.Firmada, FirmadaEn: DateTime f } ? DateOnly.FromDateTime(Textos.AHoraLocal(f)).AddMonths(a.MesesGarantia) : null;

    // ================================================================== 11. Reclamos de garantía

    public sealed class DatosReclamo
    {
        public Guid Ref { get; set; }
        public string? Rubro { get; set; }
        public string? Ubicacion { get; set; }
        public string? Descripcion { get; set; }
        public bool Urgente { get; set; }
        public string? Nombre { get; set; }
    }

    /// <summary>Desde la app del cliente. Un reintento con el mismo Ref no duplica.</summary>
    public async Task<(int? Id, string? Error)> CrearReclamoAsync(int obraId, DatosReclamo d, IReadOnlyCollection<Adjuntos.Archivo> fotos, CancellationToken ct = default)
    {
        if (d.Ref == Guid.Empty) return (null, "Volvé a cargar la página e intentá de nuevo.");
        var rubro = Relevamientos.Texto(d.Rubro, 80);
        if (rubro is null || !Rubros.Contains(rubro)) return (null, "Elegí qué tipo de problema es.");
        var desc = Relevamientos.Texto(d.Descripcion, 2000);
        if (desc is null) return (null, "Contanos qué pasa.");
        if (Adjuntos.Validar(fotos) is { } e) return (null, e);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await db.Reclamos.Where(r => r.Ref == d.Ref).Select(r => new { r.Id, r.ObraId }).SingleOrDefaultAsync(ct) is { } previo)
            return previo.ObraId == obraId ? (previo.Id, null) : (null, "Envío duplicado.");
        var r = new ReclamoGarantia
        {
            Ref = d.Ref, ObraId = obraId, Fecha = Ahora, Rubro = rubro, Ubicacion = Relevamientos.Texto(d.Ubicacion, 150),
            Descripcion = desc, Urgente = d.Urgente, ReportadoPor = Relevamientos.Texto(d.Nombre, 120),
        };
        db.Reclamos.Add(r);
        await adjuntos.GuardarAsync(db, obraId, "reclamos", fotos, a => a.Reclamo = r, ct: ct);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            await using var db2 = await dbFactory.CreateDbContextAsync(ct);
            var id = await db2.Reclamos.Where(x => x.Ref == d.Ref && x.ObraId == obraId).Select(x => (int?)x.Id).SingleOrDefaultAsync(ct);
            return id is null ? throw new InvalidOperationException("No se pudo guardar el reclamo.") : (id, null);
        }
        return (r.Id, null);
    }

    public async Task<List<ReclamoGarantia>> ReclamosAsync(int? obraId = null, bool soloAbiertos = false, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Reclamos.AsNoTracking().Include(r => r.Adjuntos).Include(r => r.Obra).Include(r => r.Etapa)
            .Where(r => (obraId == null || r.ObraId == obraId)
                && (!soloAbiertos || r.Estado == EstadoReclamo.Nuevo || r.Estado == EstadoReclamo.EnRevision || r.Estado == EstadoReclamo.Programado))
            .OrderByDescending(r => r.Estado == EstadoReclamo.Nuevo).ThenByDescending(r => r.Urgente).ThenByDescending(r => r.Fecha)
            .Take(300).ToListAsync(ct);
    }

    public sealed class DatosRevision
    {
        public EstadoReclamo Estado { get; set; }
        public string? Respuesta { get; set; }
        public DateOnly? FechaVisita { get; set; }
        public CausaReclamo? Causa { get; set; }
        public int? EtapaObraId { get; set; }
        public string? Responsable { get; set; }
        public decimal? CostoReparacion { get; set; }
        public string? NotaInterna { get; set; }
    }

    public async Task<string?> RevisarReclamoAsync(int reclamoId, DatosRevision d, CancellationToken ct = default)
    {
        if (d.Estado == EstadoReclamo.Programado && d.FechaVisita is null) return "Indicá la fecha de la visita.";
        if (d.Estado is EstadoReclamo.Resuelto or EstadoReclamo.NoCorresponde && d.Causa is null) return "Indicá la causa (sirve para no repetir el problema).";
        if (d.Estado == EstadoReclamo.NoCorresponde && Relevamientos.Texto(d.Respuesta, 2000) is null) return "Explicale al cliente por qué no corresponde.";
        if (d.CostoReparacion is < 0) return "El costo no puede ser negativo.";
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var r = await db.Reclamos.SingleOrDefaultAsync(x => x.Id == reclamoId, ct);
        if (r is null) return "El reclamo no existe.";
        if (d.EtapaObraId is int e && !await db.Etapas.AnyAsync(x => x.Id == e && x.ObraId == r.ObraId, ct)) return "La etapa no es de esta obra.";
        r.Estado = d.Estado; r.Respuesta = Relevamientos.Texto(d.Respuesta, 2000); r.FechaVisita = d.FechaVisita;
        r.Causa = d.Causa; r.EtapaObraId = d.EtapaObraId; r.Responsable = Relevamientos.Texto(d.Responsable, 150);
        r.CostoReparacion = d.CostoReparacion; r.NotaInterna = Relevamientos.Texto(d.NotaInterna, 2000);
        r.ResueltoEn = d.Estado is EstadoReclamo.Resuelto or EstadoReclamo.NoCorresponde ? r.ResueltoEn ?? Ahora : null;
        r.Visto = true;
        await db.SaveChangesAsync(ct);
        return null;
    }

    public async Task<string?> FotosResolucionReclamoAsync(int reclamoId, IReadOnlyCollection<Adjuntos.Archivo> fotos, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var obra = await db.Reclamos.Where(r => r.Id == reclamoId).Select(r => (int?)r.ObraId).SingleOrDefaultAsync(ct);
        return obra is null ? "El reclamo no existe."
            : await adjuntos.AgregarAsync(obra, "reclamos", fotos, a => a.ReclamoGarantiaId = reclamoId, esResolucion: true, ct);
    }

    public async Task MarcarReclamosVistosAsync(int? obraId = null, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.Reclamos.Where(r => !r.Visto && (obraId == null || r.ObraId == obraId)).ExecuteUpdateAsync(s => s.SetProperty(r => r.Visto, true), ct);
    }

    // ================================================================== 12. Encuestas y permiso de difusión

    /// <summary>
    /// Crea la encuesta si no existe. Las de fin de etapa solo si el cliente tiene acceso activo a la app (si no, nadie la vería).
    /// Usa el contexto de quien llama (se dispara al completar una etapa o al firmar el acta).
    /// </summary>
    public static async Task AsegurarEncuestaAsync(AppDbContext db, int obraId, MomentoEncuesta momento, int? etapaId, string titulo,
        CancellationToken ct, bool exigirAcceso = true)
    {
        if (exigirAcceso && !await db.AccesosCliente.AnyAsync(a => a.ObraId == obraId && a.Activo, ct)) return;
        if (await db.Encuestas.AnyAsync(e => e.ObraId == obraId && e.Momento == momento && e.EtapaObraId == etapaId, ct)) return;
        db.Encuestas.Add(new EncuestaCliente
        {
            ObraId = obraId, Momento = momento, EtapaObraId = etapaId, Titulo = titulo.Length > 200 ? titulo[..200] : titulo, CreadaEn = DateTime.UtcNow,
        });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { /* la creó otro pedido al mismo tiempo */ }
    }

    public async Task<List<EncuestaCliente>> EncuestasAsync(int obraId, bool soloPendientes = false, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Encuestas.AsNoTracking().Include(e => e.Etapa)
            .Where(e => e.ObraId == obraId && (!soloPendientes || e.RespondidaEn == null))
            .OrderByDescending(e => e.CreadaEn).ToListAsync(ct);
    }

    public async Task<string?> ResponderEncuestaAsync(int obraId, int encuestaId, int? puntaje, string? comentario, CancellationToken ct = default)
    {
        if (puntaje is not (>= 0 and <= 10)) return "Elegí un número del 0 al 10.";
        comentario = Relevamientos.Texto(comentario, 2000);
        var ahora = Ahora;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var n = await db.Encuestas.Where(e => e.Id == encuestaId && e.ObraId == obraId && e.RespondidaEn == null)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.RespondidaEn, ahora).SetProperty(e => e.Puntaje, puntaje)
                .SetProperty(e => e.Comentario, comentario).SetProperty(e => e.Vista, false), ct);
        return n == 1 ? null : "Esta encuesta ya fue respondida.";
    }

    public async Task MarcarEncuestasVistasAsync(int obraId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.Encuestas.Where(e => e.ObraId == obraId && e.RespondidaEn != null && !e.Vista)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.Vista, true), ct);
    }

    /// <summary>NPS: % de 9-10 menos % de 0-6. Null sin respuestas.</summary>
    public static decimal? Nps(IEnumerable<int> puntajes)
    {
        var l = puntajes.ToList();
        if (l.Count == 0) return null;
        return Math.Round((l.Count(p => p >= 9) - l.Count(p => p <= 6)) * 100m / l.Count, 0);
    }

    public async Task<PermisoDifusion?> PermisoAsync(int obraId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Permisos.AsNoTracking().SingleOrDefaultAsync(p => p.ObraId == obraId, ct);
    }

    /// <summary>Lo decide el cliente desde su app y lo puede cambiar cuando quiera (también retirar el permiso).</summary>
    public async Task<string?> GuardarPermisoAsync(int obraId, bool usarFotos, bool mostrarNombre, string? testimonio, string? nombre, string? ip, CancellationToken ct = default)
    {
        nombre = Relevamientos.Texto(nombre, 120);
        if (nombre is null) return "Escribí tu nombre.";
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var p = await db.Permisos.SingleOrDefaultAsync(x => x.ObraId == obraId, ct);
        if (p is null) db.Permisos.Add(p = new PermisoDifusion { ObraId = obraId });
        p.UsarFotos = usarFotos;
        p.Testimonio = Relevamientos.Texto(testimonio, 2000);
        p.MostrarNombre = mostrarNombre && p.Testimonio is not null;
        p.Nombre = nombre;
        p.DesdeIp = Relevamientos.Texto(ip, 60);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { return "Se guardó desde otro dispositivo al mismo tiempo. Volvé a intentar."; }
        return null;
    }

    // ================================================================== 13. Pagos que informa el cliente

    public sealed class DatosPagoInformado
    {
        public Guid Ref { get; set; }
        public DateOnly Fecha { get; set; }
        public decimal? Monto { get; set; }
        public MedioPago Medio { get; set; } = MedioPago.Transferencia;
        public int? CertificadoId { get; set; }
        public string? Nota { get; set; }
        public string? Nombre { get; set; }
    }

    public async Task<(int? Id, string? Error)> InformarPagoAsync(int obraId, DatosPagoInformado d, IReadOnlyCollection<Adjuntos.Archivo> comprobantes, CancellationToken ct = default)
    {
        if (d.Ref == Guid.Empty) return (null, "Volvé a cargar la página e intentá de nuevo.");
        if (d.Monto is not > 0 || d.Monto > 10_000_000_000) return (null, "Indicá el monto que pagaste.");
        if (d.Fecha == default || d.Fecha > Hoy || d.Fecha < Hoy.AddYears(-1)) return (null, "Revisá la fecha del pago.");
        if (d.Medio == MedioPago.Transferencia && comprobantes.Count == 0) return (null, "Adjuntá el comprobante de la transferencia.");
        if (Adjuntos.Validar(comprobantes) is { } e) return (null, e);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await db.PagosInformados.Where(p => p.Ref == d.Ref).Select(p => new { p.Id, p.ObraId }).SingleOrDefaultAsync(ct) is { } previo)
            return previo.ObraId == obraId ? (previo.Id, null) : (null, "Envío duplicado.");
        if (d.CertificadoId is int c && !await db.Certificados.AnyAsync(x => x.Id == c && x.ObraId == obraId && x.Estado == EstadoCertificado.Emitido, ct))
            return (null, "Ese certificado no está pendiente de pago.");
        var p = new PagoInformado
        {
            Ref = d.Ref, ObraId = obraId, Fecha = d.Fecha, Monto = Math.Round(d.Monto.Value, 2), Medio = d.Medio, CertificadoId = d.CertificadoId,
            Nota = Relevamientos.Texto(d.Nota, 1000), InformadoPor = Relevamientos.Texto(d.Nombre, 120), InformadoEn = Ahora,
        };
        db.PagosInformados.Add(p);
        await adjuntos.GuardarAsync(db, obraId, "pagos", comprobantes, a => a.PagoInformado = p, ct: ct);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            await using var db2 = await dbFactory.CreateDbContextAsync(ct);
            var id = await db2.PagosInformados.Where(x => x.Ref == d.Ref && x.ObraId == obraId).Select(x => (int?)x.Id).SingleOrDefaultAsync(ct);
            return id is null ? throw new InvalidOperationException("No se pudo guardar el pago.") : (id, null);
        }
        return (p.Id, null);
    }

    public async Task<List<PagoInformado>> PagosInformadosAsync(int? obraId = null, bool soloPendientes = false, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.PagosInformados.AsNoTracking().Include(p => p.Adjuntos).Include(p => p.Certificado).Include(p => p.Obra)
            .Where(p => (obraId == null || p.ObraId == obraId) && (!soloPendientes || p.Estado == EstadoPagoInformado.Pendiente))
            .OrderByDescending(p => p.Estado == EstadoPagoInformado.Pendiente).ThenByDescending(p => p.InformadoEn).Take(300).ToListAsync(ct);
    }

    /// <summary>
    /// El estudio verificó que la plata entró: se crea el pago de la obra y, si informó un certificado y cubre su monto,
    /// el certificado queda pagado. Una sola vez (si dos personas confirman a la vez, solo vale la primera).
    /// </summary>
    public async Task<string?> ConfirmarPagoAsync(int pagoInformadoId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        // Con reintentos automáticos (Azure SQL) la transacción tiene que ir dentro de la estrategia de ejecución.
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var r = await ConfirmarEnTransaccionAsync(db, pagoInformadoId, ct);
            if (r is null) await tx.CommitAsync(ct);
            return r;
        });
    }

    private async Task<string?> ConfirmarEnTransaccionAsync(AppDbContext db, int pagoInformadoId, CancellationToken ct)
    {
        var ahora = Ahora;
        var n = await db.PagosInformados.Where(p => p.Id == pagoInformadoId && p.Estado == EstadoPagoInformado.Pendiente)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Estado, EstadoPagoInformado.Confirmado).SetProperty(p => p.RevisadoEn, ahora), ct);
        if (n == 0) return "Ya estaba revisado.";
        var pi = await db.PagosInformados.SingleAsync(p => p.Id == pagoInformadoId, ct);
        var cert = pi.CertificadoId is int c ? await db.Certificados.SingleOrDefaultAsync(x => x.Id == c, ct) : null;
        var pago = new Pago
        {
            ObraId = pi.ObraId, Fecha = pi.Fecha, Monto = pi.Monto, Medio = pi.Medio,
            Concepto = cert is null ? "Pago informado por el cliente" : $"Certificado N° {cert.Numero} (informado por el cliente)",
        };
        db.Pagos.Add(pago);
        pi.Pago = pago;
        if (cert is { Estado: EstadoCertificado.Emitido } && pi.Monto >= cert.Monto)
        {
            cert.Estado = EstadoCertificado.Cobrado;
            cert.Pago = pago;
        }
        await db.SaveChangesAsync(ct);
        return null;
    }

    public async Task<string?> RechazarPagoAsync(int pagoInformadoId, string? motivo, CancellationToken ct = default)
    {
        motivo = Relevamientos.Texto(motivo, 500);
        if (motivo is null) return "Explicale al cliente por qué (lo ve en su app).";
        var ahora = Ahora;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var n = await db.PagosInformados.Where(p => p.Id == pagoInformadoId && p.Estado == EstadoPagoInformado.Pendiente)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Estado, EstadoPagoInformado.Rechazado).SetProperty(p => p.RevisadoEn, ahora)
                .SetProperty(p => p.MotivoRechazo, motivo), ct);
        return n == 1 ? null : "Ya estaba revisado.";
    }

    // ================================================================== Bandeja del panel

    public sealed record Novedades(int ReclamosNuevos, int ReclamosAbiertos, int PagosPorConfirmar, int EncuestasNuevas);

    public async Task<Novedades> NovedadesAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return new(
            await db.Reclamos.CountAsync(r => !r.Visto, ct),
            await db.Reclamos.CountAsync(r => r.Estado == EstadoReclamo.Nuevo || r.Estado == EstadoReclamo.EnRevision || r.Estado == EstadoReclamo.Programado, ct),
            await db.PagosInformados.CountAsync(p => p.Estado == EstadoPagoInformado.Pendiente, ct),
            await db.Encuestas.CountAsync(e => e.RespondidaEn != null && !e.Vista, ct));
    }
}
