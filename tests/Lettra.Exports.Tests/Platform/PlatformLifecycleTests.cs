using Lettra.Contracts.Exports;
using Lettra.Exports.Application.EInvoicing.Platform;
using Lettra.Exports.Domain.Platform;
using static Lettra.Exports.Domain.Platform.PlatformStatus;

namespace Lettra.Exports.Tests.Platform;

public sealed class PlatformLifecycleTests
{
    [Theory]
    [InlineData(Deposited, Issued)]
    [InlineData(Deposited, MadeAvailable)]
    [InlineData(Deposited, Rejected)]
    [InlineData(Received, Rejected)]
    [InlineData(MadeAvailable, Approved)]
    [InlineData(MadeAvailable, Refused)]
    [InlineData(Suspended, Completed)]
    [InlineData(Completed, Approved)]
    [InlineData(Disputed, Refused)]
    [InlineData(Approved, PaymentSent)]
    [InlineData(Deposited, Collected)]
    [InlineData(PaymentSent, Collected)]
    [InlineData(Collected, Collected)]
    public void Transition_permise(PlatformStatus from, PlatformStatus to) =>
        PlatformLifecycle.CanMove(from, to).Should().BeTrue();

    [Theory]
    [InlineData(MadeAvailable, Issued)]
    [InlineData(MadeAvailable, Rejected)]
    [InlineData(Approved, Refused)]
    [InlineData(Deposited, Completed)]
    [InlineData(Refused, Collected)]
    [InlineData(Rejected, Deposited)]
    [InlineData(Rejected, Collected)]
    [InlineData(Collected, Approved)]
    [InlineData(Deposited, Deposited)]
    public void Transition_refusee(PlatformStatus from, PlatformStatus to) =>
        PlatformLifecycle.CanMove(from, to).Should().BeFalse();

    [Fact]
    public void Refusee_et_rejetee_sont_definitifs_et_rien_n_en_sort()
    {
        foreach (PlatformStatus to in Enum.GetValues<PlatformStatus>())
        {
            PlatformLifecycle.CanMove(Refused, to).Should().BeFalse();
            PlatformLifecycle.CanMove(Rejected, to).Should().BeFalse();
        }
    }

    [Fact]
    public void Chaque_statut_sauf_deposee_a_au_moins_une_origine()
    {
        foreach (PlatformStatus to in Enum.GetValues<PlatformStatus>().Where(s => s != Deposited))
        {
            Enum.GetValues<PlatformStatus>().Any(from => PlatformLifecycle.CanMove(from, to)).Should().BeTrue(to.ToString());
        }
    }

    [Fact]
    public void L_encaissement_n_est_definitif_que_pour_tout_le_TTC_et_les_doublons_sont_ignores()
    {
        DateTimeOffset at = new(2026, 10, 15, 9, 0, 0, TimeSpan.Zero);
        PlatformTransmission transmission = new(
            Guid.CreateVersion7(), Guid.CreateVersion7(), "FAC-000042", PlatformChannel.EInvoice, EInvoiceFormat.Cii,
            "PA simulée", "PAS-000001", 360m, at, [new PlatformStatusEvent(Deposited, at)]);

        transmission.TryApply(new PlatformStatusEvent(Deposited, at.AddTicks(1))).Should().BeFalse();
        transmission.TryApply(new PlatformStatusEvent(MadeAvailable, at.AddMinutes(1))).Should().BeTrue();
        transmission.TryApply(new PlatformStatusEvent(MadeAvailable, at.AddMinutes(1).AddTicks(5))).Should().BeFalse("même instant à la milliseconde");
        transmission.TryApply(new PlatformStatusEvent(Collected, at.AddDays(3), null, 160m)).Should().BeTrue();
        transmission.IsFinal.Should().BeFalse();
        transmission.RemainingToCollect.Should().Be(200m);
        transmission.TryApply(new PlatformStatusEvent(Collected, at.AddDays(9), null, 200m)).Should().BeTrue();

        transmission.IsFinal.Should().BeTrue();
        transmission.CollectedAmount.Should().Be(360m);
        transmission.TryApply(new PlatformStatusEvent(Disputed, at.AddDays(10))).Should().BeFalse();
        transmission.ToResponse().History.Select(h => h.Status).Should().Equal(PlatformStatusDto.Deposited, PlatformStatusDto.MadeAvailable, PlatformStatusDto.Collected, PlatformStatusDto.Collected);
    }
}
