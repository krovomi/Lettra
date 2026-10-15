namespace Lettra.Contracts.Identity;

/// <summary>
/// Demande d'un instantané de droits signé pour travailler hors ligne (annexe F §4) : le poste donne son identifiant
/// et les dossiers dont il garde une réplique. Le serveur ne signe que les dossiers où l'utilisateur a des droits.
/// </summary>
/// <param name="DeviceId">Identifiant du poste, tiré au premier lancement et gardé dans son coffre.</param>
/// <param name="DossierIds">Dossiers répliqués sur le poste.</param>
public sealed record IssueRightsSnapshotRequest(Guid DeviceId, IReadOnlyList<Guid> DossierIds)
{
    public const int MaxDossiers = 500;
}

/// <summary>Instantané signé (JWS compact, ES256) et sa période de validité.</summary>
/// <param name="Token">JWS à garder tel quel ; le poste le vérifie avec les clés publiées.</param>
/// <param name="IssuedAt">Émission (heure du serveur).</param>
/// <param name="ExpiresAt">Échéance : au-delà, le poste se verrouille jusqu'à la prochaine connexion.</param>
/// <param name="DossierCount">Nombre de dossiers où l'utilisateur garde des droits hors ligne.</param>
public sealed record RightsSnapshotResponse(string Token, DateTimeOffset IssuedAt, DateTimeOffset ExpiresAt, int DossierCount);

/// <summary>Clé publique de vérification des instantanés, au format JWK (EC P-256).</summary>
public sealed record RightsSnapshotKey(string Kid, string Kty, string Crv, string X, string Y, string Alg, string Use);

/// <summary>Jeu de clés publiques (JWKS) : la clé courante et celles encore acceptées après une rotation.</summary>
public sealed record RightsSnapshotKeysResponse(IReadOnlyList<RightsSnapshotKey> Keys);
