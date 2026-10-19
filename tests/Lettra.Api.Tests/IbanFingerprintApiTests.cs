using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Lettra.Contracts.Banking;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Catalog;
using Lettra.Contracts.Common;
using Lettra.Contracts.Serialization;
using Lettra.Domain.Banking;
using Lettra.Domain.SharedKernel;
using Microsoft.Data.Sqlite;

namespace Lettra.Api.Tests;

/// <summary>
/// Empreintes des IBAN de payeur : calculées avec la clé du dossier, jamais le SHA-256 seul ; celles d'avant la clé
/// sont converties sans perdre la règle « montant et IBAN déjà connu ».
/// </summary>
public sealed class IbanFingerprintApiTests
{
    private const string PayerIban = "FR7630004000031234567890143";

    private static Uri Api(string path) => new(path, UriKind.Relative);

    private static DateOnly Today => LegalCalendar.DateOf(DateTimeOffset.UtcNow);

    [Fact]
    public async Task Les_empreintes_d_avant_la_cle_sont_converties_et_l_iban_connu_reste_reconnu()
    {
        await using LettraApiFactory api = new();
        HttpClient manager = api.ClientFor(LettraApiFactory.ManagerId);
        manager.DefaultRequestHeaders.Add(LettraHeaders.Dossier, LettraApiFactory.DossierId.ToString());

        // Un premier virement cite la facture : sa validation apprend l'IBAN du client.
        Guid first = await ExportApiTests.ValidatedInvoiceAsync(manager);
        InvoiceResponse invoice = (await manager.GetFromJsonAsync(Api($"/api/v1/invoices/{first}"), ContractsJsonContext.Default.InvoiceResponse))!;
        BankLineResponse cited = await ImportAsync(manager, $"VIR SEPA FACTURE {invoice.Number}", "Martin SARL", line => line.Label.Contains(invoice.Number!, StringComparison.Ordinal));
        using HttpResponseMessage confirmed = await manager.PostAsJsonAsync(
            Api("/api/v1/bank/reconciliation/confirm"),
            new ConfirmMatchesRequest
            {
                Items = [new ConfirmMatchItem(cited.Id, cited.Proposal!.CustomerId, [.. cited.Proposal.Allocations.Select(a => new PaymentAllocationRequest(a.InvoiceId, a.Amount))])],
            },
            ContractsJsonContext.Default.ConfirmMatchesRequest);
        (await confirmed.Content.ReadFromJsonAsync(ContractsJsonContext.Default.ConfirmMatchesResponse))!.Confirmed.Should().Be(1);

        string legacy = BankText.IbanDigest(PayerIban, LettraApiFactory.DossierId)!;
        (string Known, string Line) keyed = await ReadHashesAsync(api);
        keyed.Known.Should().StartWith(BankText.KeyedIbanPrefix).And.HaveLength(64).And.NotBe(legacy, "le SHA-256 seul se retrouve en essayant des IBAN");
        keyed.Line.Should().Be(keyed.Known);

        // Données d'avant la clé : le condensé seul, comme l'enregistrait la version précédente.
        await ExecuteAsync(api, $"UPDATE KnownCustomerIbans SET IbanHash = '{legacy}'; UPDATE BankStatementLines SET CounterpartyIbanHash = '{legacy}' WHERE CounterpartyIbanHash IS NOT NULL;");

        (await manager.GetAsync(Api("/api/v1/bank/reconciliation"))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadHashesAsync(api)).Should().Be(keyed, "la conversion redonne l'empreinte calculée avec la clé, sans connaître l'IBAN");

        // Un virement sans numéro ni nom reconnaissable, du même IBAN : la règle « montant et IBAN connu » s'applique.
        await SecondInvoiceAsync(manager, invoice.CustomerId);
        BankLineResponse anonymous = await ImportAsync(manager, "VIREMENT RECU", "PAYEUR", line => line.Label == "VIREMENT RECU");
        anonymous.Proposal.Should().NotBeNull();
        anonymous.Proposal!.Rule.Should().Be(MatchRuleDto.AmountAndIban);
        anonymous.Proposal.CustomerId.Should().Be(invoice.CustomerId);
    }

    private static async Task SecondInvoiceAsync(HttpClient client, Guid customerId)
    {
        using HttpResponseMessage draft = await client.PostAsJsonAsync(
            Api("/api/v1/invoices"),
            new SaveInvoiceRequest
            {
                CustomerId = customerId,
                Lines = [new DocumentLineRequest { Designation = "Conseil", Quantity = 1, UnitPriceExclTax = 100m, UnitCode = "HUR", Kind = CatalogItemKindDto.Service }],
            },
            ContractsJsonContext.Default.SaveInvoiceRequest);
        draft.StatusCode.Should().Be(HttpStatusCode.Created, await draft.Content.ReadAsStringAsync());
        Guid id = (await draft.Content.ReadFromJsonAsync(ContractsJsonContext.Default.InvoiceResponse))!.Id;
        using HttpRequestMessage validate = new(HttpMethod.Post, Api($"/api/v1/invoices/{id}/validate"));
        validate.Headers.IfMatch.Add(EntityTagHeaderValue.Any);
        (await client.SendAsync(validate)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static async Task<BankLineResponse> ImportAsync(HttpClient client, string label, string debtor, Func<BankLineResponse, bool> pick)
    {
        ImportBankStatementRequest file = new()
        {
            FileName = "releve.xml",
            ContentBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(BankingApiTests.Camt(Today, 120m, label, debtor))),
        };
        using HttpResponseMessage imported = await client.PostAsJsonAsync(Api("/api/v1/bank/statements"), file, ContractsJsonContext.Default.ImportBankStatementRequest);
        imported.StatusCode.Should().Be(HttpStatusCode.OK, await imported.Content.ReadAsStringAsync());
        ReconciliationResponse reconciliation = (await client.GetFromJsonAsync(Api("/api/v1/bank/reconciliation"), ContractsJsonContext.Default.ReconciliationResponse))!;
        return reconciliation.Lines.Single(l => pick(l) && l.Status == BankLineStatusDto.Pending);
    }

    private static async Task<(string Known, string Line)> ReadHashesAsync(LettraApiFactory api)
    {
        await using SqliteConnection connection = new($"Data Source={api.DossierDatabase};Pooling=False");
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT (SELECT group_concat(IbanHash) FROM KnownCustomerIbans), (SELECT group_concat(DISTINCT CounterpartyIbanHash) FROM BankStatementLines)";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        return (reader.GetString(0), reader.GetString(1));
    }

    private static async Task ExecuteAsync(LettraApiFactory api, string sql)
    {
        await using SqliteConnection connection = new($"Data Source={api.DossierDatabase};Pooling=False");
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
