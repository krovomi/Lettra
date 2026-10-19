using System.Net;
using System.Net.Http.Json;
using System.Text;
using Lettra.Contracts.Accounting;
using Lettra.Contracts.Banking;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Common;
using Lettra.Contracts.Integrity;
using Lettra.Contracts.Serialization;

namespace Lettra.Api.Tests;

/// <summary>
/// Encaissements, rapprochement bancaire et lettrage par l'API réelle (mode 2, SQLite) : saisie, écriture BQ, TVA sur
/// encaissements, lettrage, contre-passation, import d'un relevé CAMT.053, proposition et validation, doublons, FEC et
/// chaîne d'intégrité.
/// </summary>
public sealed class BankingApiTests(LettraApiFactory factory) : IClassFixture<LettraApiFactory>
{
    private static Uri Api(string path) => new(path, UriKind.Relative);

    private static HttpClient InDossier(HttpClient client)
    {
        client.DefaultRequestHeaders.Add(LettraHeaders.Dossier, LettraApiFactory.DossierId.ToString());
        return client;
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private static async Task<InvoiceResponse> InvoiceAsync(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync(Api($"/api/v1/invoices/{id}"), ContractsJsonContext.Default.InvoiceResponse))!;

    private static async Task<HttpResponseMessage> RecordAsync(HttpClient client, Guid customerId, decimal amount, params PaymentAllocationRequest[] allocations) =>
        await client.PostAsJsonAsync(
            Api("/api/v1/payments"),
            new RecordPaymentRequest { CustomerId = customerId, Date = Today, Amount = amount, Method = PaymentMethodDto.Transfer, Allocations = allocations },
            ContractsJsonContext.Default.RecordPaymentRequest);

    [Fact]
    public async Task Un_encaissement_total_passe_l_ecriture_BQ_rend_la_TVA_exigible_et_lettre_la_facture()
    {
        using HttpClient manager = InDossier(factory.CreateClient());
        Guid invoiceId = await ExportApiTests.ValidatedInvoiceAsync(manager);
        InvoiceResponse invoice = await InvoiceAsync(manager, invoiceId);

        using HttpResponseMessage recorded = await RecordAsync(manager, invoice.CustomerId, 120m, new PaymentAllocationRequest(invoiceId, 120m));

        recorded.StatusCode.Should().Be(HttpStatusCode.Created, await recorded.Content.ReadAsStringAsync());
        PaymentResponse payment = (await recorded.Content.ReadFromJsonAsync(ContractsJsonContext.Default.PaymentResponse))!;
        payment.Number.Should().StartWith("ENC-");
        payment.LetteringCode.Should().Be("A");
        payment.EntryLines.Should().ContainEquivalentOf(new JournalLineResponse("512", 120m, 0m, null));
        payment.EntryLines.Should().ContainEquivalentOf(new JournalLineResponse("411", 0m, 120m, null));
        payment.EntryLines.Should().ContainEquivalentOf(new JournalLineResponse("44574", 20m, 0m, 20m));
        payment.EntryLines.Should().ContainEquivalentOf(new JournalLineResponse("44571", 0m, 20m, 20m));
        (await InvoiceAsync(manager, invoiceId)).PaidInclTax.Should().Be(120m);

        string year = $"from={Today.Year}-01-01&to={Today.Year}-12-31";
        SalesJournalResponse journal = (await manager.GetFromJsonAsync(
            Api($"/api/v1/accounting/journal?{year}&customerId={invoice.CustomerId}"), ContractsJsonContext.Default.SalesJournalResponse))!;
        journal.Items.Should().Contain(e => e.JournalCode == "BQ" && e.PieceNumber == payment.Number && e.LetteringCode == "A");
        journal.Items.Should().Contain(e => e.JournalCode == "VE" && e.InvoiceId == invoiceId && e.LetteringCode == "A"
            && e.Lines.Any(l => l.Account == "44574" && l.Credit == 20m));

        string fec = Encoding.Latin1.GetString(await manager.GetByteArrayAsync(Api($"/api/v1/exports/fec?{year}")));
        fec.Should().Contain("BQ\tJournal de banque\t");
        fec.Should().Contain($"\t{payment.Number}\t");

        HttpResponseMessage verified = await manager.PostAsync(Api("/api/v1/integrity/verification"), null);
        (await verified.Content.ReadFromJsonAsync(ContractsJsonContext.Default.IntegrityReportResponse))!.Intact.Should().BeTrue();
    }

    [Fact]
    public async Task Un_encaissement_contre_passe_rend_la_facture_due_et_garde_la_trace_du_lettrage()
    {
        using HttpClient manager = InDossier(factory.CreateClient());
        Guid invoiceId = await ExportApiTests.ValidatedInvoiceAsync(manager);
        InvoiceResponse invoice = await InvoiceAsync(manager, invoiceId);
        using HttpResponseMessage recorded = await RecordAsync(manager, invoice.CustomerId, 120m, new PaymentAllocationRequest(invoiceId, 120m));
        PaymentResponse payment = (await recorded.Content.ReadFromJsonAsync(ContractsJsonContext.Default.PaymentResponse))!;

        using HttpResponseMessage noReason = await manager.PostAsJsonAsync(
            Api($"/api/v1/payments/{payment.Id}/reversal"), new ReversePaymentRequest(), ContractsJsonContext.Default.ReversePaymentRequest);
        noReason.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await noReason.Content.ReadAsStringAsync()).Should().Contain(ErrorCodes.Payment.ReasonRequired);

        using HttpResponseMessage reversed = await manager.PostAsJsonAsync(
            Api($"/api/v1/payments/{payment.Id}/reversal"), new ReversePaymentRequest { Reason = "Chèque rejeté" }, ContractsJsonContext.Default.ReversePaymentRequest);

        reversed.StatusCode.Should().Be(HttpStatusCode.OK, await reversed.Content.ReadAsStringAsync());
        PaymentResponse after = (await reversed.Content.ReadFromJsonAsync(ContractsJsonContext.Default.PaymentResponse))!;
        after.Status.Should().Be(PaymentStatusDto.Reversed);
        after.ReversalLines.Should().ContainEquivalentOf(new JournalLineResponse("512", 0m, 120m, null));
        after.LetteringCode.Should().Be("B", "l'encaissement se lettre avec sa contre-passation, le code A reste en trace");
        (await InvoiceAsync(manager, invoiceId)).PaidInclTax.Should().Be(0m);

        IReadOnlyList<OpenInvoiceResponse> open = (await manager.GetFromJsonAsync(
            Api($"/api/v1/payments/open-invoices?customerId={invoice.CustomerId}"), ContractsJsonContext.Default.IReadOnlyListOpenInvoiceResponse))!;
        open.Should().ContainSingle(o => o.InvoiceId == invoiceId).Which.Outstanding.Should().Be(120m);

        HttpResponseMessage verified = await manager.PostAsync(Api("/api/v1/integrity/verification"), null);
        (await verified.Content.ReadFromJsonAsync(ContractsJsonContext.Default.IntegrityReportResponse))!.Intact.Should().BeTrue();
    }

    [Fact]
    public async Task Un_ecart_au_dela_du_seuil_ou_la_facture_d_un_autre_client_est_refuse()
    {
        using HttpClient manager = InDossier(factory.CreateClient());
        Guid invoiceId = await ExportApiTests.ValidatedInvoiceAsync(manager);
        Guid otherId = await ExportApiTests.ValidatedInvoiceAsync(manager);
        InvoiceResponse invoice = await InvoiceAsync(manager, invoiceId);

        using HttpResponseMessage gap = await RecordAsync(manager, invoice.CustomerId, 100m, new PaymentAllocationRequest(invoiceId, 120m));
        gap.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await gap.Content.ReadAsStringAsync()).Should().Contain(ErrorCodes.Payment.GapAboveThreshold);

        using HttpResponseMessage other = await RecordAsync(manager, invoice.CustomerId, 120m, new PaymentAllocationRequest(otherId, 120m));
        other.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await other.Content.ReadAsStringAsync()).Should().Contain(ErrorCodes.Payment.OtherCustomerInvoice);

        // Un règlement amputé de 0,40 € (sous le seuil de 1 €) solde la facture : l'écart va en 658.
        using HttpResponseMessage shortPaid = await RecordAsync(manager, invoice.CustomerId, 119.60m, new PaymentAllocationRequest(invoiceId, 120m));
        shortPaid.StatusCode.Should().Be(HttpStatusCode.Created, await shortPaid.Content.ReadAsStringAsync());
        PaymentResponse payment = (await shortPaid.Content.ReadFromJsonAsync(ContractsJsonContext.Default.PaymentResponse))!;
        payment.WriteOff.Should().Be(0.40m);
        payment.EntryLines.Should().ContainEquivalentOf(new JournalLineResponse("658", 0.40m, 0m, null));
        payment.LetteringCode.Should().NotBeNull();
    }

    [Fact]
    public async Task Un_releve_CAMT_053_propose_la_facture_citee_et_la_validation_l_encaisse_sans_doublon_a_la_reimportation()
    {
        using HttpClient manager = InDossier(factory.CreateClient());
        Guid invoiceId = await ExportApiTests.ValidatedInvoiceAsync(manager);
        InvoiceResponse invoice = await InvoiceAsync(manager, invoiceId);
        ImportBankStatementRequest file = new()
        {
            FileName = "releve.xml",
            ContentBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(Camt(Today, 120m, $"VIR SEPA MARTIN FACTURE {invoice.Number}"))),
        };

        using HttpResponseMessage imported = await manager.PostAsJsonAsync(Api("/api/v1/bank/statements"), file, ContractsJsonContext.Default.ImportBankStatementRequest);
        imported.StatusCode.Should().Be(HttpStatusCode.OK, await imported.Content.ReadAsStringAsync());
        BankImportResponse import = (await imported.Content.ReadFromJsonAsync(ContractsJsonContext.Default.BankImportResponse))!;
        import.Format.Should().Be(BankStatementFormatDto.Camt053);
        import.Imported.Should().Be(2);
        import.AccountDisplay.Should().Be("FR76 •••• 0129");

        ReconciliationResponse reconciliation = (await manager.GetFromJsonAsync(Api("/api/v1/bank/reconciliation"), ContractsJsonContext.Default.ReconciliationResponse))!;
        BankLineResponse line = reconciliation.Lines.Single(l => l.Label.Contains(invoice.Number!, StringComparison.Ordinal));
        line.Proposal.Should().NotBeNull();
        line.Proposal!.Rule.Should().Be(MatchRuleDto.InvoiceNumber);
        line.Proposal.Allocations.Should().ContainSingle(a => a.InvoiceId == invoiceId && a.Amount == 120m);
        reconciliation.Lines.Should().Contain(l => l.Amount == -8.50m && l.Proposal == null);

        using HttpResponseMessage confirmed = await manager.PostAsJsonAsync(
            Api("/api/v1/bank/reconciliation/confirm"),
            new ConfirmMatchesRequest
            {
                Items = [new ConfirmMatchItem(line.Id, line.Proposal.CustomerId, [.. line.Proposal.Allocations.Select(a => new PaymentAllocationRequest(a.InvoiceId, a.Amount))])],
            },
            ContractsJsonContext.Default.ConfirmMatchesRequest);
        ConfirmMatchesResponse outcome = (await confirmed.Content.ReadFromJsonAsync(ContractsJsonContext.Default.ConfirmMatchesResponse))!;
        outcome.Confirmed.Should().Be(1, string.Join(", ", outcome.Outcomes.SelectMany(o => o.Errors).Select(e => e.Code)));
        (await InvoiceAsync(manager, invoiceId)).PaidInclTax.Should().Be(120m);

        BankLineResponse fee = reconciliation.Lines.Single(l => l.Amount == -8.50m);
        using HttpResponseMessage ignored = await manager.PostAsJsonAsync(
            Api($"/api/v1/bank/lines/{fee.Id}/ignore"), new IgnoreBankLineRequest { Ignored = true }, ContractsJsonContext.Default.IgnoreBankLineRequest);
        (await ignored.Content.ReadFromJsonAsync(ContractsJsonContext.Default.BankLineResponse))!.Status.Should().Be(BankLineStatusDto.Ignored);

        using HttpResponseMessage again = await manager.PostAsJsonAsync(Api("/api/v1/bank/statements"), file, ContractsJsonContext.Default.ImportBankStatementRequest);
        BankImportResponse second = (await again.Content.ReadFromJsonAsync(ContractsJsonContext.Default.BankImportResponse))!;
        second.Imported.Should().Be(0);
        second.Duplicates.Should().Be(2);

        BankReconciliationStatusResponse status = (await manager.GetFromJsonAsync(Api("/api/v1/bank/status"), ContractsJsonContext.Default.BankReconciliationStatusResponse))!;
        status.LastImportAt.Should().NotBeNull();
        status.LastMatchedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Le_releve_de_demonstration_n_existe_que_dans_la_demonstration()
    {
        using HttpClient manager = InDossier(factory.CreateClient());

        using HttpResponseMessage demo = await manager.PostAsync(Api("/api/v1/bank/demo-statement"), null);

        demo.StatusCode.Should().Be(HttpStatusCode.NotFound, "Lettra:Demo:Seed n'est pas activé");
    }

    [Fact]
    public async Task Le_seuil_d_ecart_se_regle_et_un_collaborateur_ne_peut_pas_encaisser()
    {
        using HttpClient manager = InDossier(factory.CreateClient());
        using HttpResponseMessage updated = await manager.PutAsJsonAsync(
            Api("/api/v1/bank/settings"), new UpdateBankingSettingsRequest { WriteOffThreshold = 2.5m }, ContractsJsonContext.Default.UpdateBankingSettingsRequest);
        updated.StatusCode.Should().Be(HttpStatusCode.OK, await updated.Content.ReadAsStringAsync());
        (await manager.GetFromJsonAsync(Api("/api/v1/bank/settings"), ContractsJsonContext.Default.BankingSettingsResponse))!.WriteOffThreshold.Should().Be(2.5m);
        using HttpResponseMessage reset = await manager.PutAsJsonAsync(
            Api("/api/v1/bank/settings"), new UpdateBankingSettingsRequest { WriteOffThreshold = 1m }, ContractsJsonContext.Default.UpdateBankingSettingsRequest);
        reset.EnsureSuccessStatusCode();

        using HttpClient collaborator = InDossier(factory.ClientFor(LettraApiFactory.CollaborateurId));
        (await collaborator.GetAsync(Api("/api/v1/payments"))).StatusCode.Should().Be(HttpStatusCode.OK);
        using HttpResponseMessage refused = await RecordAsync(collaborator, Guid.CreateVersion7(), 10m, new PaymentAllocationRequest(Guid.CreateVersion7(), 10m));
        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    internal static string Camt(DateOnly day, decimal amount, string label) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <Document xmlns="urn:iso:std:iso:20022:tech:xsd:camt.053.001.08">
          <BkToCstmrStmt>
            <GrpHdr><MsgId>TEST-{Guid.NewGuid():N}</MsgId><CreDtTm>{day:yyyy-MM-dd}T08:00:00</CreDtTm></GrpHdr>
            <Stmt>
              <Id>TEST</Id>
              <Acct><Id><IBAN>FR7610107001011234567890129</IBAN></Id></Acct>
              <Ntry>
                <Amt Ccy="EUR">{amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}</Amt>
                <CdtDbtInd>CRDT</CdtDbtInd>
                <Sts><Cd>BOOK</Cd></Sts>
                <BookgDt><Dt>{day:yyyy-MM-dd}</Dt></BookgDt>
                <AcctSvcrRef>REF-{label.GetHashCode(StringComparison.Ordinal):X}</AcctSvcrRef>
                <NtryDtls><TxDtls>
                  <RltdPties><Dbtr><Pty><Nm>Martin SARL</Nm></Pty></Dbtr><DbtrAcct><Id><IBAN>FR7630004000031234567890143</IBAN></Id></DbtrAcct></RltdPties>
                  <RmtInf><Ustrd>{label}</Ustrd></RmtInf>
                </TxDtls></NtryDtls>
              </Ntry>
              <Ntry>
                <Amt Ccy="EUR">8.50</Amt>
                <CdtDbtInd>DBIT</CdtDbtInd>
                <Sts><Cd>BOOK</Cd></Sts>
                <BookgDt><Dt>{day:yyyy-MM-dd}</Dt></BookgDt>
                <AddtlNtryInf>COMMISSION {label.Length}</AddtlNtryInf>
              </Ntry>
            </Stmt>
          </BkToCstmrStmt>
        </Document>
        """;
}
