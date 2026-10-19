using Lettra.Domain.Banking;
using Lettra.Domain.Billing;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Domain.Tests.Banking;

/// <summary>L'encaissement : contrôles à l'enregistrement, contre-passation avec motif, empreinte stable.</summary>
public sealed class PaymentTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 9, 0, 0, TimeSpan.Zero);
    private static readonly InvoiceId Invoice = new(Guid.CreateVersion7());

    private static Result<Payment> Record(decimal amount, decimal allocated, decimal threshold = 1m) =>
        Payment.Record(PaymentId.New(), "ENC-00001", new CustomerId(Guid.CreateVersion7()), "Dupont SARL", new DateOnly(2026, 10, 10), amount,
            PaymentMethod.Transfer, "  VIR 42  ", [new PaymentAllocation(Invoice, "F1", allocated)], [], threshold, null, Guid.Empty, Now);

    [Fact]
    public void Un_encaissement_valide_garde_son_ecart_et_sa_reference_nettoyee()
    {
        Payment payment = Record(99.60m, 100m).Value;

        payment.WriteOff.Should().Be(0.40m);
        payment.Reference.Should().Be("VIR 42");
        payment.Status.Should().Be(PaymentStatus.Posted);
    }

    [Fact]
    public void Un_ecart_au_dela_du_seuil_ou_un_montant_invalide_est_refuse()
    {
        Record(90m, 100m).Errors.Select(e => e.Code).Should().Contain(PaymentErrors.GapAboveThresholdCode);
        Record(10.001m, 10m).Errors.Select(e => e.Code).Should().Contain(PaymentErrors.AmountInvalidCode);
    }

    [Fact]
    public void La_contre_passation_exige_un_motif_et_ne_se_fait_qu_une_fois()
    {
        Payment payment = Record(100m, 100m).Value;
        string before = payment.ComputeFingerprint();

        payment.Reverse(new DateOnly(2026, 10, 12), " ", Guid.Empty, Now).Errors.Single().Code.Should().Be(PaymentErrors.ReasonRequiredCode);
        payment.Reverse(new DateOnly(2026, 10, 1), "Chèque impayé", Guid.Empty, Now).IsSuccess.Should().BeTrue();

        payment.ReversalDate.Should().Be(new DateOnly(2026, 10, 10), "jamais avant l'encaissement");
        payment.ComputeFingerprint().Should().NotBe(before);
        payment.Reverse(new DateOnly(2026, 10, 12), "encore", Guid.Empty, Now).Errors.Single().Code.Should().Be(PaymentErrors.AlreadyReversedCode);
    }
}
