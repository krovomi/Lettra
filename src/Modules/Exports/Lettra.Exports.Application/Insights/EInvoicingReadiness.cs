using Lettra.Application.Abstractions.Cabinet;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Calculations;
using Lettra.Contracts.Cabinet;
using Lettra.Contracts.Exports;
using Lettra.Contracts.Insights;
using Lettra.Domain.Billing;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;
using Lettra.Exports.Application.EInvoicing.Platform;

namespace Lettra.Exports.Application.Insights;

/// <summary>Le score de préparation du dossier à la facture électronique.</summary>
public sealed record GetEInvoicingReadinessQuery(DossierId Dossier) : IQuery<Result<EInvoicingReadinessResponse>>, IRequirePermission
{
    public Permission Permission => Permission.DossierLire;
}

/// <summary>Ce que la fiche entreprise porte, sans les valeurs.</summary>
public sealed record CompanyReadinessFacts(bool HasSiren, bool HasSiret, bool HasVatNumber, bool VatFranchise, bool HasIban);

/// <summary>Un client ou un fournisseur actif, sans ses coordonnées.</summary>
/// <param name="Id">Son identifiant.</param>
/// <param name="Name">Son nom.</param>
/// <param name="IsCompany">Vrai pour un professionnel (un fournisseur l'est toujours).</param>
/// <param name="Country">Le pays de son adresse (ISO 3166-1 alpha-2).</param>
/// <param name="HasSiren">Vrai s'il a un SIREN.</param>
/// <param name="HasVatNumber">Vrai s'il a un numéro de TVA intracommunautaire.</param>
public sealed record PartyReadinessFacts(Guid Id, string Name, bool IsCompany, string Country, bool HasSiren, bool HasVatNumber);

/// <summary>Une facture validée de la période et sa dernière transmission à la plateforme.</summary>
/// <param name="Id">La facture.</param>
/// <param name="Number">Son numéro.</param>
/// <param name="Customer">Le client figé sur la facture : il décide si elle relève de la facture électronique.</param>
/// <param name="PlatformStatus">Le dernier statut de sa dernière transmission ; <c>null</c> si elle n'est jamais partie.</param>
public sealed record PieceReadinessFacts(Guid Id, string Number, PartySnapshot Customer, PlatformStatusDto? PlatformStatus);

/// <summary>Ce que le contrôle lit dans le dossier.</summary>
public sealed record EInvoicingReadinessInput(
    CompanyReadinessFacts? Company,
    IReadOnlyList<PartyReadinessFacts> Customers,
    IReadOnlyList<PartyReadinessFacts> Suppliers,
    IReadOnlyList<PieceReadinessFacts> Pieces);

/// <summary>Lecture seule, dans la base du dossier : la fiche entreprise, les tiers actifs et les factures validées depuis une date.</summary>
public interface IEInvoicingReadinessReader
{
    Task<EInvoicingReadinessInput> ReadAsync(DateOnly since, CancellationToken cancellationToken);
}

/// <summary>
/// Le calcul du score, pur et déterministe. Sept contrôles, chacun noté de 0 à 100 selon la part des éléments prêts ;
/// le score est leur moyenne arrondie. Un contrôle sans élément concerné compte pour 100. Les noms des éléments à
/// corriger ne sortent que vers l'écran du dossier ; la vue cabinet ne reçoit que le nombre de contrôles à revoir.
/// </summary>
public static class EInvoicingReadiness
{
    /// <summary>En dessous, le dossier est à surveiller dans la vue cabinet ; sous <see cref="CriticalBelow"/>, urgent.</summary>
    public const int WarningBelow = 80;

    public const int CriticalBelow = 50;

    public static DateOnly SinceOf(DateOnly today) => today.AddDays(-EInvoicingReadinessResponse.RecentDays);

    public static EInvoicingReadinessResponse Compute(EInvoicingReadinessInput input, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(input);
        CompanyReadinessFacts? company = input.Company;
        PartyReadinessFacts[] french = [.. input.Customers.Where(c => c.IsCompany && IsFrance(c.Country))];
        PartyReadinessFacts[] european = [.. input.Customers.Where(c => c.IsCompany && !IsFrance(c.Country) && IsEuropean(c.Country))];
        PartyReadinessFacts[] suppliers = [.. input.Suppliers.Where(s => IsFrance(s.Country))];
        PieceReadinessFacts[] domestic = [.. input.Pieces.Where(p => TransmitInvoiceHandler.IsDomesticBusiness(p.Customer))];

        EInvoicingCheckResponse[] checks =
        [
            Single(EInvoicingCheckDto.CompanyIdentity, company is { HasSiren: true, HasSiret: true }),
            Single(EInvoicingCheckDto.CompanyVat, company is { HasVatNumber: true } or { VatFranchise: true }),
            Single(EInvoicingCheckDto.CompanyIban, company is { HasIban: true }),
            Of(EInvoicingCheckDto.FrenchCustomers, french, c => c.HasSiren, c => new EInvoicingGapResponse(c.Id, c.Name)),
            Of(EInvoicingCheckDto.EuropeanCustomers, european, c => c.HasVatNumber, c => new EInvoicingGapResponse(c.Id, c.Name)),
            Of(EInvoicingCheckDto.Suppliers, suppliers, s => s.HasSiren, s => new EInvoicingGapResponse(s.Id, s.Name)),
            Of(EInvoicingCheckDto.Transmissions, domestic, p => p.PlatformStatus is { } s && s != PlatformStatusDto.Rejected, p => new EInvoicingGapResponse(p.Id, p.Number)),
        ];
        int score = (int)Math.Round(checks.Average(c => c.Percent), MidpointRounding.AwayFromZero);
        return new EInvoicingReadinessResponse(today, SinceOf(today), score, checks);
    }

    /// <summary>L'indicateur de la vue cabinet : le nombre de contrôles à revoir, l'urgence selon le score.</summary>
    public static DossierIndicator Indicator(EInvoicingReadinessResponse readiness)
    {
        ArgumentNullException.ThrowIfNull(readiness);
        IndicatorLevel level = readiness.Score switch
        {
            100 => IndicatorLevel.Ok,
            >= WarningBelow => IndicatorLevel.Info,
            >= CriticalBelow => IndicatorLevel.Warning,
            _ => IndicatorLevel.Critical,
        };
        return new DossierIndicator(DossierIndicatorKeys.EInvoicingReadiness, readiness.Failing, level);
    }

    private static EInvoicingCheckResponse Single(EInvoicingCheckDto check, bool ok) =>
        new(check, 1, ok ? 1 : 0, ok ? 100 : 0, []);

    private static EInvoicingCheckResponse Of<T>(EInvoicingCheckDto check, T[] items, Func<T, bool> ready, Func<T, EInvoicingGapResponse> gap)
    {
        T[] missing = [.. items.Where(i => !ready(i))];
        int passing = items.Length - missing.Length;
        int percent = items.Length == 0 ? 100 : (int)Math.Floor(100m * passing / items.Length);
        return new EInvoicingCheckResponse(
            check,
            items.Length,
            passing,
            percent,
            [.. missing.Select(gap).OrderBy(g => g.Label, StringComparer.OrdinalIgnoreCase).Take(EInvoicingReadinessResponse.MaxGaps)]);
    }

    private static bool IsFrance(string country) => string.Equals(country, "FR", StringComparison.OrdinalIgnoreCase);

    private static bool IsEuropean(string country) => EuropeanUnion.MemberStates.Contains(country.ToUpperInvariant());
}

internal sealed class GetEInvoicingReadinessHandler(IEInvoicingReadinessReader reader, TimeProvider clock)
    : IQueryHandler<GetEInvoicingReadinessQuery, Result<EInvoicingReadinessResponse>>
{
    public async Task<Result<EInvoicingReadinessResponse>> HandleAsync(GetEInvoicingReadinessQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        DateOnly today = LegalCalendar.Today(clock);
        EInvoicingReadinessInput input = await reader.ReadAsync(EInvoicingReadiness.SinceOf(today), cancellationToken).ConfigureAwait(false);
        return EInvoicingReadiness.Compute(input, today);
    }
}

/// <summary>La préparation à la facture électronique dans la vue cabinet : un compteur de contrôles à revoir, aucun nom.</summary>
internal sealed class EInvoicingReadinessIndicatorSource(IEInvoicingReadinessReader reader) : IDossierIndicatorSource
{
    public async Task<IReadOnlyList<DossierIndicator>> ComputeAsync(DateOnly today, CancellationToken cancellationToken)
    {
        EInvoicingReadinessInput input = await reader.ReadAsync(EInvoicingReadiness.SinceOf(today), cancellationToken).ConfigureAwait(false);
        return [EInvoicingReadiness.Indicator(EInvoicingReadiness.Compute(input, today))];
    }
}
