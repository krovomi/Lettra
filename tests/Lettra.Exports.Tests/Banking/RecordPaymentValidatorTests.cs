using Lettra.Contracts.Banking;
using Lettra.Domain.Banking;
using Lettra.Domain.SharedKernel;
using Lettra.Exports.Application.Banking;

namespace Lettra.Exports.Tests.Banking;

/// <summary>Le contrôle de la saisie d'un encaissement, avant toute lecture en base.</summary>
public sealed class RecordPaymentValidatorTests
{
    private static readonly DateOnly Today = new(2026, 10, 19);

    private static readonly RecordPaymentValidator Validator = new(new FixedClock(new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero)));

    private static RecordPaymentRequest Valid() => new()
    {
        CustomerId = Guid.CreateVersion7(),
        Date = Today,
        Amount = 120m,
        Method = PaymentMethodDto.Transfer,
        Allocations = [new PaymentAllocationRequest(Guid.CreateVersion7(), 120m)],
    };

    private static IEnumerable<string> Codes(RecordPaymentRequest? request) =>
        Validator.Validate(new RecordPaymentCommand(new DossierId(Guid.CreateVersion7()), request!)).Select(e => e.Code);

    [Fact]
    public void Une_saisie_complete_passe() => Codes(Valid()).Should().BeEmpty();

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(10.001)]
    public void Un_montant_nul_negatif_ou_sous_le_centime_est_refuse(double amount)
    {
        RecordPaymentRequest request = Valid();
        request.Amount = (decimal)amount;

        Codes(request).Should().Contain(PaymentErrors.AmountInvalidCode);
    }

    [Fact]
    public void Une_date_future_absente_ou_trop_ancienne_est_refusee()
    {
        foreach (DateOnly date in new[] { Today.AddDays(1), default, new DateOnly(1999, 12, 31) })
        {
            RecordPaymentRequest request = Valid();
            request.Date = date;
            Codes(request).Should().Contain(PaymentErrors.DateInvalidCode, $"la date {date} n'est pas recevable");
        }
    }

    [Fact]
    public void Un_mode_inconnu_et_une_reference_trop_longue_sont_refuses()
    {
        RecordPaymentRequest request = Valid();
        request.Method = (PaymentMethodDto)99;
        request.Reference = new string('x', Payment.MaxReferenceLength + 1);

        Codes(request).Should().Contain([PaymentErrors.MethodInvalidCode, PaymentErrors.ReferenceTooLongCode]);
    }

    [Fact]
    public void Une_saisie_absente_n_a_aucune_imputation() =>
        Codes(null).Should().Contain([PaymentErrors.AllocationsRequiredCode, PaymentErrors.AmountInvalidCode]);

    [Fact]
    public void Trop_d_imputations_sont_refusees()
    {
        RecordPaymentRequest request = Valid();
        request.Allocations = [.. Enumerable.Range(0, Payment.MaxAllocations + 1).Select(_ => new PaymentAllocationRequest(Guid.CreateVersion7(), 1m))];

        Codes(request).Should().Contain(PaymentErrors.TooManyAllocationsCode);
    }

    [Fact]
    public void Une_imputation_invalide_ou_en_double_est_refusee()
    {
        Guid invoice = Guid.CreateVersion7();
        RecordPaymentRequest request = Valid();
        request.Allocations =
        [
            new PaymentAllocationRequest(invoice, 60m),
            new PaymentAllocationRequest(invoice, 60m),
            new PaymentAllocationRequest(Guid.Empty, 1m),
            new PaymentAllocationRequest(Guid.CreateVersion7(), 0.005m),
            null!,
        ];

        Validator.Validate(new RecordPaymentCommand(new DossierId(Guid.CreateVersion7()), request))
            .Select(e => (e.Code, e.Target)).Should().Contain(
            [
                (PaymentErrors.AllocationDuplicateCode, "Allocations"),
                (PaymentErrors.AllocationInvalidCode, "Allocations[2]"),
                (PaymentErrors.AllocationInvalidCode, "Allocations[3]"),
                (PaymentErrors.AllocationInvalidCode, "Allocations[4]"),
            ]);
    }

    [Theory]
    [InlineData(0.01, true)]
    [InlineData(120, true)]
    [InlineData(0, false)]
    [InlineData(1.005, false)]
    public void Le_montant_recevable_est_positif_au_centime(double amount, bool valid) =>
        Payment.IsValidAmount((decimal)amount).Should().Be(valid);

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
