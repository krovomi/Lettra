using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Lettra.Contracts.Common;
using Lettra.Contracts.Customers;
using Lettra.Contracts.Serialization;
using Lettra.UI.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.UI.Gateway.Http;

internal sealed class HttpCustomersGateway(HttpClient http) : ICustomersGateway
{
    public async Task<OperationResult<CustomerResponse>> CreateAsync(CreateCustomerRequest request, CancellationToken cancellationToken)
    {
        try
        {
            using HttpResponseMessage response = await http
                .PostAsJsonAsync("api/v1/customers", request, ContractsJsonContext.Default.CreateCustomerRequest, cancellationToken)
                .ConfigureAwait(false);
            return await response.ToOperationResultAsync(ContractsJsonContext.Default.CustomerResponse, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            return OperationResult<CustomerResponse>.Failure(ErrorCodes.Api.Unreachable);
        }
    }

    public async Task<OperationResult<PagedResponse<CustomerSummary>>> ListAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        try
        {
            Uri uri = new(string.Create(CultureInfo.InvariantCulture, $"api/v1/customers?page={page}&pageSize={pageSize}"), UriKind.Relative);
            using HttpResponseMessage response = await http.GetAsync(uri, cancellationToken).ConfigureAwait(false);
            return await response.ToOperationResultAsync(ContractsJsonContext.Default.PagedResponseCustomerSummary, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            return OperationResult<PagedResponse<CustomerSummary>>.Failure(ErrorCodes.Api.Unreachable);
        }
    }
}

/// <summary>Traduit une réponse HTTP en <see cref="OperationResult{T}"/> : corps typé, ou codes d'erreur du <c>ProblemResponse</c>.</summary>
internal static class HttpResponseExtensions
{
    public static async Task<OperationResult<T>> ToOperationResultAsync<T>(
        this HttpResponseMessage response, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            T? value = await response.Content.ReadFromJsonAsync(typeInfo, cancellationToken).ConfigureAwait(false);
            return value is null ? OperationResult<T>.Failure(ErrorCodes.Api.Unexpected) : OperationResult<T>.Success(value);
        }

        ProblemResponse? problem = null;
        try
        {
            problem = await response.Content.ReadFromJsonAsync(ContractsJsonContext.Default.ProblemResponse, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            // Corps illisible (proxy, page d'erreur HTML) : erreur générique ci-dessous.
        }

        List<ErrorDetail> errors = [];
        foreach ((string field, string[] codes) in problem?.Errors ?? new Dictionary<string, string[]>())
        {
            string? target = string.IsNullOrEmpty(field) ? null : char.ToUpperInvariant(field[0]) + field[1..];
            errors.AddRange(codes.Select(code => new ErrorDetail(code, target)));
        }

        // Le code principal, s'il n'est pas déjà rattaché à un champ (ex. Dossier.NotFound, Security.Forbidden).
        string? mainCode = problem?.Code;
        if (errors.Count == 0 || (mainCode is not null && !errors.Exists(e => e.Code == mainCode)))
        {
            errors.Add(new ErrorDetail(mainCode ?? ErrorCodes.Api.Unexpected));
        }

        return OperationResult<T>.Failure(errors);
    }
}

/// <summary>Ajoute l'en-tête <c>X-Dossier</c> du dossier ouvert à chaque appel.</summary>
internal sealed class DossierHeaderHandler(IDossierSelection selection) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (selection.CurrentDossierId is Guid dossier)
        {
            request.Headers.Remove(LettraHeaders.Dossier);
            request.Headers.Add(LettraHeaders.Dossier, dossier.ToString());
        }

        return base.SendAsync(request, cancellationToken);
    }
}

/// <summary>
/// En-tête anti-CSRF exigé par l'API pour les appels authentifiés par le cookie du BFF (annexe F §2). Le
/// navigateur ne l'ajoute jamais de lui-même à une requête venue d'un autre site.
/// </summary>
internal sealed class CsrfHeaderHandler : DelegatingHandler
{
    public const string HeaderName = "X-Lettra-Csrf";

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Headers.Remove(HeaderName);
        request.Headers.Add(HeaderName, "1");
        return base.SendAsync(request, cancellationToken);
    }
}

public static class DependencyInjection
{
    public static IServiceCollection AddHttpGateways(this IServiceCollection services, Uri apiBaseAddress)
    {
        services.AddTransient<DossierHeaderHandler>();
        services.AddTransient<CsrfHeaderHandler>();
        services.AddHttpClient<ICustomersGateway, HttpCustomersGateway>(client => client.BaseAddress = apiBaseAddress)
            .AddHttpMessageHandler<CsrfHeaderHandler>()
            .AddHttpMessageHandler<DossierHeaderHandler>();
        return services;
    }
}
