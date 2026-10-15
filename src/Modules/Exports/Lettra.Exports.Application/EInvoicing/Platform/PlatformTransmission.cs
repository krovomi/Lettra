using Lettra.Contracts.Exports;

namespace Lettra.Exports.Application.EInvoicing.Platform;

/// <summary>
/// La transmission d'une pièce à la plateforme agréée et l'historique de ses statuts. Une pièce rejetée peut être
/// retransmise : chaque envoi est une transmission, la dernière fait foi.
/// </summary>
public sealed class PlatformTransmission
{
    private readonly List<PlatformStatusEvent> _history;

    public PlatformTransmission(
        Guid id,
        Guid invoiceId,
        string invoiceNumber,
        PlatformChannelDto channel,
        EInvoiceFormatDto? format,
        string platform,
        string? reference,
        decimal totalInclTax,
        DateTimeOffset submittedAt,
        IEnumerable<PlatformStatusEvent> history)
    {
        Id = id;
        InvoiceId = invoiceId;
        InvoiceNumber = invoiceNumber;
        Channel = channel;
        Format = format;
        Platform = platform;
        Reference = reference;
        TotalInclTax = totalInclTax;
        SubmittedAt = submittedAt;
        _history = [.. history];
        if (_history.Count == 0)
        {
            throw new ArgumentException("Une transmission porte au moins son accusé de dépôt.", nameof(history));
        }
    }

    public Guid Id { get; }

    public Guid InvoiceId { get; }

    public string InvoiceNumber { get; }

    public PlatformChannelDto Channel { get; }

    public EInvoiceFormatDto? Format { get; }

    public string Platform { get; }

    public string? Reference { get; }

    /// <summary>TTC de la pièce, pour savoir quand l'encaissement est complet.</summary>
    public decimal TotalInclTax { get; }

    public DateTimeOffset SubmittedAt { get; }

    public IReadOnlyList<PlatformStatusEvent> History => _history;

    public PlatformStatusDto Status => _history[^1].Status;

    public decimal CollectedAmount => _history.Where(e => e.Status == PlatformStatusDto.Collected).Sum(e => e.Amount ?? 0m);

    public bool IsFinal => PlatformLifecycle.IsTerminal(Status) || (Status == PlatformStatusDto.Collected && CollectedAmount >= TotalInclTax);

    public decimal RemainingToCollect => Math.Max(0m, TotalInclTax - CollectedAmount);

    /// <summary>
    /// Ajoute un statut s'il est nouveau et permis par le cycle de vie. Un statut déjà connu (même code, même instant)
    /// est ignoré : la plateforme renvoie tout l'historique à chaque lecture. Les instants se comparent à la milliseconde,
    /// la précision de leur enregistrement.
    /// </summary>
    /// <returns>Vrai si le statut a été ajouté.</returns>
    public bool TryApply(PlatformStatusEvent status)
    {
        ArgumentNullException.ThrowIfNull(status);
        if (IsFinal || _history.Any(known => IsSame(known, status)) || !PlatformLifecycle.CanMove(Status, status.Status))
        {
            return false;
        }

        _history.Add(status);
        return true;
    }

    private static bool IsSame(PlatformStatusEvent known, PlatformStatusEvent status) =>
        known.Status == status.Status
        && known.At.ToUnixTimeMilliseconds() == status.At.ToUnixTimeMilliseconds()
        && known.Amount == status.Amount;

    public PlatformTransmissionResponse ToResponse() => new(
        Id,
        InvoiceId,
        InvoiceNumber,
        Channel,
        Format,
        Platform,
        Reference,
        Status,
        IsFinal,
        CollectedAmount,
        SubmittedAt,
        [.. _history.Select(e => new PlatformStatusEventResponse(e.Status, e.At, e.Reason, e.Amount))]);
}

/// <summary>Les transmissions d'un dossier, dans sa base.</summary>
public interface IPlatformTransmissionStore
{
    /// <summary>La dernière transmission de la pièce, ou rien si elle n'a jamais été transmise.</summary>
    Task<PlatformTransmission?> FindLatestAsync(Guid invoiceId, CancellationToken cancellationToken);

    /// <summary>
    /// Ajoute une nouvelle transmission, ou les statuts ajoutés à une transmission existante, dans la transaction de
    /// l'appelant (<c>IBillingTransaction</c>) : ils s'enregistrent avec leurs entrées au journal inaltérable.
    /// </summary>
    Task StageAsync(PlatformTransmission transmission, CancellationToken cancellationToken);
}
