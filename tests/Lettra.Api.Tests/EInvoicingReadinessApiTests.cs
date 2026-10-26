using System.Net;
using System.Net.Http.Json;
using Lettra.Contracts.Common;
using Lettra.Contracts.Insights;
using Lettra.Contracts.Serialization;

namespace Lettra.Api.Tests;

/// <summary>
/// Le score de préparation à la facture électronique par l'API réelle (mode 2, SQLite) : une facture validée à un client
/// professionnel français, jamais transmise, apparaît dans ce qu'il reste à corriger.
/// </summary>
public sealed class EInvoicingReadinessApiTests(LettraApiFactory factory) : IClassFixture<LettraApiFactory>
{
    [Fact]
    public async Task Une_facture_validee_non_transmise_est_a_corriger()
    {
        using HttpClient manager = factory.CreateClient();
        manager.DefaultRequestHeaders.Add(LettraHeaders.Dossier, LettraApiFactory.DossierId.ToString());
        Guid invoiceId = await ExportApiTests.ValidatedInvoiceAsync(manager);

        using HttpResponseMessage response = await manager.GetAsync("/api/v1/insights/e-invoicing-readiness");

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        EInvoicingReadinessResponse readiness = (await response.Content.ReadFromJsonAsync(ContractsJsonContext.Default.EInvoicingReadinessResponse))!;
        readiness.Checks.Select(c => c.Check).Should().Equal(Enum.GetValues<EInvoicingCheckDto>());
        EInvoicingCheckResponse transmissions = readiness.Checks.Single(c => c.Check == EInvoicingCheckDto.Transmissions);
        transmissions.Gaps.Should().Contain(g => g.Id == invoiceId);
        readiness.Score.Should().BeLessThan(100);
    }
}
