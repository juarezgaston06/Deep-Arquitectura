using System.Text.Json;
using DeepArquitectura.Contracts;

namespace DeepArquitectura.Capataz.Services;

/// <summary>Arma cada cosa que carga el capataz y la deja en la cola de envíos (nunca espera a la señal).</summary>
public sealed class Cargas(ColaEnvios cola)
{
    public Task NovedadAsync(ObraResumenDto obra, string titulo, string? detalle, int? etapaId, IReadOnlyList<FileResult> archivos)
    {
        var e = Nuevo(obra, TipoEnvio.Novedad, $"Novedad: {titulo}", RutasApp.Registros(obra.Id));
        e.Cuerpo = JsonSerializer.Serialize(new NuevoRegistroRequest(e.Ref, e.CreadoUtc, titulo.Trim(), Vacio(detalle), etapaId),
            AppJson.Default.NuevoRegistroRequest);
        return cola.AgregarAsync(e, archivos);
    }

    public Task EtapaAsync(ObraResumenDto obra, EtapaDto etapa, string estado)
    {
        var e = Nuevo(obra, TipoEnvio.Etapa, $"Etapa {etapa.Orden}. {etapa.Nombre}: {Ui.TextoEstado(estado).ToLowerInvariant()}",
            RutasApp.Etapa(obra.Id, etapa.Id));
        e.Metodo = "PUT";
        e.Cuerpo = JsonSerializer.Serialize(new CambiarEtapaRequest(estado), AppJson.Default.CambiarEtapaRequest);
        e.Claves.Add(ClavesEnvio.Etapa(etapa.Id, estado));
        return cola.AgregarAsync(e);
    }

    public Task ValidarAsync(ObraResumenDto obra, IReadOnlyList<ValidacionItem> items, string descripcion)
    {
        var e = Nuevo(obra, TipoEnvio.Validacion, descripcion, RutasApp.Validaciones(obra.Id));
        e.Cuerpo = JsonSerializer.Serialize(new ValidacionesRequest(items), AppJson.Default.ValidacionesRequest);
        e.Claves.AddRange(items.Select(i => ClavesEnvio.Validado(i.RegistroId)));
        return cola.AgregarAsync(e);
    }

    public Task AtrasoAsync(ObraResumenDto obra, TareaAtrasadaDto tarea, string causa, string? detalle)
    {
        var e = Nuevo(obra, TipoEnvio.Atraso, $"Atraso: {tarea.Titulo}", RutasApp.Atrasos(obra.Id));
        e.Cuerpo = JsonSerializer.Serialize(new AtrasoRequest(tarea.Id, causa, Vacio(detalle)), AppJson.Default.AtrasoRequest);
        e.Claves.Add(ClavesEnvio.Atraso(tarea.Id, causa));
        return cola.AgregarAsync(e);
    }

    public Task GastoAsync(ObraResumenDto obra, string descripcion, decimal monto, string categoria, string? proveedor, string? comprobante,
        int? etapaId, IReadOnlyList<FileResult> fotos)
    {
        var e = Nuevo(obra, TipoEnvio.Gasto, $"Gasto: {descripcion} ${Ui.Num(monto)}", RutasApp.Gastos(obra.Id));
        e.Multipart = true;
        e.Cuerpo = JsonSerializer.Serialize(new NuevoGastoRequest(e.Ref, e.CreadoUtc, descripcion.Trim(), monto, categoria, Vacio(proveedor),
            Vacio(comprobante), etapaId), AppJson.Default.NuevoGastoRequest);
        return cola.AgregarAsync(e, fotos);
    }

    public Task SobranteAsync(ObraResumenDto obra, string material, decimal cantidad, string unidad, string destino, int? obraDestinoId, string? nota)
    {
        var e = Nuevo(obra, TipoEnvio.Sobrante, $"Sobrante: {Ui.Num(cantidad)} {unidad} {material}", RutasApp.Sobrantes(obra.Id));
        e.Cuerpo = JsonSerializer.Serialize(new NuevoSobranteRequest(e.Ref, e.CreadoUtc, material.Trim(), cantidad, unidad.Trim(), destino,
            obraDestinoId, Vacio(nota)), AppJson.Default.NuevoSobranteRequest);
        return cola.AgregarAsync(e);
    }

    public Task NotaPedidoAsync(ObraResumenDto obra, string texto, IReadOnlyList<FileResult> fotos)
    {
        var e = Nuevo(obra, TipoEnvio.NotaPedido, "Nota de pedido al estudio", RutasApp.NotasPedido(obra.Id));
        e.Cuerpo = JsonSerializer.Serialize(new NotaPedidoRequest(e.Ref, e.CreadoUtc, texto.Trim()), AppJson.Default.NotaPedidoRequest);
        return cola.AgregarAsync(e, fotos);
    }

    public Task LeidoAsync(ObraResumenDto obra, IReadOnlyList<int> ordenes)
    {
        var e = Nuevo(obra, TipoEnvio.Leido, "Órdenes leídas", RutasApp.Leido(obra.Id));
        e.Cuerpo = JsonSerializer.Serialize(new LeidoRequest(ordenes), AppJson.Default.LeidoRequest);
        e.Claves.AddRange(ordenes.Select(ClavesEnvio.Leida));
        return cola.AgregarAsync(e);
    }

    private static Envio Nuevo(ObraResumenDto obra, TipoEnvio tipo, string descripcion, string ruta) =>
        new() { ObraId = obra.Id, ObraNombre = obra.Nombre, Tipo = tipo, Descripcion = descripcion, Ruta = ruta, CreadoUtc = DateTime.UtcNow };

    private static string? Vacio(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
