using Lettra.Domain.Common;

namespace Lettra.Domain.Vigilance;

/// <summary>Le tiers vérifié : un client ou un fournisseur du dossier.</summary>
public enum ThirdPartyKind
{
    Customer = 1,
    Supplier = 2,
}

/// <summary>Résultat du criblage d'un nom sur le registre national des gels des avoirs.</summary>
public enum FreezeScreening
{
    /// <summary>Aucun nom du tiers ni de ses bénéficiaires effectifs ne ressemble à une personne gelée.</summary>
    Clear = 1,

    /// <summary>Un nom ressemble à une personne gelée : à examiner avant toute opération (homonyme possible).</summary>
    PossibleMatch = 2,
}

/// <summary>Une annonce du BODACC qui change la relation avec le tiers.</summary>
public enum LegalNoticeKind
{
    /// <summary>Sauvegarde, redressement ou liquidation judiciaire : déclarer la créance, ne plus payer d'avance.</summary>
    CollectiveProceeding = 1,

    /// <summary>Radiation du registre : l'entreprise n'existe plus.</summary>
    StruckOff = 2,
}

/// <summary>Réponse de l'URSSAF sur le code de sécurité d'une attestation de vigilance.</summary>
public enum UrssafAnswer
{
    Recognized = 1,
    NotRecognized = 2,
}

/// <summary>Où en est la vérification d'un tiers.</summary>
public enum ThirdPartyCheckStatus
{
    NeverChecked = 0,

    /// <summary>La dernière vérification a trouvé quelque chose : gel possible, procédure collective, radiation, code URSSAF inconnu.</summary>
    ToReview = 1,

    /// <summary>Rien trouvé, mais il y a plus de six mois : à refaire (les registres changent).</summary>
    Outdated = 2,

    Clear = 3,
}

/// <summary>Ce que les registres ont répondu sur un tiers, à un instant donné.</summary>
/// <param name="OwnersFound">Bénéficiaires effectifs inscrits ; vide quand le tiers n'a pas de SIREN.</param>
/// <param name="Freeze">Le criblage du tiers, de ses bénéficiaires et du titulaire de son compte sur le registre des gels.</param>
/// <param name="FreezeMatch">Le nom inscrit au registre des gels qui ressemble au tiers ou à l'un de ses bénéficiaires.</param>
/// <param name="Notice">La plus récente annonce du BODACC qui change la relation, s'il y en a une.</param>
/// <param name="NoticeOn">Sa date de publication.</param>
/// <param name="NoticeLabel">Son intitulé, tel que publié.</param>
/// <param name="Urssaf">Vide pour un client, ou pour un fournisseur sans code d'attestation à vérifier.</param>
public sealed record ThirdPartyFindings(
    int? OwnersFound,
    FreezeScreening Freeze,
    string? FreezeMatch,
    LegalNoticeKind? Notice,
    DateOnly? NoticeOn,
    string? NoticeLabel,
    UrssafAnswer? Urssaf);

/// <summary>
/// La dernière vérification d'un tiers auprès des registres officiels : bénéficiaires effectifs (INPI), gel des avoirs
/// (DG Trésor), annonces légales (BODACC) et, pour un fournisseur, code de l'attestation de vigilance (URSSAF). Une ligne
/// par tiers vérifié, de même identifiant que le client ou le fournisseur.
/// </summary>
public sealed class ThirdPartyCheck : AggregateRoot<Guid>, IVersioned
{
    /// <summary>Une vérification sans alerte se refait au-delà de ce délai.</summary>
    public const int OutdatedAfterDays = 180;

    public const int MaxNameLength = 200;

    // Constructeur réservé à la persistance.
    private ThirdPartyCheck(Guid id)
        : base(id)
    {
    }

    public ThirdPartyKind Kind { get; private set; }

    public DateTimeOffset CheckedAt { get; private set; }

    public Guid CheckedBy { get; private set; }

    public int? OwnersFound { get; private set; }

    public FreezeScreening Freeze { get; private set; }

    public string? FreezeMatch { get; private set; }

    public LegalNoticeKind? Notice { get; private set; }

    public DateOnly? NoticeOn { get; private set; }

    public string? NoticeLabel { get; private set; }

    public UrssafAnswer? Urssaf { get; private set; }

    public long Version { get; private set; }

    /// <summary>Quelque chose demande un examen : gel possible, procédure collective, radiation, code URSSAF inconnu.</summary>
    public bool NeedsReview => Freeze == FreezeScreening.PossibleMatch || Notice is not null || Urssaf == UrssafAnswer.NotRecognized;

    public static ThirdPartyCheck Create(Guid partyId, ThirdPartyKind kind, ThirdPartyFindings findings, Guid checkedBy, DateTimeOffset now)
    {
        ThirdPartyCheck check = new(partyId) { Kind = kind };
        check.Record(findings, checkedBy, now);
        return check;
    }

    public void Record(ThirdPartyFindings findings, Guid checkedBy, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(findings);
        OwnersFound = findings.OwnersFound;
        Freeze = findings.Freeze;
        FreezeMatch = findings.Freeze == FreezeScreening.PossibleMatch ? Cut(findings.FreezeMatch) : null;
        Notice = findings.Notice;
        NoticeOn = findings.Notice is null ? null : findings.NoticeOn;
        NoticeLabel = findings.Notice is null ? null : Cut(findings.NoticeLabel);
        Urssaf = findings.Urssaf;
        CheckedBy = checkedBy;
        CheckedAt = now;
    }

    public ThirdPartyCheckStatus StatusOn(DateOnly today) =>
        NeedsReview ? ThirdPartyCheckStatus.ToReview
        : today.DayNumber - DateOnly.FromDateTime(CheckedAt.UtcDateTime).DayNumber > OutdatedAfterDays ? ThirdPartyCheckStatus.Outdated
        : ThirdPartyCheckStatus.Clear;

    private static string? Cut(string? text)
    {
        string? trimmed = text?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed.Length > MaxNameLength ? trimmed[..MaxNameLength] : trimmed;
    }
}

public static class ThirdPartyCheckErrors
{
    public const string PartyNotFoundCode = "ThirdPartyCheck.PartyNotFound";

    public static Error PartyNotFound => Error.NotFound(PartyNotFoundCode);
}
