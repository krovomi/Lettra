using System.Globalization;
using Lettra.Calculations;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Domain.Billing;

/// <summary>Identifiant d'un modèle de facture récurrente.</summary>
public readonly record struct RecurringInvoiceId(Guid Value)
{
    public static RecurringInvoiceId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

/// <summary>Périodicité d'un modèle (valeurs identiques à <c>RecurrenceFrequencyDto</c>).</summary>
public enum RecurrenceFrequency
{
    Monthly = 1,
    Quarterly = 2,
    Yearly = 3,
}

/// <summary>
/// Moment de la facture dans sa période (valeurs identiques à <c>BillingTimingDto</c>) : à échoir, le premier jour de
/// la période (abonnements, honoraires au forfait) ; à terme échu, le dernier jour.
/// </summary>
public enum BillingTiming
{
    InAdvance = 1,
    InArrears = 2,
}

/// <summary>Révision annuelle des prix (valeurs identiques à <c>PriceRevisionKindDto</c>).</summary>
public enum PriceRevisionKind
{
    None = 1,

    /// <summary>Pourcentage fixe à chaque anniversaire du contrat.</summary>
    FixedPercent = 2,

    /// <summary>Indice Syntec : prix × nouvel indice / indice de référence, l'indice étant saisi par l'utilisateur.</summary>
    SyntecIndex = 3,
}

/// <summary>Contenu saisi d'un modèle, contrôlé par <see cref="RecurringInvoice.Create"/> et <see cref="RecurringInvoice.Update"/>.</summary>
public sealed record RecurringInvoiceDraft(
    string? Name,
    CustomerId CustomerId,
    VatRegime VatRegime,
    string? Subject,
    string? Notes,
    string? PaymentTerms,
    IReadOnlyList<LineDraft> Lines,
    RecurrenceFrequency Frequency,
    BillingTiming Timing,
    DateOnly StartDate,
    DateOnly? EndDate,
    bool ValidateAutomatically,
    PriceRevisionKind Revision,
    decimal? RevisionPercent,
    decimal? SyntecReferenceIndex,
    decimal? SyntecLatestIndex);

/// <summary>Une période facturée : bornes incluses et date de la facture (début ou fin selon <see cref="BillingTiming"/>).</summary>
public sealed record BillingPeriod(DateOnly Start, DateOnly End, DateOnly IssueOn);

/// <summary>Une facture créée par le modèle, pour une période donnée (une seule par période).</summary>
public sealed class RecurringOccurrence
{
    // Constructeur réservé à la persistance.
    private RecurringOccurrence()
    {
    }

    internal RecurringOccurrence(BillingPeriod period, InvoiceId invoiceId, decimal? revisionCoefficient, DateTimeOffset createdAt)
    {
        PeriodStart = period.Start;
        PeriodEnd = period.End;
        InvoiceId = invoiceId;
        RevisionCoefficient = revisionCoefficient;
        CreatedAt = createdAt;
    }

    public DateOnly PeriodStart { get; private set; }

    public DateOnly PeriodEnd { get; private set; }

    public InvoiceId InvoiceId { get; private set; }

    /// <summary>Coefficient de révision appliqué aux prix juste avant cette facture ; <c>null</c> sans révision.</summary>
    public decimal? RevisionCoefficient { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}

/// <summary>
/// Modèle de facture récurrente (vague 7) : un client, des lignes et une périodicité. À chaque échéance, il produit une
/// facture brouillon de la série FAC qui porte sa période (BT-73/74) ; la numérotation reste celle de la validation.
/// Invariants :
/// <list type="bullet">
/// <item>la période <c>k</c> commence à <c>début + k × périodicité</c> : pas de dérive en fin de mois (31 janvier, 28 février, 31 mars…) ;</item>
/// <item>une seule facture par période, même si la génération est relancée ;</item>
/// <item>aucune période ne commence après la date de fin ;</item>
/// <item>la révision s'applique une fois par anniversaire, avant la première période qui commence à cette date ou après.</item>
/// </list>
/// </summary>
public sealed class RecurringInvoice : AggregateRoot<RecurringInvoiceId>, IVersioned
{
    public const int MaxNameLength = 120;
    public const int MaxPaymentTermsLength = 500;

    /// <summary>Périodes rattrapées au plus par génération (un modèle commencé il y a longtemps ne crée pas des dizaines de factures d'un coup).</summary>
    public const int MaxCatchUp = 12;

    public const decimal MaxRevisionPercent = 100m;
    public const decimal MaxIndex = 100_000m;

    private readonly List<DocumentLine> _lines = [];
    private readonly List<RecurringOccurrence> _occurrences = [];

    // Constructeur réservé à la persistance.
    private RecurringInvoice(RecurringInvoiceId id)
        : base(id)
    {
        Name = string.Empty;
    }

    public string Name { get; private set; }

    public CustomerId CustomerId { get; private set; }

    public VatRegime VatRegime { get; private set; }

    public string? Subject { get; private set; }

    public string? Notes { get; private set; }

    public string? PaymentTerms { get; private set; }

    public RecurrenceFrequency Frequency { get; private set; }

    public BillingTiming Timing { get; private set; }

    public DateOnly StartDate { get; private set; }

    public DateOnly? EndDate { get; private set; }

    /// <summary>Les factures créées sont validées dans la foulée (numéro, journal) ; sinon elles restent en brouillon.</summary>
    public bool ValidateAutomatically { get; private set; }

    /// <summary>Un modèle suspendu ne produit plus rien ; il reprend là où il s'était arrêté.</summary>
    public bool IsActive { get; private set; }

    public PriceRevisionKind Revision { get; private set; }

    public decimal? RevisionPercent { get; private set; }

    /// <summary>Indice Syntec des prix actuels (S0).</summary>
    public decimal? SyntecReferenceIndex { get; private set; }

    /// <summary>Dernier indice Syntec publié, saisi par l'utilisateur (S1) ; la révision applique S1 / S0.</summary>
    public decimal? SyntecLatestIndex { get; private set; }

    /// <summary>Prochain anniversaire de révision ; <c>null</c> sans révision.</summary>
    public DateOnly? NextRevisionDate { get; private set; }

    /// <summary>Nombre de périodes déjà facturées : la prochaine est la période de rang <see cref="GeneratedCount"/>.</summary>
    public int GeneratedCount { get; private set; }

    /// <summary>Date de la prochaine facture, tenue à jour pour la recherche des modèles dus ; <c>null</c> une fois terminé.</summary>
    public DateOnly? NextIssueDate { get; private set; }

    public decimal TotalExclTax { get; private set; }

    public long Version { get; private set; }

    public Guid CreatedBy { get; private set; }

    public Guid LastModifiedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyList<DocumentLine> Lines
    {
        get
        {
            _lines.Sort((a, b) => a.Position.CompareTo(b.Position));
            return _lines;
        }
    }

    public IReadOnlyList<RecurringOccurrence> Occurrences
    {
        get
        {
            _occurrences.Sort((a, b) => a.PeriodStart.CompareTo(b.PeriodStart));
            return _occurrences;
        }
    }

    /// <summary>La prochaine période à facturer, échue ou non ; <c>null</c> une fois la date de fin passée.</summary>
    public BillingPeriod? NextPeriod => PeriodAt(GeneratedCount);

    /// <summary>
    /// Vrai quand la révision Syntec est due avant la prochaine période mais que le nouvel indice n'a pas été saisi :
    /// les factures continuent aux prix actuels, la révision attend l'indice.
    /// </summary>
    public bool RevisionAwaitsIndex =>
        Revision == PriceRevisionKind.SyntecIndex && NextPeriod is { } next && NextRevisionDate <= next.Start && !HasNewIndex;

    private bool HasNewIndex => SyntecLatestIndex is { } latest && SyntecReferenceIndex is { } reference && latest != reference;

    private int MonthsPerPeriod => Frequency switch
    {
        RecurrenceFrequency.Quarterly => 3,
        RecurrenceFrequency.Yearly => 12,
        _ => 1,
    };

    public static Result<RecurringInvoice> Create(RecurringInvoiceId id, RecurringInvoiceDraft draft, Guid userId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(draft);
        RecurringInvoice template = new(id) { IsActive = true };
        Result applied = template.Apply(draft);
        if (applied.IsFailure)
        {
            return Result<RecurringInvoice>.Failure(applied.Errors);
        }

        template.CreatedBy = userId;
        template.CreatedAt = now;
        template.Touch(userId, now);
        template.Version = 1;
        return template;
    }

    /// <summary>
    /// Modifie le modèle. Une fois une période facturée, le début et la périodicité sont figés (les périodes déjà
    /// facturées ne bougent pas) ; les lignes, le client et la révision changent pour les périodes suivantes.
    /// </summary>
    public Result Update(RecurringInvoiceDraft draft, Guid userId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (GeneratedCount > 0 && (draft.StartDate != StartDate || draft.Frequency != Frequency))
        {
            return RecurringInvoiceErrors.ScheduleLocked;
        }

        Result applied = Apply(draft);
        if (applied.IsFailure)
        {
            return applied;
        }

        Touch(userId, now);
        return Result.Success();
    }

    public void SetActive(bool active, Guid userId, DateTimeOffset now)
    {
        if (IsActive == active)
        {
            return;
        }

        IsActive = active;
        RefreshNextIssueDate();
        Touch(userId, now);
    }

    /// <summary>La prochaine période si sa facture est due au plus tard <paramref name="today"/> et le modèle actif.</summary>
    public BillingPeriod? DuePeriod(DateOnly today) =>
        IsActive && NextPeriod is { } next && next.IssueOn <= today ? next : null;

    /// <summary>
    /// Crée la facture brouillon de la prochaine période due : révision des prix si l'anniversaire est atteint, lignes,
    /// conditions, objet complété de la période. Le modèle passe à la période suivante.
    /// </summary>
    public Result<Invoice> GenerateNext(InvoiceId invoiceId, PartySnapshot customer, DateOnly today, Guid userId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(customer);
        if (DuePeriod(today) is not { } period)
        {
            return RecurringInvoiceErrors.NothingDue;
        }

        decimal? coefficient = ReviseIfDue(period.Start);
        InvoiceHeader header = new(null, null, PaymentTerms, null, null, VatRegime, SubjectFor(period), Notes);
        Result<Invoice> invoice = Invoice.CreateForPeriod(
            invoiceId, CustomerId, customer, header, [.. Lines.Select(l => l.ToDraft())], period.Start, period.End, userId, now);
        if (invoice.IsFailure)
        {
            return invoice;
        }

        _occurrences.Add(new RecurringOccurrence(period, invoiceId, coefficient, now));
        GeneratedCount++;
        RefreshNextIssueDate();
        Touch(userId, now);
        return invoice;
    }

    /// <summary>La période de rang <paramref name="index"/> (0 = la première) ; <c>null</c> si elle commence après la date de fin.</summary>
    public BillingPeriod? PeriodAt(int index)
    {
        DateOnly start = StartDate.AddMonths(index * MonthsPerPeriod);
        if (EndDate is { } end && start > end)
        {
            return null;
        }

        DateOnly last = StartDate.AddMonths((index + 1) * MonthsPerPeriod).AddDays(-1);
        return new BillingPeriod(start, last, Timing == BillingTiming.InArrears ? last : start);
    }

    private string SubjectFor(BillingPeriod period)
    {
        string range = string.Create(
            CultureInfo.InvariantCulture,
            $"du {period.Start.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)} au {period.End.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}");
        if (Subject is null)
        {
            return "Période " + range;
        }

        // La période reste entière : c'est l'objet saisi qui est raccourci s'il ne laisse pas la place.
        int room = BillingDocument<InvoiceId>.MaxSubjectLength - range.Length - 3;
        string subject = Subject.Length <= room ? Subject : Subject[..room].TrimEnd();
        return $"{subject} ({range})";
    }

    /// <summary>
    /// Applique la révision de chaque anniversaire atteint au début de la période ; renvoie le coefficient total appliqué.
    /// Un indice Syntec non saisi laisse la révision en attente (même anniversaire, prix inchangés).
    /// </summary>
    private decimal? ReviseIfDue(DateOnly periodStart)
    {
        decimal total = 1m;
        while (NextRevisionDate is { } anniversary && anniversary <= periodStart)
        {
            decimal coefficient;
            if (Revision == PriceRevisionKind.FixedPercent)
            {
                coefficient = 1m + (RevisionPercent!.Value / 100m);
            }
            else if (HasNewIndex)
            {
                coefficient = SyntecLatestIndex!.Value / SyntecReferenceIndex!.Value;
                SyntecReferenceIndex = SyntecLatestIndex;
            }
            else
            {
                break;
            }

            List<LineDraft> revised = [.. Lines.Select(l => l.ToDraft() with { UnitPriceExclTax = PriceRevision.Apply(l.UnitPriceExclTax, coefficient) })];
            ReplaceLines(revised);
            total *= coefficient;
            NextRevisionDate = anniversary.AddYears(1);
            if (Revision == PriceRevisionKind.SyntecIndex)
            {
                // L'écart entre deux indices couvre déjà tous les anniversaires passés : une révision tardive n'en rattrape pas d'autres.
                while (NextRevisionDate <= periodStart)
                {
                    NextRevisionDate = NextRevisionDate.Value.AddYears(1);
                }
            }
        }

        return total == 1m ? null : decimal.Round(total, 4, MidpointRounding.AwayFromZero);
    }

    private Result Apply(RecurringInvoiceDraft draft)
    {
        List<Error> errors = [];
        string name = (draft.Name ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            errors.Add(RecurringInvoiceErrors.NameRequired);
        }
        else if (name.Length > MaxNameLength)
        {
            errors.Add(RecurringInvoiceErrors.NameTooLong);
        }

        if (draft.CustomerId.Value == Guid.Empty)
        {
            errors.Add(BillingErrors.CustomerRequired);
        }

        if (!Enum.IsDefined(draft.VatRegime))
        {
            errors.Add(BillingErrors.VatRegimeInvalid);
        }

        string? subject = Clean(draft.Subject);
        if (subject is { Length: > BillingDocument<InvoiceId>.MaxSubjectLength })
        {
            errors.Add(BillingErrors.SubjectTooLong);
        }

        string? notes = Clean(draft.Notes);
        if (notes is { Length: > BillingDocument<InvoiceId>.MaxNotesLength })
        {
            errors.Add(BillingErrors.NotesTooLong);
        }

        string? terms = Clean(draft.PaymentTerms);
        if (terms is { Length: > MaxPaymentTermsLength })
        {
            errors.Add(BillingErrors.PaymentTermsTooLong);
        }

        if (!Enum.IsDefined(draft.Frequency))
        {
            errors.Add(RecurringInvoiceErrors.FrequencyInvalid);
        }

        if (!Enum.IsDefined(draft.Timing))
        {
            errors.Add(RecurringInvoiceErrors.TimingInvalid);
        }

        if (draft.EndDate is { } end && end < draft.StartDate)
        {
            errors.Add(RecurringInvoiceErrors.EndBeforeStart);
        }

        errors.AddRange(RevisionErrors(draft));
        List<DocumentLine> lines = ParseLines(draft.Lines, Enum.IsDefined(draft.VatRegime) ? draft.VatRegime : VatRegime.Standard, errors);
        if (errors.Count > 0)
        {
            return Result.Failure(errors);
        }

        bool revisionChanged = draft.Revision != Revision || Revision == PriceRevisionKind.None;
        Name = name;
        CustomerId = draft.CustomerId;
        VatRegime = draft.VatRegime;
        Subject = subject;
        Notes = notes;
        PaymentTerms = terms;
        Frequency = draft.Frequency;
        Timing = draft.Timing;
        StartDate = draft.StartDate;
        EndDate = draft.EndDate;
        ValidateAutomatically = draft.ValidateAutomatically;
        Revision = draft.Revision;
        RevisionPercent = draft.Revision == PriceRevisionKind.FixedPercent ? draft.RevisionPercent : null;
        SyntecReferenceIndex = draft.Revision == PriceRevisionKind.SyntecIndex ? draft.SyntecReferenceIndex : null;
        SyntecLatestIndex = draft.Revision == PriceRevisionKind.SyntecIndex ? draft.SyntecLatestIndex ?? draft.SyntecReferenceIndex : null;
        if (Revision == PriceRevisionKind.None)
        {
            NextRevisionDate = null;
        }
        else if (revisionChanged || NextRevisionDate is null)
        {
            // Premier anniversaire après la dernière période facturée : une révision choisie en cours de contrat ne rattrape pas le passé.
            DateOnly anniversary = StartDate.AddYears(1);
            DateOnly from = PeriodAt(GeneratedCount)?.Start ?? StartDate;
            while (anniversary < from)
            {
                anniversary = anniversary.AddYears(1);
            }

            NextRevisionDate = anniversary;
        }

        _lines.Clear();
        _lines.AddRange(lines);
        TotalExclTax = DocumentCalculator.Compute(_lines.Select(l => l.ToInput())).TotalExclTax;
        RefreshNextIssueDate();
        return Result.Success();
    }

    private static IEnumerable<Error> RevisionErrors(RecurringInvoiceDraft draft)
    {
        if (!Enum.IsDefined(draft.Revision))
        {
            yield return RecurringInvoiceErrors.RevisionInvalid;
        }
        else if (draft.Revision == PriceRevisionKind.FixedPercent && draft.RevisionPercent is not (> 0m and <= MaxRevisionPercent))
        {
            yield return RecurringInvoiceErrors.RevisionPercentInvalid;
        }
        else if (draft.Revision == PriceRevisionKind.SyntecIndex)
        {
            if (draft.SyntecReferenceIndex is not (> 0m and <= MaxIndex))
            {
                yield return RecurringInvoiceErrors.SyntecIndexInvalid("SyntecReferenceIndex");
            }

            if (draft.SyntecLatestIndex is { } latest && latest is not (> 0m and <= MaxIndex))
            {
                yield return RecurringInvoiceErrors.SyntecIndexInvalid("SyntecLatestIndex");
            }
        }
    }

    private static List<DocumentLine> ParseLines(IReadOnlyList<LineDraft> drafts, VatRegime regime, List<Error> errors)
    {
        ArgumentNullException.ThrowIfNull(drafts);
        if (drafts.Count == 0)
        {
            errors.Add(BillingErrors.LinesRequired);
        }
        else if (drafts.Count > BillingDocument<InvoiceId>.MaxLines)
        {
            errors.Add(BillingErrors.TooManyLines);
        }

        List<DocumentLine> lines = [];
        for (int i = 0; i < Math.Min(drafts.Count, BillingDocument<InvoiceId>.MaxLines); i++)
        {
            Result<DocumentLine> line = DocumentLine.Create(i, drafts[i], regime);
            if (line.IsSuccess)
            {
                lines.Add(line.Value);
            }
            else
            {
                errors.AddRange(line.Errors);
            }
        }

        return lines;
    }

    private void ReplaceLines(IReadOnlyList<LineDraft> drafts)
    {
        List<Error> errors = [];
        List<DocumentLine> lines = ParseLines(drafts, VatRegime, errors);
        if (errors.Count > 0)
        {
            // Un prix révisé au-delà du plafond du catalogue : la révision ne s'applique pas plutôt que de casser le modèle.
            return;
        }

        _lines.Clear();
        _lines.AddRange(lines);
        TotalExclTax = DocumentCalculator.Compute(_lines.Select(l => l.ToInput())).TotalExclTax;
    }

    private void RefreshNextIssueDate() => NextIssueDate = IsActive ? NextPeriod?.IssueOn : null;

    private void Touch(Guid userId, DateTimeOffset now)
    {
        LastModifiedBy = userId;
        UpdatedAt = now;
        Version++;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>Arrondi d'un prix révisé : au centime, demi au-dessus (règle usuelle des clauses de révision).</summary>
public static class PriceRevision
{
    public static decimal Apply(decimal unitPrice, decimal coefficient) =>
        decimal.Round(unitPrice * coefficient, 2, MidpointRounding.AwayFromZero);
}

/// <summary>Codes d'erreur des factures récurrentes.</summary>
public static class RecurringInvoiceErrors
{
    public const string NotFoundCode = "RecurringInvoice.NotFound";
    public const string NameRequiredCode = "RecurringInvoice.NameRequired";
    public const string NameTooLongCode = "RecurringInvoice.NameTooLong";
    public const string FrequencyInvalidCode = "RecurringInvoice.FrequencyInvalid";
    public const string TimingInvalidCode = "RecurringInvoice.TimingInvalid";
    public const string EndBeforeStartCode = "RecurringInvoice.EndBeforeStart";
    public const string RevisionInvalidCode = "RecurringInvoice.RevisionInvalid";
    public const string RevisionPercentInvalidCode = "RecurringInvoice.RevisionPercentInvalid";
    public const string SyntecIndexInvalidCode = "RecurringInvoice.SyntecIndexInvalid";
    public const string ScheduleLockedCode = "RecurringInvoice.ScheduleLocked";
    public const string NothingDueCode = "RecurringInvoice.NothingDue";

    public static Error NotFound => Error.NotFound(NotFoundCode);

    public static Error NameRequired => Error.Validation(NameRequiredCode, "Name");

    public static Error NameTooLong => Error.Validation(NameTooLongCode, "Name");

    public static Error FrequencyInvalid => Error.Validation(FrequencyInvalidCode, "Frequency");

    public static Error TimingInvalid => Error.Validation(TimingInvalidCode, "Timing");

    public static Error EndBeforeStart => Error.Validation(EndBeforeStartCode, "EndDate");

    public static Error RevisionInvalid => Error.Validation(RevisionInvalidCode, "Revision");

    public static Error RevisionPercentInvalid => Error.Validation(RevisionPercentInvalidCode, "RevisionPercent");

    /// <summary>Le début et la périodicité ne changent plus une fois une période facturée.</summary>
    public static Error ScheduleLocked => Error.Conflict(ScheduleLockedCode);

    public static Error NothingDue => Error.Conflict(NothingDueCode);

    public static Error SyntecIndexInvalid(string field) => Error.Validation(SyntecIndexInvalidCode, field);
}
