using DeepArquitectura.Web.Data;
using DeepArquitectura.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace DeepArquitectura.Web.Services;

/// <summary>
/// App del cliente (/mi-obra): lo que ve y hace el comitente de una obra con el código y PIN que le manda el estudio.
/// Todo se filtra por la obra de la sesión. Nunca muestra nombres del personal, problemas internos ni costos de obra.
/// </summary>
public class AppCliente(IDbContextFactory<AppDbContext> dbFactory, IAlmacenMedios almacen, PortalCliente portal, IMemoryCache cache,
    TimeProvider? reloj = null)
{
    private readonly TimeProvider _reloj = reloj ?? TimeProvider.System;
    private DateTime Ahora => _reloj.GetUtcNow().UtcDateTime;

    // ---------------- Ingreso ----------------

    public enum ResultadoIngreso { Ok, Incorrecto, Bloqueado }

    public sealed record Ingreso(ResultadoIngreso Resultado, int ObraId = 0, string? Sello = null, string? Codigo = null);

    /// <summary>
    /// Código + PIN. Tras <see cref="AccesoClientes.IntentosAntesDeBloquear"/> PIN errados el código queda bloqueado
    /// un rato (además del límite por IP del endpoint): con un código filtrado no se puede probar el millón de PIN.
    /// </summary>
    public async Task<Ingreso> IngresarAsync(string? codigo, string? pin, CancellationToken ct = default)
    {
        var normal = AccesoClientes.Normalizar(codigo);
        if (normal is null || !AccesoClientes.PinConFormato(pin))
        {
            AccesoClientes.VerificarPin(null, pin ?? "");
            return new(ResultadoIngreso.Incorrecto);
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var a = await db.AccesosCliente.SingleOrDefaultAsync(x => x.Codigo == normal && x.Activo, ct);
        if (a?.BloqueadoHasta is DateTime hasta && hasta > Ahora) return new(ResultadoIngreso.Bloqueado);

        if (!AccesoClientes.VerificarPin(a, pin!))
        {
            if (a is not null)
            {
                a.IntentosFallidos++;
                if (a.IntentosFallidos >= AccesoClientes.IntentosAntesDeBloquear)
                {
                    a.BloqueadoHasta = Ahora + AccesoClientes.Bloqueo;
                    a.IntentosFallidos = 0;
                }
                await db.SaveChangesAsync(ct);
            }
            return new(a?.BloqueadoHasta > Ahora ? ResultadoIngreso.Bloqueado : ResultadoIngreso.Incorrecto);
        }

        a!.IntentosFallidos = 0;
        a.BloqueadoHasta = null;
        a.UltimoIngreso = Ahora;
        await db.SaveChangesAsync(ct);
        return new(ResultadoIngreso.Ok, a.ObraId, a.Sello, a.Codigo);
    }

    /// <summary>
    /// La cookie sigue valiendo mientras el acceso esté activo y con el mismo sello (cambia al dar un PIN nuevo o desactivar).
    /// Se cachea un minuto: cada foto de la galería pasa por acá.
    /// </summary>
    public async Task<bool> SesionValidaAsync(int obraId, string? sello, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(sello)) return false;
        var vigente = await cache.GetOrCreateAsync(ClaveSello(obraId), async e =>
        {
            e.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1);
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            return await db.AccesosCliente.Where(x => x.ObraId == obraId && x.Activo).Select(x => x.Sello).SingleOrDefaultAsync(ct);
        });
        return vigente is not null && vigente == sello;
    }

    internal static string ClaveSello(int obraId) => $"cliente-sello-{obraId}";

    // ---------------- Inicio ----------------

    public sealed record Inicio(PortalCliente.Vista Vista, int AprobacionesPendientes, int MensajesNuevos, string? EtapaActual,
        decimal? Saldo, List<AprobacionCliente> Pendientes);

    public async Task<Inicio?> InicioAsync(int obraId, CancellationToken ct = default)
    {
        var vista = await portal.VistaObraAsync(obraId, ct);
        if (vista is null) return null;
        var (aprob, mensajes) = await AvisosAsync(obraId, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var pendientes = await db.Aprobaciones.AsNoTracking().Where(a => a.ObraId == obraId && a.Estado == EstadoAprobacion.Pendiente)
            .OrderBy(a => a.FechaLimite == null).ThenBy(a => a.FechaLimite).ThenBy(a => a.Id).Take(3).ToListAsync(ct);
        var pagos = await PagosAsync(obraId, ct);
        return new(vista, aprob, mensajes,
            vista.Etapas.FirstOrDefault(e => e.Estado == EstadoEtapa.EnCurso)?.Nombre
                ?? vista.Etapas.FirstOrDefault(e => e.Estado == EstadoEtapa.Pendiente)?.Nombre,
            pagos?.Saldo, pendientes);
    }

    /// <summary>Insignias del menú: aprobaciones por responder y mensajes del estudio sin leer.</summary>
    public async Task<(int Aprobaciones, int Mensajes)> AvisosAsync(int obraId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var aprob = await db.Aprobaciones.CountAsync(a => a.ObraId == obraId && a.Estado == EstadoAprobacion.Pendiente, ct);
        var mensajes = await db.Mensajes.CountAsync(m => m.ObraId == obraId && !m.DelCliente && m.LeidoEn == null, ct);
        return (aprob, mensajes);
    }

    // ---------------- Avance detallado ----------------

    public sealed record TrabajoCliente(string Titulo, string? Detalle, EstadoTarea Estado, int Porcentaje, string? Unidad,
        decimal? Hecho, decimal? Objetivo, DateOnly? Inicio, DateOnly? Fin);
    public sealed record EtapaCliente(string Nombre, EstadoEtapa Estado, DateOnly? Inicio, DateOnly? Fin, int Porcentaje, List<TrabajoCliente> Trabajos);
    public sealed record Avance(List<EtapaCliente> Etapas, AvanceObra.Curva? Curva, decimal? Fisico, int AvanceEtapas);

    /// <summary>Etapas con sus trabajos y el % aprobado por el capataz o el estudio (lo informado sin validar no cuenta).</summary>
    public async Task<Avance?> AvanceAsync(int obraId, CancellationToken ct = default)
    {
        var vista = await portal.VistaObraAsync(obraId, ct);
        if (vista is null) return null;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var etapas = await db.Etapas.AsNoTracking().Where(e => e.ObraId == obraId && e.Estado != EstadoEtapa.Omitida)
            .OrderBy(e => e.Orden).ToListAsync(ct);
        var tareas = await db.Tareas.AsNoTracking().Where(t => t.ObraId == obraId && t.Estado != EstadoTarea.Cancelada)
            .OrderBy(t => t.FechaInicioPlan == null).ThenBy(t => t.FechaInicioPlan).ThenBy(t => t.Id).ToListAsync(ct);

        TrabajoCliente Trabajo(TareaObra t) => new(t.Titulo, t.Descripcion, t.Estado,
            (int)Math.Round(CalculoTareas.Fraccion(t) * 100), t.Unidad, t.Unidad is null ? null : t.CantidadAprobada, t.CantidadObjetivo,
            t.FechaInicioPlan, t.FechaLimite);

        static int Porcentaje(List<TareaObra> ts)
        {
            if (ts.Count == 0) return 0;
            var pesos = CalculoTareas.Pesos(ts);
            return (int)Math.Round(ts.Sum(t => pesos[t.Id] * CalculoTareas.Fraccion(t)) * 100);
        }

        var lista = etapas.Select(e =>
        {
            var ts = tareas.Where(t => t.EtapaObraId == e.Id).ToList();
            return new EtapaCliente(e.Nombre, e.Estado, e.FechaInicio, e.FechaFin,
                e.Estado == EstadoEtapa.Completada ? 100 : Porcentaje(ts), ts.Select(Trabajo).ToList());
        }).ToList();
        var sueltas = tareas.Where(t => t.EtapaObraId is null || etapas.All(e => e.Id != t.EtapaObraId)).ToList();
        if (sueltas.Count > 0)
            lista.Add(new EtapaCliente("Otros trabajos", sueltas.All(t => t.Estado == EstadoTarea.Completada) ? EstadoEtapa.Completada : EstadoEtapa.EnCurso,
                null, null, Porcentaje(sueltas), sueltas.Select(Trabajo).ToList()));

        return new(lista, vista.Curva, vista.AvanceFisico, vista.AvanceEtapas);
    }

    // ---------------- Pagos ----------------

    public sealed record CertificadoCliente(int Numero, DateOnly Fecha, decimal PorcentajePeriodo, decimal PorcentajeAcumulado, decimal Monto, bool Cobrado);
    public sealed record PagoCliente(DateOnly Fecha, decimal Monto, string? Concepto, MedioPago Medio);
    public sealed record Pagos(decimal? MontoObra, decimal Pagado, decimal? Saldo, decimal APagar, List<CertificadoCliente> Certificados,
        List<PagoCliente> Movimientos, decimal Adicionales);

    /// <summary>
    /// Certificados emitidos (los borradores no se muestran) y pagos registrados. <c>APagar</c> = certificados emitidos sin cobrar.
    /// <c>Adicionales</c> = montos de aprobaciones aceptadas por el cliente.
    /// </summary>
    public async Task<Pagos?> PagosAsync(int obraId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var monto = await db.Obras.Where(o => o.Id == obraId).Select(o => new { o.MontoPresupuesto }).SingleOrDefaultAsync(ct);
        if (monto is null) return null;
        var certs = (await db.Certificados.AsNoTracking().Where(c => c.ObraId == obraId && c.Estado != EstadoCertificado.Borrador)
                .ToListAsync(ct))
            .OrderByDescending(c => c.Numero)
            .Select(c => new CertificadoCliente(c.Numero, c.Fecha, c.PorcentajeAcumulado - c.PorcentajeAnterior, c.PorcentajeAcumulado, c.Monto,
                c.Estado == EstadoCertificado.Cobrado))
            .ToList();
        var pagos = (await db.Pagos.AsNoTracking().Where(p => p.ObraId == obraId).ToListAsync(ct))
            .OrderByDescending(p => p.Fecha).ThenByDescending(p => p.Id)
            .Select(p => new PagoCliente(p.Fecha, p.Monto, p.Concepto, p.Medio)).ToList();
        var adicionales = (await db.Aprobaciones.AsNoTracking()
                .Where(a => a.ObraId == obraId && a.Estado == EstadoAprobacion.Aprobada && a.Monto != null)
                .Select(a => a.Monto!.Value).ToListAsync(ct)).Sum();
        var pagado = pagos.Sum(p => p.Monto);
        decimal? total = monto.MontoPresupuesto is decimal m ? m + adicionales : null;
        return new(monto.MontoPresupuesto, pagado, total is decimal t ? t - pagado : null,
            certs.Where(c => !c.Cobrado).Sum(c => c.Monto), certs, pagos, adicionales);
    }

    // ---------------- Documentos ----------------

    public async Task<List<DocumentoObra>> DocumentosAsync(int obraId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return (await db.Documentos.AsNoTracking().Where(d => d.ObraId == obraId && d.VisibleCliente).ToListAsync(ct))
            .OrderBy(d => d.Categoria).ThenByDescending(d => d.CreadoEn).ToList();
    }

    /// <summary>Archivo de un documento visible de esa obra (o adjunto a una de sus aprobaciones); si no, null.</summary>
    public async Task<(Stream Contenido, string ContentType, string Nombre)?> DocumentoAsync(int obraId, int documentoId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var d = await db.Documentos.AsNoTracking()
            .Where(x => x.Id == documentoId && x.ObraId == obraId
                && (x.VisibleCliente || db.Aprobaciones.Any(a => a.DocumentoObraId == x.Id && a.Estado != EstadoAprobacion.Anulada)))
            .SingleOrDefaultAsync(ct);
        if (d is null) return null;
        var s = await almacen.AbrirAsync(d.Ruta, ct);
        return s is null ? null : (s, d.ContentType, NombreDescarga(d));
    }

    internal static string NombreDescarga(DocumentoObra d)
    {
        var ext = Path.GetExtension(d.NombreOriginal ?? d.Ruta);
        var baseNombre = new string(d.Titulo.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c).ToArray()).Trim();
        return (string.IsNullOrEmpty(baseNombre) ? "documento" : baseNombre) + ext;
    }

    // ---------------- Mensajes ----------------

    public const int LargoMaximoMensaje = 2000;

    /// <summary>La conversación, de la más vieja a la más nueva. Marca como leídos los mensajes del estudio.</summary>
    public async Task<List<MensajeObra>> MensajesAsync(int obraId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var ahora = Ahora;
        await db.Mensajes.Where(m => m.ObraId == obraId && !m.DelCliente && m.LeidoEn == null)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.LeidoEn, ahora), ct);
        var lista = await db.Mensajes.AsNoTracking().Where(m => m.ObraId == obraId)
            .OrderByDescending(m => m.Fecha).ThenByDescending(m => m.Id).Take(200).ToListAsync(ct);
        lista.Reverse();
        return lista;
    }

    /// <summary>Idempotente por <paramref name="refFormulario"/>: si el cliente toca dos veces «Enviar», queda uno solo.</summary>
    public async Task<string?> EnviarMensajeAsync(int obraId, string? autor, string? texto, Guid? refFormulario, CancellationToken ct = default)
    {
        texto = texto?.Trim();
        if (string.IsNullOrEmpty(texto)) return "Escribí el mensaje.";
        if (texto.Length > LargoMaximoMensaje) return $"El mensaje es muy largo (máximo {LargoMaximoMensaje} caracteres).";
        autor = Recortar(autor, 100);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (refFormulario is Guid r && await db.Mensajes.AnyAsync(m => m.Ref == r, ct)) return null;
        db.Mensajes.Add(new MensajeObra { ObraId = obraId, DelCliente = true, Autor = autor, Texto = texto, Fecha = Ahora, Ref = refFormulario });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) when (refFormulario is not null) { /* doble envío simultáneo: ya quedó el primero */ }
        return null;
    }

    // ---------------- Aprobaciones ----------------

    public async Task<List<AprobacionCliente>> AprobacionesAsync(int obraId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return (await db.Aprobaciones.AsNoTracking().Include(a => a.Documento)
                .Where(a => a.ObraId == obraId && a.Estado != EstadoAprobacion.Anulada).ToListAsync(ct))
            .OrderBy(a => a.Estado != EstadoAprobacion.Pendiente)
            .ThenBy(a => a.Estado == EstadoAprobacion.Pendiente ? a.FechaLimite ?? DateOnly.MaxValue : DateOnly.MaxValue)
            .ThenByDescending(a => a.RespondidoEn ?? a.CreadoEn)
            .ToList();
    }

    /// <summary>
    /// Respuesta del cliente. Con opciones, aprobar exige elegir una. Queda el nombre de quien respondió, la fecha y la IP
    /// como constancia, y no se puede cambiar después (si se arrepiente, lo habla con el estudio por mensaje).
    /// </summary>
    public async Task<string?> ResponderAsync(int obraId, int aprobacionId, bool aprobar, string? opcion, string? comentario, string? nombre,
        string? ip, CancellationToken ct = default)
    {
        nombre = Recortar(nombre, 100);
        if (nombre is null) return "Escribí tu nombre: queda como constancia de quién respondió.";
        comentario = Recortar(comentario, 2000);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var a = await db.Aprobaciones.SingleOrDefaultAsync(x => x.Id == aprobacionId && x.ObraId == obraId, ct);
        if (a is null || a.Estado == EstadoAprobacion.Anulada) return "Esta consulta ya no está disponible.";
        if (a.Estado != EstadoAprobacion.Pendiente) return "Ya respondiste esta consulta. Si querés cambiar algo, escribile al estudio.";

        var opciones = a.ListaOpciones;
        string? elegida = null;
        if (aprobar && opciones.Count > 0)
        {
            elegida = opciones.FirstOrDefault(o => string.Equals(o, opcion?.Trim(), StringComparison.Ordinal));
            if (elegida is null) return "Elegí una de las opciones.";
        }
        if (!aprobar && comentario is null) return "Contanos por qué no (así el estudio te propone otra cosa).";

        // Condicional en la base: si llegan dos respuestas a la vez (dos teléfonos de la familia), vale la primera.
        var estado = aprobar ? EstadoAprobacion.Aprobada : EstadoAprobacion.Rechazada;
        var (ahora, desde) = (Ahora, Recortar(ip, 64));
        var n = await db.Aprobaciones.Where(x => x.Id == a.Id && x.Estado == EstadoAprobacion.Pendiente)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Estado, estado)
                .SetProperty(x => x.OpcionElegida, elegida)
                .SetProperty(x => x.Comentario, comentario)
                .SetProperty(x => x.RespondidoPor, nombre)
                .SetProperty(x => x.RespondidoEn, ahora)
                .SetProperty(x => x.RespondidoDesdeIp, desde)
                .SetProperty(x => x.RespuestaVista, false), ct);
        return n == 1 ? null : "Ya respondiste esta consulta. Si querés cambiar algo, escribile al estudio.";
    }

    internal static string? Recortar(string? s, int max)
    {
        s = s?.Trim();
        return string.IsNullOrEmpty(s) ? null : s.Length > max ? s[..max] : s;
    }
}
