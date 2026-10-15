using Lettra.Contracts.Common;
using Lettra.Contracts.Exports;
using Lettra.Domain.Common;
using Lettra.Exports.Application.EInvoicing.Platform;

namespace Lettra.Exports.Tests.Platform;

/// <summary>
/// Les tests de contrat du port <see cref="IAccreditedPlatformClient"/> : tout adapter les passe, la PA simulée comme la
/// PA partenaire (5.5, sur son bac à sable). Une classe dérivée fournit l'adapter et des SIREN connus de son annuaire.
/// </summary>
public abstract class AccreditedPlatformContract
{
    protected abstract IAccreditedPlatformClient Platform { get; }

    [Theory]
    [InlineData(EInvoiceFormatDto.Cii)]
    [InlineData(EInvoiceFormatDto.Ubl)]
    public async Task Une_facture_conforme_est_deposee_avec_une_reference(EInvoiceFormatDto format)
    {
        Result<PlatformReceipt> receipt = await Platform.SubmitInvoiceAsync(PlatformSamples.Submission(PlatformSamples.NewNumber(), format), default);

        receipt.IsSuccess.Should().BeTrue();
        receipt.Value.Reference.Should().NotBeNullOrWhiteSpace();
        receipt.Value.Status.Status.Should().Be(PlatformStatusDto.Deposited);
    }

    [Fact]
    public async Task Les_statuts_d_un_depot_commencent_par_deposee_et_suivent_le_cycle_de_vie()
    {
        PlatformReceipt receipt = (await Platform.SubmitInvoiceAsync(PlatformSamples.Submission(PlatformSamples.NewNumber()), default)).Value;

        Result<IReadOnlyList<PlatformStatusEvent>> events = await Platform.GetStatusEventsAsync(receipt.Reference, default);

        events.IsSuccess.Should().BeTrue();
        events.Value[0].Status.Should().Be(PlatformStatusDto.Deposited);
        for (int i = 1; i < events.Value.Count; i++)
        {
            PlatformLifecycle.CanMove(events.Value[i - 1].Status, events.Value[i].Status).Should().BeTrue(
                $"{events.Value[i - 1].Status} → {events.Value[i].Status}");
            events.Value[i].At.Should().BeOnOrAfter(events.Value[i - 1].At);
        }
    }

    [Fact]
    public async Task Un_doublon_est_rejete_avec_son_motif()
    {
        string number = PlatformSamples.NewNumber();
        (await Platform.SubmitInvoiceAsync(PlatformSamples.Submission(number), default)).Value.Status.Status.Should().Be(PlatformStatusDto.Deposited);

        PlatformReceipt second = (await Platform.SubmitInvoiceAsync(PlatformSamples.Submission(number, EInvoiceFormatDto.Ubl), default)).Value;

        second.Status.Status.Should().Be(PlatformStatusDto.Rejected);
        second.Status.Reason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Un_fichier_qui_n_est_pas_une_facture_est_rejete()
    {
        PlatformInvoiceSubmission submission = PlatformSamples.Submission(PlatformSamples.NewNumber()) with { Content = "<pas-une-facture/>"u8.ToArray() };

        PlatformReceipt receipt = (await Platform.SubmitInvoiceAsync(submission, default)).Value;

        receipt.Status.Status.Should().Be(PlatformStatusDto.Rejected);
    }

    [Fact]
    public async Task Un_fichier_dans_une_autre_syntaxe_que_celle_annoncee_est_rejete()
    {
        string number = PlatformSamples.NewNumber();
        PlatformInvoiceSubmission submission = PlatformSamples.Submission(number, EInvoiceFormatDto.Ubl) with { Format = EInvoiceFormatDto.Cii };

        (await Platform.SubmitInvoiceAsync(submission, default)).Value.Status.Status.Should().Be(PlatformStatusDto.Rejected);
    }

    [Fact]
    public async Task Le_numero_du_fichier_doit_etre_celui_declare()
    {
        PlatformInvoiceSubmission submission = PlatformSamples.Submission(PlatformSamples.NewNumber()) with { InvoiceNumber = PlatformSamples.NewNumber() };

        (await Platform.SubmitInvoiceAsync(submission, default)).Value.Status.Status.Should().Be(PlatformStatusDto.Rejected);
    }

    [Fact]
    public async Task Une_transaction_est_declaree_en_e_reporting()
    {
        Result<PlatformReceipt> receipt = await Platform.ReportTransactionAsync(PlatformSamples.Transaction(PlatformSamples.NewNumber()), default);

        receipt.IsSuccess.Should().BeTrue();
        receipt.Value.Status.Status.Should().Be(PlatformStatusDto.Deposited);
    }

    [Fact]
    public async Task Un_encaissement_ajoute_le_statut_encaissee_avec_son_montant()
    {
        string number = PlatformSamples.NewNumber();
        PlatformReceipt receipt = (await Platform.SubmitInvoiceAsync(PlatformSamples.Submission(number), default)).Value;

        Result<PlatformStatusEvent> collected = await Platform.ReportPaymentAsync(
            new PlatformPaymentReport(PlatformSamples.SellerSiren, number, receipt.Reference, PlatformChannelDto.EInvoice, new DateOnly(2026, 11, 2), 360m), default);

        collected.IsSuccess.Should().BeTrue();
        collected.Value.Status.Should().Be(PlatformStatusDto.Collected);
        collected.Value.Amount.Should().Be(360m);
        (await Platform.GetStatusEventsAsync(receipt.Reference, default)).Value[^1].Should().Be(collected.Value);
    }

    [Fact]
    public async Task Une_reference_inconnue_est_signalee()
    {
        Result<IReadOnlyList<PlatformStatusEvent>> events = await Platform.GetStatusEventsAsync("REFERENCE-INCONNUE", default);

        events.Errors.Select(e => e.Code).Should().Equal(ErrorCodes.Platform.ReferenceUnknown);
    }
}
