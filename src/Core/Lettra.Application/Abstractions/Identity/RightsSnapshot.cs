using Lettra.Contracts.Identity;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Abstractions.Identity;

/// <summary>
/// Instantané des droits d'un utilisateur sur un poste, pour travailler hors ligne (annexe F §4, ADR-0010) : les
/// permissions de données de chaque dossier répliqué, figées à l'émission et limitées dans le temps. Hors ligne, il
/// remplace le registre ; à la synchronisation, le serveur revérifie chaque intention avec les droits du moment.
/// </summary>
public sealed record RightsSnapshot(
    Guid UserId,
    Guid DeviceId,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt,
    IReadOnlyDictionary<DossierId, IReadOnlySet<Permission>> Dossiers)
{
    /// <summary>Format du contenu signé ; un poste refuse un instantané d'une version qu'il ne connaît pas.</summary>
    public const int FormatVersion = 1;

    public bool IsValidAt(DateTimeOffset now) => now >= IssuedAt.AddMinutes(-5) && now < ExpiresAt;
}

/// <summary>Signe les instantanés de droits (clé du serveur, jamais sur un poste).</summary>
public interface IRightsSnapshotIssuer
{
    /// <summary>Une clé de signature est configurée sur ce déploiement.</summary>
    bool IsAvailable { get; }

    /// <summary>Durée de validité d'un instantané (7 jours par défaut).</summary>
    TimeSpan Validity { get; }

    /// <summary>JWS compact du contenu de <paramref name="snapshot"/>.</summary>
    string Sign(RightsSnapshot snapshot);

    /// <summary>Clés publiques de vérification (la clé courante et les précédentes encore valables). Rien de secret.</summary>
    RightsSnapshotKeysResponse PublicKeys();
}

public static class RightsSnapshotErrors
{
    public const string DeviceRequiredCode = "RightsSnapshot.DeviceRequired";
    public const string TooManyDossiersCode = "RightsSnapshot.TooManyDossiers";
    public const string UnavailableCode = "RightsSnapshot.Unavailable";
    public const string InvalidCode = "RightsSnapshot.Invalid";
    public const string ExpiredCode = "RightsSnapshot.Expired";
    public const string OtherDeviceCode = "RightsSnapshot.OtherDevice";

    public static Error DeviceRequired => Error.Validation(DeviceRequiredCode, "DeviceId");

    public static Error TooManyDossiers => Error.Validation(TooManyDossiersCode, "DossierIds");

    public static Error Unavailable => new(UnavailableCode, ErrorKind.Unexpected);

    /// <summary>Signature, format ou clé inconnus : l'instantané ne donne aucun droit.</summary>
    public static Error Invalid => new(InvalidCode, ErrorKind.Unauthorized);

    /// <summary>Instantané échu : le poste se verrouille jusqu'à la prochaine connexion.</summary>
    public static Error Expired => new(ExpiredCode, ErrorKind.Unauthorized);

    /// <summary>Instantané émis pour un autre poste (copié d'une machine à l'autre).</summary>
    public static Error OtherDevice => new(OtherDeviceCode, ErrorKind.Forbidden);
}
