using System.Text;
using Lettra.Contracts.Banking;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Common;
using Lettra.UI.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Api.Tests;

/// <summary>
/// Encaissements et rapprochement bancaire par la même interface, en HTTP et en direct : mêmes résultats, mêmes codes
/// d'erreur ; et la coupure réseau, qui devient <c>Api.Unreachable</c> côté HTTP.
/// </summary>
public sealed class BankingGatewayParityTests(LettraApiFactory factory) : IClassFixture<LettraApiFactory>, IDisposable
{
    private readonly GatewayHarness _harness = new(factory);

    public static TheoryData<string> Transports => GatewayHarness.Transports;

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    [Theory]
    [MemberData(nameof(Transports))]
    public async Task Encaisser_lire_lister_puis_contre_passer(string transport)
    {
        await using ServiceProvider services = _harness.Gateway(transport);
        IBankingGateway bank = services.GetRequiredService<IBankingGateway>();
        InvoiceResponse invoice = await GatewayHarness.ValidatedInvoiceAsync(services);

        OperationResult<IReadOnlyList<OpenInvoiceResponse>> open = await bank.GetOpenInvoicesAsync(invoice.CustomerId, default);
        OperationResult<IReadOnlyList<OpenInvoiceResponse>> allOpen = await bank.GetOpenInvoicesAsync(null, default);
        OperationResult<PaymentResponse> recorded = await bank.RecordPaymentAsync(
            new RecordPaymentRequest
            {
                CustomerId = invoice.CustomerId, Date = Today, Amount = 120m, Method = PaymentMethodDto.Transfer,
                Allocations = [new PaymentAllocationRequest(invoice.Id, 120m)],
            },
            default);

        open.Value!.Should().ContainSingle(o => o.InvoiceId == invoice.Id).Which.Outstanding.Should().Be(120m);
        allOpen.Value!.Should().Contain(o => o.InvoiceId == invoice.Id);
        recorded.IsSuccess.Should().BeTrue(GatewayHarness.Codes(recorded));
        recorded.Value!.Number.Should().StartWith("ENC-");
        (await bank.GetPaymentAsync(recorded.Value.Id, default)).Value!.Amount.Should().Be(120m);
        OperationResult<PagedResponse<PaymentSummary>> list = await bank.ListPaymentsAsync(
            new PaymentListQuery(Today.AddDays(-1), Today.AddDays(1), invoice.CustomerId, "  ", 1, 20), default);
        list.Value!.Items.Should().ContainSingle(p => p.Id == recorded.Value.Id);
        (await bank.ListPaymentsAsync(new PaymentListQuery(Search: recorded.Value.Number), default)).Value!.Items
            .Should().Contain(p => p.Id == recorded.Value.Id);

        OperationResult<PaymentResponse> noReason = await bank.ReversePaymentAsync(recorded.Value.Id, new ReversePaymentRequest(), default);
        OperationResult<PaymentResponse> reversed = await bank.ReversePaymentAsync(recorded.Value.Id, new ReversePaymentRequest { Reason = "Chèque rejeté" }, default);

        noReason.Errors.Select(e => e.Code).Should().Contain(ErrorCodes.Payment.ReasonRequired);
        reversed.Value!.Status.Should().Be(PaymentStatusDto.Reversed);
        (await bank.GetOpenInvoicesAsync(invoice.CustomerId, default)).Value!.Should().ContainSingle(o => o.InvoiceId == invoice.Id);
    }

    [Theory]
    [MemberData(nameof(Transports))]
    public async Task Un_encaissement_inconnu_ou_au_dela_du_seuil_est_refuse_de_meme(string transport)
    {
        await using ServiceProvider services = _harness.Gateway(transport);
        IBankingGateway bank = services.GetRequiredService<IBankingGateway>();
        InvoiceResponse invoice = await GatewayHarness.ValidatedInvoiceAsync(services);

        OperationResult<PaymentResponse> unknown = await bank.GetPaymentAsync(Guid.CreateVersion7(), default);
        OperationResult<PaymentResponse> gap = await bank.RecordPaymentAsync(
            new RecordPaymentRequest { CustomerId = invoice.CustomerId, Date = Today, Amount = 100m, Allocations = [new PaymentAllocationRequest(invoice.Id, 120m)] },
            default);
        OperationResult<PaymentResponse> reverseUnknown = await bank.ReversePaymentAsync(Guid.CreateVersion7(), new ReversePaymentRequest { Reason = "Erreur" }, default);

        unknown.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Payment.NotFound);
        gap.Errors.Select(e => e.Code).Should().Contain(ErrorCodes.Payment.GapAboveThreshold);
        reverseUnknown.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Payment.NotFound);
    }

    [Theory]
    [MemberData(nameof(Transports))]
    public async Task Importer_un_releve_rapprocher_ignorer_puis_suivre_l_etat(string transport)
    {
        await using ServiceProvider services = _harness.Gateway(transport);
        IBankingGateway bank = services.GetRequiredService<IBankingGateway>();
        InvoiceResponse invoice = await GatewayHarness.ValidatedInvoiceAsync(services);
        ImportBankStatementRequest file = new()
        {
            FileName = "releve.xml",
            ContentBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(BankingApiTests.Camt(Today, 120m, $"VIR SEPA MARTIN FACTURE {invoice.Number}"))),
        };

        OperationResult<BankImportResponse> imported = await bank.ImportStatementAsync(file, default);
        imported.IsSuccess.Should().BeTrue(GatewayHarness.Codes(imported));
        imported.Value!.Format.Should().Be(BankStatementFormatDto.Camt053);
        imported.Value.Imported.Should().Be(2);

        ReconciliationResponse reconciliation = (await bank.GetReconciliationAsync(default)).Value!;
        BankLineResponse line = reconciliation.Lines.Single(l => l.Label.Contains(invoice.Number!, StringComparison.Ordinal));
        line.Proposal!.Allocations.Should().ContainSingle(a => a.InvoiceId == invoice.Id && a.Amount == 120m);
        BankLineResponse fee = reconciliation.Lines.First(l => l.Amount == -8.50m);

        OperationResult<ConfirmMatchesResponse> confirmed = await bank.ConfirmMatchesAsync(
            new ConfirmMatchesRequest
            {
                Items = [new ConfirmMatchItem(line.Id, line.Proposal.CustomerId, [.. line.Proposal.Allocations.Select(a => new PaymentAllocationRequest(a.InvoiceId, a.Amount))])],
            },
            default);
        OperationResult<BankLineResponse> ignored = await bank.IgnoreLineAsync(fee.Id, ignored: true, default);
        OperationResult<BankLineResponse> restored = await bank.IgnoreLineAsync(fee.Id, ignored: false, default);
        OperationResult<BankImportResponse> again = await bank.ImportStatementAsync(file, default);
        OperationResult<BankReconciliationStatusResponse> status = await bank.GetStatusAsync(default);

        confirmed.Value!.Confirmed.Should().Be(1);
        ignored.Value!.Status.Should().Be(BankLineStatusDto.Ignored);
        restored.Value!.Status.Should().NotBe(BankLineStatusDto.Ignored);
        again.Value!.Imported.Should().Be(0);
        again.Value.Duplicates.Should().Be(2);
        status.Value!.LastImportAt.Should().NotBeNull();
        status.Value.LastMatchedAt.Should().NotBeNull();
    }

    [Theory]
    [MemberData(nameof(Transports))]
    public async Task Un_releve_illisible_ou_une_ligne_inconnue_est_refuse_de_meme(string transport)
    {
        await using ServiceProvider services = _harness.Gateway(transport);
        IBankingGateway bank = services.GetRequiredService<IBankingGateway>();

        OperationResult<BankImportResponse> garbage = await bank.ImportStatementAsync(
            new ImportBankStatementRequest { FileName = "releve.bin", ContentBase64 = Convert.ToBase64String("pas un relevé"u8.ToArray()) }, default);
        OperationResult<BankLineResponse> unknownLine = await bank.IgnoreLineAsync(Guid.CreateVersion7(), ignored: true, default);

        garbage.Errors.Select(e => e.Code).Should().ContainSingle().Which.Should().Be(ErrorCodes.BankStatement.CsvColumnsInvalid, "un fichier inconnu est lu comme un CSV");
        unknownLine.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.BankStatement.LineNotFound);
    }

    [Theory]
    [MemberData(nameof(Transports))]
    public async Task Le_seuil_d_ecart_se_lit_se_change_et_refuse_une_valeur_hors_bornes(string transport)
    {
        await using ServiceProvider services = _harness.Gateway(transport);
        IBankingGateway bank = services.GetRequiredService<IBankingGateway>();

        OperationResult<BankingSettingsResponse> before = await bank.GetSettingsAsync(default);
        OperationResult<BankingSettingsResponse> changed = await bank.UpdateSettingsAsync(new UpdateBankingSettingsRequest { WriteOffThreshold = 2.5m }, default);
        OperationResult<BankingSettingsResponse> read = await bank.GetSettingsAsync(default);
        OperationResult<BankingSettingsResponse> tooHigh = await bank.UpdateSettingsAsync(new UpdateBankingSettingsRequest { WriteOffThreshold = 1_000m }, default);
        (await bank.UpdateSettingsAsync(new UpdateBankingSettingsRequest { WriteOffThreshold = 1m }, default)).IsSuccess.Should().BeTrue();

        before.IsSuccess.Should().BeTrue();
        changed.Value!.WriteOffThreshold.Should().Be(2.5m);
        read.Value!.WriteOffThreshold.Should().Be(2.5m);
        tooHigh.Errors.Select(e => e.Code).Should().Contain(ErrorCodes.BankingSettings.ThresholdInvalid);
    }

    [Theory]
    [MemberData(nameof(Transports))]
    public async Task Le_releve_de_demonstration_n_est_ouvert_que_sur_le_poste(string transport)
    {
        await using ServiceProvider services = _harness.Gateway(transport);
        IBankingGateway bank = services.GetRequiredService<IBankingGateway>();
        await GatewayHarness.ValidatedInvoiceAsync(services);

        OperationResult<BankImportResponse> demo = await bank.ImportDemoStatementAsync(default);

        if (transport == "http")
        {
            // Écart voulu (InProcessBankingGateway) : sans Lettra:Demo:Seed, l'API répond 404 sans corps, que la gateway
            // HTTP traduit en Api.Unexpected ; le poste isolé, lui, importe le relevé fictif.
            demo.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Api.Unexpected);
        }
        else
        {
            demo.IsSuccess.Should().BeTrue(GatewayHarness.Codes(demo));
        }
    }

    [Fact]
    public async Task Serveur_injoignable_chaque_appel_rend_Api_Unreachable()
    {
        await using ServiceProvider services = GatewayHarness.Unreachable();
        IBankingGateway bank = services.GetRequiredService<IBankingGateway>();
        Guid id = Guid.CreateVersion7();

        List<IReadOnlyList<ErrorDetail>> failures =
        [
            (await bank.ListPaymentsAsync(new PaymentListQuery(Today, Today, id, "x"), default)).Errors,
            (await bank.GetPaymentAsync(id, default)).Errors,
            (await bank.GetOpenInvoicesAsync(id, default)).Errors,
            (await bank.GetOpenInvoicesAsync(null, default)).Errors,
            (await bank.RecordPaymentAsync(new RecordPaymentRequest(), default)).Errors,
            (await bank.ReversePaymentAsync(id, new ReversePaymentRequest(), default)).Errors,
            (await bank.ImportStatementAsync(new ImportBankStatementRequest(), default)).Errors,
            (await bank.ImportDemoStatementAsync(default)).Errors,
            (await bank.GetReconciliationAsync(default)).Errors,
            (await bank.ConfirmMatchesAsync(new ConfirmMatchesRequest(), default)).Errors,
            (await bank.IgnoreLineAsync(id, ignored: true, default)).Errors,
            (await bank.GetSettingsAsync(default)).Errors,
            (await bank.UpdateSettingsAsync(new UpdateBankingSettingsRequest(), default)).Errors,
            (await bank.GetStatusAsync(default)).Errors,
        ];

        failures.Should().AllSatisfy(errors => errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Api.Unreachable));
    }

    public void Dispose() => _harness.Dispose();
}
