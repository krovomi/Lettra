using System.Security.Cryptography;
using System.Text;
using Lettra.Domain.Common;
using Lettra.Supervision.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Lettra.Supervision.Infrastructure;

/// <summary>
/// Webhook appelé par l'Alertmanager de la cellule : les alertes d'infrastructure partent vers l'astreinte sans dépendre
/// de Lettra, et sont recopiées ici pour apparaître dans le centre d'alertes (annexe G §4, « deux chemins indépendants »).
/// </summary>
public static class SupervisionEndpoints
{
    public const string AlertmanagerRoute = "/internal/supervision/alertmanager";

    public static IEndpointRouteBuilder MapSupervisionWebhooks(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        SupervisionOptions options = endpoints.ServiceProvider.GetRequiredService<IOptions<SupervisionOptions>>().Value;
        if (string.IsNullOrWhiteSpace(options.WebhookToken))
        {
            return endpoints; // Pas de jeton, pas de webhook : rien n'est exposé par défaut.
        }

        endpoints.MapPost(AlertmanagerRoute, ReceiveAsync).ExcludeFromDescription();
        return endpoints;
    }

    private static async Task<IResult> ReceiveAsync(HttpContext context, IOptions<SupervisionOptions> options, AlertIngestion ingestion, CancellationToken cancellationToken)
    {
        if (!IsAuthorized(context.Request.Headers.Authorization.ToString(), options.Value.WebhookToken!))
        {
            return TypedResults.Unauthorized();
        }

        AlertmanagerPayload? payload = await context.Request
            .ReadFromJsonAsync(AlertmanagerJsonContext.Default.AlertmanagerPayload, cancellationToken)
            .ConfigureAwait(false);
        if (payload?.Alerts is null)
        {
            return TypedResults.BadRequest();
        }

        int accepted = 0;
        int rejected = 0;
        foreach (AlertmanagerAlert alert in payload.Alerts)
        {
            Result<AlertSignal> signal = AlertmanagerMapper.ToSignal(alert);
            bool ok = signal.IsSuccess && (await ingestion.IngestAsync(signal.Value, cancellationToken).ConfigureAwait(false)).IsSuccess;
            _ = ok ? accepted++ : rejected++;
        }

        // Alertmanager rejoue sur 5xx seulement : une alerte mal étiquetée n'est pas rejouée, elle est comptée.
        return TypedResults.Ok(new WebhookReceipt(accepted, rejected));
    }

    internal static bool IsAuthorized(string header, string token)
    {
        const string scheme = "Bearer ";
        if (!header.StartsWith(scheme, StringComparison.Ordinal))
        {
            return false;
        }

        byte[] presented = SHA256.HashData(Encoding.UTF8.GetBytes(header[scheme.Length..]));
        byte[] expected = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return CryptographicOperations.FixedTimeEquals(presented, expected);
    }
}

internal sealed record WebhookReceipt(int Accepted, int Rejected);
