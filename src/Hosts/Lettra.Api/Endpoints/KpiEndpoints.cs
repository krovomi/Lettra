using System.Security.Cryptography;
using System.Text;
using Lettra.Application.Abstractions.Kpi;
using Lettra.Infrastructure.Catalog.Kpi;
using Lettra.Infrastructure.Observability;
using Microsoft.Extensions.Options;

namespace Lettra.Api.Endpoints;

/// <summary>
/// Indicateurs de pilotage pour le Prometheus de la cellule, puis Grafana (docs/indicateurs-grafana.md). Exposé
/// seulement si un jeton est réglé (<c>Lettra:Kpi:ScrapeToken</c>) ; Prometheus le présente en <c>Bearer</c>. Hors du
/// chemin OpenTelemetry : ces valeurs restent dans la cellule.
/// </summary>
internal static class KpiEndpoints
{
    public const string Route = "/internal/kpi/metrics";

    public static IEndpointRouteBuilder MapKpiScrape(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        KpiOptions options = endpoints.ServiceProvider.GetRequiredService<IOptions<KpiOptions>>().Value;
        if (string.IsNullOrWhiteSpace(options.ScrapeToken))
        {
            return endpoints; // Pas de jeton, pas d'indicateurs : rien n'est exposé par défaut.
        }

        endpoints.MapGet(Route, ScrapeAsync).AllowAnonymous().ExcludeFromDescription();
        return endpoints;
    }

    private static async Task<IResult> ScrapeAsync(HttpContext context, IOptions<KpiOptions> options, IKpiSnapshotProvider snapshots, CancellationToken cancellationToken)
    {
        if (!IsAuthorized(context.Request.Headers.Authorization.ToString(), options.Value.ScrapeToken!))
        {
            return TypedResults.Unauthorized();
        }

        KpiSnapshot snapshot = await snapshots.GetAsync(cancellationToken).ConfigureAwait(false);
        return TypedResults.Text(KpiExposition.Write(snapshot), KpiExposition.ContentType);
    }

    /// <summary>Compare le jeton présenté en temps constant (empreintes de même longueur).</summary>
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
