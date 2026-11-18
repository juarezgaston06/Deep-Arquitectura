using System.Text.Json;
using System.Text.Json.Serialization;
using DeepArquitectura.Web.Data;
using DeepArquitectura.Web.Data.Entities;
using DeepArquitectura.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace DeepArquitectura.Web.Presupuestos;

public class PresupuestoService(IDbContextFactory<AppDbContext> dbFactory, EstudioService estudio)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serializar<T>(T valor) => JsonSerializer.Serialize(valor, Json);
    public static T Deserializar<T>(string json) where T : new() =>
        string.IsNullOrWhiteSpace(json) ? new T() : JsonSerializer.Deserialize<T>(json, Json) ?? new T();

    /// <summary>Copia profunda (para editar una versión sin tocar la original).</summary>
    public static T Clonar<T>(T valor) where T : new() => Deserializar<T>(Serializar(valor));

    // ------------------------------------------------------------------ Aranceles

    /// <summary>Carga la escala del PDF 1er semestre 2026 si la base no tiene ninguna.</summary>
    public async Task AsegurarArancelInicialAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await db.Aranceles.AnyAsync(ct)) return;
        var a = ArancelSemilla.Crear();
        db.Aranceles.Add(new ArancelVersion
        {
            Nombre = ArancelSemilla.Nombre,
            VigenteDesde = ArancelSemilla.VigenteDesde,
            VigenteHasta = ArancelSemilla.VigenteHasta,
            ValorBasico = a.ValorBasico,
            Contenido = Serializar(a),
            Notas = "Cargado del PDF «Escala arancelaria y tabla de valores según uso y destino» (Regional 1 Capital).",
        });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Versión a usar en una fecha: la que la cubre; si ninguna la cubre (ej. venció y todavía no se cargó la nueva),
    /// la más reciente que ya empezó, o la más nueva de todas.
    /// </summary>
    public async Task<ArancelVersion?> VigenteAsync(DateOnly fecha, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var todas = await db.Aranceles.AsNoTracking().OrderByDescending(a => a.VigenteDesde).ThenByDescending(a => a.Id).ToListAsync(ct);
        return ElegirVigente(todas, fecha);
    }

    internal static ArancelVersion? ElegirVigente(IReadOnlyList<ArancelVersion> ordenadasDesc, DateOnly fecha) =>
        ordenadasDesc.FirstOrDefault(a => a.VigenteDesde <= fecha && fecha <= a.VigenteHasta)
        ?? ordenadasDesc.FirstOrDefault(a => a.VigenteDesde <= fecha)
        ?? ordenadasDesc.FirstOrDefault();

    public async Task<ArancelVersion> GuardarArancelAsync(ArancelVersion datos, Arancel arancel, CancellationToken ct = default)
    {
        if (datos.VigenteHasta < datos.VigenteDesde) throw new ArgumentException("La vigencia termina antes de empezar.");
        if (arancel.ValorBasico <= 0) throw new ArgumentException("El valor básico tiene que ser mayor a cero.");
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var v = datos.Id == 0 ? new ArancelVersion() : await db.Aranceles.SingleAsync(a => a.Id == datos.Id, ct);
        v.Nombre = string.IsNullOrWhiteSpace(datos.Nombre) ? $"Arancel desde {Textos.Fecha(datos.VigenteDesde)}" : datos.Nombre.Trim();
        v.VigenteDesde = datos.VigenteDesde;
        v.VigenteHasta = datos.VigenteHasta;
        v.Notas = datos.Notas;
        v.ValorBasico = arancel.ValorBasico;
        // Tramos ordenados por límite (los "en adelante" al final) para que la escala progresiva sea consistente.
        foreach (var e in arancel.Escalas())
            e.Tramos = [.. e.Tramos.OrderBy(t => t.Hasta ?? decimal.MaxValue)];
        v.Contenido = Serializar(arancel);
        if (v.Id == 0) db.Aranceles.Add(v);
        await db.SaveChangesAsync(ct);
        return v;
    }

    // ------------------------------------------------------------------ Presupuestos

    /// <summary>
    /// Crea o actualiza un presupuesto. El cálculo se rehace acá (no se confía en lo que muestra la pantalla)
    /// y los renglones quedan guardados tal cual se emitieron.
    /// </summary>
    public async Task<Presupuesto> GuardarAsync(Presupuesto datos, PresupuestoEntrada entrada, CancellationToken ct = default)
    {
        if (datos.Estado == EstadoPresupuesto.Rechazado && datos.MotivoPerdida is null)
            throw new InvalidOperationException("elegí por qué se perdió el presupuesto (precio, plazo, eligió a otro…).");
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var version = await db.Aranceles.AsNoTracking().SingleAsync(a => a.Id == datos.ArancelVersionId, ct);
        var resultado = CalculadoraHonorarios.Calcular(Deserializar<Arancel>(version.Contenido), entrada);

        Presupuesto p;
        if (datos.Id == 0)
        {
            p = new Presupuesto
            {
                Numero = await SiguienteNumeroAsync(db, (datos.Fecha == default ? Textos.HoyLocal() : datos.Fecha).Year, ct),
            };
            db.Presupuestos.Add(p);
        }
        else
        {
            p = await db.Presupuestos.Include(x => x.Items).SingleAsync(x => x.Id == datos.Id, ct);
            db.PresupuestoItems.RemoveRange(p.Items);
            p.Items.Clear();
        }

        p.Titulo = string.IsNullOrWhiteSpace(datos.Titulo) ? "Presupuesto de honorarios" : Recortar(datos.Titulo, 200);
        p.Servicio = datos.Servicio;
        if (p.Estado != datos.Estado)
            p.DecididoEn = datos.Estado is EstadoPresupuesto.Aceptado or EstadoPresupuesto.Rechazado ? DateTime.UtcNow : null;
        p.Estado = datos.Estado;
        p.MotivoPerdida = datos.Estado == EstadoPresupuesto.Rechazado ? datos.MotivoPerdida : null;
        p.DetallePerdida = datos.Estado == EstadoPresupuesto.Rechazado && !string.IsNullOrWhiteSpace(datos.DetallePerdida)
            ? Recortar(datos.DetallePerdida, 1000) : null;
        p.Fecha = datos.Fecha == default ? Textos.HoyLocal() : datos.Fecha;
        p.ValidezDias = Math.Clamp(datos.ValidezDias, 1, 365);
        p.ClienteId = datos.ClienteId is > 0 ? datos.ClienteId : null;
        p.Destinatario = string.IsNullOrWhiteSpace(datos.Destinatario) ? null : Recortar(datos.Destinatario, 200);
        p.Ubicacion = string.IsNullOrWhiteSpace(datos.Ubicacion) ? null : Recortar(datos.Ubicacion, 300);
        p.Notas = string.IsNullOrWhiteSpace(datos.Notas) ? null : Recortar(datos.Notas, 4000);
        p.ArancelVersionId = version.Id;
        p.SuperficieTotal = resultado.SuperficieTotal;
        p.MontoObra = resultado.MontoObra;
        p.Total = resultado.Total;
        p.Parametros = Serializar(entrada);
        var orden = 1;
        foreach (var l in resultado.Lineas)
            p.Items.Add(new PresupuestoItem
            {
                Orden = orden++,
                Seccion = l.Seccion,
                Concepto = Recortar(l.Concepto, 200),
                Detalle = l.Detalle is null ? null : Recortar(l.Detalle, 500),
                Importe = l.Importe,
            });

        await db.SaveChangesAsync(ct);
        return p;
    }

    public async Task CambiarEstadoAsync(int id, EstadoPresupuesto estado, MotivoPerdida? motivo = null, CancellationToken ct = default)
    {
        if (estado == EstadoPresupuesto.Rechazado && motivo is null)
            throw new InvalidOperationException("Elegí por qué se perdió el presupuesto.");
        DateTime? decidido = estado is EstadoPresupuesto.Aceptado or EstadoPresupuesto.Rechazado ? DateTime.UtcNow : null;
        MotivoPerdida? m = estado == EstadoPresupuesto.Rechazado ? motivo : null;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.Presupuestos.Where(p => p.Id == id && p.Estado != estado)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Estado, estado).SetProperty(p => p.ActualizadoEn, DateTime.UtcNow)
                .SetProperty(p => p.DecididoEn, decidido).SetProperty(p => p.MotivoPerdida, m), ct);
    }

    /// <summary>
    /// Presupuesto aceptado → obra en seguimiento (con sus etapas y el total como monto presupuestado).
    /// Idempotente: si ya tiene obra, devuelve esa.
    /// </summary>
    public async Task<int> CrearObraAsync(int presupuestoId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var p = await db.Presupuestos.SingleAsync(x => x.Id == presupuestoId, ct);
        if (p.ObraId is int existente) return existente;
        if (p.ClienteId is not int clienteId)
            throw new InvalidOperationException("Para crear la obra el presupuesto tiene que estar asignado a un cliente.");

        var obra = await estudio.CrearObraAsync(new Obra
        {
            ClienteId = clienteId,
            Nombre = p.Titulo,
            Servicio = p.Servicio,
            Direccion = p.Ubicacion,
            MontoPresupuesto = p.Total,
            Descripcion = $"Creada desde el presupuesto {p.Numero}.",
        }, ct);

        p.ObraId = obra.Id;
        if (p.Estado != EstadoPresupuesto.Aceptado) p.DecididoEn = DateTime.UtcNow;
        p.Estado = EstadoPresupuesto.Aceptado;
        p.MotivoPerdida = null;
        await db.SaveChangesAsync(ct);
        return obra.Id;
    }

    internal static async Task<string> SiguienteNumeroAsync(AppDbContext db, int anio, CancellationToken ct)
    {
        var prefijo = $"PR-{anio}-";
        var numeros = await db.Presupuestos.Where(p => p.Numero.StartsWith(prefijo)).Select(p => p.Numero).ToListAsync(ct);
        var max = numeros.Select(n => int.TryParse(n[prefijo.Length..], out var x) ? x : 0).DefaultIfEmpty(0).Max();
        return $"{prefijo}{max + 1:000}";
    }

    private static string Recortar(string s, int max)
    {
        s = s.Trim();
        return s.Length <= max ? s : s[..max];
    }
}
