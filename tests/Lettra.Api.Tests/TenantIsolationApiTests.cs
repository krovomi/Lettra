using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using Lettra.Api.Security;
using Lettra.Contracts.Common;
using Lettra.Contracts.Serialization;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Api.Tests;

/// <summary>
/// Matrice d'isolation des cabinets (plan du mode 5, étape 3) : l'administrateur d'un autre cabinet appelle chaque route
/// de l'API avec le dossier, le cabinet ou le groupe du cabinet visé. Il n'obtient jamais de réponse réussie, et la
/// réponse est exactement celle d'un identifiant qui n'existe pas : rien ne trahit l'existence de ce qu'il vise.
/// </summary>
public sealed class TenantIsolationApiTests(LettraApiFactory factory) : IClassFixture<LettraApiFactory>
{
    /// <summary>Identifiant des autres paramètres de route (client, facture…) : le même dans les deux appels comparés.</summary>
    private static readonly Guid Other = new("0199e3a0-0000-7000-8000-00000000f00d");

    public static TheoryData<string> Scopes() => [.. Enum.GetNames<PermissionScopeKind>()];

    /// <remarks>
    /// Une route de dossier répond « dossier introuvable », ou refuse la forme de la requête (paramètre de requête
    /// manquant, corps illisible ou d'un autre type) avant même de regarder le dossier : dans les deux cas, comme pour un dossier qui n'existe pas.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Scopes))]
    public async Task Un_autre_cabinet_n_atteint_rien_et_ne_distingue_rien_d_un_identifiant_inconnu(string scope)
    {
        PermissionScopeKind kind = Enum.Parse<PermissionScopeKind>(scope);
        List<(string Method, RouteEndpoint Route)> routes = [.. Routes().Where(r => r.Route.Metadata.GetMetadata<RequiredPermissionMetadata>()?.Scope == kind)];
        using HttpClient attacker = factory.ClientFor(LettraApiFactory.AutreCabinetAdminId);
        List<string> failures = [];

        foreach ((string method, RouteEndpoint route) in routes)
        {
            Target victim = new(LettraApiFactory.DossierId, LettraApiFactory.CabinetId);
            Target unknown = new(Guid.CreateVersion7(), Guid.CreateVersion7());
            (HttpStatusCode status, string? code) aimed = await CallAsync(attacker, method, route, victim);
            (HttpStatusCode status, string? code) missing = await CallAsync(attacker, method, route, unknown);
            string name = $"{method} {route.RoutePattern.RawText}";

            if (kind is PermissionScopeKind.Filtered)
            {
                // Rien de l'autre cabinet n'est désigné : la route répond de même, quel que soit le dossier visé.
                if (aimed != missing)
                {
                    failures.Add($"{name} : {aimed} au lieu de {missing}");
                }

                continue;
            }

            if (aimed.status == HttpStatusCode.TooManyRequests || missing.status == HttpStatusCode.TooManyRequests)
            {
                // Le limiteur de débit de l'utilisateur passe avant tout : la comparaison n'a pas de sens, rien n'a été atteint.
                continue;
            }

            if ((int)aimed.status < 400)
            {
                failures.Add($"{name} : {(int)aimed.status} pour un autre cabinet");
            }
            else if (aimed != missing)
            {
                failures.Add($"{name} : {aimed} au lieu de {missing} (réponse d'un identifiant inconnu)");
            }
            else if (kind is PermissionScopeKind.Dossier
                && aimed != (HttpStatusCode.NotFound, ErrorCodes.Tenancy.DossierNotFound)
                && aimed.status is not (HttpStatusCode.BadRequest or HttpStatusCode.UnsupportedMediaType))
            {
                failures.Add($"{name} : {aimed} au lieu de 404 {ErrorCodes.Tenancy.DossierNotFound}");
            }
        }

        if (kind is not PermissionScopeKind.Group)
        {
            routes.Should().NotBeEmpty($"des routes portent sur « {kind} »");
        }

        failures.Should().BeEmpty();
    }

    [Fact]
    public void La_matrice_couvre_toutes_les_routes_de_l_api() =>
        Routes().Where(r => r.Route.Metadata.GetMetadata<RequiredPermissionMetadata>() is null && r.Route.Metadata.GetMetadata<OwnAccountOnlyMetadata>() is null)
            .Select(r => r.Route.RoutePattern.RawText)
            .Should().BeEmpty("chaque route déclare la portée de sa permission, que la matrice parcourt");

    private IEnumerable<(string Method, RouteEndpoint Route)> Routes() =>
        factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("/api/", StringComparison.Ordinal) == true)
            .SelectMany(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["GET"]).Select(m => (m, e)))
            .OrderBy(r => r.e.RoutePattern.RawText, StringComparer.Ordinal)
            .ThenBy(r => r.m, StringComparer.Ordinal);

    private static async Task<(HttpStatusCode, string?)> CallAsync(HttpClient client, string method, RouteEndpoint route, Target target)
    {
        using HttpRequestMessage request = new(new HttpMethod(method), new Uri(Path(route.RoutePattern, target), UriKind.Relative));
        request.Headers.Add(LettraHeaders.Dossier, target.Dossier.ToString());
        request.Headers.IfMatch.ParseAdd("\"1\"");
        if (method is "POST" or "PUT" or "PATCH")
        {
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        }

        using HttpResponseMessage response = await client.SendAsync(request);
        string? code = response.Content.Headers.ContentType?.MediaType == "application/problem+json"
            ? (await response.Content.ReadFromJsonAsync(ContractsJsonContext.Default.ProblemResponse))?.Code
            : null;
        return (response.StatusCode, code);
    }

    /// <summary>Le chemin concret : le cabinet et le dossier visés, une valeur fixe pour les autres paramètres.</summary>
    private static string Path(RoutePattern pattern, Target target)
    {
        StringBuilder path = new();
        foreach (RoutePatternPathSegment segment in pattern.PathSegments)
        {
            path.Append('/');
            foreach (RoutePatternPart part in segment.Parts)
            {
                path.Append(part switch
                {
                    RoutePatternLiteralPart literal => literal.Content,
                    RoutePatternSeparatorPart separator => separator.Content,
                    RoutePatternParameterPart parameter => Value(parameter, target),
                    _ => string.Empty,
                });
            }
        }

        return path.ToString();
    }

    private static string Value(RoutePatternParameterPart parameter, Target target) => parameter.Name switch
    {
        "cabinetId" or "organisationId" => target.Cabinet.ToString(),
        "dossierId" => target.Dossier.ToString(),
        _ when parameter.ParameterPolicies.Any(p => p.Content is "int" or "long") => "1",
        _ when parameter.ParameterPolicies.Any(p => p.Content == "guid") => Other.ToString(),
        "account" => "411000",
        "siren" => "552100554",
        "vatNumber" => "FR40552100554",
        _ => string.Create(CultureInfo.InvariantCulture, $"{2026}-10"),
    };

    private sealed record Target(Guid Dossier, Guid Cabinet);
}
