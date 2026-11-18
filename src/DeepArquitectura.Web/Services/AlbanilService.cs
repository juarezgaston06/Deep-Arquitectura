using System.Security.Cryptography;
using DeepArquitectura.Contracts;
using DeepArquitectura.Web.Data;
using DeepArquitectura.Web.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DeepArquitectura.Web.Services;

/// <summary>
/// Albañiles (cuadrilla de un capataz) que usan la app de obra: alta y PIN desde el panel, login con token revocable,
/// y lo que mandan desde el teléfono (avances de tareas con fotos y videos, fichadas, pedidos, problemas y partes).
/// <para>
/// Un albañil ve una obra solo si está asignado a ella <b>y</b> la obra es de su capataz: si el estudio le saca la obra
/// al capataz, la cuadrilla deja de verla en el momento. Todo lo que manda queda guardado con la obra, el albañil
/// y su capataz.
/// </para>
/// </summary>
public partial class AlbanilService(IDbContextFactory<AppDbContext> dbFactory, IAlmacenMedios almacen, EstudioService estudio, TimeProvider? reloj = null)
{
    public const string ErrorNoAsignada = "Obra no asignada.";
    public const string ErrorNovedadPendiente = "La novedad todavía no llegó.";
    public const string ErrorEntradaPendiente = "La entrada todavía no llegó.";

    private static readonly PasswordHasher<string> Hasher = new();
    private static readonly string FalsoHash = Hasher.HashPassword("x", Guid.NewGuid().ToString());
    private readonly TimeProvider _reloj = reloj ?? TimeProvider.System;
    private DateTime Ahora => _reloj.GetUtcNow().UtcDateTime;
    private DateOnly HoyLocal => DateOnly.FromDateTime(Textos.AHoraLocal(Ahora));

    // ================================================================== Panel: cuentas

    /// <summary>Alta de albañil en la cuadrilla de un capataz. Las obras tienen que ser de ese capataz.</summary>
    public async Task<(int? Id, string? Error)> CrearAsync(string nombre, string usuario, string? telefono, string? oficio,
        int capatazId, string pin, IEnumerable<int> obras, string? empresa = null, CancellationToken ct = default)
    {
        usuario = CapatazService.NormalizarUsuario(usuario);
        if (string.IsNullOrWhiteSpace(nombre)) return (null, "Ingresá el nombre.");
        if (usuario.Length < 3 || usuario.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-')))
            return (null, "El usuario debe tener al menos 3 caracteres: letras, números, punto, guion o guion bajo.");
        if (CapatazService.ValidarPin(pin) is { } errorPin) return (null, errorPin);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await db.Capataces.AnyAsync(c => c.Id == capatazId, ct)) return (null, "Elegí el capataz de su cuadrilla.");
        if (await db.Albaniles.AnyAsync(a => a.Usuario == usuario, ct)) return (null, $"Ya existe el usuario «{usuario}».");

        var deseadas = obras.Distinct().ToList();
        var delCapataz = await db.CapatazObras.Where(x => x.CapatazId == capatazId && deseadas.Contains(x.ObraId)).Select(x => x.ObraId).ToListAsync(ct);
        if (delCapataz.Count != deseadas.Count) return (null, "Solo se le pueden asignar obras de su capataz.");

        var albanil = new Albanil
        {
            Nombre = nombre.Trim(),
            Usuario = usuario,
            Telefono = Limpio(telefono, 50),
            Oficio = Limpio(oficio, 60),
            Empresa = Limpio(empresa, 150),
            CapatazId = capatazId,
            PinHash = Hasher.HashPassword(usuario, pin),
        };
        foreach (var obraId in deseadas) albanil.Obras.Add(new AlbanilObra { ObraId = obraId });
        db.Albaniles.Add(albanil);
        await db.SaveChangesAsync(ct);
        return (albanil.Id, null);
    }

    public async Task ActualizarDatosAsync(int id, string nombre, string? telefono, string? oficio, string? empresa = null, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.Albaniles.Where(a => a.Id == id).ExecuteUpdateAsync(s => s
            .SetProperty(a => a.Nombre, nombre.Trim())
            .SetProperty(a => a.Telefono, Limpio(telefono, 50))
            .SetProperty(a => a.Oficio, Limpio(oficio, 60))
            .SetProperty(a => a.Empresa, Limpio(empresa, 150)), ct);
    }

    /// <summary>Pasa el albañil a otra cuadrilla: se le quitan las obras que no son del capataz nuevo.</summary>
    public async Task<string?> CambiarCapatazAsync(int id, int capatazId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await db.Capataces.AnyAsync(c => c.Id == capatazId, ct)) return "Capataz inexistente.";
        await db.Albaniles.Where(a => a.Id == id).ExecuteUpdateAsync(s => s.SetProperty(a => a.CapatazId, capatazId), ct);
        await db.AlbanilObras
            .Where(x => x.AlbanilId == id && !db.CapatazObras.Any(c => c.CapatazId == capatazId && c.ObraId == x.ObraId))
            .ExecuteDeleteAsync(ct);
        return null;
    }

    /// <summary>Nuevo PIN: cierra las sesiones abiertas en los teléfonos.</summary>
    public async Task<string?> CambiarPinAsync(int id, string pin, CancellationToken ct = default)
    {
        if (CapatazService.ValidarPin(pin) is { } error) return error;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var a = await db.Albaniles.SingleAsync(x => x.Id == id, ct);
        a.PinHash = Hasher.HashPassword(a.Usuario, pin);
        await db.SaveChangesAsync(ct);
        await db.SesionesAlbanil.Where(s => s.AlbanilId == id).ExecuteDeleteAsync(ct);
        return null;
    }

    public async Task CambiarActivoAsync(int id, bool activo, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.Albaniles.Where(a => a.Id == id).ExecuteUpdateAsync(s => s.SetProperty(a => a.Activo, activo), ct);
        if (!activo) await db.SesionesAlbanil.Where(s => s.AlbanilId == id).ExecuteDeleteAsync(ct);
    }

    public async Task<int> CerrarSesionesAsync(int id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.SesionesAlbanil.Where(s => s.AlbanilId == id).ExecuteDeleteAsync(ct);
    }

    /// <summary>Asigna o quita una obra. Solo se asignan obras del capataz del albañil.</summary>
    public async Task<string?> AsignarAsync(int albanilId, int obraId, bool asignado, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var existe = await db.AlbanilObras.AnyAsync(x => x.AlbanilId == albanilId && x.ObraId == obraId, ct);
        if (asignado && !existe)
        {
            var capatazId = await db.Albaniles.Where(a => a.Id == albanilId).Select(a => a.CapatazId).SingleAsync(ct);
            if (!await db.CapatazObras.AnyAsync(x => x.CapatazId == capatazId && x.ObraId == obraId, ct))
                return "Esa obra no es de su capataz: asignásela primero al capataz.";
            db.AlbanilObras.Add(new AlbanilObra { AlbanilId = albanilId, ObraId = obraId });
            await db.SaveChangesAsync(ct);
        }
        else if (!asignado && existe)
            await db.AlbanilObras.Where(x => x.AlbanilId == albanilId && x.ObraId == obraId).ExecuteDeleteAsync(ct);
        return null;
    }

    // ================================================================== App: sesión

    public async Task<LoginAlbanilResponse?> LoginAsync(string? usuario, string? pin, string? dispositivo, CancellationToken ct = default)
    {
        var u = CapatazService.NormalizarUsuario(usuario);
        if (u.Length == 0 || string.IsNullOrEmpty(pin) || pin.Length > CapatazService.PinMaximo) return null;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var a = await db.Albaniles.Include(x => x.Capataz).SingleOrDefaultAsync(x => x.Usuario == u, ct);
        if (a is null)
        {
            Hasher.VerifyHashedPassword(u, FalsoHash, pin); // mismo costo que un usuario real: no revela cuáles existen
            return null;
        }
        if (!a.Activo || Hasher.VerifyHashedPassword(a.Usuario, a.PinHash, pin) == PasswordVerificationResult.Failed) return null;

        var token = Base64Url(RandomNumberGenerator.GetBytes(32));
        db.SesionesAlbanil.Add(new SesionAlbanil
        {
            AlbanilId = a.Id,
            TokenHash = CapatazService.HashToken(token),
            Dispositivo = Limpio(dispositivo, 120),
            CreadaEn = Ahora,
            UltimoUso = Ahora,
            ExpiraEn = Ahora + CapatazService.DuracionSesion,
        });
        a.UltimoAcceso = Ahora;
        await db.SaveChangesAsync(ct);
        return new LoginAlbanilResponse(token, await PerfilAsync(db, a, ct));
    }

    /// <summary>Albañil dueño del token, si la sesión sigue vigente y el albañil activo.</summary>
    public async Task<int?> AutenticarAsync(string? token, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 100) return null;
        var hash = CapatazService.HashToken(token);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var s = await db.SesionesAlbanil.Include(x => x.Albanil).SingleOrDefaultAsync(x => x.TokenHash == hash, ct);
        if (s is null || !s.Albanil.Activo || s.ExpiraEn < Ahora) return null;

        // Uso deslizante, grabado como mucho cada 10 minutos.
        if (Ahora - s.UltimoUso > TimeSpan.FromMinutes(10))
        {
            s.UltimoUso = Ahora;
            s.ExpiraEn = Ahora + CapatazService.DuracionSesion;
            s.Albanil.UltimoAcceso = Ahora;
            await db.SaveChangesAsync(ct);
        }
        return s.AlbanilId;
    }

    public async Task LogoutAsync(string token, CancellationToken ct = default)
    {
        var hash = CapatazService.HashToken(token);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.SesionesAlbanil.Where(s => s.TokenHash == hash).ExecuteDeleteAsync(ct);
    }

    public async Task<int> LimpiarSesionesVencidasAsync(CancellationToken ct = default)
    {
        var ahora = Ahora;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.SesionesAlbanil.Where(s => s.ExpiraEn < ahora).ExecuteDeleteAsync(ct);
    }

    public async Task<AlbanilDto?> PerfilAsync(int albanilId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var a = await db.Albaniles.Include(x => x.Capataz).SingleOrDefaultAsync(x => x.Id == albanilId, ct);
        return a is null ? null : await PerfilAsync(db, a, ct);
    }

    private static async Task<AlbanilDto> PerfilAsync(AppDbContext db, Albanil a, CancellationToken ct)
    {
        var obras = await db.Obras.AsNoTracking()
            .Where(o => ObrasVisibles(db, a.Id).Contains(o.Id))
            .Include(o => o.Etapas)
            .OrderBy(o => o.Nombre)
            .ToListAsync(ct);
        return new AlbanilDto(a.Id, a.Nombre, a.Usuario, a.Oficio, a.Capataz.Nombre, a.Capataz.Telefono,
            obras.Select(o => new ObraResumenDto(o.Id, o.Codigo, o.Nombre, o.Direccion, o.Localidad, o.Estado.ToString(), o.PorcentajeAvance)).ToList(),
            a.Empresa);
    }

    // ================================================================== App: acceso

    /// <summary>Obras asignadas al albañil que además son de su capataz.</summary>
    private static IQueryable<int> ObrasVisibles(AppDbContext db, int albanilId) =>
        db.AlbanilObras
            .Where(x => x.AlbanilId == albanilId && db.CapatazObras.Any(c => c.ObraId == x.ObraId && c.CapatazId == x.Albanil.CapatazId))
            .Select(x => x.ObraId);

    /// <summary>Capataz del albañil si puede trabajar en la obra; null si no.</summary>
    private static async Task<int?> AccesoAsync(AppDbContext db, int albanilId, int obraId, CancellationToken ct) =>
        await ObrasVisibles(db, albanilId).AnyAsync(id => id == obraId, ct)
            ? await db.Albaniles.Where(a => a.Id == albanilId).Select(a => (int?)a.CapatazId).SingleAsync(ct)
            : null;

    public async Task<bool> PuedeVerObraAsync(int albanilId, int obraId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await AccesoAsync(db, albanilId, obraId, ct) is not null;
    }

    /// <summary>
    /// Tareas que el albañil puede informar: las suyas y las de la cuadrilla sin asignar (no canceladas). Una tarea sin
    /// asignar pero de un oficio (ej. Electricista) solo la ve el personal de ese oficio.
    /// </summary>
    private static IQueryable<TareaObra> TareasVisibles(AppDbContext db, int albanilId, int obraId) =>
        db.Tareas.Where(t => t.ObraId == obraId && t.Estado != EstadoTarea.Cancelada
            && (t.AlbanilId == albanilId
                || (t.AlbanilId == null && (t.Oficio == null || t.Oficio == db.Albaniles.Where(a => a.Id == albanilId).Select(a => a.Oficio).FirstOrDefault()))));

    public async Task<ObraAlbanilDto?> ObraAsync(int albanilId, int obraId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await AccesoAsync(db, albanilId, obraId, ct) is null) return null;

        var o = await db.Obras.AsNoTracking().Include(x => x.Etapas).SingleAsync(x => x.Id == obraId, ct);
        var tareas = await TareasVisibles(db, albanilId, obraId).AsNoTracking().Include(t => t.Etapa).ToListAsync(ct);
        // Primero lo que está en curso, después lo pendiente; de lo terminado, solo lo más reciente.
        var lista = tareas.Where(t => t.Estado != EstadoTarea.Completada)
            .OrderBy(t => t.Estado == EstadoTarea.EnCurso ? 0 : 1).ThenBy(t => t.FechaLimite ?? DateOnly.MaxValue).ThenBy(t => t.Id)
            .Concat(tareas.Where(t => t.Estado == EstadoTarea.Completada).OrderByDescending(t => t.CompletadaEn).Take(10))
            .Select(t => ADto(t, albanilId))
            .ToList();

        var desde = Ahora.AddHours(-20);
        var jornada = await db.Jornadas.AsNoTracking()
            .Where(j => j.AlbanilId == albanilId && j.ObraId == obraId && j.Salida == null && j.Entrada > desde)
            .OrderByDescending(j => j.Entrada)
            .Select(j => new JornadaAbiertaDto(j.Ref, j.Entrada))
            .FirstOrDefaultAsync(ct);

        var avances = await db.Registros.AsNoTracking()
            .Where(r => r.ObraId == obraId && r.AlbanilId == albanilId)
            .OrderByDescending(r => r.Fecha).Take(20)
            .Select(r => new AvanceDto(r.Id, r.Fecha, r.Titulo, r.Tarea != null ? r.Tarea.Titulo : null, r.Cantidad,
                r.Tarea != null ? r.Tarea.Unidad : null,
                r.Medios.Count(m => m.Tipo == TipoMedio.Foto), r.Medios.Count(m => m.Tipo == TipoMedio.Video),
                r.Validacion != null ? r.Validacion.ToString() : null, r.MotivoValidacion))
            .ToListAsync(ct);

        // Pedidos de la obra (de cualquiera de la cuadrilla): el que recibe el material no siempre es el que lo pidió.
        var listaPedidos = await db.Pedidos.AsNoTracking().Include(p => p.Items)
            .Where(p => p.ObraId == obraId && (p.AlbanilId == albanilId || p.Estado == EstadoPedido.Aprobado || p.Estado == EstadoPedido.Pendiente || p.Estado == EstadoPedido.EntregaParcial))
            .OrderByDescending(p => p.Fecha).Take(15).ToListAsync(ct);
        var recibido = await RecibidoPorItemAsync(db, listaPedidos.SelectMany(p => p.Items).Select(i => i.Id).ToList(), ct);
        var pedidos = listaPedidos
            .Select(p => new PedidoDto(p.Id, p.Fecha, p.Estado.ToString(), p.Urgente, DetallePedido(p), p.Respuesta,
                p.Items.OrderBy(i => i.Id).Select(i => new PedidoItemDto(i.Id, i.Material, i.Cantidad, i.Unidad, recibido.GetValueOrDefault(i.Id))).ToList()))
            .ToList();

        var problemas = await db.Problemas.AsNoTracking()
            .Where(p => p.ObraId == obraId && p.AlbanilId == albanilId)
            .OrderBy(p => p.Estado == EstadoProblema.Resuelto ? 1 : 0).ThenByDescending(p => p.Fecha).Take(10)
            .Select(p => new ProblemaDto(p.Id, p.Fecha, p.Tipo.ToString(), p.Urgencia.ToString(), p.Descripcion, p.Estado.ToString(), p.Respuesta))
            .ToListAsync(ct);

        var hoy = HoyLocal;
        var parte = await db.Partes.AsNoTracking().Include(p => p.Items)
            .SingleOrDefaultAsync(p => p.AlbanilId == albanilId && p.ObraId == obraId && p.Fecha == hoy, ct);

        return new ObraAlbanilDto(o.Id, o.Codigo, o.Nombre, o.Direccion, o.Localidad, o.Estado.ToString(), o.PorcentajeAvance,
            lista, jornada, avances, pedidos, problemas,
            parte is null ? null : new ParteDto(parte.Fecha, parte.Resumen, parte.HorasTotales,
                parte.Items.OrderBy(i => i.Id).Select(i => new ParteItemDto(i.TareaObraId, i.Horas, i.Detalle)).ToList()),
            o.Latitud, o.Longitud, o.RadioMetros,
            await ControlesAsync(db, obraId, ct),
            await OrdenesAsync(db, obraId, albanilId, ct),
            await PuntosAsync(db, obraId, ct),
            await ClimaService.ProximosAsync(db, obraId, hoy, ct));
    }

    // ================================================================== App: avances de tareas

    /// <summary>
    /// Avance de una tarea (o fotos/videos generales de la obra si no hay tarea). Queda en la bitácora de la obra con
    /// el albañil y su capataz, y recalcula el avance de la tarea. Idempotente por <see cref="NuevoAvanceRequest.Ref"/>.
    /// </summary>
    public async Task<(NuevoAvanceResponse? Respuesta, string? Error)> RegistrarAvanceAsync(int albanilId, int obraId, NuevoAvanceRequest r, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await AccesoAsync(db, albanilId, obraId, ct) is not int capatazId) return (null, ErrorNoAsignada);
        if (r.Ref == Guid.Empty) return (null, "Falta el identificador del envío.");
        if (r.Nota is { Length: > 2000 }) return (null, "La nota es demasiado larga (hasta 2000 caracteres).");

        var existente = await db.Registros.Where(x => x.Ref == r.Ref).Select(x => new { x.Id, x.ObraId, x.AlbanilId, x.TareaObraId }).SingleOrDefaultAsync(ct);
        if (existente is not null)
            return existente.ObraId == obraId && existente.AlbanilId == albanilId
                ? (new NuevoAvanceResponse(existente.Id, await TareaDtoAsync(db, existente.TareaObraId, albanilId, ct)), null)
                : (null, "Envío duplicado.");

        TareaObra? tarea = null;
        if (r.TareaId is int tareaId)
        {
            tarea = await TareasVisibles(db, albanilId, obraId).Include(t => t.Etapa).SingleOrDefaultAsync(t => t.Id == tareaId, ct);
            if (tarea is null) return (null, "La tarea no existe, fue cancelada o es de otro compañero.");
            if (tarea.Unidad is not null && r.Cantidad is < 0 or > 1_000_000) return (null, "La cantidad no es válida.");
            if (tarea.Unidad is null && r.Porcentaje is < 0 or > 100) return (null, "El porcentaje tiene que estar entre 0 y 100.");
        }

        PuntoFoto? punto = null;
        if (r.PuntoFotoId is int puntoId)
        {
            punto = await db.PuntosFoto.SingleOrDefaultAsync(p => p.Id == puntoId && p.ObraId == obraId && p.Activo, ct);
            if (punto is null) return (null, "Ese punto de foto ya no existe en la obra.");
        }

        var fecha = NormalizarFecha(r.FechaUtc);
        var nota = Limpio(r.Nota, 2000);
        decimal? cantidad = tarea?.Unidad is not null && r.Cantidad is > 0 ? Math.Round(r.Cantidad.Value, 2) : null;
        // «Terminada» se marca con 100 % también en tareas con unidad (así el capataz la puede rechazar como cualquier reporte).
        int? porcentaje = tarea is null ? null : r.Terminada ? 100 : tarea.Unidad is null ? r.Porcentaje : null;

        var registro = new RegistroAvance
        {
            ObraId = obraId,
            AlbanilId = albanilId,
            CapatazId = capatazId,
            Ref = r.Ref,
            TareaObraId = tarea?.Id,
            EtapaObraId = tarea?.EtapaObraId,
            Fecha = fecha,
            Titulo = Recortar(punto is not null ? $"Foto fija: {punto.Nombre}" : TituloAvance(tarea, cantidad, porcentaje, r.Terminada, nota), 200),
            Descripcion = nota,
            Cantidad = cantidad,
            PorcentajeTarea = porcentaje,
            // Lo que cuenta para el avance lo confirma el capataz (o el estudio).
            Validacion = tarea is not null && (cantidad is not null || porcentaje is not null) ? EstadoValidacion.Pendiente : null,
            PuntoFotoId = punto?.Id,
            VisibleCliente = punto?.VisibleCliente ?? false,
        };
        db.Registros.Add(registro);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Dos reintentos simultáneos con el mismo Ref: gana el primero.
            await using var db2 = await dbFactory.CreateDbContextAsync(ct);
            var previo = await db2.Registros.Where(x => x.Ref == r.Ref && x.ObraId == obraId && x.AlbanilId == albanilId)
                .Select(x => new { x.Id, x.TareaObraId }).SingleOrDefaultAsync(ct)
                ?? throw new InvalidOperationException("No se pudo guardar el avance.");
            return (new NuevoAvanceResponse(previo.Id, await TareaDtoAsync(db2, previo.TareaObraId, albanilId, ct)), null);
        }

        if (tarea is null) return (new NuevoAvanceResponse(registro.Id, null), null);

        if (r.Terminada)
        {
            tarea.Estado = EstadoTarea.Completada;
            tarea.CompletadaEn = fecha;
        }
        else if (tarea.Estado == EstadoTarea.Pendiente)
            tarea.Estado = EstadoTarea.EnCurso;
        await CalculoTareas.RecalcularAsync(db, tarea, ct);
        // Empezar una tarea pone en curso su etapa (y la obra, si estaba solo presupuestada).
        if (tarea.EtapaObraId is int etapaId && await db.Etapas.AnyAsync(e => e.Id == etapaId && e.Estado == EstadoEtapa.Pendiente, ct))
            await estudio.CambiarEstadoEtapaAsync(etapaId, EstadoEtapa.EnCurso, ct);
        return (new NuevoAvanceResponse(registro.Id, ADto(tarea, albanilId)), null);
    }

    private static string TituloAvance(TareaObra? tarea, decimal? cantidad, int? porcentaje, bool terminada, string? nota)
    {
        if (tarea is null) return nota is null ? "Fotos de avance" : PrimeraLinea(nota);
        var detalle = terminada ? "terminada"
            : cantidad is not null ? $"+{Textos.Numero(cantidad.Value)} {tarea.Unidad}"
            : porcentaje is not null ? $"{porcentaje}%"
            : "fotos de avance";
        return $"{tarea.Titulo}: {detalle}";
    }

    private static async Task<TareaDto?> TareaDtoAsync(AppDbContext db, int? tareaId, int albanilId, CancellationToken ct)
    {
        if (tareaId is null) return null;
        var t = await db.Tareas.AsNoTracking().Include(x => x.Etapa).SingleOrDefaultAsync(x => x.Id == tareaId, ct);
        return t is null ? null : ADto(t, albanilId);
    }

    private static TareaDto ADto(TareaObra t, int albanilId) =>
        new(t.Id, t.Titulo, t.Descripcion, t.Etapa?.Nombre, t.Unidad, t.CantidadObjetivo, t.CantidadHecha, t.Porcentaje,
            t.Estado.ToString(), t.FechaLimite, t.AlbanilId == albanilId,
            t.FechaInicioPlan, CalculoTareas.Atrasada(t, DateOnly.FromDateTime(Textos.AHoraLocal(DateTime.UtcNow))));

    /// <summary>Foto o video de un avance o de un problema (registroRef = Ref del avance o del problema).</summary>
    public async Task<(int? Id, string? Error)> SubirMedioAsync(int albanilId, int obraId, Guid registroRef, Guid medioRef,
        string nombreArchivo, long tamano, Func<Stream> abrir, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await AccesoAsync(db, albanilId, obraId, ct) is null) return (null, ErrorNoAsignada);
        if (medioRef == Guid.Empty || registroRef == Guid.Empty) return (null, "Faltan identificadores del envío.");
        if (!FormatosMedio.TryObtener(nombreArchivo, out var formato)) return (null, FormatosMedio.ErrorFormato);
        if (!FormatosMedio.TamanoValido(tamano)) return (null, FormatosMedio.ErrorTamano);

        var previo = await db.Medios.Where(m => m.Ref == medioRef).Select(m => (int?)m.Id).SingleOrDefaultAsync(ct);
        if (previo is not null) return (previo, null);

        var registro = await db.Registros.Where(x => x.Ref == registroRef && x.ObraId == obraId && x.AlbanilId == albanilId)
            .Select(x => (int?)x.Id).SingleOrDefaultAsync(ct);
        if (registro is null) return (null, ErrorNovedadPendiente);

        return (await FormatosMedio.GuardarAsync(dbFactory, almacen, registro.Value, obraId, medioRef, formato, nombreArchivo, tamano, abrir, Ahora, ct), null);
    }

    // ================================================================== App: asistencia

    public async Task<(int? Id, string? Error)> FicharEntradaAsync(int albanilId, int obraId, FichadaRequest r, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await AccesoAsync(db, albanilId, obraId, ct) is not int capatazId) return (null, ErrorNoAsignada);
        if (r.Ref == Guid.Empty) return (null, "Falta el identificador del envío.");
        if (ValidarUbicacion(r) is { } errorGps) return (null, errorGps);

        var previa = await db.Jornadas.Where(j => j.Ref == r.Ref).Select(j => new { j.Id, j.AlbanilId }).SingleOrDefaultAsync(ct);
        if (previa is not null) return previa.AlbanilId == albanilId ? (previa.Id, null) : (null, "Envío duplicado.");

        var faltante = r.EppFaltante?.Select(x => x.Trim()).Where(x => x.Length > 0).Distinct().Take(10).ToList();
        if (faltante?.Any(x => x.Length > 40) == true) return (null, "Elemento de protección inválido.");

        var obra = await db.Obras.Where(o => o.Id == obraId).Select(o => new { o.Latitud, o.Longitud }).SingleAsync(ct);
        var valorHora = await db.Albaniles.Where(a => a.Id == albanilId).Select(a => a.ValorHora).SingleAsync(ct);
        var jornada = new JornadaAlbanil
        {
            ValorHora = valorHora,
            AlbanilId = albanilId,
            ObraId = obraId,
            CapatazId = capatazId,
            Ref = r.Ref,
            Entrada = NormalizarFecha(r.FechaUtc),
            EntradaLatitud = r.Latitud,
            EntradaLongitud = r.Longitud,
            EntradaPrecision = r.PrecisionMetros,
            EntradaDistanciaMetros = Geo.DistanciaMetros(obra.Latitud, obra.Longitud, r.Latitud, r.Longitud),
            TrabajoEnAltura = r.TrabajoEnAltura,
            EppFaltante = faltante is null ? null : string.Join(", ", faltante),
        };
        db.Jornadas.Add(jornada);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            await using var db2 = await dbFactory.CreateDbContextAsync(ct);
            var id = await db2.Jornadas.Where(j => j.Ref == r.Ref && j.AlbanilId == albanilId).Select(j => (int?)j.Id).SingleOrDefaultAsync(ct);
            return id is null ? throw new InvalidOperationException("No se pudo guardar la entrada.") : (id, null);
        }

        // Sin la protección completa queda un aviso de seguridad (urgente si trabaja en altura sin arnés).
        if (faltante is { Count: > 0 })
        {
            var sinArnes = r.TrabajoEnAltura == true && faltante.Contains(ValoresAlbanil.Arnes, StringComparer.OrdinalIgnoreCase);
            await CrearProblemaAsync(db, DeterministaRef(r.Ref, "epp"), obraId, albanilId, capatazId, null, TipoProblema.Seguridad,
                sinArnes ? UrgenciaProblema.Alta : UrgenciaProblema.Media,
                $"Al fichar la entrada le faltaba: {string.Join(", ", faltante)}{(r.TrabajoEnAltura == true ? " (trabaja en altura)" : "")}.",
                jornada.Entrada, ct);
        }
        return (jornada.Id, null);
    }

    /// <summary>Ref derivado de otro (mismo resultado en cada reintento): para lo que el servidor crea solo.</summary>
    internal static Guid DeterministaRef(Guid origen, string tipo)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{origen:N}:{tipo}"));
        return new Guid(bytes.AsSpan(0, 16));
    }

    /// <summary>
    /// Cierra la jornada de la entrada <see cref="FichadaRequest.Ref"/>. No exige que la obra siga asignada: aunque el
    /// estudio se la haya quitado en el día, el albañil tiene que poder fichar la salida de lo que ya trabajó.
    /// </summary>
    public async Task<(int? Id, string? Error)> FicharSalidaAsync(int albanilId, int obraId, FichadaRequest r, CancellationToken ct = default)
    {
        if (r.Ref == Guid.Empty) return (null, "Falta el identificador de la entrada.");
        if (ValidarUbicacion(r) is { } errorGps) return (null, errorGps);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var j = await db.Jornadas.SingleOrDefaultAsync(x => x.Ref == r.Ref && x.AlbanilId == albanilId && x.ObraId == obraId, ct);
        if (j is null) return (null, ErrorEntradaPendiente);
        if (j.Salida is not null) return (j.Id, null); // reintento

        var salida = NormalizarFecha(r.FechaUtc);
        j.Salida = salida < j.Entrada ? j.Entrada : salida;
        j.SalidaLatitud = r.Latitud;
        j.SalidaLongitud = r.Longitud;
        j.SalidaPrecision = r.PrecisionMetros;
        var obra = await db.Obras.Where(o => o.Id == obraId).Select(o => new { o.Latitud, o.Longitud }).SingleAsync(ct);
        j.SalidaDistanciaMetros = Geo.DistanciaMetros(obra.Latitud, obra.Longitud, r.Latitud, r.Longitud);
        await db.SaveChangesAsync(ct);
        return (j.Id, null);
    }

    private static string? ValidarUbicacion(FichadaRequest r) =>
        r.Latitud is < -90 or > 90 || r.Longitud is < -180 or > 180 || r.PrecisionMetros is < 0
        || (r.Latitud is null) != (r.Longitud is null)
            ? "Ubicación inválida."
            : null;

    // ================================================================== App: pedidos, problemas y partes

    public async Task<(int? Id, string? Error)> PedirMaterialAsync(int albanilId, int obraId, NuevoPedidoRequest r, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await AccesoAsync(db, albanilId, obraId, ct) is not int capatazId) return (null, ErrorNoAsignada);
        if (r.Ref == Guid.Empty) return (null, "Falta el identificador del envío.");
        if (r.Items is null || r.Items.Count == 0) return (null, "Agregá al menos un material.");
        if (r.Items.Count > RutasAlbanil.MaximoItemsPedido) return (null, $"Hasta {RutasAlbanil.MaximoItemsPedido} materiales por pedido.");
        foreach (var i in r.Items)
        {
            if (string.IsNullOrWhiteSpace(i.Material) || i.Material.Trim().Length > 150) return (null, "Escribí qué material (hasta 150 caracteres).");
            if (i.Cantidad is <= 0 or > 1_000_000) return (null, $"La cantidad de «{i.Material.Trim()}» no es válida.");
            if (string.IsNullOrWhiteSpace(i.Unidad) || i.Unidad.Trim().Length > 20) return (null, $"Falta la unidad de «{i.Material.Trim()}».");
        }
        if (r.Nota is { Length: > 1000 }) return (null, "La nota es demasiado larga (hasta 1000 caracteres).");

        var previo = await db.Pedidos.Where(p => p.Ref == r.Ref).Select(p => new { p.Id, p.AlbanilId }).SingleOrDefaultAsync(ct);
        if (previo is not null) return previo.AlbanilId == albanilId ? (previo.Id, null) : (null, "Envío duplicado.");
        if (r.TareaId is int tareaId && !await TareasVisibles(db, albanilId, obraId).AnyAsync(t => t.Id == tareaId, ct))
            return (null, "La tarea no es de esta obra.");

        var pedido = new PedidoMaterial
        {
            Ref = r.Ref,
            ObraId = obraId,
            AlbanilId = albanilId,
            CapatazId = capatazId,
            TareaObraId = r.TareaId,
            Fecha = NormalizarFecha(r.FechaUtc),
            Urgente = r.Urgente,
            Nota = Limpio(r.Nota, 1000),
            Items = r.Items.Select(i => new PedidoMaterialItem { Material = i.Material.Trim(), Cantidad = Math.Round(i.Cantidad, 2), Unidad = i.Unidad.Trim() }).ToList(),
        };
        db.Pedidos.Add(pedido);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            await using var db2 = await dbFactory.CreateDbContextAsync(ct);
            var id = await db2.Pedidos.Where(p => p.Ref == r.Ref && p.AlbanilId == albanilId).Select(p => (int?)p.Id).SingleOrDefaultAsync(ct);
            return id is null ? throw new InvalidOperationException("No se pudo guardar el pedido.") : (id, null);
        }
        return (pedido.Id, null);
    }

    /// <summary>
    /// Aviso de problema. Además del registro del problema, crea una entrada en la bitácora con el mismo Ref: ahí se
    /// adjuntan las fotos y videos (mismo endpoint de medios que los avances).
    /// </summary>
    public async Task<(int? Id, string? Error)> ReportarProblemaAsync(int albanilId, int obraId, NuevoProblemaRequest r, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await AccesoAsync(db, albanilId, obraId, ct) is not int capatazId) return (null, ErrorNoAsignada);
        if (r.Ref == Guid.Empty) return (null, "Falta el identificador del envío.");
        if (!Enum.TryParse<TipoProblema>(r.Tipo, ignoreCase: true, out var tipo) || !Enum.IsDefined(tipo)) return (null, "Elegí qué tipo de problema es.");
        if (!Enum.TryParse<UrgenciaProblema>(r.Urgencia, ignoreCase: true, out var urgencia) || !Enum.IsDefined(urgencia)) return (null, "Elegí la urgencia.");
        if (string.IsNullOrWhiteSpace(r.Descripcion) || r.Descripcion.Trim().Length > 2000) return (null, "Contá qué pasó (hasta 2000 caracteres).");

        var previo = await db.Problemas.Where(p => p.Ref == r.Ref).Select(p => new { p.Id, p.AlbanilId }).SingleOrDefaultAsync(ct);
        if (previo is not null) return previo.AlbanilId == albanilId ? (previo.Id, null) : (null, "Envío duplicado.");
        if (await db.Registros.AnyAsync(x => x.Ref == r.Ref, ct)) return (null, "Envío duplicado.");
        if (r.TareaId is int tareaId && !await TareasVisibles(db, albanilId, obraId).AnyAsync(t => t.Id == tareaId, ct))
            return (null, "La tarea no es de esta obra.");

        return (await CrearProblemaAsync(db, r.Ref, obraId, albanilId, capatazId, r.TareaId, tipo, urgencia, r.Descripcion.Trim(),
            NormalizarFecha(r.FechaUtc), ct), null);
    }

    /// <summary>
    /// Problema + su entrada de bitácora (mismo Ref, ahí van las fotos). Idempotente por Ref: si ya existe (reintento,
    /// o dos envíos simultáneos) devuelve el que está.
    /// </summary>
    internal async Task<int> CrearProblemaAsync(AppDbContext db, Guid referencia, int obraId, int albanilId, int capatazId, int? tareaId,
        TipoProblema tipo, UrgenciaProblema urgencia, string descripcion, DateTime fecha, CancellationToken ct)
    {
        if (await db.Problemas.Where(p => p.Ref == referencia).Select(p => (int?)p.Id).SingleOrDefaultAsync(ct) is int previo) return previo;
        var etapa = tareaId is null ? null : await db.Tareas.Where(t => t.Id == tareaId).Select(t => t.EtapaObraId).SingleAsync(ct);
        var registro = new RegistroAvance
        {
            ObraId = obraId,
            AlbanilId = albanilId,
            CapatazId = capatazId,
            Ref = referencia,
            TareaObraId = tareaId,
            EtapaObraId = etapa,
            Fecha = fecha,
            Titulo = Recortar($"⚠ Problema ({Textos.Etiqueta(tipo)}): {PrimeraLinea(descripcion)}", 200),
            Descripcion = Recortar(descripcion, 2000),
        };
        var problema = new ProblemaObra
        {
            Ref = referencia,
            ObraId = obraId,

            AlbanilId = albanilId,

            CapatazId = capatazId,
            TareaObraId = tareaId,
            Registro = registro,
            Tipo = tipo,
            Urgencia = urgencia,
            Descripcion = Recortar(descripcion, 2000),
            Fecha = fecha,
        };
        db.Problemas.Add(problema);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            await using var db2 = await dbFactory.CreateDbContextAsync(ct);
            return await db2.Problemas.Where(p => p.Ref == referencia).Select(p => (int?)p.Id).SingleOrDefaultAsync(ct)
                ?? throw new InvalidOperationException("No se pudo guardar el problema.");
        }
        return problema.Id;
    }

    /// <summary>Parte del día: uno por albañil, obra y día. Mandarlo de nuevo lo reemplaza (por eso no necesita Ref).</summary>
    public Task<(int? Id, string? Error)> GuardarParteAsync(int albanilId, int obraId, ParteRequest r, CancellationToken ct = default) =>
        GuardarParteAsync(albanilId, obraId, r, reintento: false, ct);

    private async Task<(int? Id, string? Error)> GuardarParteAsync(int albanilId, int obraId, ParteRequest r, bool reintento, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await AccesoAsync(db, albanilId, obraId, ct) is not int capatazId) return (null, ErrorNoAsignada);
        var hoy = HoyLocal;
        if (r.Fecha > hoy.AddDays(1) || r.Fecha < hoy.AddDays(-7)) return (null, "El parte tiene que ser de esta semana.");
        var items = r.Items ?? [];
        if (items.Count > 50) return (null, "Demasiados renglones.");
        if (items.Any(i => i.Horas is <= 0 or > RutasAlbanil.MaximoHorasDia)) return (null, "Las horas de cada tarea tienen que estar entre 0 y 24.");
        var total = items.Sum(i => i.Horas);
        if (total > RutasAlbanil.MaximoHorasDia) return (null, "El total del día no puede superar las 24 horas.");
        if (items.Any(i => i.Detalle is { Length: > 300 })) return (null, "El detalle de una tarea es demasiado largo (hasta 300 caracteres).");
        if (r.Resumen is { Length: > 2000 }) return (null, "El resumen es demasiado largo (hasta 2000 caracteres).");
        var resumen = Limpio(r.Resumen, 2000);
        if (resumen is null && items.Count == 0) return (null, "Contá qué hiciste o cargá las horas.");

        var tareaIds = items.Where(i => i.TareaId is not null).Select(i => i.TareaId!.Value).Distinct().ToList();
        // Una tarea terminada hoy sigue valiendo en el parte; solo se rechazan las ajenas o de otra obra.
        if (tareaIds.Count > 0 && await db.Tareas.CountAsync(t => tareaIds.Contains(t.Id) && t.ObraId == obraId && (t.AlbanilId == null || t.AlbanilId == albanilId), ct) != tareaIds.Count)
            return (null, "Alguna tarea no es de esta obra.");

        var parte = await db.Partes.Include(p => p.Items).SingleOrDefaultAsync(p => p.AlbanilId == albanilId && p.ObraId == obraId && p.Fecha == r.Fecha, ct);
        var nuevo = parte is null;
        if (parte is null)
        {
            parte = new ParteDiario { AlbanilId = albanilId, ObraId = obraId, Fecha = r.Fecha };
            db.Partes.Add(parte);
        }
        parte.CapatazId = capatazId;
        parte.Resumen = resumen;
        parte.HorasTotales = Math.Round(total, 2);
        db.ParteItems.RemoveRange(parte.Items);
        parte.Items = items.Select(i => new ParteDiarioItem { TareaObraId = i.TareaId, Horas = Math.Round(i.Horas, 2), Detalle = Limpio(i.Detalle, 300) }).ToList();
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) when (nuevo && !reintento)
        {
            // Dos envíos simultáneos del mismo día: el segundo vuelve a intentar sobre el que ya quedó guardado.
            return await GuardarParteAsync(albanilId, obraId, r, reintento: true, ct);
        }
        return (parte.Id, null);
    }

    // ================================================================== util

    internal static string DetallePedido(PedidoMaterial p) =>
        string.Join(", ", p.Items.OrderBy(i => i.Id).Select(i => $"{Textos.Numero(i.Cantidad)} {i.Unidad} {i.Material}"));

    /// <summary>La fecha es la del teléfono (cuándo pasó), pero sin aceptar fechas futuras ni muy viejas.</summary>
    private DateTime NormalizarFecha(DateTime fechaUtc)
    {
        var fecha = fechaUtc.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(fechaUtc, DateTimeKind.Utc) : fechaUtc.ToUniversalTime();
        return fecha > Ahora.AddMinutes(5) || fecha < Ahora.AddDays(-60) ? Ahora : fecha;
    }

    private static string? Limpio(string? texto, int max)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;
        var t = texto.Trim();
        return t.Length <= max ? t : t[..max];
    }

    private static string Recortar(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

    private static string PrimeraLinea(string s)
    {
        var i = s.IndexOfAny(['\r', '\n']);
        return i < 0 ? s : s[..i];
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
