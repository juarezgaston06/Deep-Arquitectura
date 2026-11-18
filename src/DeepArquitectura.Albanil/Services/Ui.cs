using System.Globalization;
using Microsoft.Maui.Controls.Shapes;

namespace DeepArquitectura.Albanil.Services;

/// <summary>
/// Piezas de pantalla comunes. Todo grande y con mucho contraste: se usa con guantes, con polvo y a pleno sol.
/// </summary>
public static class Ui
{
    public static readonly Color Marron = Color.FromArgb("#4A3728");
    public static readonly Color MarronSuave = Color.FromArgb("#8D6E63");
    public static readonly Color Fondo = Color.FromArgb("#F5EBE0");
    public static readonly Color Verde = Color.FromArgb("#2E7D32");
    public static readonly Color Rojo = Color.FromArgb("#B23A2A");
    public static readonly Color Ambar = Color.FromArgb("#B26A00");
    public static readonly Color FondoAlerta = Color.FromArgb("#F6DCD8");
    public static readonly Color FondoOk = Color.FromArgb("#DDEFD9");

    private static readonly CultureInfo EsAr = new("es-AR");

    public static string Num(decimal v) => v.ToString("#,0.##", EsAr);

    public static string Hora(DateTime utc) => utc.ToLocalTime().ToString("HH:mm", EsAr);

    public static string FechaCorta(DateTime utc) => utc.ToLocalTime().ToString("ddd dd/MM HH:mm", EsAr);

    public static string Duracion(TimeSpan t) => t.TotalMinutes < 60 ? $"{(int)t.TotalMinutes} min" : $"{(int)t.TotalHours} h {t.Minutes:00} min";

    /// <summary>Acepta «8,5» y «8.5» (el teclado numérico de algunos teléfonos solo tiene punto).</summary>
    public static decimal? Decimal(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;
        var t = texto.Trim().Replace(" ", "");
        if (t.Contains(',') && t.Contains('.')) t = t.Replace(".", ""); // 1.234,5
        t = t.Replace(',', '.');
        return decimal.TryParse(t, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    public static Label Titulo(string texto) => new() { Text = texto, FontSize = 22, FontFamily = "OpenSansSemibold", TextColor = Marron };

    public static Label Seccion(string texto) => new() { Text = texto, FontSize = 18, FontFamily = "OpenSansSemibold", TextColor = Marron, Margin = new Thickness(0, 12, 0, 0) };

    public static Label Texto(string? texto, double tamano = 16, Color? color = null) =>
        new() { Text = texto, FontSize = tamano, TextColor = color ?? Marron, LineBreakMode = LineBreakMode.WordWrap };

    public static Label Suave(string? texto) => Texto(texto, 14, MarronSuave);

    public static Border Tarjeta(View contenido, Color? fondo = null) => new()
    {
        BackgroundColor = fondo ?? Colors.White,
        StrokeThickness = 0,
        Padding = new Thickness(16),
        StrokeShape = new RoundRectangle { CornerRadius = 12 },
        Content = contenido,
    };

    public static Button Boton(string texto, Func<Task> accion, bool primario = true, Color? color = null)
    {
        var b = new Button
        {
            Text = texto,
            FontSize = 19,
            FontFamily = "OpenSansSemibold",
            MinimumHeightRequest = 64,
            CornerRadius = 10,
            Padding = new Thickness(12, 8),
            BackgroundColor = primario ? color ?? Marron : Colors.White,
            TextColor = primario ? Colors.White : color ?? Marron,
            BorderColor = color ?? Marron,
            BorderWidth = primario ? 0 : 2,
            LineBreakMode = LineBreakMode.WordWrap,
        };
        b.Clicked += async (_, _) =>
        {
            if (!b.IsEnabled) return;
            b.IsEnabled = false; // evita el doble toque (con guantes pasa seguido)
            try { await accion(); }
            finally { b.IsEnabled = true; }
        };
        return b;
    }

    public static View Progreso(int porcentaje) => new ProgressBar
    {
        Progress = Math.Clamp(porcentaje, 0, 100) / 100.0,
        ProgressColor = porcentaje >= 100 ? Verde : Marron,
        HeightRequest = 12,
    };

    public static Border Chip(string texto, Color color) => new()
    {
        BackgroundColor = color.WithAlpha(0.15f),
        StrokeThickness = 0,
        Padding = new Thickness(10, 3),
        StrokeShape = new RoundRectangle { CornerRadius = 10 },
        HorizontalOptions = LayoutOptions.Start,
        Content = new Label { Text = texto, FontSize = 13, FontFamily = "OpenSansSemibold", TextColor = color },
    };

    public static Color ColorEstado(string estado) => estado switch
    {
        "Completada" or "Entregado" or "Resuelto" => Verde,
        "EnCurso" or "Aprobado" or "EnRevision" => Marron,
        "Rechazado" => Rojo,
        _ => Ambar, // Pendiente, Abierto
    };

    public static string TextoEstado(string estado) => estado switch
    {
        "EnCurso" => "En curso",
        "EnRevision" => "En revisión",
        _ => estado,
    };

    public static Entry Campo(string placeholder, Keyboard? teclado = null, int max = 200) => new()
    {
        Placeholder = placeholder,
        FontSize = 18,
        BackgroundColor = Colors.White,
        TextColor = Marron,
        Keyboard = teclado ?? Keyboard.Text,
        MaxLength = max,
        MinimumHeightRequest = 52,
    };

    public static Editor Area(string placeholder, int max = 2000) => new()
    {
        Placeholder = placeholder,
        FontSize = 17,
        BackgroundColor = Colors.White,
        TextColor = Marron,
        AutoSize = EditorAutoSizeOption.TextChanges,
        MinimumHeightRequest = 100,
        MaxLength = max,
    };

    /// <summary>El campo de texto con un botón de micrófono: lo dictado se agrega al final.</summary>
    public static View ConDictado(Editor editor)
    {
        if (!Dictado.Disponible) return editor;
        var mic = new Button
        {
            Text = "🎤", FontSize = 24, WidthRequest = 64, MinimumHeightRequest = 64, CornerRadius = 32, Padding = 0,
            BackgroundColor = Marron, TextColor = Colors.White, VerticalOptions = LayoutOptions.Start,
        };
        SemanticProperties.SetDescription(mic, "Dictar por voz");
        mic.Clicked += async (_, _) =>
        {
            mic.IsEnabled = false;
            try
            {
                if (await Dictado.EscucharAsync() is { } dicho)
                    editor.Text = string.IsNullOrWhiteSpace(editor.Text) ? dicho : $"{editor.Text.TrimEnd()} {dicho}";
            }
            finally { mic.IsEnabled = true; }
        };
        var grilla = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)], ColumnSpacing = 8 };
        grilla.Add(editor, 0, 0);
        grilla.Add(mic, 1, 0);
        return grilla;
    }

    public static ScrollView Contenedor(params View[] hijos)
    {
        var pila = new VerticalStackLayout { Padding = new Thickness(16, 16, 16, 32), Spacing = 12 };
        foreach (var h in hijos) pila.Children.Add(h);
        return new ScrollView { Content = pila };
    }

    /// <summary>Aviso corto al guardar (el envío sigue solo en segundo plano).</summary>
    public static Task AvisarAsync(Page pagina, string titulo, string mensaje) => pagina.DisplayAlertAsync(titulo, mensaje, "Listo");
}
