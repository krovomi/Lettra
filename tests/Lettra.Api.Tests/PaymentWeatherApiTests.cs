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
/// Météo des paiements par l'API réelle (mode 2, SQLite) : les factures validées, les encaissements saisis et la
/// prévision lus dans la base du dossier, sans rien y écrire.
/// </summary>
public sealed class PaymentWeatherApiTests
{
    private static Uri Api(string path) => new(path, UriKind.Relative);

    [Fact]
    public async Task Une_facture_payee_fonde_la_meteo_et_une_facture_due_entre_dans_la_prevision()
    {
        await using LettraApiFactory api = new();
        HttpClient client = api.ClientFor(LettraApiFactory.ManagerId);
        client.DefaultRequestHeaders.Add(LettraHeaders.Dossier, LettraApiFactory.DossierId.ToString());
        DateOnly today = LegalCalendar.DateOf(DateTimeOffset.UtcNow);

        Guid paidId = await ExportApiTests.ValidatedInvoiceAsync(client);
        InvoiceResponse paid = (await client.GetFromJsonAsync(Api($"/api/v1/invoices/{paidId}"), ContractsJsonContext.Default.InvoiceResponse))!;
        using HttpResponseMessage recorded = await client.PostAsJsonAsync(
            Api("/api/v1/payments"),
            new RecordPaymentRequest
            {
                CustomerId = paid.CustomerId,
                Date = today,
                Amount = 120m,
                Method = PaymentMethodDto.Transfer,
                Allocations = [new PaymentAllocationRequest(paidId, 120m)],
            },
            ContractsJsonContext.Default.RecordPaymentRequest);
        recorded.StatusCode.Should().Be(HttpStatusCode.Created, await recorded.Content.ReadAsStringAsync());
        Guid dueId = await ExportApiTests.ValidatedInvoiceAsync(client);
        InvoiceResponse due = (await client.GetFromJsonAsync(Api($"/api/v1/invoices/{dueId}"), ContractsJsonContext.Default.InvoiceResponse))!;

        using HttpResponseMessage read = await client.GetAsync(Api("/api/v1/insights/payment-weather"));

        read.StatusCode.Should().Be(HttpStatusCode.OK, await read.Content.ReadAsStringAsync());
        PaymentWeatherResponse weather = (await read.Content.ReadFromJsonAsync(ContractsJsonContext.Default.PaymentWeatherResponse))!;
        weather.Today.Should().Be(today);
        CustomerPaymentWeatherResponse payer = weather.Customers.Single(c => c.CustomerId == paid.CustomerId);
        payer.Samples.Should().Be(1, "la facture réglée à l'échéance ou avant compte");
        payer.OnTimePercent.Should().Be(100);
        payer.Weather.Should().Be(PaymentWeatherDto.Unknown, "une seule facture ne fait pas une météo");
        payer.Expected.Should().NotContain(e => e.InvoiceId == paidId, "elle est soldée");

        ExpectedCollectionResponse expected = weather.Customers.SelectMany(c => c.Expected).Single(e => e.InvoiceId == dueId);
        expected.OutstandingInclTax.Should().Be(120m);
        expected.ExpectedDate.Should().Be(due.DueDate!.Value > today ? due.DueDate.Value : today.AddDays(1));
        (weather.Forecast.Sum(w => w.ExpectedInclTax) + weather.Later).Should().Be(120m);
        weather.Forecast.Should().HaveCount(PaymentWeatherResponse.ForecastWeeks);
    }

    [Fact]
    public async Task Un_dossier_sans_piece_n_a_aucun_bulletin()
    {
        await using LettraApiFactory api = new();
        HttpClient client = api.ClientFor(LettraApiFactory.ManagerId);
        client.DefaultRequestHeaders.Add(LettraHeaders.Dossier, LettraApiFactory.DossierId.ToString());

        PaymentWeatherResponse weather = (await client.GetFromJsonAsync(Api("/api/v1/insights/payment-weather"), ContractsJsonContext.Default.PaymentWeatherResponse))!;

        weather.Customers.Should().BeEmpty();
        weather.Later.Should().Be(0m);
        weather.Forecast.Should().OnlyContain(w => w.ExpectedInclTax == 0m);
    }
}
