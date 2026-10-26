using System.Threading.RateLimiting;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Contracts.Portal;
using Lettra.Domain.Common;
using Lettra.Domain.Portal;
using Lettra.Domain.SharedKernel;
using Lettra.Exports.Application.Portal;
using Lettra.Exports.Infrastructure;
using Lettra.Infrastructure.Persistence.Integrity;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Lettra.Api.Portal;

/// <summary>Réglages de l'espace client (<c>Lettra:CustomerPortal</c>).</summary>
public sealed class CustomerPortalOptions
{
    public const string SectionName = "Lettra:CustomerPortal";

    /// <summary>
    /// Le compte de service sous lequel agit l'espace client (lire les pièces du client, enregistrer un paiement en
    /// ligne, accepter un devis signé, facturer son acompte). Déclaré au registre des droits comme tout utilisateur : ses
    /// rôles disent ce qu'il peut faire. Vide, l'espace client est fermé et aucun lien ne peut être créé.
    /// </summary>
    public Guid? ServiceAccountId { get; set; }

    /// <summary>Requêtes par minute et par adresse IP sur les pages de l'espace client.</summary>
    public int PermitsPerMinute { get; set; } = 60;
}

/// <summary>Le compte sous lequel agit l'espace client dans la portée de la requête ; vide ailleurs.</summary>
internal sealed class PortalActor
{
    public Guid UserId { get; set; }
}

/// <summary>Le compte de service dans la portée d'une page de l'espace client, l'utilisateur de la requête ailleurs.</summary>
internal sealed class PortalCurrentUser(PortalActor actor, ICurrentUser host) : ICurrentUser
{
    public bool IsAuthenticated => actor.UserId != Guid.Empty || host.IsAuthenticated;

    public Guid UserId => actor.UserId != Guid.Empty ? actor.UserId : host.UserId;
}

/// <summary>L'espace client est ouvert quand un compte de service lui est déclaré.</summary>
internal sealed class ConfiguredCustomerPortal(IOptions<CustomerPortalOptions> options) : ICustomerPortalSettings
{
    public bool IsEnabled => options.Value.ServiceAccountId is not null;
}

/// <summary>
/// Exécute un cas d'usage de l'espace client : le jeton du lien donne le dossier (cherché au catalogue) et l'empreinte
/// du secret ; la portée reçoit le dossier et le compte de service. Un jeton mal formé, un dossier inconnu et un lien
/// révoqué donnent la même réponse.
/// </summary>
internal sealed class PortalRunner(IServiceScopeFactory scopes, ITenantCatalog catalog, IOptions<CustomerPortalOptions> options)
{
    public async Task<Result<T>> RunAsync<T>(string token, Func<DossierId, string, ICommandDispatcher, CancellationToken, Task<Result<T>>> work, CancellationToken cancellationToken)
    {
        if (options.Value.ServiceAccountId is not Guid account)
        {
            return PortalErrors.Disabled;
        }

        if (PortalToken.Read(token) is not (DossierId dossier, string hash))
        {
            return PortalErrors.LinkNotFound;
        }

        TenantDescriptor? descriptor = await catalog.FindAsync(dossier, cancellationToken).ConfigureAwait(false);
        if (descriptor is null)
        {
            return PortalErrors.LinkNotFound;
        }

        return await DossierJobScope.RunAsync(scopes, descriptor, (provider, ct) =>
        {
            provider.GetRequiredService<PortalActor>().UserId = account;
            return work(dossier, hash, provider.GetRequiredService<ICommandDispatcher>(), ct);
        }, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// L'espace client servi par l'API, hors de <c>/api/v1</c> et sans session : <c>/espace-client/{jeton}</c> (factures,
/// devis, paiement, signature) et, avec le prestataire simulé, sa page de paiement <c>/paiement-simule/{id}</c>. Pages
/// HTML sans script, limitées en débit par adresse IP ; les formulaires refusent une origine étrangère.
/// </summary>
public static class CustomerPortalHosting
{
    public const string RateLimitPolicy = "espace-client";

    private const string CheckoutPlaceholder = "{CHECKOUT_ID}";

    /// <summary>
    /// Branche l'espace client, ouvert si <c>Lettra:CustomerPortal:ServiceAccountId</c> est renseigné : le compte de
    /// service vaut dans la portée des pages de l'espace client, l'identité de la requête partout ailleurs (l'identité de
    /// l'hôte, <c>ICurrentUser</c>, doit être enregistrée avant cet appel). À appeler après <c>AddLettra</c> et
    /// <c>AddLettraApiAuthentication</c> ; un second appel ne change rien.
    /// </summary>
    public static IServiceCollection AddCustomerPortal(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (services.Any(d => d.ServiceType == typeof(PortalRunner)))
        {
            return services;
        }

        IConfigurationSection section = configuration.GetSection(CustomerPortalOptions.SectionName);
        services.AddOptions<CustomerPortalOptions>().Bind(section)
            .Validate(o => o.ServiceAccountId != Guid.Empty, $"{CustomerPortalOptions.SectionName}:ServiceAccountId ne peut pas être vide.")
            .Validate(o => o.PermitsPerMinute is >= 1 and <= 10_000, $"{CustomerPortalOptions.SectionName}:PermitsPerMinute va de 1 à 10 000.")
            .ValidateOnStart();
        services.AddSingleton<PortalRunner>();
        services.AddRateLimiter(limiter => limiter.AddPolicy(RateLimitPolicy, context =>
        {
            int permits = context.RequestServices.GetRequiredService<IOptions<CustomerPortalOptions>>().Value.PermitsPerMinute;
            return RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "", _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permits,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            });
        }));

        // Ouvert ou fermé selon les réglages lus au démarrage (pas à l'enregistrement : un hôte peut encore les compléter).
        services.Replace(ServiceDescriptor.Singleton<ICustomerPortalSettings, ConfiguredCustomerPortal>());
        ServiceDescriptor host = services.LastOrDefault(d => d.ServiceType == typeof(ICurrentUser) && !d.IsKeyedService)
            ?? throw new InvalidOperationException("L'identité de l'hôte (ICurrentUser) doit être enregistrée avant AddCustomerPortal.");
        services.Remove(host);
        services.AddScoped<PortalActor>();
        services.AddScoped<ICurrentUser>(sp => new PortalCurrentUser(sp.GetRequiredService<PortalActor>(), Resolve(sp, host)));
        return services;
    }

    /// <summary>Les pages de l'espace client et, avec le prestataire simulé, sa page de paiement.</summary>
    public static IEndpointRouteBuilder MapCustomerPortal(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        RouteGroupBuilder portal = app.MapGroup("/espace-client/{token}").RequireRateLimiting(RateLimitPolicy).ExcludeFromDescription();
        portal.MapGet("/", ShowAsync);
        portal.MapPost("/devis/{quoteId:guid}/signer", SignAsync);
        portal.MapPost("/factures/{invoiceId:guid}/payer", PayAsync);
        portal.MapGet("/paiement/{checkoutId}", ReturnFromPaymentAsync);

        RouteGroupBuilder checkout = app.MapGroup("/paiement-simule/{checkoutId}").RequireRateLimiting(RateLimitPolicy).ExcludeFromDescription();
        checkout.MapGet("/", ShowCheckoutAsync);
        checkout.MapPost("/", SubmitCheckoutAsync);
        return app;
    }

    private static async Task<IResult> ShowAsync(string token, HttpContext context, PortalRunner runner, CancellationToken ct,
        string? paye = null, string? signe = null, string? acompte = null, string? attente = null, string? erreur = null)
    {
        Result<PortalView> view = await runner.RunAsync(token, (dossier, hash, commands, c) =>
            commands.SendAsync(new OpenPortalCommand(dossier, hash), c), ct).ConfigureAwait(false);
        if (view.IsFailure)
        {
            return ErrorPage(context, view.Errors[0], null);
        }

        PortalTexts texts = PortalTexts.For(view.Value.Language, context.Request.Headers.AcceptLanguage);
        PortalBanner? banner = (paye, signe, attente, erreur) switch
        {
            ({ } number, _, _, _) => new PortalBanner("Banner.Paid", number, false),
            (_, { } number, _, _) => new PortalBanner(acompte is null ? "Banner.Signed" : "Banner.SignedWithDownPayment", number, false),
            (_, _, { } number, _) => new PortalBanner("Banner.PaymentPending", number, false),
            (_, _, _, { } code) when PortalTexts.Keys.Contains("Error." + code) => new PortalBanner("Error." + code, null, true),
            _ => null,
        };
        return Html(context, PortalPages.Portal(view.Value, texts, BasePath(token), banner));
    }

    private static async Task<IResult> SignAsync(string token, Guid quoteId, HttpContext context, PortalRunner runner, CancellationToken ct)
    {
        if (!SameOrigin(context))
        {
            return TypedResults.StatusCode(StatusCodes.Status403Forbidden);
        }

        IFormCollection form = await context.Request.ReadFormAsync(ct).ConfigureAwait(false);
        SignQuoteRequest request = new(form["signer"].ToString(), form["consent"].ToString() == "true");
        Result<SignQuoteResult> signed = await runner.RunAsync(token, (dossier, hash, commands, c) =>
            commands.SendAsync(new SignQuoteOnPortalCommand(dossier, hash, quoteId, request), c), ct).ConfigureAwait(false);
        if (signed.IsFailure)
        {
            return Back(token, $"?erreur={Uri.EscapeDataString(signed.Errors[0].Code)}#quote-{quoteId}");
        }

        string number = Uri.EscapeDataString(signed.Value.QuoteNumber);
        return signed.Value.DownPaymentInvoiceId is Guid invoice
            ? Back(token, $"?signe={number}&acompte=1#invoice-{invoice}")
            : Back(token, $"?signe={number}#quote-{quoteId}");
    }

    private static async Task<IResult> PayAsync(string token, Guid invoiceId, HttpContext context, PortalRunner runner, CancellationToken ct)
    {
        if (!SameOrigin(context))
        {
            return TypedResults.StatusCode(StatusCodes.Status403Forbidden);
        }

        string returnUrl = $"{BasePath(token)}/paiement/{CheckoutPlaceholder}";
        Result<OnlineCheckoutResponse> started = await runner.RunAsync(token, (dossier, hash, commands, c) =>
            commands.SendAsync(new StartOnlinePaymentCommand(dossier, hash, invoiceId, returnUrl), c), ct).ConfigureAwait(false);
        return started.IsFailure
            ? Back(token, $"?erreur={Uri.EscapeDataString(started.Errors[0].Code)}#invoice-{invoiceId}")
            : TypedResults.Redirect(started.Value.RedirectUrl);
    }

    private static async Task<IResult> ReturnFromPaymentAsync(string token, string checkoutId, HttpContext context, PortalRunner runner, CancellationToken ct)
    {
        Result<OnlinePaymentResult> completed = await runner.RunAsync(token, (dossier, hash, commands, c) =>
            commands.SendAsync(new CompleteOnlinePaymentCommand(dossier, hash, checkoutId), c), ct).ConfigureAwait(false);
        if (completed.IsFailure)
        {
            return completed.Errors[0].Code == PortalErrors.LinkNotFoundCode
                ? ErrorPage(context, completed.Errors[0], null)
                : Back(token, $"?erreur={Uri.EscapeDataString(completed.Errors[0].Code)}#portal-invoices");
        }

        string number = Uri.EscapeDataString(completed.Value.InvoiceNumber);
        return Back(token, completed.Value.Paid ? $"?paye={number}#portal-invoices" : $"?attente={number}#portal-invoices");
    }

    private static async Task<IResult> ShowCheckoutAsync(string checkoutId, HttpContext context, IServiceProvider services, CancellationToken ct, string? refus = null)
    {
        if (services.GetService<SimulatedPaymentProvider>() is not { } provider
            || await provider.FindAsync(checkoutId, ct).ConfigureAwait(false) is not { State: CheckoutState.Pending } found)
        {
            return ErrorPage(context, PortalErrors.CheckoutNotFound, null);
        }

        PortalTexts texts = PortalTexts.For(null, context.Request.Headers.AcceptLanguage);
        return Html(context, PortalPages.SimulatedCheckout(found, texts, refus is not null));
    }

    private static async Task<IResult> SubmitCheckoutAsync(string checkoutId, HttpContext context, IServiceProvider services, CancellationToken ct)
    {
        if (!SameOrigin(context))
        {
            return TypedResults.StatusCode(StatusCodes.Status403Forbidden);
        }

        if (services.GetService<SimulatedPaymentProvider>() is not { } provider
            || await provider.FindAsync(checkoutId, ct).ConfigureAwait(false) is not { State: CheckoutState.Pending } pending)
        {
            return ErrorPage(context, PortalErrors.CheckoutNotFound, null);
        }

        // Carte refusée : la page se réaffiche pour un autre essai, tant que la session reste ouverte. Sinon (payé,
        // annulé, session fermée), retour à l'espace client qui dit où en est le paiement.
        IFormCollection form = await context.Request.ReadFormAsync(ct).ConfigureAwait(false);
        if (form["decision"].ToString() == "pay"
            && provider.Pay(checkoutId, form["card"].ToString()) == CheckoutState.Declined
            && await provider.FindAsync(checkoutId, ct).ConfigureAwait(false) is { State: CheckoutState.Pending })
        {
            return TypedResults.Redirect($"/paiement-simule/{Uri.EscapeDataString(checkoutId)}?refus=1");
        }

        return TypedResults.Redirect(pending.Request.ReturnUrl.Replace(CheckoutPlaceholder, Uri.EscapeDataString(checkoutId), StringComparison.Ordinal));
    }

    private static string BasePath(string token) => "/espace-client/" + Uri.EscapeDataString(token);

    private static RedirectHttpResult Back(string token, string suffix) => TypedResults.Redirect(BasePath(token) + suffix);

    /// <summary>
    /// Un formulaire de l'espace client ne se poste que depuis l'espace client : une origine annoncée doit être la nôtre.
    /// Les pages demandent <c>referrer same-origin</c> : le jeton du lien ne part vers aucun autre site, et le navigateur
    /// annonce une vraie origine (avec <c>no-referrer</c>, il enverrait « null », invérifiable, donc refusée).
    /// </summary>
    internal static bool SameOrigin(HttpContext context)
    {
        string? origin = context.Request.Headers.Origin;
        if (string.IsNullOrEmpty(origin))
        {
            return true;
        }

        return Uri.TryCreate(origin, UriKind.Absolute, out Uri? uri)
            && string.Equals(uri.Authority, context.Request.Host.Value, StringComparison.OrdinalIgnoreCase);
    }

    private static ContentHttpResult ErrorPage(HttpContext context, Error error, string? preferredLanguage)
    {
        PortalTexts texts = PortalTexts.For(preferredLanguage, context.Request.Headers.AcceptLanguage);
        string key = PortalTexts.Keys.Contains("Error." + error.Code) ? "Error." + error.Code : "Error.Unexpected";
        int status = error.Kind switch
        {
            ErrorKind.NotFound => StatusCodes.Status404NotFound,
            ErrorKind.Forbidden or ErrorKind.Unauthorized => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status409Conflict,
        };
        return Html(context, PortalPages.Message(texts, "NotFound.Title", key), status);
    }

    private static ContentHttpResult Html(HttpContext context, string html, int status = StatusCodes.Status200OK)
    {
        // Les pièces du client ne restent ni dans un cache partagé ni dans l'historique du navigateur.
        context.Response.Headers[HeaderNames.CacheControl] = "no-store";
        return TypedResults.Content(html, "text/html; charset=utf-8", statusCode: status);
    }

    private static ICurrentUser Resolve(IServiceProvider services, ServiceDescriptor host) =>
        (ICurrentUser)(host.ImplementationInstance
            ?? host.ImplementationFactory?.Invoke(services)
            ?? ActivatorUtilities.CreateInstance(services, host.ImplementationType!));
}
