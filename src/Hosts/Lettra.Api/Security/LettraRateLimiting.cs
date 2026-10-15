using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Lettra.Contracts.Common;
using Lettra.Infrastructure.Identity.Oidc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Lettra.Api.Security;

/// <summary>Limites de débit (section <c>Lettra:RateLimiting</c>) : une fenêtre glissante par cabinet, puis par utilisateur.</summary>
public sealed class RateLimitingOptions
{
    public const string SectionName = "Lettra:RateLimiting";

    public bool Enabled { get; set; } = true;

    /// <summary>Requêtes admises par cabinet et par fenêtre, tous utilisateurs confondus.</summary>
    public int PermitsPerCabinet { get; set; } = 2000;

    /// <summary>Requêtes admises par utilisateur et par fenêtre : un poste emballé ne prive pas tout le cabinet.</summary>
    public int PermitsPerUser { get; set; } = 300;

    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(1);

    public int SegmentsPerWindow { get; set; } = 6;
}

/// <summary>
/// Limitation de débit par cabinet (phase 3) : un cabinet ne sature pas la cellule partagée. Les compteurs sont en
/// mémoire de chaque instance : la limite effective est multipliée par le nombre de pods, ce qui suffit à contenir
/// un abus sans dépendre de Redis. Les requêtes anonymes ne sont pas comptées (l'authentification les refuse déjà).
/// </summary>
public static class LettraRateLimiting
{
    public static IServiceCollection AddLettraRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddOptions<RateLimitingOptions>()
            .Bind(configuration.GetSection(RateLimitingOptions.SectionName))
            .Validate(o => o.PermitsPerCabinet > 0 && o.PermitsPerUser > 0 && o.Window > TimeSpan.Zero && o.SegmentsPerWindow > 0,
                $"{RateLimitingOptions.SectionName} : limites et fenêtre strictement positives.")
            .ValidateOnStart();

        services.AddRateLimiter(_ => { });
        services.AddOptions<RateLimiterOptions>().Configure<IOptions<RateLimitingOptions>>((limiter, settings) =>
        {
            RateLimitingOptions o = settings.Value;
            limiter.GlobalLimiter = PartitionedRateLimiter.CreateChained(
                Partition(o, Cabinet, o.PermitsPerCabinet),
                Partition(o, User, o.PermitsPerUser));
            limiter.OnRejected = RejectAsync;
        });
        return services;
    }

    private static PartitionedRateLimiter<HttpContext> Partition(RateLimitingOptions o, Func<ClaimsPrincipal, string?> key, int permits) =>
        PartitionedRateLimiter.Create<HttpContext, string>(context =>
            o.Enabled && context.User is { Identity.IsAuthenticated: true } user && key(user) is string partition
                ? RateLimitPartition.GetSlidingWindowLimiter(partition, _ => new SlidingWindowRateLimiterOptions
                {
                    PermitLimit = permits,
                    Window = o.Window,
                    SegmentsPerWindow = o.SegmentsPerWindow,
                    QueueLimit = 0,
                    AutoReplenishment = true,
                })
                : RateLimitPartition.GetNoLimiter(string.Empty));

    private static string? Cabinet(ClaimsPrincipal user) => OidcClaims.Cabinet(user) is { } cabinet ? "c:" + cabinet.Value : null;

    private static string? User(ClaimsPrincipal user) => OidcClaims.UserId(user) is Guid id ? "u:" + id : null;

    private static async ValueTask RejectAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        HttpContext http = context.HttpContext;

        // Le limiteur chaîné ne remonte pas toujours le délai : à défaut, un segment de la fenêtre glissante.
        if (!context.Lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan retryAfter))
        {
            RateLimitingOptions o = http.RequestServices.GetRequiredService<IOptions<RateLimitingOptions>>().Value;
            retryAfter = o.Window / o.SegmentsPerWindow;
        }

        http.Response.Headers.RetryAfter = Math.Max(1, Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);

        await TypedResults.Problem(
                statusCode: StatusCodes.Status429TooManyRequests,
                title: ErrorCodes.Api.TooManyRequests,
                extensions: new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = ErrorCodes.Api.TooManyRequests })
            .ExecuteAsync(http)
            .ConfigureAwait(false);
    }
}
