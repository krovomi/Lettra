using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Lettra.Application.Abstractions.External;
using Lettra.Contracts.Directory;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lettra.Infrastructure.External.Sirene;

/// <summary>
/// Annuaire SIRENE via l'API Recherche d'entreprises. On ne journalise jamais la saisie (elle peut
/// désigner un client du dossier) : seulement le statut HTTP et la durée.
/// </summary>
internal sealed partial class RechercheEntreprisesDirectory(
    HttpClient http,
    HybridCache cache,
    IOptions<ExternalServicesOptions> options,
    ILogger<RechercheEntreprisesDirectory> logger) : ICompanyDirectory
{
    public async Task<Result<CompanySearchResponse>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        string normalized = Normalize(query);
        HybridCacheEntryOptions entry = new() { Expiration = options.Value.SireneCacheDuration, LocalCacheExpiration = options.Value.SireneCacheDuration };

        // Un échec n'est pas mis en cache : la fabrique lève, et l'exception est traduite plus bas.
        try
        {
            CompanySearchResponse response = await cache.GetOrCreateAsync(
                string.Create(CultureInfo.InvariantCulture, $"lettra:sirene:{maxResults}:{normalized}"),
                (normalized, maxResults),
                async (state, ct) => await FetchAsync(state.normalized, state.maxResults, ct).ConfigureAwait(false),
                entry,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return response;
        }
        catch (DirectoryUnavailableException)
        {
            return DirectoryErrors.Unavailable;
        }
    }

    private async Task<CompanySearchResponse> FetchAsync(string query, int maxResults, CancellationToken cancellationToken)
    {
        Uri uri = new(
            string.Create(CultureInfo.InvariantCulture, $"search?q={Uri.EscapeDataString(query)}&page=1&per_page={maxResults}"),
            UriKind.Relative);
        try
        {
            using HttpResponseMessage response = await http.GetAsync(uri, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                LogFailure(logger, (int)response.StatusCode);
                throw new DirectoryUnavailableException();
            }

            SearchResponse? body = await response.Content
                .ReadFromJsonAsync(SireneJsonContext.Default.SearchResponse, cancellationToken)
                .ConfigureAwait(false);
            List<CompanySearchResult> items = (body?.Results ?? [])
                .Select(ToResult)
                .OfType<CompanySearchResult>()
                .ToList();
            return new CompanySearchResponse(items, body?.TotalResults ?? items.Count);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            LogUnreachable(logger, ex.GetType().Name);
            throw new DirectoryUnavailableException();
        }
    }

    /// <summary>Les espaces de lisibilité d'un SIREN ou d'un SIRET sont retirés ; le reste est gardé tel quel.</summary>
    internal static string Normalize(string query)
    {
        string trimmed = query.Trim();
        string compact = string.Concat(trimmed.Where(c => !char.IsWhiteSpace(c)));
        return compact.Length is 9 or 14 && compact.All(char.IsAsciiDigit) ? compact : trimmed;
    }

    internal static CompanySearchResult? ToResult(CompanyResult company)
    {
        Result<Siren> siren = Siren.Create(company.Siren);
        if (siren.IsFailure)
        {
            return null;
        }

        Establishment? office = company.HeadOffice;
        bool abroad = !string.IsNullOrWhiteSpace(office?.ForeignCountryCode);
        string? siret = office?.Siret is { } rawSiret && Siret.Create(rawSiret).IsSuccess ? rawSiret : null;
        string name = FirstNonEmpty(company.LegalName, company.FullName) ?? siren.Value.Value;

        return new CompanySearchResult(
            siren.Value.Value,
            siret,
            name,
            office is null ? null : StreetLine(office),
            office is null ? null : FirstNonEmpty(office.AddressComplement, office.SpecialDistribution),
            office?.PostalCode,
            office is null ? null : abroad ? office.ForeignCity : FirstNonEmpty(office.CedexLabel, office.City),
            abroad ? "XX" : CountryCode.France.Value,
            VatNumber.FromSiren(siren.Value).Value,
            company.MainActivity,
            !string.Equals(company.AdministrativeState, "C", StringComparison.Ordinal));
    }

    /// <summary>« 12 B RUE DE LA PAIX » à partir des morceaux de l'adresse INSEE.</summary>
    private static string? StreetLine(Establishment office)
    {
        string line = string.Join(' ', new[] { office.StreetNumber, office.RepetitionIndex, office.StreetType, office.StreetName }
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!.Trim()));
        return line.Length == 0 ? null : line;
    }

    private static string? FirstNonEmpty(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();

    [LoggerMessage(EventId = 7101, Level = LogLevel.Warning, Message = "Annuaire SIRENE : réponse HTTP {StatusCode}")]
    private static partial void LogFailure(ILogger logger, int statusCode);

    [LoggerMessage(EventId = 7102, Level = LogLevel.Warning, Message = "Annuaire SIRENE injoignable ({Reason})")]
    private static partial void LogUnreachable(ILogger logger, string reason);
}

/// <summary>Signal interne : le service ne répond pas utilement (jamais mis en cache, traduit en erreur métier).</summary>
internal sealed class DirectoryUnavailableException : Exception;
