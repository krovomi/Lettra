using System.Security.Claims;
using System.Text.Json;
using Lettra.Domain.SharedKernel;

namespace Lettra.Infrastructure.Identity.Oidc;

/// <summary>
/// Lecture des claims Keycloak (jetons lus sans renommage des claims). Le jeton porte l'utilisateur, le cabinet
/// et le niveau d'authentification ; jamais les droits.
/// </summary>
public static class OidcClaims
{
    public const string Subject = "sub";
    public const string Organization = "organization";
    public const string AuthenticationMethods = "amr";
    public const string AuthenticationContext = "acr";
    public const string PreferredUserName = "preferred_username";
    public const string Name = "name";

    /// <summary>Identifiant Lettra de l'utilisateur : le <c>sub</c> Keycloak, un UUID.</summary>
    public static Guid? UserId(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        return Guid.TryParse(principal.FindFirst(Subject)?.Value, out Guid id) && id != Guid.Empty ? id : null;
    }

    /// <summary>
    /// Cabinet de l'utilisateur : l'organisation Keycloak. Le claim <c>organization</c> vaut
    /// <c>{"alias": {"id": "uuid"}}</c> quand le mapper inclut l'identifiant. Un utilisateur dans plusieurs
    /// organisations n'a pas de cabinet implicite.
    /// </summary>
    public static OrganisationId? Cabinet(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        List<Guid> ids = [];
        foreach (Claim claim in principal.FindAll(Organization))
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(claim.Value);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                foreach (JsonProperty organisation in document.RootElement.EnumerateObject())
                {
                    if (organisation.Value.ValueKind == JsonValueKind.Object
                        && organisation.Value.TryGetProperty("id", out JsonElement id)
                        && Guid.TryParse(id.GetString(), out Guid value))
                    {
                        ids.Add(value);
                    }
                }
            }
            catch (JsonException)
            {
                // Claim illisible (alias seul, sans identifiant) : ignoré, l'utilisateur n'a pas de cabinet implicite.
            }
        }

        return ids.Distinct().Count() == 1 ? new OrganisationId(ids[0]) : null;
    }

    /// <summary>Second facteur prouvé par <c>amr</c> ou <c>acr</c>.</summary>
    public static bool HasStrongAuthentication(ClaimsPrincipal principal, OidcOptions options)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(options);
        return principal.FindAll(AuthenticationMethods).Any(c => options.StrongAmrValues.Contains(c.Value, StringComparer.Ordinal))
            || principal.FindAll(AuthenticationContext).Any(c => options.StrongAcrValues.Contains(c.Value, StringComparer.Ordinal));
    }

    public static string? DisplayName(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        return principal.FindFirst(Name)?.Value ?? principal.FindFirst(PreferredUserName)?.Value;
    }
}
