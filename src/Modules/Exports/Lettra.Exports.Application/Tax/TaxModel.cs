using Lettra.Calculations;
using Lettra.Contracts.Tax;
using Lettra.Domain.Billing;

namespace Lettra.Exports.Application.Tax;

/// <summary>Une ligne d'une pièce validée vue par la TVA : sa nature, sa catégorie EN 16931, son taux et son montant HT.</summary>
public sealed record TaxLine(RevenueNature Nature, string Category, decimal RatePercent, string? ExemptionCode, decimal TotalExclTax);

/// <summary>
/// Une pièce validée (facture, acompte, avoir) telle que les déclarations la lisent : figée à la validation, rien n'est
/// recalculé. <paramref name="Summaries"/> est le récapitulatif de TVA imprimé sur la pièce ; <paramref name="Transmitted"/>
/// dit si elle est partie vers la plateforme agréée ; <paramref name="VatOnDebits"/> si elle porte l'option pour les débits.
/// </summary>
public sealed record TaxDocument(
    Guid InvoiceId,
    InvoiceKind Kind,
    string Number,
    DateOnly Date,
    Guid CustomerId,
    PartySnapshot Customer,
    VatRegime Regime,
    bool VatOnDebits,
    IReadOnlyList<TaxLine> Lines,
    IReadOnlyList<VatSummary> Summaries,
    bool Transmitted);

/// <summary>
/// Une opération soumise à déclaration : la part d'une pièce pour une catégorie, un taux et une nature, à sa date
/// d'exigibilité. Les montants d'un avoir sont négatifs. C'est l'unité que les cases du brouillon additionnent.
/// <paramref name="DueOnReceipt"/> : prestation de services taxée sans option pour les débits, dont la TVA est exigible
/// à l'encaissement (art. 269-2-c du CGI).
/// </summary>
public sealed record VatOperation(
    Guid InvoiceId,
    InvoiceKind Kind,
    string PieceNumber,
    DateOnly Date,
    Guid CustomerId,
    string CustomerName,
    string CustomerCountry,
    string? CustomerVatNumber,
    VatRegime Regime,
    string Category,
    decimal RatePercent,
    string? ExemptionCode,
    RevenueNature Nature,
    decimal BaseExclTax,
    decimal VatAmount,
    bool DueOnReceipt = false);

/// <summary>
/// TVA rendue exigible par un encaissement (prestations de services sans option pour les débits) : la part payée d'une
/// pièce, par taux. Fournie par le module des encaissements ; voir <see cref="IVatOnReceiptsSource"/>.
/// </summary>
public sealed record VatReceiptAllocation(
    Guid InvoiceId,
    InvoiceKind Kind,
    string PieceNumber,
    DateOnly ReceiptDate,
    Guid CustomerId,
    string CustomerName,
    decimal VatRatePercent,
    decimal BaseExclTax,
    decimal VatAmount);

/// <summary>Réglages fiscaux du dossier (valeurs par défaut tant qu'ils n'ont jamais été enregistrés).</summary>
public sealed record FiscalSettings(
    int FiscalYearEndMonth,
    int FiscalYearEndDay,
    VatPeriodicityDto VatPeriodicity,
    int VatDeadlineDay,
    bool CorporateIncomeTax,
    bool Cfe,
    DateOnly? TrackedFrom,
    long Version)
{
    public const int MinDeadlineDay = 15;
    public const int MaxDeadlineDay = 24;

    /// <summary>Clôture au 31 décembre, CA3 mensuelle, TVA au 15 (la date la plus tôt possible), IS et CFE.</summary>
    public static FiscalSettings Default { get; } = new(12, 31, VatPeriodicityDto.Monthly, MinDeadlineDay, true, true, null, 0);

    public bool IsCalendarYear => FiscalYearEndMonth == 12 && FiscalYearEndDay == 31;

    /// <summary>Date de clôture de l'exercice qui se termine dans l'année <paramref name="year"/> (28 février les années non bissextiles).</summary>
    public DateOnly YearEnd(int year) => new(year, FiscalYearEndMonth, Math.Min(FiscalYearEndDay, DateTime.DaysInMonth(year, FiscalYearEndMonth)));

    /// <summary>L'exercice qui contient <paramref name="date"/> : du lendemain de la clôture précédente à la clôture.</summary>
    public (DateOnly Start, DateOnly End) FiscalYearOf(DateOnly date)
    {
        DateOnly end = YearEnd(date.Year);
        if (date > end)
        {
            end = YearEnd(date.Year + 1);
        }

        return (YearEnd(end.Year - 1).AddDays(1), end);
    }

    public FiscalSettingsResponse ToResponse() =>
        new(FiscalYearEndMonth, FiscalYearEndDay, VatPeriodicity, VatDeadlineDay, CorporateIncomeTax, Cfe, TrackedFrom, Version);
}

/// <summary>Une échéance marquée comme déposée (ou payée) par un utilisateur.</summary>
public sealed record FiscalFiling(FiscalObligationDto Obligation, DateOnly PeriodStart, DateTimeOffset FiledAt, Guid FiledBy);

/// <summary>Lecture des pièces validées d'un dossier pour les déclarations.</summary>
public interface ITaxDocumentReader
{
    /// <summary>Pièces validées datées de <paramref name="from"/> à <paramref name="to"/>, bornes comprises.</summary>
    Task<IReadOnlyList<TaxDocument>> ReadAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken);

    /// <summary>Clients des brouillons de factures, d'acomptes ou d'avoirs créés avant <paramref name="before"/> et pas encore validés.</summary>
    Task<IReadOnlyList<string>> ListDraftsAsync(DateTimeOffset before, CancellationToken cancellationToken);
}

/// <summary>Réglages fiscaux et échéances déposées, dans la base du dossier.</summary>
public interface IFiscalStore
{
    Task<FiscalSettings?> FindSettingsAsync(CancellationToken cancellationToken);

    Task SaveSettingsAsync(FiscalSettings settings, DateTimeOffset at, CancellationToken cancellationToken);

    Task<IReadOnlyList<FiscalFiling>> ListFilingsAsync(DateOnly fromPeriodStart, DateOnly toPeriodStart, CancellationToken cancellationToken);

    /// <summary>Marque l'échéance déposée (<paramref name="filing"/>) ou retire la marque (<c>null</c>).</summary>
    Task SetFilingAsync(FiscalObligationDto obligation, DateOnly periodStart, FiscalFiling? filing, CancellationToken cancellationToken);
}

/// <summary>
/// Point de branchement de la TVA sur les encaissements. Tant que les encaissements ne sont pas suivis (implémentation
/// par défaut, inactive), toute la TVA est déclarée à la date de la pièce, comme le journal des ventes la passe en 44571.
/// Une fois actif, la TVA des prestations de services sans option pour les débits quitte la période de la facture et
/// revient à la date de chaque encaissement, au prorata de ce qui est payé.
/// </summary>
public interface IVatOnReceiptsSource
{
    bool IsActive { get; }

    Task<IReadOnlyList<VatReceiptAllocation>> ReadAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken);
}

/// <summary>Pas de suivi des encaissements : la TVA est exigible à la date de la pièce.</summary>
public sealed class NoVatOnReceipts : IVatOnReceiptsSource
{
    public bool IsActive => false;

    public Task<IReadOnlyList<VatReceiptAllocation>> ReadAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<VatReceiptAllocation>>([]);
}
