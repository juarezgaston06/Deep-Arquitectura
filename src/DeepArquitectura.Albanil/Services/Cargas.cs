using System.Text.Json;
using DeepArquitectura.Contracts;

namespace DeepArquitectura.Albanil.Services;

/// <summary>Arma cada cosa que carga el albañil y la deja en la cola de envíos (nunca espera a la señal).</summary>
public sealed class Cargas(ColaEnvios cola, EstadoLocal estado)
{
    public Task AvanceAsync(ObraResumenDto obra, TareaDto? tarea, decimal? cantidad, int? porcentaje, bool terminada, string? nota,
        IReadOnlyList<FileResult> archivos, PuntoFotoDto? punto = null)
    {
        var e = Nuevo(obra, TipoEnvio.Avance, punto is not null ? $"Foto fija: {punto.Nombre}" : tarea is null ? "Fotos de avance" : $"Avance: {tarea.Titulo}");
        e.Cuerpo = JsonSerializer.Serialize(new NuevoAvanceRequest(e.Ref, e.CreadoUtc, tarea?.Id, cantidad, porcentaje, terminada, Vacio(nota), punto?.Id),
            AppJson.Default.NuevoAvanceRequest);
        return cola.AgregarAsync(e, archivos);
    }

    /// <param name="eppFaltante">Lo que le falta de protección (vacío = tiene todo; null = no se controló).</param>
    public async Task<JornadaLocal> EntradaAsync(ObraResumenDto obra, Punto? punto, bool? enAltura = null, IReadOnlyList<string>? eppFaltante = null)
    {
        var e = Nuevo(obra, TipoEnvio.Entrada, "Entrada");
        e.Cuerpo = JsonSerializer.Serialize(new FichadaRequest(e.Ref, e.CreadoUtc, punto?.Latitud, punto?.Longitud, punto?.Precision, enAltura, eppFaltante),
            AppJson.Default.FichadaRequest);
        var j = new JornadaLocal(e.Ref, e.CreadoUtc);
        estado.AbrirJornada(obra.Id, j);
        await cola.AgregarAsync(e);
        return j;
    }

    public async Task SalidaAsync(ObraResumenDto obra, JornadaLocal jornada, Punto? punto)
    {
        var e = Nuevo(obra, TipoEnvio.Salida, "Salida");
        // El cuerpo lleva el Ref de la entrada que cierra; el envío tiene su propio Ref.
        e.Cuerpo = JsonSerializer.Serialize(new FichadaRequest(jornada.Ref, e.CreadoUtc, punto?.Latitud, punto?.Longitud, punto?.Precision),
            AppJson.Default.FichadaRequest);
        estado.CerrarJornada(obra.Id);
        await cola.AgregarAsync(e);
    }

    public Task PedidoAsync(ObraResumenDto obra, int? tareaId, bool urgente, string? nota, IReadOnlyList<PedidoItemRequest> items)
    {
        var e = Nuevo(obra, TipoEnvio.Pedido, $"Pedido: {string.Join(", ", items.Select(i => i.Material))}");
        e.Cuerpo = JsonSerializer.Serialize(new NuevoPedidoRequest(e.Ref, e.CreadoUtc, tareaId, urgente, Vacio(nota), items),
            AppJson.Default.NuevoPedidoRequest);
        return cola.AgregarAsync(e);
    }

    public Task ProblemaAsync(ObraResumenDto obra, int? tareaId, string tipo, string urgencia, string descripcion, IReadOnlyList<FileResult> archivos)
    {
        var e = Nuevo(obra, TipoEnvio.Problema, $"Problema: {ValoresAlbanil.Etiqueta(tipo)}");
        e.Cuerpo = JsonSerializer.Serialize(new NuevoProblemaRequest(e.Ref, e.CreadoUtc, tareaId, tipo, urgencia, descripcion.Trim()),
            AppJson.Default.NuevoProblemaRequest);
        return cola.AgregarAsync(e, archivos);
    }

    public Task ParteAsync(ObraResumenDto obra, DateOnly fecha, string? resumen, IReadOnlyList<ParteItemDto> items)
    {
        var e = Nuevo(obra, TipoEnvio.Parte, $"Parte del {fecha:dd/MM}");
        e.Cuerpo = JsonSerializer.Serialize(new ParteRequest(fecha, Vacio(resumen), items), AppJson.Default.ParteRequest);
        return cola.AgregarAsync(e);
    }

    public Task RecepcionAsync(ObraResumenDto obra, PedidoDto? pedido, string? proveedor, string? remito, string? nota,
        IReadOnlyList<RecepcionItemRequest> items, IReadOnlyList<FileResult> fotos)
    {
        var e = Nuevo(obra, TipoEnvio.Recepcion, pedido is null ? "Llegó material" : $"Llegó el pedido #{pedido.Id}");
        e.Cuerpo = JsonSerializer.Serialize(new NuevaRecepcionRequest(e.Ref, e.CreadoUtc, pedido?.Id, Vacio(proveedor), Vacio(remito), Vacio(nota), items),
            AppJson.Default.NuevaRecepcionRequest);
        return cola.AgregarAsync(e, fotos);
    }

    public Task ControlAsync(ObraResumenDto obra, ControlDto control, bool ok, string? nota, IReadOnlyList<FileResult> fotos)
    {
        var e = Nuevo(obra, TipoEnvio.Control, $"Control: {control.Texto}");
        e.Cuerpo = JsonSerializer.Serialize(new RespuestaControlRequest(e.Ref, e.CreadoUtc, control.Id, ok ? "Ok" : "Observado", Vacio(nota)),
            AppJson.Default.RespuestaControlRequest);
        return cola.AgregarAsync(e, fotos);
    }

    public Task NotaPedidoAsync(ObraResumenDto obra, string texto, IReadOnlyList<FileResult> fotos)
    {
        var e = Nuevo(obra, TipoEnvio.NotaPedido, "Nota de pedido al estudio");
        e.Cuerpo = JsonSerializer.Serialize(new NotaPedidoRequest(e.Ref, e.CreadoUtc, texto.Trim()), AppJson.Default.NotaPedidoRequest);
        return cola.AgregarAsync(e, fotos);
    }

    public Task LeidoAsync(ObraResumenDto obra, IReadOnlyList<int> ordenes)
    {
        var e = Nuevo(obra, TipoEnvio.Leido, "Órdenes leídas");
        e.Cuerpo = JsonSerializer.Serialize(new LeidoRequest(ordenes), AppJson.Default.LeidoRequest);
        estado.MarcarLeidas(obra.Id, ordenes);
        return cola.AgregarAsync(e);
    }

    private static Envio Nuevo(ObraResumenDto obra, TipoEnvio tipo, string descripcion) =>
        new() { ObraId = obra.Id, ObraNombre = obra.Nombre, Tipo = tipo, Descripcion = descripcion, CreadoUtc = DateTime.UtcNow };

    private static string? Vacio(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
