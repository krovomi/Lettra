using System.Net;
using System.Net.Http.Json;
using Lettra.Contracts.Accounting;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Common;
using Lettra.Contracts.Serialization;

namespace Lettra.Api.Tests;

/// <summary>Consultation de la comptabilité par l'API réelle (mode 2, SQLite) : journal, balance, grand livre, refus.</summary>
public sealed class AccountingApiTests(LettraApiFactory factory) : IClassFixture<LettraApiFactory>
{
    private static Uri Api(string path) => new(path, UriKind.Relative);

    private static HttpClient InDossier(HttpClient client)
    {
        client.DefaultRequestHeaders.Add(LettraHeaders.Dossier, LettraApiFactory.DossierId.ToString());
        return client;
    }

    private static string Year => $"from={DateTime.UtcNow.Year}-01-01&to={DateTime.UtcNow.Year}-12-31";

    [Fact]
    public async Task Une_facture_validee_se_lit_au_journal_a_la_balance_et_au_grand_livre_de_son_client()
    {
        using HttpClient manager = InDossier(factory.CreateClient());
        Guid invoiceId = await ExportApiTests.ValidatedInvoiceAsync(manager);
        InvoiceResponse invoice = (await manager.GetFromJsonAsync(Api($"/api/v1/invoices/{invoiceId}"), ContractsJsonContext.Default.InvoiceResponse))!;

        SalesJournalResponse journal = (await manager.GetFromJsonAsync(
            Api($"/api/v1/accounting/journal?{Year}&search={invoice.Number}"), ContractsJsonContext.Default.SalesJournalResponse))!;
        JournalEntryResponse entry = journal.Items.Should().ContainSingle().Subject;
        entry.InvoiceId.Should().Be(invoiceId);
        entry.Amount.Should().Be(120m);
        entry.Lines.Select(l => l.Account).Should().Contain(["411", "706", "44571"]);
        journal.TotalDebit.Should().Be(journal.TotalCredit);

        TrialBalanceResponse balance = (await manager.GetFromJsonAsync(Api($"/api/v1/accounting/balance?{Year}"), ContractsJsonContext.Default.TrialBalanceResponse))!;
        balance.TotalDebit.Should().Be(balance.TotalCredit);
        balance.Customers.Should().Contain(c => c.CustomerId == invoice.CustomerId);

        AccountLedgerResponse ledger = (await manager.GetFromJsonAsync(
            Api($"/api/v1/accounting/accounts/411/ledger?{Year}&customerId={invoice.CustomerId}"), ContractsJsonContext.Default.AccountLedgerResponse))!;
        ledger.Lines.Should().Contain(l => l.InvoiceId == invoiceId && l.Debit == 120m);
        ledger.ClosingBalance.Should().Be(ledger.OpeningBalance + ledger.TotalDebit - ledger.TotalCredit);
    }

    [Fact]
    public async Task Un_compte_ou_une_periode_invalide_est_refuse()
    {
        using HttpClient manager = InDossier(factory.CreateClient());

        HttpResponseMessage account = await manager.GetAsync(Api($"/api/v1/accounting/accounts/9A1/ledger?{Year}"));
        account.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await account.Content.ReadAsStringAsync()).Should().Contain(ErrorCodes.Accounting.AccountInvalid);

        HttpResponseMessage period = await manager.GetAsync(Api("/api/v1/accounting/balance?from=2026-12-31&to=2026-01-01"));
        period.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await period.Content.ReadAsStringAsync()).Should().Contain(ErrorCodes.Accounting.PeriodInvalid);
    }

    [Fact]
    public async Task Un_collaborateur_du_dossier_peut_consulter()
    {
        using HttpClient collaborator = InDossier(factory.ClientFor(LettraApiFactory.CollaborateurId));

        (await collaborator.GetAsync(Api($"/api/v1/accounting/journal?{Year}"))).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Sans_dossier_la_consultation_est_refusee()
    {
        using HttpClient anonymous = factory.CreateClient();

        (await anonymous.GetAsync(Api($"/api/v1/accounting/journal?{Year}"))).IsSuccessStatusCode.Should().BeFalse();
    }
}
