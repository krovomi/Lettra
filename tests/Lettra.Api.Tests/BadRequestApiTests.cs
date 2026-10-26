using System.Net;
using Lettra.Contracts.Common;

namespace Lettra.Api.Tests;

/// <summary>
/// Un paramètre de requête illisible (une date ou un nombre mal écrit) est une erreur du client : 400 et son code,
/// jamais l'erreur inattendue (500), y compris en développement, où la liaison des paramètres lève une exception.
/// </summary>
public sealed class BadRequestApiTests(LettraApiFactory factory) : IClassFixture<LettraApiFactory>
{
    [Theory]
    [InlineData("/api/v1/tax/payment-delays?closingDate=hier")]
    [InlineData("/api/v1/accounting/closing-review?closingDate=31-12-2025x")]
    [InlineData("/api/v1/customers?page=abc")]
    [InlineData("/api/v1/insights/as-of?at=hier")]
    [InlineData("/api/v1/insights/why?figure=Inconnu")]
    [InlineData("/api/v1/insights/practice-audit?from=hier&to=2025-12-31")]
    public async Task Un_parametre_illisible_rend_400_et_non_500(string path)
    {
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(LettraHeaders.Dossier, LettraApiFactory.DossierId.ToString());

        using HttpResponseMessage response = await client.GetAsync(new Uri(path, UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain($"\"code\":\"{ErrorCodes.Api.BadRequest}\"");
    }
}
