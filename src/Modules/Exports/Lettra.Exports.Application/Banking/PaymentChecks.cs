using Lettra.Application.Abstractions.Messaging;
using Lettra.Calculations;
using Lettra.Contracts.Banking;
using Lettra.Contracts.Exports;
using Lettra.Domain.Banking;
using Lettra.Domain.Billing;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;
using Lettra.Exports.Application.EInvoicing.Platform;
using Microsoft.Extensions.Logging;

namespace Lettra.Exports.Application.Banking;

/// <summary>
/// Contrôle de la saisie d'un encaissement, avant toute lecture en base : montant au centime, date passée, mode,
/// référence, imputations (au moins une, sans doublon). Les invariants restent dans <see cref="Payment.Record"/>.
/// </summary>
internal sealed class RecordPaymentValidator(TimeProvider clock) : IValidator<RecordPaymentCommand>
{
    public IReadOnlyList<Error> Validate(RecordPaymentCommand instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        RecordPaymentRequest request = instance.Request ?? new RecordPaymentRequest();
        DateOnly today = LegalCalendar.Today(clock);
        List<Error> errors = [];
        if (!Payment.IsValidAmount(request.Amount))
        {
            errors.Add(PaymentErrors.AmountInvalid);
        }

        if (request.Date == default || request.Date.Year < 2000 || request.Date > today)
        {
            errors.Add(PaymentErrors.DateInvalid);
        }

        if (!Enum.IsDefined(request.Method))
        {
            errors.Add(PaymentErrors.MethodInvalid);
        }

        if (request.Reference is { Length: > Payment.MaxReferenceLength })
        {
            errors.Add(PaymentErrors.ReferenceTooLong);
        }

        IReadOnlyList<PaymentAllocationRequest> allocations = request.Allocations ?? [];
        if (allocations.Count == 0)
        {
            errors.Add(PaymentErrors.AllocationsRequired);
        }
        else if (allocations.Count > Payment.MaxAllocations)
        {
            errors.Add(PaymentErrors.TooManyAllocations);
        }
        else
        {
            for (int i = 0; i < allocations.Count; i++)
            {
                if (allocations[i] is null || allocations[i].InvoiceId == Guid.Empty || !Payment.IsValidAmount(allocations[i].Amount))
                {
                    errors.Add(PaymentErrors.AllocationInvalid(i));
                }
            }

            if (allocations.Where(a => a is not null).GroupBy(a => a.InvoiceId).Any(g => g.Count() > 1))
            {
                errors.Add(PaymentErrors.AllocationDuplicate);
            }
        }

        return errors;
    }
}

/// <summary>
/// Déclaration à la plateforme agréée d'un encaissement enregistré, pour chaque facture qui y a été transmise et reste
/// à encaisser. Un échec est journalisé sans annuler l'encaissement (la déclaration se refait depuis la facture).
/// </summary>
internal sealed partial class PaymentPlatformReporter(
    IPlatformTransmissionStore transmissions,
    ICommandDispatcher commands,
    ILogger<PaymentPlatformReporter> logger)
{
    public async Task<int> ReportAsync(DossierId dossier, RecordPaymentRequest request, CancellationToken cancellationToken)
    {
        int reported = 0;
        foreach (PaymentAllocationRequest allocation in request.Allocations)
        {
            PlatformTransmission? transmission = await transmissions.FindLatestAsync(allocation.InvoiceId, cancellationToken).ConfigureAwait(false);
            if (transmission?.Reference is null || transmission.Status == PlatformStatusDto.Rejected || transmission.IsFinal)
            {
                continue;
            }

            decimal amount = Math.Min(allocation.Amount, transmission.RemainingToCollect);
            if (amount <= 0m)
            {
                continue;
            }

            Result<PlatformTransmissionResponse> sent = await commands.SendAsync(
                new ReportPaymentCommand(dossier, new InvoiceId(allocation.InvoiceId), request.Date, amount), cancellationToken).ConfigureAwait(false);
            if (sent.IsSuccess)
            {
                reported++;
            }
            else
            {
                LogNotReported(logger, string.Join(", ", sent.Errors.Select(e => e.Code)));
            }
        }

        return reported;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Encaissement non déclaré à la plateforme pour une facture : {Codes}")]
    private static partial void LogNotReported(ILogger logger, string codes);
}
