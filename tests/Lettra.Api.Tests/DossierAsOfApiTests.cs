using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Lettra.Contracts.Banking;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Common;
using Lettra.Contracts.Insights;
using Lettra.Contracts.Serialization;
using Lettra.Domain.SharedKernel;

namespace Lettra.Api.Tests;

/// <summary>
/// « Remonter le temps » par l'API réelle (mode 2, SQLite) : le journal inaltérable rejoué jusqu'à un instant donne le
/// dossier de ce moment-là, chaîne vérifiée ; ce qui est arrivé après n'y figure pas.
/// </summary>
public sealed class DossierAsOfApiTests(LettraApiFactory factory) : IClassFixture<LettraApiFactory>
{
    private static Uri Api(string path) => new(path, UriKind.Relative);

    private HttpClient Manager()
    {
        HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(LettraHeaders.Dossier, LettraApiFactory.DossierId.ToString());
        return client;
    }

    private static async Task<DossierAsOfResponse> AsOfAsync(HttpClient client, DateTimeOffset at)
    {
        using HttpResponseMessage response = await client.GetAsync(Api($"/api/v1/insights/as-of?at={Uri.EscapeDataString(at.ToString("O", CultureInfo.InvariantCulture))}"));
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync(ContractsJsonContext.Default.DossierAsOfResponse))!;
    }

    /// <summary>Un instant franchement passé : les entrées du journal sont tronquées à la milliseconde.</summary>
    private static async Task<DateTimeOffset> MomentAsync()
    {
        DateTimeOffset moment = DateTimeOffset.UtcNow;
        await Task.Delay(20);
        return moment;
    }

    [Fact]
    public async Task Le_dossier_d_un_instant_passe_ne_montre_que_ce_qui_existait_alors()
    {
        using HttpClient manager = Manager();
        DateTimeOffset before = await MomentAsync();
        Guid invoiceId = await ExportApiTests.ValidatedInvoiceAsync(manager);
        InvoiceResponse invoice = (await manager.GetFromJsonAsync(Api($"/api/v1/invoices/{invoiceId}"), ContractsJsonContext.Default.InvoiceResponse))!;
        DateTimeOffset validated = await MomentAsync();
        using HttpResponseMessage paid = await manager.PostAsJsonAsync(
            Api("/api/v1/payments"),
            new RecordPaymentRequest
            {
                CustomerId = invoice.CustomerId,
                Date = LegalCalendar.DateOf(DateTimeOffset.UtcNow),
                Amount = invoice.Totals.TotalInclTax,
                Method = PaymentMethodDto.Transfer,
                Allocations = [new PaymentAllocationRequest(invoiceId, invoice.Totals.TotalInclTax)],
            },
            ContractsJsonContext.Default.RecordPaymentRequest);
        paid.StatusCode.Should().Be(HttpStatusCode.Created, await paid.Content.ReadAsStringAsync());

        DossierAsOfResponse earlier = await AsOfAsync(manager, before);
        DossierAsOfResponse then = await AsOfAsync(manager, validated);
        DossierAsOfResponse now = await AsOfAsync(manager, DateTimeOffset.UtcNow.AddSeconds(1));

        earlier.OpenDocuments.Should().NotContain(d => d.DocumentId == invoiceId);
        earlier.EntriesAfter.Should().BeGreaterThanOrEqualTo(2, "la validation et l'encaissement sont venus après");

        then.ChainIntact.Should().BeTrue();
        then.EntriesReplayed.Should().BeGreaterThan(earlier.EntriesReplayed);
        DossierAsOfDocumentResponse open = then.OpenDocuments.Should().ContainSingle(d => d.DocumentId == invoiceId).Subject;
        open.Number.Should().Be(invoice.Number);
        open.OutstandingInclTax.Should().Be(invoice.Totals.TotalInclTax);
        open.OutstandingNowInclTax.Should().Be(0m, "elle est payée depuis");
        then.Counts.Invoices.Should().Be(earlier.Counts.Invoices + 1);
        then.LastEvents[0].Kind.Should().Be(DossierAsOfEventKindDto.InvoiceValidated);
        then.LastEvents[0].Number.Should().Be(invoice.Number);

        now.ChainIntact.Should().BeTrue();
        now.EntriesAfter.Should().Be(0);
        now.OpenDocuments.Should().NotContain(d => d.DocumentId == invoiceId);
        now.Counts.Payments.Should().Be(then.Counts.Payments + 1);
        now.LastEvents[0].Kind.Should().Be(DossierAsOfEventKindDto.PaymentRecorded);
        now.ReceivablesInclTax.Should().Be(now.ReceivablesNowInclTax, "à l'instant présent, le rejeu et le dossier disent la même chose");
    }

    [Fact]
    public async Task Avant_la_premiere_piece_le_dossier_est_vide()
    {
        using HttpClient manager = Manager();
        await ExportApiTests.ValidatedInvoiceAsync(manager);

        DossierAsOfResponse past = await AsOfAsync(manager, new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero));

        past.EntriesReplayed.Should().Be(0);
        past.ChainIntact.Should().BeTrue();
        past.FirstEntryAt.Should().NotBeNull();
        past.OpenDocuments.Should().BeEmpty();
        past.Months.Should().ContainSingle().Which.ExclTax.Should().Be(0m);
        past.LastClosing.Should().BeNull();
    }
}
