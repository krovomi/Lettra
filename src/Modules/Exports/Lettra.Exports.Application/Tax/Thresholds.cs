using Lettra.Application.Abstractions.Cabinet;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Calculations;
using Lettra.Calculations.Compliance;
using Lettra.Calculations.Thresholds;
using Lettra.Contracts.Cabinet;
using Lettra.Contracts.Tax;
using Lettra.Domain.Billing;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Exports.Application.Tax;

/// <summary>Une facture fournisseur reçue, rattachée à un fournisseur du dossier : sa date et son montant HT (négatif pour un avoir).</summary>
public sealed record SupplierPurchase(Guid SupplierId, DateOnly Date, decimal AmountExclTax);

/// <summary>Les achats de chaque fournisseur, lus dans les factures reçues par la plateforme agréée (base du dossier).</summary>
public interface ISupplierPurchasesReader
{
    /// <summary>Les factures et avoirs reçus datés du <paramref name="from"/> au <paramref name="to"/>, rattachés à un fournisseur.</summary>
    Task<IReadOnlyList<SupplierPurchase>> ReadAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken);
}

/// <summary>Une vente qui compte pour le chiffre d'affaires : sa date, sa nature (biens ou services) et son montant HT (négatif pour un avoir).</summary>
public sealed record SalesAmount(DateOnly Date, RevenueNature Nature, decimal AmountExclTax);

/// <summary>Les sommes d'un tiers suivi (fournisseur, bénéficiaire d'honoraires) : datées, plus celles notées pour l'exercice sans date.</summary>
public sealed record PartyAmounts(Guid PartyId, string Name, IReadOnlyList<DatedAmount> Amounts, decimal Undated = 0m);

/// <summary>
/// Ce que lit le calcul des seuils, déjà extrait des bases : la fiche entreprise (absente : <paramref name="ProfileKnown"/>
/// faux), les réglages fiscaux, les ventes, les achats des fournisseurs pas encore soumis à l'attestation de vigilance et
/// les sommes versées aux bénéficiaires d'honoraires suivis.
/// </summary>
public sealed record ThresholdInput(
    bool ProfileKnown,
    bool VatFranchise,
    VatDeclarationRegime? Regime,
    CompanySize Size,
    FiscalSettings Settings,
    IReadOnlyList<SalesAmount> Sales,
    IReadOnlyList<PartyAmounts> Suppliers,
    IReadOnlyList<PartyAmounts> FeeBeneficiaries);

/// <summary>Les seuils réglementaires qui approchent pour le dossier : franchise et régime de TVA, catégorie, vigilance, DAS 2.</summary>
public sealed record GetThresholdsQuery(DossierId Dossier) : IQuery<Result<ThresholdsResponse>>, IRequirePermission
{
    public Permission Permission => Permission.DossierLire;
}

/// <summary>
/// Les seuils qui approchent (règles écrites en C#, aucune IA) : pour chaque seuil qui s'applique au dossier, le cumul de
/// sa période, la projection au rythme des 90 derniers jours jusqu'à la fin de la période, la date de franchissement
/// constatée ou prévue et la date à laquelle les règles changent. Les montants viennent du catalogue daté
/// <see cref="ThresholdCatalog"/>. La vue cabinet n'en reçoit qu'un compteur ; la future liste « Décisions à prendre »
/// lit <see cref="ReadAsync"/> par un adaptateur mince.
/// </summary>
public sealed class ThresholdWatch(
    ICompanyProfileRepository profiles,
    ITaxDocumentReader documents,
    TaxDeclarations tax,
    ISupplierPurchasesReader purchases,
    IAttestationStore attestations,
    FeeReturns fees)
{
    /// <summary>Un seuil franchi, ou dont le franchissement est prévu dans ce nombre de mois, est « proche ».</summary>
    public const int SoonMonths = 3;

    public async Task<ThresholdsResponse> ReadAsync(DateOnly today, CancellationToken cancellationToken)
    {
        CompanyProfile? profile = await profiles.FindAsync(cancellationToken).ConfigureAwait(false);
        FiscalSettings settings = await tax.SettingsAsync(cancellationToken).ConfigureAwait(false);
        (DateOnly fiscalStart, _) = settings.FiscalYearOf(today);
        DateOnly from = Earliest(new DateOnly(today.Year, 1, 1), fiscalStart, today.AddDays(1 - ThresholdProjection.RateDays));

        IReadOnlyList<TaxDocument> sold = await documents.ReadAsync(from, today, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<SupplierPurchase> bought = await purchases.ReadAsync(from, today, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<SupplierAttestationEntry> suppliers = await attestations.ListAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<FeePayments> paid = await fees.PaymentsAsync(from, today, fiscalStart, cancellationToken).ConfigureAwait(false);

        ThresholdInput input = new(
            profile is not null,
            profile?.VatFranchise ?? false,
            profile?.VatDeclarationRegime,
            profile?.Size ?? CompanySize.Unknown,
            settings,
            [.. sold.SelectMany(Sales)],
            [
                .. suppliers
                    .Where(s => s.Attestation is not { IsRequired: true })
                    .Select(s => new PartyAmounts(
                        s.SupplierId.Value,
                        s.SupplierName,
                        [.. bought.Where(b => b.SupplierId == s.SupplierId.Value).Select(b => new DatedAmount(b.Date, b.AmountExclTax))]))
                    .Where(s => s.Amounts.Count > 0),
            ],
            [.. paid.Select(p => new PartyAmounts(p.Payee.Id.Value, p.Payee.Name, [.. p.Debits.Select(d => new DatedAmount(d.Date, d.Amount))], p.PaidOutside))]);
        return new ThresholdsResponse(today, ThresholdProjection.RateDays, SoonMonths, profile is null, Evaluate(input, today));
    }

    /// <summary>
    /// Ce qu'une pièce validée apporte au chiffre d'affaires : ses lignes de biens et de services, hors débours et hors
    /// acomptes (comptés sur la facture finale), en négatif pour un avoir.
    /// </summary>
    public static IEnumerable<SalesAmount> Sales(TaxDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        int sign = document.Kind == InvoiceKind.CreditNote ? -1 : 1;
        return document.Lines
            .Where(l => l.Nature is RevenueNature.Goods or RevenueNature.Services && l.ExemptionCode != VatOperations.DisbursementExemption)
            .GroupBy(l => l.Nature)
            .Select(g => new SalesAmount(document.Date, g.Key, sign * g.Sum(l => l.TotalExclTax)));
    }

    /// <summary>Tous les seuils qui s'appliquent au dossier, le plus urgent d'abord ; les tiers n'apparaissent qu'à l'approche de leur seuil.</summary>
    public static IReadOnlyList<ThresholdWatchResponse> Evaluate(ThresholdInput input, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(input);
        List<ThresholdWatchResponse> rows = [];
        DateOnly yearStart = new(today.Year, 1, 1);
        DateOnly yearEnd = new(today.Year, 12, 31);
        DateOnly nextYear = yearEnd.AddDays(1);
        (DateOnly fiscalStart, DateOnly fiscalEnd) = input.Settings.FiscalYearOf(today);

        DatedAmount[] total = [.. input.Sales.Select(s => new DatedAmount(s.Date, s.AmountExclTax))];
        DatedAmount[] services = [.. input.Sales.Where(s => s.Nature == RevenueNature.Services).Select(s => new DatedAmount(s.Date, s.AmountExclTax))];
        bool sellsGoods = input.Sales.Any(s => s.Nature == RevenueNature.Goods);
        bool sellsServices = services.Length > 0 || !sellsGoods;

        void Add(string code, ThresholdFamilyDto family, ThresholdTierDto tier, DateOnly parameterDate, IReadOnlyList<DatedAmount> amounts,
            DateOnly start, DateOnly end, Func<ThresholdProgress, DateOnly?> effective, PartyAmounts? party = null, bool inclusive = false, bool onlyNear = false)
        {
            if (ThresholdCatalog.Find(code, parameterDate) is not { } parameter)
            {
                return;
            }

            ThresholdProgress progress = ThresholdProjection.Project(amounts, parameter.Amount, start, end, today, inclusive, party?.Undated ?? 0m);
            if (onlyNear && progress.Status == ThresholdStatus.Far)
            {
                return;
            }

            rows.Add(Row(parameter, family, tier, party, start, end, progress, effective(progress), today));
        }

        if (input.ProfileKnown && input.VatFranchise)
        {
            // Franchise en base : au-delà du seuil majoré, la TVA s'applique dès le jour du dépassement ; au-delà du seuil de
            // base sur l'année, à partir du 1er janvier suivant. Le chiffre d'affaires global se compare au seuil des ventes.
            if (sellsServices)
            {
                Add(ThresholdCodes.FranchiseServicesIncreased, ThresholdFamilyDto.VatFranchise, ThresholdTierDto.Increased, today, services, yearStart, yearEnd, Crossing);
                Add(ThresholdCodes.FranchiseServicesBase, ThresholdFamilyDto.VatFranchise, ThresholdTierDto.Base, nextYear, services, yearStart, yearEnd, p => Next(p, nextYear));
            }

            if (sellsGoods)
            {
                Add(ThresholdCodes.FranchiseGoodsIncreased, ThresholdFamilyDto.VatFranchise, ThresholdTierDto.Increased, today, total, yearStart, yearEnd, Crossing);
                Add(ThresholdCodes.FranchiseGoodsBase, ThresholdFamilyDto.VatFranchise, ThresholdTierDto.Base, nextYear, total, yearStart, yearEnd, p => Next(p, nextYear));
            }
        }

        if (input.ProfileKnown && !input.VatFranchise && input.Regime == VatDeclarationRegime.RealSimplified)
        {
            // Régime simplifié (jusqu'au 31 décembre 2026) : au-delà du seuil majoré, le réel normal s'applique depuis le
            // 1er janvier de l'année du dépassement (le premier jour de l'exercice s'il ne suit pas l'année civile).
            DateOnly from = input.Settings.IsCalendarYear ? yearStart : fiscalStart;
            if (sellsServices)
            {
                Add(ThresholdCodes.SimplifiedServicesIncreased, ThresholdFamilyDto.VatSimplifiedRegime, ThresholdTierDto.Increased, today, services, yearStart, yearEnd, p => Next(p, from));
            }

            if (sellsGoods)
            {
                Add(ThresholdCodes.SimplifiedGoodsIncreased, ThresholdFamilyDto.VatSimplifiedRegime, ThresholdTierDto.Increased, today, total, yearStart, yearEnd, p => Next(p, from));
            }
        }

        if (input.ProfileKnown && !input.VatFranchise
            && (input.Regime == VatDeclarationRegime.RealSimplified || input.Settings.VatPeriodicity == VatPeriodicityDto.Quarterly))
        {
            // Déclaration trimestrielle (à partir de 2027) : au-delà du seuil sur l'année, mensuelle l'année suivante ;
            // au-delà du seuil majoré en cours d'année, mensuelle dès le mois du dépassement.
            Add(ThresholdCodes.QuarterlyBase, ThresholdFamilyDto.VatQuarterlyReturn, ThresholdTierDto.Base, nextYear, total, yearStart, yearEnd, p => Next(p, nextYear));
            Add(ThresholdCodes.QuarterlyIncreased, ThresholdFamilyDto.VatQuarterlyReturn, ThresholdTierDto.Increased, today, total, yearStart, yearEnd,
                p => (p.CrossedOn ?? p.ExpectedOn) is { } day ? new DateOnly(day.Year, day.Month, 1) : null);
        }

        if (input.ProfileKnown && SizeCode(input.Size) is { } sizeCode)
        {
            // Catégorie de l'entreprise : seul le chiffre d'affaires de l'exercice est connu ; l'effectif et le bilan se vérifient avec le cabinet.
            Add(sizeCode, ThresholdFamilyDto.CompanySize, ThresholdTierDto.Simple, today, total, fiscalStart, fiscalEnd, _ => null);
        }

        foreach (PartyAmounts supplier in input.Suppliers)
        {
            // Attestation de vigilance : la prestation s'apprécie dans son ensemble, même payée ou facturée en plusieurs fois.
            Add(ThresholdCodes.Vigilance, ThresholdFamilyDto.SubcontractorVigilance, ThresholdTierDto.Simple, today, supplier.Amounts, fiscalStart, fiscalEnd,
                Crossing, supplier, inclusive: true, onlyNear: true);
        }

        foreach (PartyAmounts beneficiary in input.FeeBeneficiaries)
        {
            Add(ThresholdCodes.FeeReturn, ThresholdFamilyDto.FeeReturn, ThresholdTierDto.Simple, fiscalEnd, beneficiary.Amounts, fiscalStart, fiscalEnd,
                _ => null, beneficiary, onlyNear: true);
        }

        return
        [
            .. rows
                .OrderByDescending(r => r.Status)
                .ThenBy(r => DueOn(r) ?? DateOnly.MaxValue)
                .ThenBy(r => r.Family)
                .ThenBy(r => r.Code, StringComparer.Ordinal)
                .ThenBy(r => r.PartyName, StringComparer.CurrentCulture),
        ];
    }

    /// <summary>La date qui compte pour un seuil : le franchissement constaté ou prévu.</summary>
    public static DateOnly? DueOn(ThresholdWatchResponse row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return row.CrossedOn ?? row.ExpectedOn;
    }

    /// <summary>
    /// L'indicateur de la vue cabinet : le nombre de seuils proches (franchis ou prévus dans les trois mois), sans montant
    /// ni nom. Critique quand un seuil de TVA à effet immédiat est déjà franchi (la TVA des factures change dès
    /// maintenant), à traiter sinon ; la date est le franchissement le plus proche.
    /// </summary>
    public static DossierIndicator? Indicator(ThresholdsResponse view)
    {
        ArgumentNullException.ThrowIfNull(view);
        ThresholdWatchResponse[] soon = [.. view.Rows.Where(r => r.Soon)];
        if (soon.Length == 0)
        {
            return null;
        }

        bool vatNow = soon.Any(r => r is { Status: ThresholdStatusDto.Crossed, Tier: ThresholdTierDto.Increased }
            && r.Family is ThresholdFamilyDto.VatFranchise or ThresholdFamilyDto.VatSimplifiedRegime or ThresholdFamilyDto.VatQuarterlyReturn);
        return new DossierIndicator(
            DossierIndicatorKeys.Thresholds,
            soon.Length,
            vatNow ? IndicatorLevel.Critical : IndicatorLevel.Warning,
            soon.Select(DueOn).Where(d => d is not null).Min());
    }

    private static ThresholdWatchResponse Row(
        ThresholdParameter parameter,
        ThresholdFamilyDto family,
        ThresholdTierDto tier,
        PartyAmounts? party,
        DateOnly start,
        DateOnly end,
        ThresholdProgress progress,
        DateOnly? effectiveOn,
        DateOnly today)
    {
        bool soon = progress.Status == ThresholdStatus.Crossed
            || (progress.Status == ThresholdStatus.Projected && progress.ExpectedOn <= today.AddMonths(SoonMonths));
        return new ThresholdWatchResponse(
            parameter.Code,
            family,
            tier,
            party?.PartyId.ToString() ?? parameter.Code,
            party?.PartyId,
            party?.Name,
            parameter.Amount,
            (ThresholdBasisDto)(int)parameter.Basis,
            start,
            end,
            progress.ToDate,
            progress.Projected,
            progress.Excess,
            (ThresholdStatusDto)(int)progress.Status,
            progress.CrossedOn,
            progress.ExpectedOn,
            progress.Status is ThresholdStatus.Crossed or ThresholdStatus.Projected ? effectiveOn : null,
            soon,
            parameter.Source);
    }

    /// <summary>Effet immédiat : les règles changent le jour du franchissement.</summary>
    private static DateOnly? Crossing(ThresholdProgress progress) => progress.CrossedOn ?? progress.ExpectedOn;

    /// <summary>Effet à une date fixe (le 1er janvier suivant, ou le début de l'année du dépassement).</summary>
    private static DateOnly? Next(ThresholdProgress progress, DateOnly on) => progress.CrossedOn is not null || progress.ExpectedOn is not null ? on : null;

    private static string? SizeCode(CompanySize size) => size switch
    {
        CompanySize.Micro => ThresholdCodes.SizeMicro,
        CompanySize.SmallOrMedium => ThresholdCodes.SizeSmallOrMedium,
        CompanySize.Intermediate => ThresholdCodes.SizeIntermediate,
        _ => null,
    };

    private static DateOnly Earliest(params ReadOnlySpan<DateOnly> dates)
    {
        DateOnly earliest = DateOnly.MaxValue;
        foreach (DateOnly date in dates)
        {
            earliest = date < earliest ? date : earliest;
        }

        return earliest;
    }
}

internal sealed class GetThresholdsHandler(ThresholdWatch watch, TimeProvider clock) : IQueryHandler<GetThresholdsQuery, Result<ThresholdsResponse>>
{
    public async Task<Result<ThresholdsResponse>> HandleAsync(GetThresholdsQuery query, CancellationToken cancellationToken) =>
        await watch.ReadAsync(LegalCalendar.Today(clock), cancellationToken).ConfigureAwait(false);
}

/// <summary>« Seuils proches » pour la vue cabinet ; voir <see cref="ThresholdWatch.Indicator"/>.</summary>
internal sealed class ThresholdIndicatorSource(ThresholdWatch watch) : IDossierIndicatorSource
{
    public async Task<IReadOnlyList<DossierIndicator>> ComputeAsync(DateOnly today, CancellationToken cancellationToken) =>
        ThresholdWatch.Indicator(await watch.ReadAsync(today, cancellationToken).ConfigureAwait(false)) is { } indicator ? [indicator] : [];
}
