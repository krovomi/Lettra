namespace Lettra.Infrastructure.Identity.Oidc.Offline;

/// <summary>
/// Signature des instantanés de droits (section <c>Lettra:Identity:RightsSnapshot</c>). La clé privée vient du coffre
/// de la cellule ou du serveur du cabinet, jamais du dépôt ; sans clé, le hors ligne est indisponible.
/// </summary>
public sealed class RightsSnapshotOptions
{
    public const string SectionName = "Lettra:Identity:RightsSnapshot";

    /// <summary>Clé privée EC P-256 au format PEM (PKCS#8 ou SEC1).</summary>
    public string? SigningKeyPem { get; set; }

    /// <summary>Identifiant de la clé (<c>kid</c>), à changer à chaque rotation.</summary>
    public string KeyId { get; set; } = "lettra-rights-1";

    /// <summary>Clés publiques encore acceptées après une rotation, le temps que les instantanés en circulation expirent.</summary>
    public IList<RightsSnapshotPublicKeyOptions> PreviousKeys { get; init; } = [];

    /// <summary>Émetteur inscrit dans l'instantané (<c>iss</c>).</summary>
    public string Issuer { get; set; } = "lettra";

    /// <summary>Validité d'un instantané : 7 jours par défaut (annexe F §4), entre 1 heure et 30 jours.</summary>
    public TimeSpan Validity { get; set; } = TimeSpan.FromDays(7);

    /// <summary>Développement et tests : une clé éphémère est tirée au démarrage si aucune n'est configurée.</summary>
    public bool AllowEphemeralKey { get; set; }

    internal static readonly TimeSpan MinValidity = TimeSpan.FromHours(1);

    internal static readonly TimeSpan MaxValidity = TimeSpan.FromDays(30);
}

public sealed class RightsSnapshotPublicKeyOptions
{
    public string KeyId { get; set; } = string.Empty;

    /// <summary>Clé publique EC P-256 au format PEM (SubjectPublicKeyInfo).</summary>
    public string PublicKeyPem { get; set; } = string.Empty;
}
