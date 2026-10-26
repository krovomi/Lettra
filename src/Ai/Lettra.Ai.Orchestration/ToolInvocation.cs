using System.Text.Json;
using Lettra.Ai.Privacy;
using Lettra.Application.Abstractions.Ai;
using Microsoft.Extensions.Logging;

namespace Lettra.Ai.Orchestration;

/// <summary>
/// L'exécution d'un outil, commune à l'assistant de Lettra et au serveur MCP : l'entrée est validée contre le schéma de
/// l'outil, rétablie (vraies valeurs) si l'outil en a besoin, et une panne de l'outil devient un résultat d'erreur sans
/// détail technique : le message pourrait contenir des données.
/// </summary>
internal static partial class ToolInvocation
{
    /// <summary>La raison du refus de l'entrée, ou <c>null</c> si elle respecte le schéma de l'outil.</summary>
    public static string? Reject(IAiTool tool, JsonElement input) => JsonSchemaValidator.Validate(input, tool.InputSchema);

    public static async Task<AiToolResult> RunAsync(
        IAiTool tool, JsonElement input, AiToolContext context, PseudonymizationSession privacy, ILogger logger, CancellationToken cancellationToken)
    {
        try
        {
            return await tool
                .InvokeAsync(tool.ReceivesRealValues ? privacy.RestoreJson(input) : input, context, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidOperationException or FormatException or KeyNotFoundException
            && !cancellationToken.IsCancellationRequested)
        {
            LogToolFailed(logger, tool.Name, exception.GetType().Name);
            return AiToolResult.Error("L'outil a échoué.");
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Outil de l'assistant {Tool} en échec ({ExceptionType}).")]
    private static partial void LogToolFailed(ILogger logger, string tool, string exceptionType);
}
