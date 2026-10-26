using System.Net;
using System.Net.Http.Json;
using Lettra.Contracts.Common;
using Lettra.Contracts.Insights;
using Lettra.Contracts.Serialization;

namespace Lettra.Api.Tests;

/// <summary>
/// La fiche d'appel par l'API réelle (mode 2, SQLite) : le client d'une facture validée y retrouve son nom, sa facture
/// due et son reste dû ; un client inconnu est refusé.
/// </summary>
public sealed class CallSheetApiTests(LettraApiFactory factory) : IClassFixture<LettraApiFactory>
{
    private HttpClient Manager()
    {
        HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(LettraHeaders.Dossier, LettraApiFactory.DossierId.ToString());
        return client;
    }

    [Fact]
    public async Task La_fiche_du_client_d_une_facture_validee_montre_sa_facture_due()
    {
        using HttpClient manager = Manager();
        Guid invoiceId = await ExportApiTests.ValidatedInvoiceAsync(manager);
        Guid customerId = (await manager.GetFromJsonAsync($"/api/v1/invoices/{invoiceId}", ContractsJsonContext.Default.InvoiceResponse))!.CustomerId;

        using HttpResponseMessage response = await manager.GetAsync($"/api/v1/insights/call-sheet/{customerId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        CallSheetResponse sheet = (await response.Content.ReadFromJsonAsync(ContractsJsonContext.Default.CallSheetResponse))!;
        sheet.CustomerName.Should().Be(CustomerApiTests.Martin().Name);
        sheet.Email.Should().Be(CustomerApiTests.Martin().Email);
        sheet.Invoices.Should().Contain(i => i.InvoiceId == invoiceId);
        sheet.OutstandingInclTax.Should().Be(sheet.Invoices.Sum(i => i.OutstandingInclTax));
    }

    [Fact]
    public async Task Un_client_inconnu_repond_404()
    {
        using HttpClient manager = Manager();

        using HttpResponseMessage response = await manager.GetAsync($"/api/v1/insights/call-sheet/{Guid.CreateVersion7()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().Contain(ErrorCodes.Customer.NotFound);
    }
}
