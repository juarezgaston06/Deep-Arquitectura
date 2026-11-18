using DeepArquitectura.Capataz.Services;
using DeepArquitectura.Contracts;

namespace DeepArquitectura.Capataz.Pages;

/// <summary>
/// Pantalla principal de la obra: lo que hay que atender (órdenes del estudio, avances de la cuadrilla por validar,
/// tareas atrasadas sin causa), los botones para cargar y lo último que entró. Sin señal muestra lo último guardado
/// y todo lo que se carga se envía solo después.
/// </summary>
public sealed class ObraPage : ContentPage
{
    private readonly ObraResumenDto _resumen;
    private readonly ApiCliente _api;
    private readonly ColaEnvios _cola;
    private readonly EstadoLocal _estado;
    private readonly Cargas _cargas;
    private readonly Sesion _sesion;

    private readonly VerticalStackLayout _pila = new() { Padding = new Thickness(16, 12, 16, 32), Spacing = 12 };
    private readonly Label _envios = Ui.Texto("", 15);
    private readonly Border _envioBorde;
    private readonly RefreshView _refresh;
    private ObraDetalleDto? _obra;
    private HashSet<string> _resueltos = [];
    private string? _avisoCarga;

    public ObraPage(ObraResumenDto obra)
    {
        _resumen = obra;
        _api = App.Servicio<ApiCliente>();
        _cola = App.Servicio<ColaEnvios>();
        _estado = App.Servicio<EstadoLocal>();
        _cargas = App.Servicio<Cargas>();
        _sesion = App.Servicio<Sesion>();
        Title = obra.Nombre;
        BackgroundColor = Ui.Fondo;

        _envioBorde = Ui.Tarjeta(_envios, Ui.FondoOk);
        var toque = new TapGestureRecognizer();
        toque.Tapped += async (_, _) => await GestionarEnviosAsync();
        _envioBorde.GestureRecognizers.Add(toque);

        _refresh = new RefreshView { Content = new ScrollView { Content = _pila }, RefreshColor = Ui.Marron };
        _refresh.Refreshing += async (_, _) => { await CargarAsync(); _refresh.IsRefreshing = false; };
        Content = _refresh;
        ToolbarItems.Add(new ToolbarItem("Salir", null, async () => await ((App)Application.Current!).CerrarSesionAsync()));
    }

    /// <summary>La obra como la ve el teléfono ahora (para las pantallas hijas).</summary>
    internal ObraDetalleDto? Obra => _obra;

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _cola.Cambio += OnCola;
        _cola.Enviado += OnEnviado;
        _obra ??= _estado.ObraGuardada(_resumen.Id);
        await DibujarAsync();
        await CargarAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _cola.Cambio -= OnCola;
        _cola.Enviado -= OnEnviado;
    }

    private void OnCola(object? s, EventArgs e) => MainThread.BeginInvokeOnMainThread(async () => await ActualizarEnviosAsync());

    private void OnEnviado(object? s, int obraId)
    {
        if (obraId == _resumen.Id) MainThread.BeginInvokeOnMainThread(async () => await CargarAsync());
    }

    private async Task CargarAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            _obra = await _api.ObraAsync(_resumen.Id, cts.Token);
            _estado.GuardarObra(_obra);
            _avisoCarga = null;
        }
        catch (SesionVencidaException ex) { await ((App)Application.Current!).IrAlLoginAsync(ex.Message); return; }
        catch (ErrorApiException ex) when (ex.Estado == System.Net.HttpStatusCode.Forbidden)
        {
            await DisplayAlertAsync("Obra", "Esta obra ya no está asignada a tu usuario. Lo que tengas pendiente de esta obra no se va a poder enviar.", "Listo");
            _sesion.ObraActual = null;
            await Navigation.PopToRootAsync();
            return;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or ErrorApiException)
        {
            _avisoCarga = _obra is null ? "Sin señal y sin datos guardados de esta obra. Igual podés cargar fotos, novedades y gastos: se envían solos."
                : "Sin señal: se muestra lo último que se cargó. Lo que hagas se envía solo cuando vuelva la señal.";
        }
        await DibujarAsync();
    }

    /// <summary>Lo resuelto en el teléfono (en la cola o recién enviado) que la copia de la obra todavía muestra pendiente.</summary>
    internal async Task<HashSet<string>> ResueltosAsync()
    {
        var claves = await _cola.ClavesEnColaAsync(_resumen.Id);
        claves.UnionWith(_estado.Enviados(_resumen.Id));
        return claves;
    }

    internal List<AvancePorValidarDto> PorValidar() =>
        (_obra?.PorValidar ?? []).Where(p => !_resueltos.Contains(ClavesEnvio.Validado(p.RegistroId))).ToList();

    private async Task DibujarAsync()
    {
        _resueltos = await ResueltosAsync();
        _pila.Children.Clear();
        var o = _obra;
        _pila.Children.Add(Ui.Titulo(_resumen.Nombre));
        _pila.Children.Add(Ui.Suave(string.Join(" · ", new[] { o?.Direccion ?? _resumen.Direccion, o?.Localidad ?? _resumen.Localidad,
            $"avance {o?.PorcentajeAvance ?? _resumen.PorcentajeAvance}%" }.Where(x => !string.IsNullOrEmpty(x)))));
        if (_avisoCarga is not null) _pila.Children.Add(Ui.Tarjeta(Ui.Texto(_avisoCarga, 15), Ui.FondoAlerta));
        _pila.Children.Add(_envioBorde);
        await ActualizarEnviosAsync();

        // Órdenes de servicio del estudio sin confirmar: van arriba de todo.
        var sinLeer = (o?.Ordenes ?? []).Where(x => !_resueltos.Contains(ClavesEnvio.Leida(x.Id))).OrderBy(x => x.Numero).ToList();
        if (sinLeer.Count > 0)
        {
            var pilaOrdenes = new VerticalStackLayout { Spacing = 8, Children = { Ui.Texto("📖 Órdenes del estudio", 18) } };
            foreach (var orden in sinLeer) pilaOrdenes.Children.Add(Ui.Texto($"N° {orden.Numero} ({Ui.FechaCorta(orden.FechaUtc)}): {orden.Texto}", 16));
            pilaOrdenes.Children.Add(Ui.Boton("Leí y entendí", async () =>
            {
                await _cargas.LeidoAsync(_resumen, sinLeer.Select(x => x.Id).ToList());
                await DibujarAsync();
            }, color: Ui.Verde));
            _pila.Children.Add(Ui.Tarjeta(pilaOrdenes, Color.FromArgb("#FFF1D6")));
        }

        // Lo que espera al capataz.
        var porValidar = PorValidar();
        if (porValidar.Count > 0)
            _pila.Children.Add(Ui.Boton($"✔  {porValidar.Count} avance(s) de la cuadrilla para validar",
                () => Navigation.PushAsync(new ValidarPage(_resumen, this)), color: Ui.Verde));
        var atrasadas = o?.Atrasadas ?? [];
        var sinCausa = atrasadas.Count(t => t.Causa is null && ClavesEnvio.CausaLocal(_resueltos, t.Id) is null);
        if (sinCausa > 0)
            _pila.Children.Add(Ui.Boton($"⏰  {sinCausa} tarea(s) atrasada(s): contá por qué",
                () => Navigation.PushAsync(new AtrasosPage(_resumen, this)), color: Ui.Ambar));

        // Acciones
        var acciones = new Grid
        {
            ColumnDefinitions = [new(GridLength.Star), new(GridLength.Star)],
            RowDefinitions = [new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto)],
            ColumnSpacing = 10, RowSpacing = 10,
        };
        acciones.Add(Ui.Boton("📷\nFotos y\nnovedades", () => Navigation.PushAsync(new NovedadPage(_resumen, o?.Etapas ?? []))), 0, 0);
        acciones.Add(Ui.Boton("🧱\nEtapas de\nla obra", () => Navigation.PushAsync(new EtapasPage(_resumen, this))), 1, 0);
        acciones.Add(Ui.Boton("💲\nCargar\nun gasto", () => Navigation.PushAsync(new GastoPage(_resumen, o?.Etapas ?? []))), 0, 1);
        acciones.Add(Ui.Boton("📦\nMaterial\nque sobró", () => Navigation.PushAsync(new SobrantePage(_resumen))), 1, 1);
        acciones.Add(Ui.Boton("📖\nNota al\nestudio", () => Navigation.PushAsync(new NotaPedidoPage(_resumen)), primario: false), 0, 2);
        acciones.Add(Ui.Boton("⏰\nTareas\natrasadas", () => Navigation.PushAsync(new AtrasosPage(_resumen, this)), primario: false), 1, 2);
        _pila.Children.Add(acciones);

        // Lo último que entró a la bitácora (de todos: capataz, cuadrilla y estudio).
        if (o is { Registros.Count: > 0 })
        {
            _pila.Children.Add(Ui.Seccion("Últimas novedades"));
            foreach (var r in o.Registros.Take(8))
            {
                var medios = r.Fotos + r.Videos > 0 ? $" · 📷 {r.Fotos} 🎥 {r.Videos}" : "";
                _pila.Children.Add(Ui.Tarjeta(new VerticalStackLayout
                {
                    Spacing = 2,
                    Children =
                    {
                        Ui.Texto(r.Titulo, 16),
                        Ui.Suave($"{Ui.FechaCorta(r.FechaUtc)} · {r.Autor}{(r.Etapa is null ? "" : " · " + r.Etapa)}{medios}"),
                    },
                }));
            }
        }
    }

    /// <summary>Las pantallas hijas avisan que cargaron algo: se vuelve a dibujar con lo que quedó en la cola.</summary>
    internal Task RefrescarAsync() => DibujarAsync();

    // ------------------------------------------------------------------ envíos

    private async Task ActualizarEnviosAsync()
    {
        var pendientes = await _cola.PendientesAsync();
        var rechazados = pendientes.Count(e => e.Rechazado);
        var esperando = pendientes.Count - rechazados;
        _envios.Text = rechazados > 0 ? $"⚠ {rechazados} envío(s) rechazado(s) por el estudio. Tocá para ver."
            : esperando > 0 ? $"⏳ {esperando} envío(s) {(_cola.Enviando ? "enviándose…" : "esperando señal. Se envían solos.")}"
            : "✓ Todo enviado al estudio.";
        _envioBorde.BackgroundColor = rechazados > 0 ? Ui.FondoAlerta : esperando > 0 ? Color.FromArgb("#FFF1D6") : Ui.FondoOk;
    }

    private async Task GestionarEnviosAsync()
    {
        var pendientes = await _cola.PendientesAsync();
        if (pendientes.Count == 0) return;
        var detalle = string.Join("\n", pendientes.Select(e => $"• {e.ObraNombre} — {e.Descripcion}" + (e.Error is null ? "" : $"\n   {e.Error}")));
        if (!pendientes.Any(e => e.Rechazado))
        {
            await DisplayAlertAsync("Pendientes de envío", detalle, "Listo");
            _cola.Disparar();
            return;
        }
        var opcion = await DisplayActionSheetAsync("Envíos rechazados", "Volver", null, "Reintentar", "Borrar los rechazados", "Ver detalle");
        switch (opcion)
        {
            case "Reintentar": await _cola.ReintentarRechazadosAsync(); break;
            case "Borrar los rechazados":
                if (await DisplayAlertAsync("Borrar", "Se borran del teléfono y no llegan al estudio. ¿Seguro?", "Borrar", "Cancelar"))
                    await _cola.DescartarRechazadosAsync();
                break;
            case "Ver detalle": await DisplayAlertAsync("Envíos", detalle, "Listo"); break;
        }
        await DibujarAsync();
    }
}
