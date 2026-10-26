using Lettra.Domain.Common;

namespace Lettra.Domain.Vigilance;

/// <summary>Niveau de risque de blanchiment attribué au client (art. L561-4-1 du Code monétaire et financier).</summary>
public enum RiskLevel
{
    Low = 1,
    Standard = 2,
    High = 3,
}

/// <summary>Ce qui fait monter le risque ; une personne politiquement exposée ou un pays à risque impose le niveau élevé (art. L561-10).</summary>
[Flags]
public enum RiskFactors
{
    None = 0,
    PoliticallyExposed = 1,
    HighRiskCountry = 2,
    CashIntensive = 4,
    ComplexStructure = 8,
    RemoteRelationship = 16,
    UnusualTransactions = 32,
    All = PoliticallyExposed | HighRiskCountry | CashIntensive | ComplexStructure | RemoteRelationship | UnusualTransactions,

    /// <summary>Les facteurs qui imposent des mesures de vigilance complémentaires : le niveau ne peut être qu'élevé.</summary>
    RequireHigh = PoliticallyExposed | HighRiskCountry,
}

/// <summary>Les pièces d'identification reçues (art. L561-5 et R561-5) ; les deux premières sont exigées.</summary>
[Flags]
public enum IdentityEvidence
{
    None = 0,

    /// <summary>Extrait K-bis (ou équivalent) de moins de trois mois.</summary>
    CompanyRegistration = 1,

    /// <summary>Pièce d'identité du dirigeant.</summary>
    ManagerIdentity = 2,

    /// <summary>Extrait du registre des bénéficiaires effectifs.</summary>
    BeneficialOwnersRegister = 4,

    /// <summary>Statuts à jour.</summary>
    Statutes = 8,

    Required = CompanyRegistration | ManagerIdentity,
    All = CompanyRegistration | ManagerIdentity | BeneficialOwnersRegister | Statutes,
}

/// <summary>Pourquoi une personne est bénéficiaire effectif (art. R561-1).</summary>
public enum OwnershipKind
{
    /// <summary>Plus de 25 % du capital.</summary>
    Capital = 1,

    /// <summary>Plus de 25 % des droits de vote.</summary>
    VotingRights = 2,

    /// <summary>Un contrôle par d'autres moyens (pacte, pouvoir de nomination…).</summary>
    Control = 3,

    /// <summary>À défaut de détenteur au-delà de 25 %, le représentant légal.</summary>
    LegalRepresentative = 4,
}

/// <summary>Où en est la fiche à une date donnée.</summary>
public enum DueDiligenceStatus
{
    /// <summary>Il manque l'extrait K-bis, la pièce d'identité du dirigeant, la date de vérification ou un bénéficiaire effectif.</summary>
    Incomplete = 1,

    /// <summary>La date de la prochaine revue est atteinte.</summary>
    ReviewDue = 2,

    UpToDate = 3,

    /// <summary>La relation a pris fin : la fiche est gardée cinq ans, puis peut être effacée.</summary>
    Ended = 4,
}

/// <summary>Un bénéficiaire effectif : une personne physique, sa part (capital ou droits de vote) et la nature de son contrôle.</summary>
public sealed class BeneficialOwner
{
    // Constructeur réservé à la persistance.
    private BeneficialOwner()
    {
        Name = string.Empty;
    }

    internal BeneficialOwner(int position, string name, decimal? sharePercent, OwnershipKind kind)
    {
        Position = position;
        Name = name;
        SharePercent = sharePercent;
        Kind = kind;
    }

    public int Position { get; private set; }

    public string Name { get; private set; }

    /// <summary>La part détenue, au-delà de 25 % ; vide pour un contrôle ou un représentant légal.</summary>
    public decimal? SharePercent { get; private set; }

    public OwnershipKind Kind { get; private set; }
}

/// <summary>
/// La fiche de connaissance du client que le cabinet tient au titre de la lutte contre le blanchiment (LCB-FT) :
/// pièces d'identification reçues, bénéficiaires effectifs, niveau de risque et sa justification, date de la dernière
/// revue. La prochaine revue suit le risque ; la fiche est gardée cinq ans après la fin de la relation (art. L561-12).
/// Unique dans la base du dossier, comme la mission.
/// </summary>
public sealed class ClientDueDiligence : AggregateRoot<Guid>, IVersioned
{
    /// <summary>Identifiant fixe : la fiche est unique dans la base du dossier.</summary>
    public static readonly Guid SingletonId = new("6c657474-7261-0000-0000-6c6362667466");

    public const int MaxOwners = 10;
    public const int MaxNameLength = 120;
    public const int MaxJustificationLength = 1000;

    /// <summary>Au-delà de cette part, une personne est bénéficiaire effectif (art. R561-1).</summary>
    public const decimal OwnershipThreshold = 25m;

    /// <summary>Durée de conservation après la fin de la relation (art. L561-12).</summary>
    public const int RetentionYears = 5;

    private readonly List<BeneficialOwner> _owners = [];

    // Constructeur réservé à la persistance.
    private ClientDueDiligence(Guid id)
        : base(id)
    {
    }

    public IdentityEvidence Evidence { get; private set; }

    /// <summary>Le jour où le cabinet a vérifié l'identité sur les pièces originales.</summary>
    public DateOnly? IdentityVerifiedOn { get; private set; }

    public IReadOnlyList<BeneficialOwner> Owners
    {
        get
        {
            _owners.Sort((a, b) => a.Position.CompareTo(b.Position));
            return _owners;
        }
    }

    public RiskLevel Level { get; private set; }

    public RiskFactors Factors { get; private set; }

    /// <summary>Pourquoi ce niveau ; exigée pour un risque faible ou élevé.</summary>
    public string? Justification { get; private set; }

    public DateOnly ReviewedOn { get; private set; }

    /// <summary>Le membre du cabinet qui a fait la dernière revue.</summary>
    public Guid ReviewedBy { get; private set; }

    /// <summary>La fin de la relation d'affaires ; vide tant qu'elle dure.</summary>
    public DateOnly? RelationshipEndedOn { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public long Version { get; private set; }

    /// <summary>La prochaine revue : un an après la dernière pour un risque élevé, deux ans pour un risque standard, trois pour un risque faible.</summary>
    public DateOnly NextReviewOn => ReviewedOn.AddYears(ReviewIntervalYears(Level));

    /// <summary>Jusqu'à quand garder la fiche ; vide tant que la relation dure.</summary>
    public DateOnly? KeepUntil => RelationshipEndedOn?.AddYears(RetentionYears);

    public static int ReviewIntervalYears(RiskLevel level) => level switch
    {
        RiskLevel.High => 1,
        RiskLevel.Low => 3,
        _ => 2,
    };

    public static Result<ClientDueDiligence> Create(Draft draft, Guid reviewer, DateOnly today, DateTimeOffset now)
    {
        ClientDueDiligence file = new(SingletonId);
        Result applied = file.Update(draft, reviewer, today, now);
        return applied.IsFailure ? Result<ClientDueDiligence>.Failure(applied.Errors) : file;
    }

    public Result Update(Draft draft, Guid reviewer, DateOnly today, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(draft);
        List<Error> errors = [.. Validate(draft, today)];
        if (errors.Count > 0)
        {
            return Result.Failure(errors);
        }

        Evidence = draft.Evidence;
        IdentityVerifiedOn = draft.IdentityVerifiedOn;
        _owners.Clear();
        _owners.AddRange(draft.Owners.Select((o, i) => new BeneficialOwner(i + 1, o.Name.Trim(), o.SharePercent, o.Kind)));
        Level = draft.Level;
        Factors = draft.Factors;
        Justification = string.IsNullOrWhiteSpace(draft.Justification) ? null : draft.Justification.Trim();
        ReviewedOn = draft.ReviewedOn;
        ReviewedBy = reviewer;
        RelationshipEndedOn = draft.RelationshipEndedOn;
        UpdatedAt = now;
        return Result.Success();
    }

    /// <summary>Où en est la fiche à <paramref name="today"/> (la date légale du jour).</summary>
    public DueDiligenceStatus StatusOn(DateOnly today)
    {
        if (RelationshipEndedOn is not null)
        {
            return DueDiligenceStatus.Ended;
        }

        if ((Evidence & IdentityEvidence.Required) != IdentityEvidence.Required || IdentityVerifiedOn is null || _owners.Count == 0)
        {
            return DueDiligenceStatus.Incomplete;
        }

        return NextReviewOn <= today ? DueDiligenceStatus.ReviewDue : DueDiligenceStatus.UpToDate;
    }

    private static IEnumerable<Error> Validate(Draft draft, DateOnly today)
    {
        if ((draft.Evidence & ~IdentityEvidence.All) != 0)
        {
            yield return VigilanceErrors.EvidenceInvalid;
        }

        if (draft.IdentityVerifiedOn > today || draft.ReviewedOn > today || draft.RelationshipEndedOn > today)
        {
            yield return VigilanceErrors.DateInFuture;
        }

        foreach (Error error in ValidateOwners(draft.Owners))
        {
            yield return error;
        }

        if (!Enum.IsDefined(draft.Level))
        {
            yield return VigilanceErrors.LevelInvalid;
        }
        else if ((draft.Factors & RiskFactors.RequireHigh) != 0 && draft.Level != RiskLevel.High)
        {
            yield return VigilanceErrors.LevelTooLow;
        }

        if ((draft.Factors & ~RiskFactors.All) != 0)
        {
            yield return VigilanceErrors.FactorsInvalid;
        }

        if (draft.Justification?.Trim().Length > MaxJustificationLength)
        {
            yield return VigilanceErrors.JustificationTooLong;
        }
        else if (draft.Level is RiskLevel.Low or RiskLevel.High && string.IsNullOrWhiteSpace(draft.Justification))
        {
            yield return VigilanceErrors.JustificationRequired;
        }
    }

    private static IEnumerable<Error> ValidateOwners(IReadOnlyList<OwnerDraft> owners)
    {
        if (owners.Count > MaxOwners)
        {
            yield return VigilanceErrors.TooManyOwners;
            yield break;
        }

        if (owners.Any(o => string.IsNullOrWhiteSpace(o.Name) || o.Name.Trim().Length > MaxNameLength))
        {
            yield return VigilanceErrors.OwnerNameInvalid;
        }

        if (owners.Any(o => !Enum.IsDefined(o.Kind)
            || (HoldsShare(o.Kind) ? o.SharePercent is not (> OwnershipThreshold and <= 100m) : o.SharePercent is not null)))
        {
            yield return VigilanceErrors.OwnerShareInvalid;
        }
        else if (owners.GroupBy(o => o.Kind).Any(g => HoldsShare(g.Key) && g.Sum(o => o.SharePercent ?? 0m) > 100m))
        {
            yield return VigilanceErrors.SharesOver100;
        }
    }

    private static bool HoldsShare(OwnershipKind kind) => kind is OwnershipKind.Capital or OwnershipKind.VotingRights;

    /// <summary>Un bénéficiaire effectif tel que l'écran l'envoie.</summary>
    public sealed record OwnerDraft(string Name, decimal? SharePercent, OwnershipKind Kind);

    /// <summary>Ce que l'écran envoie : toute la fiche, d'un coup (chaque enregistrement vaut revue).</summary>
    public sealed record Draft(
        IdentityEvidence Evidence,
        DateOnly? IdentityVerifiedOn,
        IReadOnlyList<OwnerDraft> Owners,
        RiskLevel Level,
        RiskFactors Factors,
        string? Justification,
        DateOnly ReviewedOn,
        DateOnly? RelationshipEndedOn);
}

public static class VigilanceErrors
{
    public const string EvidenceInvalidCode = "Vigilance.EvidenceInvalid";
    public const string DateInFutureCode = "Vigilance.DateInFuture";
    public const string TooManyOwnersCode = "Vigilance.TooManyOwners";
    public const string OwnerNameInvalidCode = "Vigilance.OwnerNameInvalid";
    public const string OwnerShareInvalidCode = "Vigilance.OwnerShareInvalid";
    public const string SharesOver100Code = "Vigilance.SharesOver100";
    public const string LevelInvalidCode = "Vigilance.LevelInvalid";
    public const string LevelTooLowCode = "Vigilance.LevelTooLow";
    public const string FactorsInvalidCode = "Vigilance.FactorsInvalid";
    public const string JustificationRequiredCode = "Vigilance.JustificationRequired";
    public const string JustificationTooLongCode = "Vigilance.JustificationTooLong";

    public static Error EvidenceInvalid => Error.Validation(EvidenceInvalidCode, "Evidence");

    public static Error DateInFuture => Error.Validation(DateInFutureCode, "ReviewedOn");

    public static Error TooManyOwners => Error.Validation(TooManyOwnersCode, "Owners");

    public static Error OwnerNameInvalid => Error.Validation(OwnerNameInvalidCode, "Owners");

    public static Error OwnerShareInvalid => Error.Validation(OwnerShareInvalidCode, "Owners");

    public static Error SharesOver100 => Error.Validation(SharesOver100Code, "Owners");

    public static Error LevelInvalid => Error.Validation(LevelInvalidCode, "Level");

    public static Error LevelTooLow => Error.Validation(LevelTooLowCode, "Level");

    public static Error FactorsInvalid => Error.Validation(FactorsInvalidCode, "Factors");

    public static Error JustificationRequired => Error.Validation(JustificationRequiredCode, "Justification");

    public static Error JustificationTooLong => Error.Validation(JustificationTooLongCode, "Justification");
}
