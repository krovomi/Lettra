using Lettra.Contracts.Common;
using Lettra.Domain.Common;
using Lettra.Exports.Application.EInvoicing.Platform;
using Lettra.Exports.Application.EInvoicing.Reception;
using Lettra.Exports.Domain.Platform;
using Lettra.Exports.Domain.Reception;
using Lettra.Exports.Infrastructure;
using Lettra.Exports.Tests.Platform;

namespace Lettra.Exports.Tests.Reception;

/// <summary>
/// La facture reçue et ses réponses d'acheteur (XP Z12-012), puis la plateforme simulée côté réception : factures mises
/// à disposition, réponses idempotentes, référence inconnue, annuaire.
/// </summary>
public sealed class ReceivedInvoiceTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 20, 8, 0, 0, TimeSpan.Zero);

    private static ReceivedInvoice Received(ReceivedInvoiceKind kind = ReceivedInvoiceKind.Invoice) => new(
        Guid.CreateVersion7(), "Plateforme agréée simulée", "PAS-R-1", EInvoiceFormat.Cii, "PM-1.xml", "<x/>"u8.ToArray(),
        new ReceivedInvoiceHeader(kind, "PM-1", new DateOnly(2026, 10, 18), null, "Papeterie Moreau SARL", "842157307", null,
            PlatformSamples.BuyerSiren, "EUR", 100m, 20m, 120m, "B1"),
        null,
        [new PlatformStatusEvent(PlatformStatus.MadeAvailable, At)]);

    [Fact]
    public void Une_facture_mise_a_disposition_propose_toutes_les_reponses_de_l_acheteur()
    {
        ReceivedInvoice invoice = Received();

        invoice.Status.Should().Be(PlatformStatus.MadeAvailable);
        invoice.ReceivedAt.Should().Be(At);
        invoice.IsFinal.Should().BeFalse();
        invoice.AvailableResponses.Should().BeEquivalentTo(
            [PlatformStatus.InProgress, PlatformStatus.Approved, PlatformStatus.Disputed, PlatformStatus.Refused, PlatformStatus.PaymentSent]);
    }

    [Fact]
    public void Un_refus_est_definitif()
    {
        ReceivedInvoice invoice = Received();

        invoice.Apply(new PlatformStatusEvent(PlatformStatus.Refused, At.AddHours(1), "Jamais livrée"));

        invoice.IsFinal.Should().BeTrue();
        invoice.AvailableResponses.Should().BeEmpty();
        FluentActions.Invoking(() => invoice.Apply(new PlatformStatusEvent(PlatformStatus.Approved, At.AddHours(2))))
            .Should().Throw<InvalidOperationException>();
        invoice.History.Should().HaveCount(2);
    }

    [Fact]
    public void Un_avoir_ne_se_paie_pas_et_une_prise_en_charge_ne_se_repete_pas()
    {
        Received(ReceivedInvoiceKind.CreditNote).AvailableResponses.Should().NotContain(PlatformStatus.PaymentSent);

        ReceivedInvoice invoice = Received();
        invoice.Apply(new PlatformStatusEvent(PlatformStatus.InProgress, At.AddHours(1)));
        invoice.AvailableResponses.Should().NotContain(PlatformStatus.InProgress);
    }

    [Fact]
    public void Le_litige_et_le_refus_se_motivent()
    {
        ReceivedInvoice.RequiresReason(PlatformStatus.Disputed).Should().BeTrue();
        ReceivedInvoice.RequiresReason(PlatformStatus.Refused).Should().BeTrue();
        ReceivedInvoice.RequiresReason(PlatformStatus.Approved).Should().BeFalse();
    }

    [Fact]
    public void Une_facture_recue_porte_au_moins_sa_mise_a_disposition()
    {
        FluentActions.Invoking(() => new ReceivedInvoice(
                Guid.CreateVersion7(), "PA", "R", EInvoiceFormat.Ubl, "f.xml", Array.Empty<byte>(), Received().Header, null, []))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task La_plateforme_simulee_rend_les_factures_de_l_acheteur_et_enregistre_ses_reponses_une_fois()
    {
        SimulatedAccreditedPlatform platform = new(TimeProvider.System);

        IReadOnlyList<PlatformIncomingInvoice> incoming = (await platform.ListIncomingInvoicesAsync(PlatformSamples.BuyerSiren, default)).Value;
        incoming.Should().HaveCount(4);
        string reference = incoming[0].Reference;

        PlatformBuyerResponse response = new(PlatformSamples.BuyerSiren, reference, PlatformStatus.Disputed, "Quantité erronée", null, "cle-1");
        PlatformStatusEvent first = (await platform.RespondToIncomingInvoiceAsync(response, default)).Value;
        PlatformStatusEvent replayed = (await platform.RespondToIncomingInvoiceAsync(response, default)).Value;

        replayed.Should().Be(first);
        platform.IncomingHistory(reference).Select(e => e.Status).Should().Equal(PlatformStatus.MadeAvailable, PlatformStatus.Disputed);
        platform.IncomingHistory(reference)[^1].Reason.Should().Be("Quantité erronée");

        Result<PlatformStatusEvent> unknown = await platform.RespondToIncomingInvoiceAsync(response with { Reference = "PAS-R-INCONNUE", IdempotencyKey = "cle-2" }, default);
        unknown.Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Platform.ReferenceUnknown);
        Result<PlatformStatusEvent> otherBuyer = await platform.RespondToIncomingInvoiceAsync(response with { BuyerSiren = "552100554", IdempotencyKey = "cle-3" }, default);
        otherBuyer.IsFailure.Should().BeTrue("la facture n'est pas adressée à cet acheteur");
    }

    [Fact]
    public async Task L_annuaire_simule_connait_les_siren_valides_et_suit_les_pannes()
    {
        SimulatedAccreditedPlatform platform = new(TimeProvider.System);

        DirectoryEntry? entry = (await platform.LookupAsync(PlatformSamples.BuyerSiren, default)).Value;
        entry!.ElectronicAddress.Should().Be($"0225:{PlatformSamples.BuyerSiren}");
        entry.PlatformName.Should().Be(SimulatedAccreditedPlatform.PlatformName);
        entry.Since.Should().Be(new DateOnly(2026, 6, 1));
        (await platform.LookupAsync("12345678", default)).Value.Should().BeNull();

        platform.IsUnavailable = true;
        (await platform.LookupAsync(PlatformSamples.BuyerSiren, default)).Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Platform.Unavailable);
        (await platform.ListIncomingInvoicesAsync(PlatformSamples.BuyerSiren, default)).IsFailure.Should().BeTrue();
        (await platform.RespondToIncomingInvoiceAsync(new PlatformBuyerResponse(PlatformSamples.BuyerSiren, "R", PlatformStatus.Approved, null, null, null), default))
            .IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Sans_plateforme_ni_reception_ni_annuaire()
    {
        UnconfiguredPlatformClient none = new();

        (await none.ListIncomingInvoicesAsync(PlatformSamples.BuyerSiren, default)).Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Platform.NotConfigured);
        (await none.RespondToIncomingInvoiceAsync(new PlatformBuyerResponse(PlatformSamples.BuyerSiren, "R", PlatformStatus.Approved, null, null, null), default))
            .Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Platform.NotConfigured);
        (await new UnconfiguredDirectory().LookupAsync(PlatformSamples.BuyerSiren, default)).Errors.Should().ContainSingle().Which.Code.Should().Be(ErrorCodes.Platform.NotConfigured);
    }
}
