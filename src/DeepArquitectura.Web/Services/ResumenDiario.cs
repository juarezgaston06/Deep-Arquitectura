using System.Text;
using DeepArquitectura.Web.Data;
using DeepArquitectura.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;

namespace DeepArquitectura.Web.Services;

/// <summary>
/// Resumen del día por obra a partir de todo lo que entró desde el campo, con alertas de lo urgente.
/// Se arma por reglas (siempre funciona); si Azure OpenAI está configurado, además se puede redactar como texto corrido.
/// </summary>
public class ResumenDiario(IDbContextFactory<AppDbContext> dbFactory, ValidacionAvances validacion, IChatClient? chat = null)
{
    public bool ConIA => chat is not null;

    public sealed record Presente(string Nombre, DateTime Entrada, DateTime? Salida, double? Horas, int? Distancia, string? EppFaltante);

    public sealed record Obra(int Id, string Codigo, string Nombre, List<Presente> Presentes, List<string> Avances, int Fotos, int Videos,
        List<string> ProblemasNuevos, List<string> PedidosNuevos, List<string> Recepciones, List<string> ControlesObservados,
        List<string> NotasPedido, int PorValidar, List<string> Atrasadas, ClimaDiario? Clima, List<Alerta> Alertas)
    {
        public bool SinActividad => Presentes.Count == 0 && Avances.Count == 0 && Fotos + Videos == 0 && ProblemasNuevos.Count == 0;
        public double HorasTotales => Presentes.Sum(p => p.Horas ?? 0);
    }

    /// <summary>Nivel: 2 = urgente, 1 = atender, 0 = informativo.</summary>
    public sealed record Alerta(int Nivel, string Texto);

    public async Task<List<Obra>> GenerarAsync(DateOnly fecha, CancellationToken ct = default)
    {
        var desde = fecha.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddHours(3); // 00:00 en Argentina
        var hasta = desde.AddDays(1);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var activas = new[] { EstadoObra.EnCurso, EstadoObra.EnTramiteMunicipal, EstadoObra.Pausada };
        var obras = await db.Obras.AsNoTracking()
            .Where(o => activas.Contains(o.Estado) || db.Registros.Any(r => r.ObraId == o.Id && r.Fecha >= desde && r.Fecha < hasta))
            .OrderBy(o => o.Nombre).Select(o => new { o.Id, o.Codigo, o.Nombre, o.RadioMetros }).ToListAsync(ct);
        var pendientesValidar = await validacion.PendientesAsync(ct: ct);

        var lista = new List<Obra>();
        foreach (var o in obras)
        {
            var jornadas = await db.Jornadas.AsNoTracking().Include(j => j.Albanil)
                .Where(j => j.ObraId == o.Id && j.Entrada >= desde && j.Entrada < hasta).OrderBy(j => j.Entrada).ToListAsync(ct);
            var registros = await db.Registros.AsNoTracking().Include(r => r.Albanil).Include(r => r.Capataz)
                .Where(r => r.ObraId == o.Id && r.Fecha >= desde && r.Fecha < hasta)
                .Select(r => new { r.Titulo, Autor = r.Albanil != null ? r.Albanil.Nombre : r.Capataz != null ? r.Capataz.Nombre : "Estudio",
                    r.TareaObraId, Fotos = r.Medios.Count(m => m.Tipo == TipoMedio.Foto), Videos = r.Medios.Count(m => m.Tipo == TipoMedio.Video) })
                .ToListAsync(ct);
            var problemas = await db.Problemas.AsNoTracking().Where(p => p.ObraId == o.Id && p.Fecha >= desde && p.Fecha < hasta)
                .Select(p => new { p.Tipo, p.Urgencia, p.Descripcion }).ToListAsync(ct);
            var abiertosAlta = await db.Problemas.CountAsync(p => p.ObraId == o.Id && p.Estado != EstadoProblema.Resuelto && p.Urgencia == UrgenciaProblema.Alta, ct);
            var pedidos = await db.Pedidos.AsNoTracking().Include(p => p.Items).Where(p => p.ObraId == o.Id && p.Fecha >= desde && p.Fecha < hasta).ToListAsync(ct);
            var urgentesPendientes = await db.Pedidos.CountAsync(p => p.ObraId == o.Id && p.Urgente && p.Estado == EstadoPedido.Pendiente, ct);
            var recepciones = await db.Recepciones.AsNoTracking().Include(r => r.Items).Where(r => r.ObraId == o.Id && r.Fecha >= desde && r.Fecha < hasta).ToListAsync(ct);
            var observados = await db.RespuestasControl.AsNoTracking()
                .Where(r => r.Control.Etapa.ObraId == o.Id && r.Fecha >= desde && r.Fecha < hasta && r.Resultado == ResultadoControl.Observado)
                .Select(r => r.Control.Texto + (r.Nota == null ? "" : ": " + r.Nota)).ToListAsync(ct);
            var notas = await db.Asientos.AsNoTracking().Where(a => a.ObraId == o.Id && a.Tipo == TipoAsiento.NotaPedido && a.Fecha >= desde && a.Fecha < hasta)
                .Select(a => $"N° {a.Numero}: {a.Texto}").ToListAsync(ct);
            var atrasadas = await db.Tareas.AsNoTracking()
                .Where(t => t.ObraId == o.Id && (t.Estado == EstadoTarea.Pendiente || t.Estado == EstadoTarea.EnCurso) && t.FechaLimite != null && t.FechaLimite < fecha)
                .Select(t => $"{t.Titulo} (para el {t.FechaLimite:dd/MM}, va {t.PorcentajeAprobado}%)").ToListAsync(ct);
            var clima = await db.Climas.AsNoTracking().SingleOrDefaultAsync(c => c.ObraId == o.Id && c.Fecha == fecha, ct);

            var presentes = jornadas.Select(j => new Presente(j.Albanil.Nombre, j.Entrada, j.Salida, j.Horas, j.EntradaDistanciaMetros, j.EppFaltante)).ToList();
            var alertas = new List<Alerta>();
            foreach (var p in problemas.Where(p => p.Urgencia == UrgenciaProblema.Alta))
                alertas.Add(new(2, $"Problema urgente ({Textos.Etiqueta(p.Tipo)}): {Recortar(p.Descripcion, 120)}"));
            if (abiertosAlta > problemas.Count(p => p.Urgencia == UrgenciaProblema.Alta))
                alertas.Add(new(2, $"{abiertosAlta} problema(s) urgente(s) sin resolver."));
            if (urgentesPendientes > 0) alertas.Add(new(2, $"{urgentesPendientes} pedido(s) de material urgente(s) sin responder."));
            foreach (var c in observados) alertas.Add(new(1, $"Control observado: {Recortar(c, 120)}"));
            foreach (var p in presentes.Where(p => !string.IsNullOrEmpty(p.EppFaltante))) alertas.Add(new(1, $"{p.Nombre} entró sin: {p.EppFaltante}."));
            foreach (var p in presentes.Where(p => p.Distancia > o.RadioMetros)) alertas.Add(new(1, $"{p.Nombre} fichó a {Geo.Texto(p.Distancia!.Value)} de la obra."));
            foreach (var p in presentes.Where(p => p.Salida is null && fecha < DateOnly.FromDateTime(Textos.AHoraLocal(DateTime.UtcNow))))
                alertas.Add(new(1, $"{p.Nombre} no fichó la salida."));
            if (notas.Count > 0) alertas.Add(new(1, $"{notas.Count} nota(s) de pedido nueva(s) en el libro de obra."));
            var porValidar = pendientesValidar.Count(v => v.ObraId == o.Id);
            if (porValidar > 0) alertas.Add(new(0, $"{porValidar} avance(s) esperando validación del capataz."));
            if (atrasadas.Count > 0) alertas.Add(new(1, $"{atrasadas.Count} tarea(s) atrasada(s)."));
            if (clima?.Desfavorable == true) alertas.Add(new(0, $"Día desfavorable: {ClimaService.Descripcion(clima.CodigoTiempo)}, {clima.LluviaMm:0.#} mm."));

            lista.Add(new Obra(o.Id, o.Codigo, o.Nombre, presentes,
                registros.Where(r => r.TareaObraId != null).Select(r => $"{r.Titulo} ({r.Autor})").ToList(),
                registros.Sum(r => r.Fotos), registros.Sum(r => r.Videos),
                problemas.Select(p => $"{Textos.Etiqueta(p.Tipo)} ({p.Urgencia.ToString().ToLowerInvariant()}): {Recortar(p.Descripcion, 150)}").ToList(),
                pedidos.Select(p => (p.Urgente ? "URGENTE: " : "") + AlbanilService.DetallePedido(p)).ToList(),
                recepciones.Select(r => string.Join(", ", r.Items.Select(i => $"{Textos.Numero(i.Cantidad)} {i.Unidad} {i.Material}"))).ToList(),
                observados, notas, porValidar, atrasadas, clima,
                alertas.OrderByDescending(a => a.Nivel).ToList()));
        }
        return lista;
    }

    /// <summary>Texto corrido para leer en un minuto (requiere Azure OpenAI). No inventa: solo usa los datos del resumen.</summary>
    public async Task<string?> RedactarAsync(DateOnly fecha, IReadOnlyList<Obra> obras, CancellationToken ct = default)
    {
        if (chat is null) return null;
        var datos = new StringBuilder($"Resumen de obras del {fecha:dd/MM/yyyy}:\n");
        foreach (var o in obras.Where(o => !o.SinActividad || o.Alertas.Count > 0))
        {
            datos.AppendLine($"\n## {o.Nombre} ({o.Codigo})");
            datos.AppendLine($"Presentes: {string.Join(", ", o.Presentes.Select(p => $"{p.Nombre} {p.Horas:0.#} h"))}; total {o.HorasTotales:0.#} h");
            if (o.Clima is not null) datos.AppendLine($"Clima: {ClimaService.Descripcion(o.Clima.CodigoTiempo)}, lluvia {o.Clima.LluviaMm:0.#} mm");
            foreach (var a in o.Avances) datos.AppendLine($"Avance: {a}");
            datos.AppendLine($"Fotos {o.Fotos}, videos {o.Videos}");
            foreach (var x in o.ProblemasNuevos) datos.AppendLine($"Problema: {x}");
            foreach (var x in o.PedidosNuevos) datos.AppendLine($"Pedido: {x}");
            foreach (var x in o.Recepciones) datos.AppendLine($"Llegó: {x}");
            foreach (var x in o.ControlesObservados) datos.AppendLine($"Control observado: {x}");
            foreach (var x in o.NotasPedido) datos.AppendLine($"Nota de pedido: {x}");
            foreach (var x in o.Atrasadas) datos.AppendLine($"Atrasada: {x}");
            foreach (var a in o.Alertas) datos.AppendLine($"Alerta: {a.Texto}");
        }
        var r = await chat.GetResponseAsync(
        [
            new(ChatRole.System, "Sos el asistente de Deep Arquitectura (estudio de Córdoba). Redactá para el arquitecto un resumen del día " +
                "de sus obras: español rioplatense con voseo, primero lo urgente, después una línea por obra. Máximo 180 palabras. " +
                "Usá SOLO los datos que te paso; si algo no está, no lo menciones. Sin saludos ni despedidas."),
            new(ChatRole.User, datos.ToString()),
        ], new ChatOptions { Temperature = 0.2f, MaxOutputTokens = 500 }, ct);
        return string.IsNullOrWhiteSpace(r.Text) ? null : r.Text.Trim();
    }

    private static string Recortar(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
}
