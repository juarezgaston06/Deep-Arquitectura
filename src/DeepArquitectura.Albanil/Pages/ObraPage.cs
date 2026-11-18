using DeepArquitectura.Albanil.Services;
using DeepArquitectura.Contracts;

namespace DeepArquitectura.Albanil.Pages;

/// <summary>
/// Pantalla principal en la obra: fichar, cargar avance (fotos/videos), pedir material, avisar un problema,
/// parte del día, y la lista de tareas. Sin señal muestra lo último guardado y todo se envía después.
/// </summary>
public sealed class ObraPage : ContentPage
{
    private readonly ObraResumenDto _resumen;
    private readonly Sesion _sesion;
    private readonly ApiCliente _api;
    private readonly ColaEnvios _cola;
    private readonly EstadoLocal _estado;
    private readonly Cargas _cargas;

    private readonly VerticalStackLayout _pila = new() { Padding = new Thickness(16, 12, 16, 32), Spacing = 12 };
    private readonly Label _envios = Ui.Texto("", 15);
    private readonly Border _envioBorde;
    private readonly Label _jornadaTexto = Ui.Texto("", 17);
    private readonly Button _fichar;
    private readonly RefreshView _refresh;
    private readonly IDispatcherTimer _reloj;
    private ObraAlbanilDto? _obra;
    private string? _avisoCarga;

    private readonly Border _jornadaTarjeta;

    public ObraPage(ObraResumenDto obra)
    {
        _resumen = obra;
        _sesion = App.Servicio<Sesion>();
        _api = App.Servicio<ApiCliente>();
        _cola = App.Servicio<ColaEnvios>();
        _estado = App.Servicio<EstadoLocal>();
        _cargas = App.Servicio<Cargas>();
        Title = obra.Nombre;
        BackgroundColor = Ui.Fondo;

        _envioBorde = Ui.Tarjeta(_envios, Ui.FondoOk);
        var toque = new TapGestureRecognizer();
        toque.Tapped += async (_, _) => await GestionarEnviosAsync();
        _envioBorde.GestureRecognizers.Add(toque);

        _fichar = Ui.Boton("Fichar entrada", FicharAsync, color: Ui.Verde);
        _jornadaTarjeta = Ui.Tarjeta(new VerticalStackLayout { Spacing = 10, Children = { _jornadaTexto, _fichar } });

        _refresh = new RefreshView { Content = new ScrollView { Content = _pila }, RefreshColor = Ui.Marron };
        _refresh.Refreshing += async (_, _) => { await CargarAsync(); _refresh.IsRefreshing = false; };
        Content = _refresh;

        _reloj = Dispatcher.CreateTimer();
        _reloj.Interval = TimeSpan.FromMinutes(1);
        _reloj.Tick += (_, _) => ActualizarJornada();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _cola.Cambio += OnCola;
        _cola.Enviado += OnEnviado;
        _reloj.Start();
        _obra ??= _estado.ObraGuardada(_resumen.Id);
        Dibujar();
        await ActualizarEnviosAsync();
        await CargarAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _cola.Cambio -= OnCola;
        _cola.Enviado -= OnEnviado;
        _reloj.Stop();
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
            _estado.Reconciliar(_obra, await _cola.PendientesAsync());
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
            _avisoCarga = _obra is null ? "Sin señal y sin datos guardados de esta obra. Igual podés fichar y cargar fotos: se envían solas."
                : "Sin señal: se muestra lo último que se cargó. Lo que hagas se envía solo cuando vuelva la señal.";
        }
        Dibujar();
    }

    // ------------------------------------------------------------------ pantalla

    private void Dibujar()
    {
        _pila.Children.Clear();
        var o = _obra;
        _pila.Children.Add(Ui.Titulo(_resumen.Nombre));
        _pila.Children.Add(Ui.Suave(string.Join(" · ", new[] { o?.Direccion ?? _resumen.Direccion, o?.Localidad ?? _resumen.Localidad, $"avance de obra {o?.PorcentajeAvance ?? _resumen.PorcentajeAvance}%" }.Where(x => !string.IsNullOrEmpty(x)))));
        if (o?.Clima is { Count: > 0 } clima)
            _pila.Children.Add(Ui.Suave(string.Join("   ", clima.Select(c =>
                $"{(c.Fecha == DateOnly.FromDateTime(DateTime.Now) ? "Hoy" : c.Fecha.ToString("ddd", new System.Globalization.CultureInfo("es-AR")))}: {c.Descripcion} {c.TempMax:0}°{(c.LluviaMm >= 1 ? $" 🌧 {c.LluviaMm:0} mm" : "")}"))));
        if (_avisoCarga is not null) _pila.Children.Add(Ui.Tarjeta(Ui.Texto(_avisoCarga, 15), Ui.FondoAlerta));
        _pila.Children.Add(_envioBorde);

        // Órdenes de servicio del estudio sin confirmar: van arriba de todo.
        var sinLeer = (o?.Ordenes ?? []).Where(x => !x.Leida && !_estado.Leida(_resumen.Id, x.Id)).OrderBy(x => x.Numero).ToList();
        if (sinLeer.Count > 0)
        {
            var pilaOrdenes = new VerticalStackLayout { Spacing = 8, Children = { Ui.Texto("📖 Órdenes del estudio", 18) } };
            foreach (var orden in sinLeer) pilaOrdenes.Children.Add(Ui.Texto($"N° {orden.Numero} ({Ui.FechaCorta(orden.FechaUtc)}): {orden.Texto}", 16));
            pilaOrdenes.Children.Add(Ui.Boton("Leí y entendí", async () =>
            {
                await _cargas.LeidoAsync(_resumen, sinLeer.Select(x => x.Id).ToList());
                Dibujar();
            }, color: Ui.Verde));
            _pila.Children.Add(Ui.Tarjeta(pilaOrdenes, Color.FromArgb("#FFF1D6")));
        }

        // Jornada
        ActualizarJornada();
        _pila.Children.Add(_jornadaTarjeta);

        // Acciones
        var acciones = new Grid
        {
            ColumnDefinitions = [new(GridLength.Star), new(GridLength.Star)],
            RowDefinitions = [new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto)],
            ColumnSpacing = 10, RowSpacing = 10,
        };
        acciones.Add(Ui.Boton("📷\nFoto / video\nde avance", () => Navigation.PushAsync(new AvancePage(_resumen, null))), 0, 0);
        acciones.Add(Ui.Boton("📦\nPedir\nmaterial", () => Navigation.PushAsync(new PedidoPage(_resumen, Tareas()))), 1, 0);
        acciones.Add(Ui.Boton("⚠\nAvisar un\nproblema", () => Navigation.PushAsync(new ProblemaPage(_resumen, Tareas())), color: Ui.Rojo), 0, 1);
        acciones.Add(Ui.Boton("📝\nParte\ndel día", () => Navigation.PushAsync(new PartePage(_resumen, Tareas(), _obra?.ParteDeHoy))), 1, 1);
        acciones.Add(Ui.Boton("🚚\nLlegó\nmaterial", ElegirRecepcionAsync, primario: false), 0, 2);
        acciones.Add(Ui.Boton("📖\nNota al\nestudio", () => Navigation.PushAsync(new NotaPedidoPage(_resumen)), primario: false), 1, 2);
        _pila.Children.Add(acciones);

        if (_sesion.Perfil is { CapatazTelefono: { Length: > 0 } tel } perfil)
            _pila.Children.Add(Ui.Boton($"📞  Llamar a {perfil.Capataz}", () => LlamarAsync(tel), primario: false));

        // Controles de las etapas en curso que faltan (o que quedaron observados).
        var controles = (o?.Controles ?? []).Where(c => c.Resultado != "Ok" && _estado.Hecho("control", _resumen.Id, c.Id) is null).ToList();
        if (controles.Count > 0)
        {
            _pila.Children.Add(Ui.Seccion("Controles antes de seguir"));
            foreach (var c in controles)
            {
                var vista = Ui.Tarjeta(new VerticalStackLayout
                {
                    Spacing = 4,
                    Children =
                    {
                        Ui.Texto((c.RequiereFoto ? "📷 " : "") + c.Texto, 17),
                        Ui.Suave(c.Resultado == "Observado" ? $"{c.Etapa} · OBSERVADO: {c.Nota}" : $"{c.Etapa} · tocá para cargarlo ›"),
                    },
                }, c.Resultado == "Observado" ? Ui.FondoAlerta : null);
                Tocar(vista, () => Navigation.PushAsync(new ControlPage(_resumen, c)));
                _pila.Children.Add(vista);
            }
        }

        // Fotos desde puntos fijos que toca sacar.
        // Si se sacó desde este teléfono después de la última que conoce el estudio, ya está (aunque esté esperando señal).
        var puntos = (o?.PuntosFoto ?? [])
            .Where(p => p.Pendiente && !(_estado.Hecho("punto", _resumen.Id, p.Id) is { } h && (p.UltimaUtc is null || h > p.UltimaUtc)))
            .ToList();
        if (puntos.Count > 0)
        {
            _pila.Children.Add(Ui.Seccion("Fotos fijas de la semana"));
            foreach (var p in puntos)
            {
                var vista = Ui.Tarjeta(new VerticalStackLayout { Spacing = 4, Children = { Ui.Texto($"📷 {p.Nombre}", 17), Ui.Suave(p.Indicacion ?? "Mismo encuadre que la vez anterior ›") } });
                Tocar(vista, () => Navigation.PushAsync(new FotoFijaPage(_resumen, p)));
                _pila.Children.Add(vista);
            }
        }

        // Tareas
        var tareas = o?.Tareas ?? [];
        var abiertas = tareas.Where(t => t.Estado != "Completada").ToList();
        _pila.Children.Add(Ui.Seccion("Mis tareas"));
        if (abiertas.Count == 0)
            _pila.Children.Add(Ui.Suave(o is null ? "Las tareas aparecen cuando haya señal." : "No tenés tareas pendientes. Podés cargar fotos de avance igual."));
        foreach (var t in abiertas.Where(t => t.Mia)) _pila.Children.Add(Tarea(t));
        if (abiertas.Any(t => !t.Mia))
        {
            _pila.Children.Add(Ui.Suave("De la cuadrilla (sin asignar):"));
            foreach (var t in abiertas.Where(t => !t.Mia)) _pila.Children.Add(Tarea(t));
        }
        var terminadas = tareas.Where(t => t.Estado == "Completada").ToList();
        if (terminadas.Count > 0)
            _pila.Children.Add(Ui.Suave("Terminadas: " + string.Join(", ", terminadas.Select(t => t.Titulo))));

        // Pedidos y problemas
        if (o is { Pedidos.Count: > 0 })
        {
            _pila.Children.Add(Ui.Seccion("Pedidos de material"));
            foreach (var pe in o.Pedidos.Take(6))
            {
                var linea = Linea(pe.Detalle, pe.Estado, Ui.FechaCorta(pe.FechaUtc) + (pe.Urgente ? " · urgente" : ""), pe.Respuesta);
                if (pe.Estado is "Pendiente" or "Aprobado" or "EntregaParcial")
                    _pila.Children.Add(new VerticalStackLayout { Spacing = 4, Children = { linea, Ui.Boton("🚚  Llegó este pedido", () => Navigation.PushAsync(new RecepcionPage(_resumen, pe)), primario: false) } });
                else _pila.Children.Add(linea);
            }
        }
        if (o is { Problemas.Count: > 0 })
        {
            _pila.Children.Add(Ui.Seccion("Mis avisos de problemas"));
            foreach (var pr in o.Problemas.Take(5))
                _pila.Children.Add(Linea($"{ValoresAlbanil.Etiqueta(pr.Tipo)}: {pr.Descripcion}", pr.Estado, Ui.FechaCorta(pr.FechaUtc) + $" · urgencia {pr.Urgencia.ToLowerInvariant()}", pr.Respuesta));
        }
        if (o is { MisAvances.Count: > 0 })
        {
            _pila.Children.Add(Ui.Seccion("Lo último que mandé"));
            foreach (var a in o.MisAvances.Take(5))
                _pila.Children.Add(Ui.Texto($"{Ui.FechaCorta(a.FechaUtc)} · {a.Titulo}{(a.Fotos + a.Videos > 0 ? $" · 📷 {a.Fotos} 🎥 {a.Videos}" : "")}" + a.Validacion switch
                {
                    "Pendiente" => " · a confirmar por el capataz",
                    "Aprobado" => " · ✔ aprobado",
                    "Corregido" => " · ✔ corregido por el capataz",
                    "Rechazado" => $" · ✖ rechazado: {a.Motivo}",
                    _ => "",
                }, 14, a.Validacion == "Rechazado" ? Ui.Rojo : Ui.MarronSuave));
        }
    }

    private List<TareaDto> Tareas() => _obra?.Tareas.Where(t => t.Estado != "Completada").ToList() ?? [];

    private View Tarea(TareaDto t)
    {
        var avance = t.Unidad is null ? $"{t.Porcentaje}%" : $"{Ui.Num(t.CantidadHecha)} de {Ui.Num(t.CantidadObjetivo ?? 0)} {t.Unidad} ({t.Porcentaje}%)";
        var pila = new VerticalStackLayout
        {
            Spacing = 6,
            Children =
            {
                new Label { Text = t.Titulo, FontSize = 18, FontFamily = "OpenSansSemibold", TextColor = Ui.Marron },
                Ui.Suave(string.Join(" · ", new[] { t.Etapa, t.FechaLimite is { } f ? $"para el {f:dd/MM}" : null, t.Atrasada ? "ATRASADA" : null }.Where(x => x is not null))),
                Ui.Progreso(t.Porcentaje),
                new HorizontalStackLayout { Spacing = 8, Children = { Ui.Chip(Ui.TextoEstado(t.Estado), Ui.ColorEstado(t.Estado)), Ui.Texto(avance, 15) } },
                Ui.Suave("Tocá para cargar el avance ›"),
            },
        };
        if (!string.IsNullOrWhiteSpace(t.Descripcion)) pila.Children.Insert(2, Ui.Texto(t.Descripcion, 15));
        var tarjeta = Ui.Tarjeta(pila);
        var toque = new TapGestureRecognizer();
        toque.Tapped += async (_, _) => await Navigation.PushAsync(new AvancePage(_resumen, t));
        tarjeta.GestureRecognizers.Add(toque);
        return tarjeta;
    }

    private static void Tocar(View vista, Func<Task> accion)
    {
        var toque = new TapGestureRecognizer();
        toque.Tapped += async (_, _) => await accion();
        vista.GestureRecognizers.Add(toque);
    }

    /// <summary>Llegó material: de cuál pedido (o sin pedido).</summary>
    private async Task ElegirRecepcionAsync()
    {
        var abiertos = (_obra?.Pedidos ?? []).Where(p => p.Estado is "Pendiente" or "Aprobado" or "EntregaParcial").ToList();
        if (abiertos.Count == 0) { await Navigation.PushAsync(new RecepcionPage(_resumen, null)); return; }
        var opciones = abiertos.Select(p => $"#{p.Id}: {p.Detalle}").Append("No estaba pedido").ToArray();
        var elegido = await DisplayActionSheetAsync("¿De qué pedido?", "Cancelar", null, opciones);
        if (elegido is null or "Cancelar") return;
        var pedido = abiertos.FirstOrDefault(p => elegido.StartsWith($"#{p.Id}:"));
        await Navigation.PushAsync(new RecepcionPage(_resumen, pedido));
    }

    private static View Linea(string texto, string estado, string detalle, string? respuesta)
    {
        var pila = new VerticalStackLayout
        {
            Spacing = 4,
            Children = { Ui.Texto(texto, 16), new HorizontalStackLayout { Spacing = 8, Children = { Ui.Chip(Ui.TextoEstado(estado), Ui.ColorEstado(estado)), Ui.Suave(detalle) } } },
        };
        if (respuesta is not null) pila.Children.Add(Ui.Texto("Estudio: " + respuesta, 15, Ui.Verde));
        return Ui.Tarjeta(pila);
    }

    // ------------------------------------------------------------------ jornada

    private void ActualizarJornada()
    {
        if (_estado.Jornada(_resumen.Id) is { } j)
        {
            _jornadaTexto.Text = $"🟢 Trabajando desde las {Ui.Hora(j.EntradaUtc)} ({Ui.Duracion(DateTime.UtcNow - j.EntradaUtc)})";
            _fichar.Text = "Fichar salida";
            _fichar.BackgroundColor = Ui.Rojo;
        }
        else
        {
            _jornadaTexto.Text = "Todavía no fichaste la entrada en esta obra.";
            _fichar.Text = "Fichar entrada";
            _fichar.BackgroundColor = Ui.Verde;
        }
    }

    private async Task FicharAsync()
    {
        var abierta = _estado.Jornada(_resumen.Id);
        if (abierta is not null && !await DisplayAlertAsync("Salida", $"¿Fichar la salida? Entraste a las {Ui.Hora(abierta.EntradaUtc)}.", "Fichar salida", "Cancelar"))
            return;

        EppPage.Resultado? epp = null;
        if (abierta is null)
        {
            epp = await EppPage.PedirAsync(Navigation);
            if (epp is null) return; // volvió atrás
        }

        _fichar.Text = "Buscando ubicación…";
        var punto = await Ubicacion.ObtenerAsync();
        // Lejos de la obra (si el estudio cargó su ubicación): se puede fichar igual, pero se avisa.
        if (punto is not null && _obra is { Latitud: double lat, Longitud: double lon } && Distancia(lat, lon, punto) is var metros && metros > _obra.RadioMetros
            && !await DisplayAlertAsync("Ubicación", $"Estás a {(metros < 1000 ? $"{metros:0} m" : $"{metros / 1000:0.#} km")} de la obra. ¿Fichar igual? Queda registrado.", "Fichar igual", "Cancelar"))
        {
            ActualizarJornada();
            return;
        }
        if (abierta is null)
        {
            var j = await _cargas.EntradaAsync(_resumen, punto, epp!.EnAltura, epp.Faltante);
            await Ui.AvisarAsync(this, "Entrada", $"Entrada fichada a las {Ui.Hora(j.EntradaUtc)}{(punto is null ? " (sin ubicación: el GPS está apagado o sin permiso)" : "")}.");
        }
        else
        {
            await _cargas.SalidaAsync(_resumen, abierta, punto);
            var horas = Ui.Duracion(DateTime.UtcNow - abierta.EntradaUtc);
            if (await DisplayAlertAsync("Salida", $"Salida fichada. Trabajaste {horas}. ¿Cargás el parte del día?", "Cargar parte", "Ahora no"))
                await Navigation.PushAsync(new PartePage(_resumen, Tareas(), _obra?.ParteDeHoy));
        }
        ActualizarJornada();
    }

    private static double Distancia(double lat, double lon, Punto p) =>
        Location.CalculateDistance(lat, lon, p.Latitud, p.Longitud, DistanceUnits.Kilometers) * 1000;

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
    }

    private async Task LlamarAsync(string telefono)
    {
        try { PhoneDialer.Default.Open(telefono); }
        catch (FeatureNotSupportedException) { await DisplayAlertAsync("Llamar", $"Teléfono: {telefono}", "Listo"); }
        await Task.CompletedTask;
    }
}
