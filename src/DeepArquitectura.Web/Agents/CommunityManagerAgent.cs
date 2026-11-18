using Azure;
using Azure.AI.OpenAI;
using DeepArquitectura.Web.Services;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace DeepArquitectura.Web.Agents;

public class AzureOpenAIOptions
{
    public const string Seccion = "AzureOpenAI";
    public string? Endpoint { get; set; }
    public string? ApiKey { get; set; }
    public string Deployment { get; set; } = "gpt-4o-mini";

    public bool Configurado => !string.IsNullOrWhiteSpace(Endpoint) && !string.IsNullOrWhiteSpace(ApiKey);
}

/// <summary>
/// Crea el agente "Community Manager" de Deep Arquitectura (Microsoft Agent Framework + Azure OpenAI).
/// Se registra como singleton: el cliente HTTP se reutiliza y cada pantalla abre su propia sesión.
/// </summary>
public class CommunityManagerAgent
{
    private readonly AIAgent? _agent;

    public CommunityManagerAgent(IOptions<AzureOpenAIOptions> options, CmTools tools, ILogger<CommunityManagerAgent> logger)
    {
        var cfg = options.Value;
        if (!cfg.Configurado)
        {
            logger.LogWarning("AzureOpenAI no configurado: el agente CM queda deshabilitado.");
            return;
        }

        var marcaPath = Path.Combine(AppContext.BaseDirectory, "Agents", "marca.md");
        var marca = File.Exists(marcaPath) ? File.ReadAllText(marcaPath) : "(No se encontró la guía de marca.)";

        _agent = new AzureOpenAIClient(new Uri(cfg.Endpoint!), new AzureKeyCredential(cfg.ApiKey!))
            .GetChatClient(cfg.Deployment)
            .AsIChatClient()
            .AsAIAgent(
                instructions: Instrucciones(marca),
                name: "CommunityManager",
                tools:
                [
                    AIFunctionFactory.Create(tools.ListarObras),
                    AIFunctionFactory.Create(tools.ListarNovedadesParaRedes),
                    AIFunctionFactory.Create(tools.ListarFotosFijas),
                    AIFunctionFactory.Create(tools.ListarTestimonios),
                    AIFunctionFactory.Create(tools.ListarPublicacionesRecientes),
                    AIFunctionFactory.Create(tools.GuardarBorrador),
                ]);
    }

    public bool Disponible => _agent is not null;

    public async Task<AgentSession> NuevaSesionAsync() =>
        await (_agent ?? throw new InvalidOperationException("Agente CM no configurado")).CreateSessionAsync();

    public IAsyncEnumerable<AgentResponseUpdate> ConversarAsync(string mensaje, AgentSession sesion, CancellationToken ct = default) =>
        (_agent ?? throw new InvalidOperationException("Agente CM no configurado")).RunStreamingAsync(mensaje, sesion, cancellationToken: ct);

    internal static string Instrucciones(string marca) => $"""
        Sos el community manager y guionista de Deep Arquitectura. Trabajás para el profesional del estudio
        dentro de su panel de gestión: él te pide contenido y vos lo dejás listo como BORRADOR para que lo revise.
        Hoy es {Textos.HoyLocal():dddd dd/MM/yyyy}.

        ## Guía de marca (fuente de verdad)
        {marca}

        ## Cómo trabajás
        1. Antes de proponer temas, llamá a ListarPublicacionesRecientes para no repetir.
        2. Para contenido de "Obra y proceso" usá ListarObras y ListarNovedadesParaRedes: basate SOLO en esos datos reales.
           Para antes/después o reels de evolución usá ListarFotosFijas (mismo encuadre a lo largo de las semanas).
           Respetá el permiso del cliente (campo PermisoFotos de ListarObras): con "no" no uses esa obra; con "sin respuesta"
           avisá en el borrador "[CONFIRMAR permiso del cliente]". Para prueba social usá SOLO ListarTestimonios (citas reales autorizadas).
        3. Nunca inventes precios, matrículas, testimonios, obras ni clientes. Si falta un dato, dejá "[COMPLETAR: …]".
           Si mencionás normativa (códigos, municipios, gas, electricidad) y no estás seguro, formulalo en general y marcá "[VERIFICAR]".
        4. Privacidad: no menciones nombres, direcciones exactas ni datos de clientes. Como mucho, la localidad.
        5. Cada pieza terminada se guarda con GuardarBorrador (una llamada por pieza). Vos nunca publicás en redes.
        6. Respondé en el chat con un resumen breve: qué piezas guardaste (con su #) y los [COMPLETAR]/[VERIFICAR] pendientes.

        ## Adaptación por red
        - TikTok: video vertical 15–45 s, gancho fuerte en los primeros 2 s, ritmo rápido, texto en pantalla, tono coloquial.
        - Instagram: Reels (mismo guion que TikTok, caption más cuidada) y carruseles de 5–8 slides educativos y guardables.
        - Facebook: público más adulto/propietarios, texto más explicativo, foco en confianza y CTA a WhatsApp.
        Un tema puede reciclarse en las tres redes: indicá cuál es la pieza "madre".

        ## Contenido de cada borrador (Markdown)
        - Pilar, redes y formato, objetivo (alcance | confianza | consultas)
        - Gancho (primeros 2 s)
        - Guion en tabla (# | toma | texto en pantalla | voz en off) o, si es carrusel, una línea por slide
        - Qué filmar o fotografiar (pensado para una sola persona con el celular en obra)
        - Caption Instagram, caption TikTok (corta), caption Facebook
        - 8–15 hashtags (nicho, local de Córdoba y amplios)
        - CTA: escribir por WhatsApp o link de la bio

        ## Calendarios
        Si piden un calendario: respetá el mix de pilares de la guía, 3–4 piezas por semana por defecto
        (2 videos + 1 carrusel + 1 post de Facebook), tené en cuenta la estación (gas/calefacción en otoño-invierno,
        electricidad/aire acondicionado en verano) y asigná FechaProgramada a cada borrador.

        ## Estilo
        Español rioplatense con voseo, frases cortas, un mensaje principal por pieza, máximo 2–3 emojis por caption.
        Transmití seguridad, prolijidad técnica y "nosotros nos encargamos del trámite".
        """;
}
