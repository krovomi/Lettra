using Lettra.Contracts.Common;
using Lettra.Exports.Application.EInvoicing.Platform;
using Lettra.Exports.Domain.Platform;
using Lettra.Exports.Infrastructure;

namespace Lettra.Exports.Tests.Platform;

/// <summary>La PA simulée passe le contrat, plus ce qu'elle seule sait simuler : annuaire, panne, réponses de l'acheteur.</summary>
public sealed class SimulatedAccreditedPlatformTests : AccreditedPlatformContract
{
    private readonly SimulatedAccreditedPlatform _platform = new(TimeProvider.System);

    protected override IAccreditedPlatformClient Platform => _platform;

    [Fact]
    public async Task Une_facture_deposee_est_mise_a_disposition_a_la_premiere_lecture()
    {
        PlatformReceipt receipt = (await _platform.SubmitInvoiceAsync(PlatformSamples.Submission(PlatformSamples.NewNumber()), default)).Value;

        IReadOnlyList<PlatformStatusEvent> events = (await _platform.GetStatusEventsAsync(receipt.Reference, default)).Value;

        events.Select(e => e.Status).Should().Equal(
            PlatformStatus.Deposited, PlatformStatus.Issued, PlatformStatus.Received, PlatformStatus.MadeAvailable);
        (await _platform.GetStatusEventsAsync(receipt.Reference, default)).Value.Should().Equal(events);
    }

    [Fact]
    public async Task Un_destinataire_absent_de_l_annuaire_fait_rejeter_la_facture()
    {
        _platform.Unregister(PlatformSamples.BuyerSiren);

        PlatformReceipt receipt = (await _platform.SubmitInvoiceAsync(PlatformSamples.Submission(PlatformSamples.NewNumber()), default)).Value;

        receipt.Status.Status.Should().Be(PlatformStatus.Rejected);
        receipt.Status.Reason.Should().Contain("annuaire");
    }

    [Fact]
    public async Task Une_plateforme_injoignable_ne_recoit_rien()
    {
        _platform.IsUnavailable = true;

        (await _platform.SubmitInvoiceAsync(PlatformSamples.Submission(PlatformSamples.NewNumber()), default))
            .Errors.Select(e => e.Code).Should().Equal(ErrorCodes.Platform.Unavailable);

        _platform.IsUnavailable = false;
        (await _platform.GetStatusEventsAsync("PAS-000001", default)).Errors.Select(e => e.Code).Should().Equal(ErrorCodes.Platform.ReferenceUnknown);
    }

    [Fact]
    public async Task Les_reponses_de_l_acheteur_suivent_le_cycle_de_vie()
    {
        PlatformReceipt receipt = (await _platform.SubmitInvoiceAsync(PlatformSamples.Submission(PlatformSamples.NewNumber()), default)).Value;
        await _platform.GetStatusEventsAsync(receipt.Reference, default);

        _platform.SimulateBuyerStatus(receipt.Reference, PlatformStatus.Disputed, "Quantité contestée");
        _platform.SimulateBuyerStatus(receipt.Reference, PlatformStatus.Refused, "Prestation non commandée");

        FluentActions.Invoking(() => _platform.SimulateBuyerStatus(receipt.Reference, PlatformStatus.Approved))
            .Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => _platform.SimulateBuyerStatus(receipt.Reference, PlatformStatus.Collected))
            .Should().Throw<InvalidOperationException>();
        (await _platform.GetStatusEventsAsync(receipt.Reference, default)).Value[^1].Reason.Should().Be("Prestation non commandée");
    }
}
