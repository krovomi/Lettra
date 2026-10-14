using System.Net.Http.Json;
using System.Text.Json;
using Lettra.Application.Abstractions.External;
using Lettra.Contracts.Directory;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;
using Lettra.Infrastructure.External.Sirene;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lettra.Infrastructure.External.Vies;

/// <summary>
/// Vérification VIES. Un numéro inconnu est une réponse (<c>IsValid = false</c>) ; un État membre
/// indisponible, une saturation ou une coupure réseau donnent <c>Directory.Unavailable</c>.
/// </summary>
internal sealed partial class ViesVatNumberChecker(
    HttpClient http,
    HybridCache cache,
    TimeProvider clock,
    IOptions<ExternalServicesOptions> options,
    ILogger<ViesVatNumberChecker> logger) : IVatNumberChecker
{
    private static readonly Uri CheckUri = new("check-vat-number", UriKind.Relative);

    public async Task<Result<VatNumberCheckResponse>> CheckAsync(VatNumber vatNumber, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(vatNumber);
        HybridCacheEntryOptions entry = new() { Expiration = options.Value.ViesCacheDuration, LocalCacheExpiration = options.Value.ViesCacheDuration };
        try
        {
            return await cache.GetOrCreateAsync(
                $"lettra:vies:{vatNumber.Value}",
                vatNumber,
                async (vat, ct) => await FetchAsync(vat, ct).ConfigureAwait(false),
                entry,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (DirectoryUnavailableException)
        {
            return DirectoryErrors.Unavailable;
        }
    }

    private async Task<VatNumberCheckResponse> FetchAsync(VatNumber vatNumber, CancellationToken cancellationToken)
    {
        ViesRequest request = new(vatNumber.CountryPrefix, vatNumber.Value[2..]);
        try
        {
            using HttpResponseMessage response = await http
                .PostAsJsonAsync(CheckUri, request, ViesJsonContext.Default.ViesRequest, cancellationToken)
                .ConfigureAwait(false);
            ViesResponse? body = null;
            try
            {
                body = await response.Content.ReadFromJsonAsync(ViesJsonContext.Default.ViesResponse, cancellationToken).ConfigureAwait(false);
            }
            catch (JsonException) when (!response.IsSuccessStatusCode)
            {
                // Page d'erreur non JSON : traitée comme une indisponibilité ci-dessous.
            }

            if (body?.Valid is not bool valid || body.ActionSucceed == false || body.ErrorWrappers is { Count: > 0 } || IsServiceError(body.UserError))
            {
                LogUnavailable(logger, (int)response.StatusCode, body?.UserError ?? (body?.ErrorWrappers is { Count: > 0 } wrappers ? wrappers[0].Error : null) ?? "none");
                throw new DirectoryUnavailableException();
            }

            return new VatNumberCheckResponse(
                vatNumber.Value,
                valid,
                Published(body.Name),
                Published(body.Address),
                body.RequestDate ?? clock.GetUtcNow());
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            LogUnavailable(logger, 0, ex.GetType().Name);
            throw new DirectoryUnavailableException();
        }
    }

    /// <summary>« VALID » et « INVALID » sont des réponses ; tout autre code signale un incident du service.</summary>
    private static bool IsServiceError(string? userError) =>
        userError is not null and not "VALID" and not "INVALID";

    private static string? Published(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.Trim() == "---" ? null : value.Trim();

    [LoggerMessage(EventId = 7111, Level = LogLevel.Warning, Message = "VIES indisponible (HTTP {StatusCode}, {Reason})")]
    private static partial void LogUnavailable(ILogger logger, int statusCode, string reason);
}
