using DeepArquitectura.Web.Data;
using DeepArquitectura.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace DeepArquitectura.Web.Services;

/// <summary>Lo que hace el estudio desde el panel con la app del cliente: acceso, documentos, mensajes y aprobaciones.</summary>
public class GestionCliente(IDbContextFactory<AppDbContext> dbFactory, IAlmacenMedios almacen, IMemoryCache cache, TimeProvider? reloj = null)
{
    private readonly TimeProvider _reloj = reloj ?? TimeProvider.System;
    private DateTime Ahora => _reloj.GetUtcNow().UtcDateTime;
    public const string AutorEstudio = "Deep Arquitectura";

    // ---------------- Acceso ----------------

    public async Task<AccesoCliente?> AccesoAsync(int obraId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.AccesosCliente.AsNoTracking().SingleOrDefaultAsync(a => a.ObraId == obraId, ct);
    }

    /// <summary>
    /// Crea el acceso o da un PIN nuevo (y con <paramref name="nuevoCodigo"/> también otro código). Las sesiones abiertas
    /// se cierran. Devuelve el PIN en claro: solo se ve esta vez, no se guarda.
    /// </summary>
    public async Task<(string Codigo, string Pin)> GenerarAccesoAsync(int obraId, bool nuevoCodigo = false, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await db.Obras.AnyAsync(o => o.Id == obraId, ct)) throw new InvalidOperationException("La obra no existe.");
        var a = await db.AccesosCliente.SingleOrDefaultAsync(x => x.ObraId == obraId, ct);
        if (a is null) db.AccesosCliente.Add(a = new AccesoCliente { ObraId = obraId });
        if (a.Codigo == "" || nuevoCodigo)
        {
            string codigo;
            do codigo = AccesoClientes.NuevoCodigo();
            while (await db.AccesosCliente.AnyAsync(x => x.Codigo == codigo, ct));
            a.Codigo = codigo;
        }
        var pin = AccesoClientes.NuevoPin();
        a.PinHash = AccesoClientes.HashPin(a.Codigo, pin);
        a.Sello = AccesoClientes.NuevoSello();
        a.Activo = true;
        a.IntentosFallidos = 0;
        a.BloqueadoHasta = null;
        await db.SaveChangesAsync(ct);
        cache.Remove(AppCliente.ClaveSello(obraId));
        return (a.Codigo, pin);
    }

    /// <summary>El código deja de funcionar y se cierran las sesiones (se puede volver a activar con un PIN nuevo).</summary>
    public async Task DesactivarAccesoAsync(int obraId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var sello = AccesoClientes.NuevoSello();
        await db.AccesosCliente.Where(a => a.ObraId == obraId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.Activo, false).SetProperty(a => a.Sello, sello), ct);
        cache.Remove(AppCliente.ClaveSello(obraId));
    }

    /// <summary>Texto listo para mandar por WhatsApp o email.</summary>
    public static string Invitacion(string nombreObra, string urlApp, string codigo, string pin) =>
        $"Hola! Ya podés seguir tu obra «{nombreObra}» desde el celular.\n\n" +
        $"1. Entrá a {urlApp}\n2. Código de obra: {codigo}\n3. PIN: {pin}\n\n" +
        "Vas a ver el avance, las fotos, los pagos y los planos, y nos podés escribir desde ahí. " +
        "Tocá «Instalar» (o en iPhone: Compartir → Agregar a inicio) para tenerla como app. No compartas el PIN.";

    // ---------------- Documentos ----------------

    public async Task<List<DocumentoObra>> DocumentosAsync(int obraId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return (await db.Documentos.AsNoTracking().Where(d => d.ObraId == obraId).ToListAsync(ct))
            .OrderBy(d => d.Categoria).ThenByDescending(d => d.CreadoEn).ToList();
    }

    public async Task<(int? Id, string? Error)> SubirDocumentoAsync(int obraId, string? titulo, CategoriaDocumento categoria, bool visible,
        string nombreArchivo, long tamano, Func<Stream> abrir, CancellationToken ct = default)
    {
        if (!AccesoClientes.TryFormatoDocumento(nombreArchivo, out var contentType)) return (null, AccesoClientes.ErrorFormatoDocumento);
        if (tamano <= 0 || tamano > AccesoClientes.TamanoMaximoDocumento) return (null, "El archivo está vacío o supera los 50 MB.");
        titulo = AppCliente.Recortar(titulo, 200) ?? AppCliente.Recortar(Path.GetFileNameWithoutExtension(nombreArchivo), 200) ?? "Documento";

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await db.Obras.AnyAsync(o => o.Id == obraId, ct)) return (null, "La obra no existe.");
        var ahora = Ahora;
        var ruta = $"obra-{obraId}/documentos/{ahora:yyyy}/{Guid.NewGuid():N}{Path.GetExtension(nombreArchivo).ToLowerInvariant()}";
        await using (var s = abrir())
            await almacen.GuardarAsync(ruta, s, contentType, ct);

        var nombre = Path.GetFileName(nombreArchivo);
        var d = new DocumentoObra
        {
            ObraId = obraId, Titulo = titulo, Categoria = categoria, Ruta = ruta, ContentType = contentType, Tamano = tamano,
            NombreOriginal = nombre.Length > 260 ? nombre[..260] : nombre, VisibleCliente = visible,
        };
        db.Documentos.Add(d);
        await db.SaveChangesAsync(ct);
        return (d.Id, null);
    }

    public async Task CambiarVisibleAsync(int documentoId, bool visible, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.Documentos.Where(d => d.Id == documentoId).ExecuteUpdateAsync(s => s.SetProperty(d => d.VisibleCliente, visible), ct);
    }

    public async Task BorrarDocumentoAsync(int documentoId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var d = await db.Documentos.SingleOrDefaultAsync(x => x.Id == documentoId, ct);
        if (d is null) return;
        await db.Aprobaciones.Where(a => a.DocumentoObraId == documentoId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.DocumentoObraId, (int?)null), ct);
        db.Documentos.Remove(d);
        await db.SaveChangesAsync(ct);
        await almacen.BorrarAsync(d.Ruta, ct);
    }

    /// <summary>Para verlo desde el panel (cualquier documento, visible o no).</summary>
    public async Task<(Stream Contenido, string ContentType, string Nombre)?> AbrirDocumentoAsync(int documentoId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var d = await db.Documentos.AsNoTracking().SingleOrDefaultAsync(x => x.Id == documentoId, ct);
        if (d is null) return null;
        var s = await almacen.AbrirAsync(d.Ruta, ct);
        return s is null ? null : (s, d.ContentType, AppCliente.NombreDescarga(d));
    }

    // ---------------- Mensajes ----------------

    /// <summary>La conversación de la obra. Marca como leídos los mensajes del cliente.</summary>
    public async Task<List<MensajeObra>> MensajesAsync(int obraId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var ahora = Ahora;
        await db.Mensajes.Where(m => m.ObraId == obraId && m.DelCliente && m.LeidoEn == null)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.LeidoEn, ahora), ct);
        return (await db.Mensajes.AsNoTracking().Where(m => m.ObraId == obraId).ToListAsync(ct))
            .OrderBy(m => m.Fecha).ThenBy(m => m.Id).ToList();
    }

    public async Task<string?> ResponderAsync(int obraId, string? texto, CancellationToken ct = default)
    {
        texto = texto?.Trim();
        if (string.IsNullOrEmpty(texto)) return "Escribí la respuesta.";
        if (texto.Length > AppCliente.LargoMaximoMensaje) return $"Máximo {AppCliente.LargoMaximoMensaje} caracteres.";
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.Mensajes.Add(new MensajeObra { ObraId = obraId, DelCliente = false, Autor = AutorEstudio, Texto = texto, Fecha = Ahora });
        await db.SaveChangesAsync(ct);
        return null;
    }

    // ---------------- Aprobaciones ----------------

    public sealed class DatosAprobacion
    {
        public string? Titulo { get; set; }
        public string? Descripcion { get; set; }
        /// <summary>Una por línea. Vacío = aprobar / no aprobar.</summary>
        public string? Opciones { get; set; }
        public decimal? Monto { get; set; }
        public DateOnly? FechaLimite { get; set; }
        public int? DocumentoObraId { get; set; }
    }

    public async Task<(int? Id, string? Error)> PedirAprobacionAsync(int obraId, DatosAprobacion d, CancellationToken ct = default)
    {
        var titulo = AppCliente.Recortar(d.Titulo, 200);
        if (titulo is null) return (null, "Poné un título (ej. «Cerámico del baño»).");
        if (d.Monto is < 0) return (null, "El monto no puede ser negativo.");
        var opciones = (d.Opciones ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(o => o.Length > 200 ? o[..200] : o).Distinct().ToList();
        if (opciones.Count == 1) return (null, "Con una sola opción no hay qué elegir: dejá el campo vacío (aprobar / no aprobar) o agregá otra.");
        if (opciones.Count > 10) return (null, "Máximo 10 opciones.");

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await db.Obras.AnyAsync(o => o.Id == obraId, ct)) return (null, "La obra no existe.");
        if (d.DocumentoObraId is int doc && !await db.Documentos.AnyAsync(x => x.Id == doc && x.ObraId == obraId, ct))
            return (null, "El adjunto no es de esta obra.");
        var a = new AprobacionCliente
        {
            ObraId = obraId, Titulo = titulo, Descripcion = AppCliente.Recortar(d.Descripcion, 4000),
            Opciones = opciones.Count == 0 ? null : string.Join('\n', opciones), Monto = d.Monto, FechaLimite = d.FechaLimite,
            DocumentoObraId = d.DocumentoObraId, RespuestaVista = true,
        };
        db.Aprobaciones.Add(a);
        await db.SaveChangesAsync(ct);
        return (a.Id, null);
    }

    /// <summary>Solo las pendientes: lo que el cliente ya respondió queda como constancia.</summary>
    public async Task<string?> AnularAprobacionAsync(int aprobacionId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var n = await db.Aprobaciones.Where(a => a.Id == aprobacionId && a.Estado == EstadoAprobacion.Pendiente)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.Estado, EstadoAprobacion.Anulada), ct);
        return n == 1 ? null : "El cliente ya respondió: queda como constancia.";
    }

    /// <summary>Todas las de la obra; marca como vistas las respuestas nuevas.</summary>
    public async Task<List<AprobacionCliente>> AprobacionesAsync(int obraId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var lista = (await db.Aprobaciones.AsNoTracking().Include(a => a.Documento).Where(a => a.ObraId == obraId).ToListAsync(ct))
            .OrderBy(a => a.Estado is EstadoAprobacion.Anulada).ThenByDescending(a => a.RespondidoEn ?? a.CreadoEn).ToList();
        await db.Aprobaciones.Where(a => a.ObraId == obraId && !a.RespuestaVista)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.RespuestaVista, true), ct);
        return lista;
    }

    // ---------------- Bandeja del estudio ----------------

    public sealed record ResumenObra(int ObraId, string Codigo, string Obra, string Cliente, string? CodigoCliente, bool Activo,
        DateTime? UltimoIngreso, int MensajesSinLeer, int RespuestasNuevas, int Pendientes, DateTime? UltimoMensaje);

    /// <summary>Obras con acceso de cliente o con actividad (para la bandeja del panel), lo nuevo primero.</summary>
    public async Task<List<ResumenObra>> BandejaAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var accesos = await db.AccesosCliente.AsNoTracking().Select(a => new { a.ObraId, a.Codigo, a.Activo, a.UltimoIngreso }).ToListAsync(ct);
        var mensajes = await db.Mensajes.AsNoTracking().GroupBy(m => m.ObraId)
            .Select(g => new { ObraId = g.Key, SinLeer = g.Count(m => m.DelCliente && m.LeidoEn == null), Ultimo = g.Max(m => m.Fecha) })
            .ToListAsync(ct);
        var aprob = await db.Aprobaciones.AsNoTracking().GroupBy(a => a.ObraId)
            .Select(g => new
            {
                ObraId = g.Key,
                Nuevas = g.Count(a => !a.RespuestaVista && a.Estado != EstadoAprobacion.Pendiente && a.Estado != EstadoAprobacion.Anulada),
                Pendientes = g.Count(a => a.Estado == EstadoAprobacion.Pendiente),
            })
            .ToListAsync(ct);
        var ids = accesos.Select(a => a.ObraId).Concat(mensajes.Select(m => m.ObraId)).Concat(aprob.Select(a => a.ObraId)).Distinct().ToList();
        var obras = await db.Obras.AsNoTracking().Where(o => ids.Contains(o.Id))
            .Select(o => new { o.Id, o.Codigo, o.Nombre, Cliente = o.Cliente.Nombre }).ToListAsync(ct);

        return obras.Select(o =>
            {
                var a = accesos.FirstOrDefault(x => x.ObraId == o.Id);
                var m = mensajes.FirstOrDefault(x => x.ObraId == o.Id);
                var p = aprob.FirstOrDefault(x => x.ObraId == o.Id);
                return new ResumenObra(o.Id, o.Codigo, o.Nombre, o.Cliente, a?.Codigo, a?.Activo ?? false, a?.UltimoIngreso,
                    m?.SinLeer ?? 0, p?.Nuevas ?? 0, p?.Pendientes ?? 0, m?.Ultimo);
            })
            .OrderByDescending(r => r.MensajesSinLeer + r.RespuestasNuevas > 0)
            .ThenByDescending(r => r.UltimoMensaje ?? r.UltimoIngreso ?? DateTime.MinValue)
            .ToList();
    }

    /// <summary>Para el inicio del panel y el menú.</summary>
    public async Task<(int Mensajes, int Respuestas)> NovedadesAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var m = await db.Mensajes.CountAsync(x => x.DelCliente && x.LeidoEn == null, ct);
        var r = await db.Aprobaciones.CountAsync(x => !x.RespuestaVista && x.Estado != EstadoAprobacion.Pendiente && x.Estado != EstadoAprobacion.Anulada, ct);
        return (m, r);
    }
}
