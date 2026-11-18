using Microsoft.Extensions.AI;

namespace DeepArquitectura.Web.Agents;

/// <summary>Cliente de Azure OpenAI para redactar el resumen diario (opcional: sin configurar, el resumen sale por reglas).</summary>
public static class ClienteChat
{
    /// <returns>null si Azure OpenAI no está configurado.</returns>
    public static IChatClient? Crear(AzureOpenAIOptions cfg) =>
        !cfg.Configurado ? null
        : new Azure.AI.OpenAI.AzureOpenAIClient(new Uri(cfg.Endpoint!), new Azure.AzureKeyCredential(cfg.ApiKey!))
            .GetChatClient(cfg.Deployment)
            .AsIChatClient();
}
