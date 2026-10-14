namespace Lettra.Infrastructure.Identity.Oidc;

/// <summary>Réglages Keycloak (section <c>Lettra:Identity:Oidc</c>). Le secret du client BFF vient du coffre, jamais du dépôt.</summary>
public sealed class OidcOptions
{
    public const string SectionName = "Lettra:Identity:Oidc";

    /// <summary>Realm Keycloak, par exemple <c>https://auth.lettra.fr/realms/lettra</c>.</summary>
    public string Authority { get; set; } = string.Empty;

    /// <summary>Audience attendue dans les jetons d'accès de l'API.</summary>
    public string Audience { get; set; } = "lettra-api";

    /// <summary>Client confidentiel du BFF (navigateur).</summary>
    public string BffClientId { get; set; } = "lettra-bff";

    public string? BffClientSecret { get; set; }

    public bool RequireHttpsMetadata { get; set; } = true;

    /// <summary>MFA obligatoire (annexe F §2). Désactivable seulement pour des tests.</summary>
    public bool RequireMfa { get; set; } = true;

    /// <summary>Valeurs de <c>amr</c> qui prouvent un second facteur (Keycloak : otp, hwk pour WebAuthn).</summary>
    public IList<string> StrongAmrValues { get; init; } = ["otp", "hwk", "swk", "mfa"];

    /// <summary>Valeurs de <c>acr</c> qui prouvent un second facteur (niveau 2 du « ACR to LoA mapping »).</summary>
    public IList<string> StrongAcrValues { get; init; } = ["2", "mfa"];

    /// <summary>Durée de vie des permissions en cache ; plafonnée à 5 minutes (annexe F §4).</summary>
    public TimeSpan PermissionCacheDuration { get; set; } = TimeSpan.FromMinutes(1);

    internal static readonly TimeSpan MaxPermissionCacheDuration = TimeSpan.FromMinutes(5);
}
