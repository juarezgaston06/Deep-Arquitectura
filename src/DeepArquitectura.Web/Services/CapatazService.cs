using System.Security.Cryptography;
using System.Text;
using DeepArquitectura.Contracts;
using DeepArquitectura.Web.Data;
using DeepArquitectura.Web.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DeepArquitectura.Web.Services;

/// <summary>
/// Capataces que usan la app Android: alta y PIN desde el panel, login del teléfono con token revocable,
/// y todo lo que pueden hacer sobre las obras que tienen asignadas (y solo esas).
/// </summary>
public class CapatazService(IDbContextFactory<AppDbContext> dbFactory, IAlmacenMedios almacen, EstudioService estudio, TimeProvider? reloj = null)
{
    public const int PinMinimo = 6;
    public const int PinMaximo = 20;
    public static readonly TimeSpan DuracionSesion = TimeSpan.FromDays(90);

    private static readonly PasswordHasher<string> Hasher = new();
    private readonly TimeProvider _reloj = reloj ?? TimeProvider.System;
    private DateTime Ahora => _reloj.GetUtcNow().UtcDateTime;

    public static bool FormatoAdmitido(string? nombreArchivo) => FormatosMedio.Admitido(nombreArchivo);

    // ------------------------------------------------------------------ Panel

    public static string NormalizarUsuario(string? usuario) => (usuario ?? "").Trim().ToLowerInvariant();

    public static string? ValidarPin(string? pin) =>
        string.IsNullOrEmpty(pin) || pin.Length < PinMinimo ? $"El PIN debe tener al menos {PinMinimo} caracteres."
        : pin.Length > PinMaximo ? $"El PIN no puede superar los {PinMaximo} caracteres."
        : null;

    /// <summary>Alta de capataz. Devuelve el id o un mensaje de error.</summary>
    public async Task<(int? Id, string? Error)> CrearAsync(string nombre, string usuario, string? telefono, string pin, IEnumerable<int> obras, CancellationToken ct = default)
    {
        usuario = NormalizarUsuario(usuario);
        if (string.IsNullOrWhiteSpace(nombre)) return (null, "Ingresá el nombre.");
        if (usuario.Length < 3 || usuario.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-')))
            return (null, "El usuario debe tener al menos 3 caracteres: letras, números, punto, guion o guion bajo.");
        if (ValidarPin(pin) is { } errorPin) return (null, errorPin);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await db.Capataces.AnyAsync(c => c.Usuario == usuario, ct)) return (null, $"Ya existe el usuario «{usuario}».");

        var capataz = new Capataz
        {
            Nombre = nombre.Trim(),
            Usuario = usuario,
            Telefono = string.IsNullOrWhiteSpace(telefono) ? null : telefono.Trim(),
            PinHash = Hasher.HashPassword(usuario, pin),
        };
        foreach (var obraId in obras.Distinct()) capataz.Obras.Add(new CapatazObra { ObraId = obraId });
        db.Capataces.Add(capataz);
        await db.SaveChangesAsync(ct);
        return (capataz.Id, null);
    }

    public async Task ActualizarDatosAsync(int id, string nombre, string? telefono, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.Capataces.Where(c => c.Id == id).ExecuteUpdateAsync(s => s
            .SetProperty(c => c.Nombre, nombre.Trim())
            .SetProperty(c => c.Telefono, string.IsNullOrWhiteSpace(telefono) ? null : telefono.Trim()), ct);
    }

    /// <summary>Nuevo PIN: cierra las sesiones abiertas en los teléfonos.</summary>
    public async Task<string?> CambiarPinAsync(int id, string pin, CancellationToken ct = default)
    {
        if (ValidarPin(pin) is { } error) return error;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var c = await db.Capataces.SingleAsync(x => x.Id == id, ct);
        c.PinHash = Hasher.HashPassword(c.Usuario, pin);
        await db.SaveChangesAsync(ct);
        await db.SesionesCapataz.Where(s => s.CapatazId == id).ExecuteDeleteAsync(ct);
        return null;
    }

    /// <summary>Desactivar corta el acceso en el momento (y cierra sesiones).</summary>
    public async Task CambiarActivoAsync(int id, bool activo, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.Capataces.Where(c => c.Id == id).ExecuteUpdateAsync(s => s.SetProperty(c => c.Activo, activo), ct);
        if (!activo) await db.SesionesCapataz.Where(s => s.CapatazId == id).ExecuteDeleteAsync(ct);
    }

    public async Task<int> CerrarSesionesAsync(int id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.SesionesCapataz.Where(s => s.CapatazId == id).ExecuteDeleteAsync(ct);
    }

    /// <summary>Deja asignadas exactamente estas obras.</summary>
    public async Task AsignarObrasAsync(int id, IEnumerable<int> obras, CancellationToken ct = default)
    {
        var deseadas = obras.ToHashSet();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var actuales = await db.CapatazObras.Where(x => x.CapatazId == id).ToListAsync(ct);
        db.CapatazObras.RemoveRange(actuales.Where(a => !deseadas.Contains(a.ObraId)));
        foreach (var obraId in deseadas.Except(actuales.Select(a => a.ObraId)))
            db.CapatazObras.Add(new CapatazObra { CapatazId = id, ObraId = obraId });
        await db.SaveChangesAsync(ct);
    }

    public async Task AsignarAsync(int capatazId, int obraId, bool asignado, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var existe = await db.CapatazObras.AnyAsync(x => x.CapatazId == capatazId && x.ObraId == obraId, ct);
        if (asignado && !existe) { db.CapatazObras.Add(new CapatazObra { CapatazId = capatazId, ObraId = obraId }); await db.SaveChangesAsync(ct); }
        else if (!asignado && existe) await db.CapatazObras.Where(x => x.CapatazId == capatazId && x.ObraId == obraId).ExecuteDeleteAsync(ct);
    }

    // ------------------------------------------------------------------ App: sesión

    public async Task<LoginResponse?> LoginAsync(string? usuario, string? pin, string? dispositivo, CancellationToken ct = default)
    {
        var u = NormalizarUsuario(usuario);
        if (u.Length == 0 || string.IsNullOrEmpty(pin) || pin.Length > PinMaximo) return null;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var c = await db.Capataces.SingleOrDefaultAsync(x => x.Usuario == u, ct);
        if (c is null)
        {
            Hasher.VerifyHashedPassword(u, FalsoHash, pin); // mismo costo que un usuario real: no revela cuáles existen
            return null;
        }
        if (!c.Activo || Hasher.VerifyHashedPassword(c.Usuario, c.PinHash, pin) == PasswordVerificationResult.Failed) return null;

        var token = Base64Url(RandomNumberGenerator.GetBytes(32));
        db.SesionesCapataz.Add(new SesionCapataz
        {
            CapatazId = c.Id,
            TokenHash = HashToken(token),
            Dispositivo = string.IsNullOrWhiteSpace(dispositivo) ? null : dispositivo.Trim()[..Math.Min(dispositivo.Trim().Length, 120)],
            CreadaEn = Ahora,
            UltimoUso = Ahora,
            ExpiraEn = Ahora + DuracionSesion,
        });
        c.UltimoAcceso = Ahora;
        await db.SaveChangesAsync(ct);
        return new LoginResponse(token, await PerfilAsync(db, c, ct));
    }

    /// <summary>Capataz dueño del token, si la sesión sigue vigente y el capataz activo.</summary>
    public async Task<int?> AutenticarAsync(string? token, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 100) return null;
        var hash = HashToken(token);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var s = await db.SesionesCapataz.Include(x => x.Capataz).SingleOrDefaultAsync(x => x.TokenHash == hash, ct);
        if (s is null || !s.Capataz.Activo || s.ExpiraEn < Ahora) return null;

        // Uso deslizante: si el teléfono se usa, la sesión no vence. Se graba como mucho cada 10 minutos.
        if (Ahora - s.UltimoUso > TimeSpan.FromMinutes(10))
        {
            s.UltimoUso = Ahora;
            s.ExpiraEn = Ahora + DuracionSesion;
            s.Capataz.UltimoAcceso = Ahora;
            await db.SaveChangesAsync(ct);
        }
        return s.CapatazId;
    }

    public async Task LogoutAsync(string token, CancellationToken ct = default)
    {
        var hash = HashToken(token);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.SesionesCapataz.Where(s => s.TokenHash == hash).ExecuteDeleteAsync(ct);
    }

    /// <summary>Borra las sesiones de teléfonos que ya vencieron (no sirven para nada y la tabla crecería sin fin).</summary>
    public async Task<int> LimpiarSesionesVencidasAsync(CancellationToken ct = default)
    {
        var ahora = Ahora;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.SesionesCapataz.Where(s => s.ExpiraEn < ahora).ExecuteDeleteAsync(ct);
    }

    public async Task<CapatazDto?> PerfilAsync(int capatazId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var c = await db.Capataces.SingleOrDefaultAsync(x => x.Id == capatazId, ct);
        return c is null ? null : await PerfilAsync(db, c, ct);
    }

    private static async Task<CapatazDto> PerfilAsync(AppDbContext db, Capataz c, CancellationToken ct)
    {
        var obras = await db.Obras.AsNoTracking()
            .Where(o => db.CapatazObras.Any(x => x.CapatazId == c.Id && x.ObraId == o.Id))
            .Include(o => o.Etapas)
            .OrderBy(o => o.Nombre)
            .ToListAsync(ct);
        return new CapatazDto(c.Id, c.Nombre, c.Usuario,
            obras.Select(o => new ObraResumenDto(o.Id, o.Codigo, o.Nombre, o.Direccion, o.Localidad, o.Estado.ToString(), o.PorcentajeAvance)).ToList());
    }

    // ------------------------------------------------------------------ App: obra

    public async Task<bool> PuedeVerObraAsync(int capatazId, int obraId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.CapatazObras.AnyAsync(x => x.CapatazId == capatazId && x.ObraId == obraId, ct);
    }

    public async Task<ObraDetalleDto?> ObraAsync(int capatazId, int obraId, CancellationToken ct = default)
    {
        if (!await PuedeVerObraAsync(capatazId, obraId, ct)) return null;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var o = await db.Obras.AsNoTracking().Include(x => x.Etapas).SingleAsync(x => x.Id == obraId, ct);
        var registros = await db.Registros.AsNoTracking()
            .Where(r => r.ObraId == obraId)
            .OrderByDescending(r => r.Fecha).Take(30)
            .Select(r => new RegistroDto(r.Id, r.Fecha, r.Titulo, r.Descripcion,
                r.Etapa != null ? r.Etapa.Nombre : null,
                r.Albanil != null ? r.Albanil.Nombre : r.Capataz != null ? r.Capataz.Nombre : "Estudio",
                r.Medios.Count(m => m.Tipo == TipoMedio.Foto),
                r.Medios.Count(m => m.Tipo == TipoMedio.Video)))
            .ToListAsync(ct);
        return new ObraDetalleDto(o.Id, o.Codigo, o.Nombre, o.Direccion, o.Localidad, o.Estado.ToString(), o.PorcentajeAvance,
            o.Etapas.OrderBy(e => e.Orden).Select(e => new EtapaDto(e.Id, e.Orden, e.Nombre, e.Estado.ToString(), e.FechaInicio, e.FechaFin)).ToList(),
            registros);
    }

    /// <summary>Crea la novedad, o devuelve la existente si el teléfono reintenta el mismo envío (mismo Ref).</summary>
    public async Task<(int? Id, string? Error)> RegistrarAsync(int capatazId, int obraId, NuevoRegistroRequest r, CancellationToken ct = default)
    {
        if (!await PuedeVerObraAsync(capatazId, obraId, ct)) return (null, "Obra no asignada.");
        if (r.Ref == Guid.Empty) return (null, "Falta el identificador del envío.");
        if (string.IsNullOrWhiteSpace(r.Titulo) || r.Titulo.Length > 200) return (null, "Escribí qué pasó (hasta 200 caracteres).");
        if (r.Descripcion is { Length: > 4000 }) return (null, "El detalle es demasiado largo.");

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var existente = await db.Registros.Where(x => x.Ref == r.Ref).Select(x => new { x.Id, x.ObraId, x.CapatazId, x.AlbanilId }).SingleOrDefaultAsync(ct);
        if (existente is not null)
            return existente.ObraId == obraId && existente.CapatazId == capatazId && existente.AlbanilId is null ? (existente.Id, null) : (null, "Envío duplicado.");

        if (r.EtapaId is int etapaId && !await db.Etapas.AnyAsync(e => e.Id == etapaId && e.ObraId == obraId, ct))
            return (null, "La etapa no es de esta obra.");

        // La fecha es la del teléfono (cuándo se sacó), pero sin aceptar fechas futuras ni muy viejas.
        var fecha = r.FechaUtc.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(r.FechaUtc, DateTimeKind.Utc) : r.FechaUtc.ToUniversalTime();
        if (fecha > Ahora.AddMinutes(5) || fecha < Ahora.AddDays(-60)) fecha = Ahora;

        var registro = new RegistroAvance
        {
            ObraId = obraId,
            CapatazId = capatazId,
            Ref = r.Ref,
            EtapaObraId = r.EtapaId,
            Fecha = fecha,
            Titulo = r.Titulo.Trim(),
            Descripcion = string.IsNullOrWhiteSpace(r.Descripcion) ? null : r.Descripcion.Trim(),
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
            var id = await db2.Registros.Where(x => x.Ref == r.Ref && x.ObraId == obraId).Select(x => (int?)x.Id).SingleOrDefaultAsync(ct);
            return id is null ? throw new InvalidOperationException("No se pudo guardar la novedad.") : (id, null);
        }
        return (registro.Id, null);
    }

    /// <summary>Guarda una foto o video de una novedad. Idempotente por <paramref name="medioRef"/>.</summary>
    public async Task<(int? Id, string? Error)> SubirMedioAsync(int capatazId, int obraId, Guid registroRef, Guid medioRef,
        string nombreArchivo, long tamano, Func<Stream> abrir, CancellationToken ct = default)
    {
        if (!await PuedeVerObraAsync(capatazId, obraId, ct)) return (null, "Obra no asignada.");
        if (medioRef == Guid.Empty || registroRef == Guid.Empty) return (null, "Faltan identificadores del envío.");
        if (!FormatosMedio.TryObtener(nombreArchivo, out var formato)) return (null, FormatosMedio.ErrorFormato);
        if (!FormatosMedio.TamanoValido(tamano)) return (null, FormatosMedio.ErrorTamano);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var previo = await db.Medios.Where(m => m.Ref == medioRef).Select(m => (int?)m.Id).SingleOrDefaultAsync(ct);
        if (previo is not null) return (previo, null);

        var registro = await db.Registros.Where(x => x.Ref == registroRef && x.ObraId == obraId && x.CapatazId == capatazId && x.AlbanilId == null)
            .Select(x => (int?)x.Id).SingleOrDefaultAsync(ct);
        if (registro is null) return (null, "La novedad todavía no llegó.");

        return (await FormatosMedio.GuardarAsync(dbFactory, almacen, registro.Value, obraId, medioRef, formato, nombreArchivo!, tamano, abrir, Ahora, ct), null);
    }

    /// <summary>El capataz marca una etapa como pendiente, en curso o completada (omitir lo decide el estudio).</summary>
    public async Task<string?> CambiarEtapaAsync(int capatazId, int obraId, int etapaId, string? estado, CancellationToken ct = default)
    {
        if (!await PuedeVerObraAsync(capatazId, obraId, ct)) return "Obra no asignada.";
        if (!Enum.TryParse<EstadoEtapa>(estado, ignoreCase: true, out var nuevo) || !Enum.IsDefined(nuevo) || nuevo == EstadoEtapa.Omitida)
            return "Estado de etapa inválido.";
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await db.Etapas.AnyAsync(e => e.Id == etapaId && e.ObraId == obraId, ct)) return "La etapa no es de esta obra.";
        await estudio.CambiarEstadoEtapaAsync(etapaId, nuevo, ct);
        return null;
    }

    // ------------------------------------------------------------------ util

    private static readonly string FalsoHash = Hasher.HashPassword("x", Guid.NewGuid().ToString());

    internal static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
