using Lettra.Contracts.Exports;
using Lettra.Exports.Domain.Platform;

namespace Lettra.Exports.Application.EInvoicing.Platform;

/// <summary>
/// Le passage entre le domaine de la transmission et les contrats d'échange. Les deux énumérations portent les mêmes
/// valeurs (le code du statut XP Z12-012, la voie, la syntaxe), vérifiées membre à membre par les tests.
/// </summary>
public static class PlatformMapping
{
    public static PlatformStatusDto ToDto(this PlatformStatus status) => (PlatformStatusDto)(int)status;

    public static PlatformStatus ToDomain(this PlatformStatusDto status) => (PlatformStatus)(int)status;

    public static PlatformChannelDto ToDto(this PlatformChannel channel) => (PlatformChannelDto)(int)channel;

    public static EInvoiceFormatDto ToDto(this EInvoiceFormat format) => (EInvoiceFormatDto)(int)format;

    public static EInvoiceFormat ToDomain(this EInvoiceFormatDto format) => (EInvoiceFormat)(int)format;

    public static PlatformTransmissionResponse ToResponse(this PlatformTransmission transmission)
    {
        ArgumentNullException.ThrowIfNull(transmission);
        return new(
            transmission.Id,
            transmission.InvoiceId,
            transmission.InvoiceNumber,
            transmission.Channel.ToDto(),
            transmission.Format?.ToDto(),
            transmission.Platform,
            transmission.Reference,
            transmission.Status.ToDto(),
            transmission.IsFinal,
            transmission.CollectedAmount,
            transmission.SubmittedAt,
            [.. transmission.History.Select(e => new PlatformStatusEventResponse(e.Status.ToDto(), e.At, e.Reason, e.Amount))]);
    }
}
