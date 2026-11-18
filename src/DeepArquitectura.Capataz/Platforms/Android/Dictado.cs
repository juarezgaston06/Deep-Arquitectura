using Android.Content;
using Android.Speech;

namespace DeepArquitectura.Capataz.Services;

/// <summary>
/// Dictado por voz con el reconocedor del teléfono (la pantallita de Google): la app no pide permiso de micrófono y
/// funciona sin señal si el teléfono tiene el idioma descargado. Para quien escribe poco o tiene las manos sucias.
/// </summary>
public static class Dictado
{
    public const int CodigoPedido = 4711;
    private static TaskCompletionSource<string?>? _pendiente;

    public static bool Disponible =>
        Platform.CurrentActivity?.PackageManager is { } pm && new Intent(RecognizerIntent.ActionRecognizeSpeech).ResolveActivity(pm) is not null;

    /// <summary>Lo que dijo, o null si canceló o no hay reconocedor.</summary>
    public static Task<string?> EscucharAsync(string indicacion = "Hablá ahora")
    {
        var actividad = Platform.CurrentActivity;
        if (actividad is null || !Disponible) return Task.FromResult<string?>(null);
        _pendiente?.TrySetResult(null);
        _pendiente = new TaskCompletionSource<string?>();
        var intent = new Intent(RecognizerIntent.ActionRecognizeSpeech);
        intent.PutExtra(RecognizerIntent.ExtraLanguageModel, RecognizerIntent.LanguageModelFreeForm);
        intent.PutExtra(RecognizerIntent.ExtraLanguage, "es-AR");
        intent.PutExtra(RecognizerIntent.ExtraPrompt, indicacion);
        intent.PutExtra(RecognizerIntent.ExtraMaxResults, 1);
        actividad.StartActivityForResult(intent, CodigoPedido);
        return _pendiente.Task;
    }

    /// <summary>Lo llama MainActivity.OnActivityResult.</summary>
    internal static void Resultado(int codigo, Android.App.Result resultado, Intent? datos)
    {
        if (codigo != CodigoPedido) return;
        var texto = resultado == Android.App.Result.Ok ? datos?.GetStringArrayListExtra(RecognizerIntent.ExtraResults)?.FirstOrDefault() : null;
        _pendiente?.TrySetResult(string.IsNullOrWhiteSpace(texto) ? null : char.ToUpperInvariant(texto[0]) + texto[1..]);
        _pendiente = null;
    }
}
