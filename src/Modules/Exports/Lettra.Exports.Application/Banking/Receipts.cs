using System.Globalization;
using System.Text;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Mail;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Contracts.Administration;
using Lettra.Contracts.Banking;
using Lettra.Contracts.Common;
using Lettra.Domain.Banking;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;
using Lettra.Exports.Application.Reminders;

namespace Lettra.Exports.Application.Banking;

/// <summary>Une pièce déposée, prête à télécharger.</summary>
public sealed record ReceiptFile(string FileName, string ContentType, byte[] Content);

/// <summary>Pièces justificatives des dépenses du relevé (base du dossier).</summary>
public interface IReceiptStore
{
    /// <summary>Les opérations débitrices du relevé et leur pièce (sans son contenu), les plus récentes en tête.</summary>
    Task<IReadOnlyList<ReceiptLineResponse>> ListDebitsAsync(int max, CancellationToken cancellationToken);

    /// <summary>Les opérations demandées, suivies pour modification.</summary>
    Task<IReadOnlyList<BankStatementLine>> FindLinesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    /// <summary>La pièce déposée d'une opération, avec son contenu.</summary>
    Task<BankLineReceipt?> FindReceiptAsync(Guid lineId, CancellationToken cancellationToken);

    /// <summary>Enregistre la pièce d'une opération, à la place de celle déposée avant s'il y en avait une.</summary>
    Task SaveReceiptAsync(BankLineReceipt receipt, CancellationToken cancellationToken);
}

/// <summary>Les dépenses du relevé et leurs justificatifs, pour le cabinet.</summary>
public sealed record GetReceiptsQuery(DossierId Dossier, int Max = 500) : IQuery<Result<ReceiptsResponse>>, IRequirePermission
{
    public Permission Permission => Permission.DossierLire;
}

/// <summary>Les pièces que le cabinet attend du client : ce que le client voit pour les déposer.</summary>
public sealed record GetRequestedReceiptsQuery(DossierId Dossier) : IQuery<Result<ReceiptsResponse>>, IRequirePermission
{
    public Permission Permission => Permission.PiecesDeposer;
}

/// <summary>Réclamer au client les pièces de plusieurs dépenses, par un seul e-mail (ou les réclamer de nouveau).</summary>
public sealed record RequestReceiptsCommand(DossierId Dossier, RequestReceiptsRequest Request)
    : ICommand<Result<RequestReceiptsResponse>>, IRequirePermission
{
    public Permission Permission => Permission.FactureEmettre;
}

/// <summary>Aucune pièce n'est attendue pour une dépense (le relevé suffit), ou elle l'est de nouveau.</summary>
public sealed record SetReceiptNotNeededCommand(DossierId Dossier, Guid LineId, SetReceiptNotNeededRequest Request)
    : ICommand<Result<ReceiptLineResponse>>, IRequirePermission
{
    public Permission Permission => Permission.FactureEmettre;
}

/// <summary>Déposer la pièce d'une dépense : une photo prise au téléphone ou un PDF. Un nouveau dépôt la remplace.</summary>
public sealed record UploadReceiptCommand(DossierId Dossier, Guid LineId, UploadReceiptRequest Request)
    : ICommand<Result<ReceiptLineResponse>>, IRequirePermission
{
    public Permission Permission => Permission.PiecesDeposer;
}

/// <summary>Télécharger la pièce déposée d'une dépense.</summary>
public sealed record GetReceiptFileQuery(DossierId Dossier, Guid LineId) : IQuery<Result<ReceiptFile>>, IRequirePermission
{
    public Permission Permission => Permission.DossierLire;
}

/// <summary>Règles d'affichage communes : totaux, dernière adresse, une ligne après un changement.</summary>
internal static class ReceiptViews
{
    /// <summary>Une demande porte sur 100 dépenses au plus : un e-mail reste lisible.</summary>
    public const int MaxLinesPerRequest = 100;

    public static ReceiptsResponse Summarize(IReadOnlyList<ReceiptLineResponse> lines, bool emailConfigured) => new(
        lines,
        lines.Count(l => l.Status is ReceiptStatusDto.Missing or ReceiptStatusDto.Requested),
        lines.Where(l => l.Status is ReceiptStatusDto.Missing or ReceiptStatusDto.Requested).Sum(l => l.Amount),
        lines.Count(l => l.Status == ReceiptStatusDto.Requested),
        lines.Count(l => l.Status == ReceiptStatusDto.Received),
        lines.Where(l => l.RequestedAt is not null).OrderByDescending(l => l.RequestedAt).Select(l => l.RequestedTo).FirstOrDefault(),
        emailConfigured);

    public static ReceiptLineResponse ToResponse(BankStatementLine line, BankLineReceipt? receipt) => new(
        line.Id,
        line.BookingDate,
        -line.Amount,
        line.Label,
        line.CounterpartyName,
        (ReceiptStatusDto)(int)line.ReceiptStatus,
        line.ReceiptRequestedAt,
        line.ReceiptRequestedTo,
        line.ReceiptRequests,
        receipt is null ? null : new ReceiptFileResponse(receipt.Id, receipt.FileName, receipt.ContentType, receipt.Size, receipt.AddedAt, ToDto(receipt.Figures)),
        line.Version);

    public static ReceiptFiguresDto? ToDto(ReceiptFigures? figures) => figures is null ? null : new ReceiptFiguresDto
    {
        AmountInclTax = figures.AmountInclTax,
        Date = figures.Date,
        VatAmount = figures.VatAmount,
        ReadOnDevice = figures.ReadOnDevice,
    };
}

internal sealed class GetReceiptsHandler(IReceiptStore store, IEmailSender email) : IQueryHandler<GetReceiptsQuery, Result<ReceiptsResponse>>
{
    public async Task<Result<ReceiptsResponse>> HandleAsync(GetReceiptsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        IReadOnlyList<ReceiptLineResponse> lines = await store.ListDebitsAsync(Math.Clamp(query.Max, 1, 2000), cancellationToken).ConfigureAwait(false);
        return ReceiptViews.Summarize(lines, email.IsConfigured);
    }
}

internal sealed class GetRequestedReceiptsHandler(IReceiptStore store) : IQueryHandler<GetRequestedReceiptsQuery, Result<ReceiptsResponse>>
{
    public async Task<Result<ReceiptsResponse>> HandleAsync(GetRequestedReceiptsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        IReadOnlyList<ReceiptLineResponse> lines = await store.ListDebitsAsync(2000, cancellationToken).ConfigureAwait(false);
        return ReceiptViews.Summarize([.. lines.Where(l => l.Status == ReceiptStatusDto.Requested)], emailConfigured: false);
    }
}

internal sealed class RequestReceiptsHandler(
    IReceiptStore store, IUnitOfWork unitOfWork, ICurrentUser user, TimeProvider clock, ReceiptRequestMailer mailer)
    : ICommandHandler<RequestReceiptsCommand, Result<RequestReceiptsResponse>>
{
    public async Task<Result<RequestReceiptsResponse>> HandleAsync(RequestReceiptsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        RequestReceiptsRequest request = command.Request ?? new RequestReceiptsRequest();
        Guid[] ids = [.. (request.LineIds ?? []).Distinct()];
        if (ids.Length is 0 or > ReceiptViews.MaxLinesPerRequest)
        {
            return ReceiptErrors.LinesRequired;
        }

        Result<Email> to = Email.Create(request.Email);
        if (to.IsFailure)
        {
            return ReceiptErrors.EmailInvalid;
        }

        IReadOnlyList<BankStatementLine> lines = await store.FindLinesAsync(ids, cancellationToken).ConfigureAwait(false);
        if (lines.Count != ids.Length)
        {
            return BankStatementErrors.LineNotFound;
        }

        DateTimeOffset now = clock.GetUtcNow();
        List<Error> errors = [.. lines.Select(l => l.RequestReceipt(to.Value.Value, user.UserId, now)).Where(r => r.IsFailure).SelectMany(r => r.Errors).Distinct()];
        if (errors.Count > 0)
        {
            return Result<RequestReceiptsResponse>.Failure(errors);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        InvitationEmailStatusDto sent = await mailer.SendAsync(to.Value, lines, request.Note, cancellationToken).ConfigureAwait(false);
        return new RequestReceiptsResponse(lines.Count, sent);
    }
}

internal sealed class SetReceiptNotNeededHandler(IBankStatementRepository statements, IUnitOfWork unitOfWork, ICurrentUser user, TimeProvider clock)
    : ICommandHandler<SetReceiptNotNeededCommand, Result<ReceiptLineResponse>>
{
    public async Task<Result<ReceiptLineResponse>> HandleAsync(SetReceiptNotNeededCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (await statements.FindLineAsync(command.LineId, cancellationToken).ConfigureAwait(false) is not { } line)
        {
            return BankStatementErrors.LineNotFound;
        }

        Result changed = line.SetReceiptNotNeeded(command.Request?.NotNeeded ?? true, user.UserId, clock.GetUtcNow());
        if (changed.IsFailure)
        {
            return Result<ReceiptLineResponse>.Failure(changed.Errors);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ReceiptViews.ToResponse(line, null);
    }
}

internal sealed class UploadReceiptHandler(IBankStatementRepository statements, IReceiptStore store, IUnitOfWork unitOfWork, ICurrentUser user, TimeProvider clock)
    : ICommandHandler<UploadReceiptCommand, Result<ReceiptLineResponse>>
{
    public async Task<Result<ReceiptLineResponse>> HandleAsync(UploadReceiptCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (await statements.FindLineAsync(command.LineId, cancellationToken).ConfigureAwait(false) is not { } line)
        {
            return BankStatementErrors.LineNotFound;
        }

        Result<byte[]> content = Decode(command.Request?.ContentBase64);
        if (content.IsFailure)
        {
            return Result<ReceiptLineResponse>.Failure(content.Errors);
        }

        DateTimeOffset now = clock.GetUtcNow();
        Result<BankLineReceipt> receipt = BankLineReceipt.Create(Guid.CreateVersion7(), line.Id, command.Request?.FileName, content.Value, user.UserId, now);
        if (receipt.IsFailure)
        {
            return Result<ReceiptLineResponse>.Failure(receipt.Errors);
        }

        if (command.Request?.Figures is { } given)
        {
            Result<ReceiptFigures> figures = ReceiptFigures.Create(given.AmountInclTax, given.Date, given.VatAmount, given.ReadOnDevice, LegalCalendar.DateOf(now));
            if (figures.IsFailure)
            {
                return Result<ReceiptLineResponse>.Failure(figures.Errors);
            }

            receipt.Value.Confirm(figures.Value);
        }

        Result received = line.ReceiptReceived(user.UserId, now);
        if (received.IsFailure)
        {
            return Result<ReceiptLineResponse>.Failure(received.Errors);
        }

        await store.SaveReceiptAsync(receipt.Value, cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ReceiptViews.ToResponse(line, receipt.Value);
    }

    /// <summary>Le contenu en base 64 ; trop long, il est refusé avant d'être décodé.</summary>
    private static Result<byte[]> Decode(string? base64)
    {
        if (string.IsNullOrWhiteSpace(base64))
        {
            return ReceiptErrors.FileRequired;
        }

        if (base64.Length > (BankLineReceipt.MaxContentLength / 3 * 4) + 8)
        {
            return ReceiptErrors.FileTooLarge;
        }

        byte[] buffer = new byte[base64.Length * 3 / 4];
        return Convert.TryFromBase64String(base64, buffer, out int written) && written > 0
            ? Result<byte[]>.Success(buffer[..written])
            : ReceiptErrors.FileRequired;
    }
}

internal sealed class GetReceiptFileHandler(IReceiptStore store) : IQueryHandler<GetReceiptFileQuery, Result<ReceiptFile>>
{
    public async Task<Result<ReceiptFile>> HandleAsync(GetReceiptFileQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return await store.FindReceiptAsync(query.LineId, cancellationToken).ConfigureAwait(false) is { } receipt
            ? Result<ReceiptFile>.Success(new ReceiptFile(receipt.FileName, receipt.ContentType, receipt.Content))
            : ReceiptErrors.NotFound;
    }
}

/// <summary>
/// L'e-mail qui réclame les pièces, en français puis en anglais : la liste des dépenses (date, montant, libellé) et
/// l'écran « Pièces à fournir » où le client les dépose, en photo depuis son téléphone. Un échec d'envoi n'annule
/// jamais les demandes : elles restent notées, et l'écran propose de les renvoyer.
/// </summary>
internal sealed class ReceiptRequestMailer(IEmailSender sender, LettraPublicAddress publicAddress)
{
    public async Task<InvitationEmailStatusDto> SendAsync(Email to, IReadOnlyList<BankStatementLine> lines, string? note, CancellationToken cancellationToken)
    {
        if (!sender.IsConfigured)
        {
            return InvitationEmailStatusDto.NotConfigured;
        }

        Result sent = await sender.SendAsync(new EmailMessage(to, Subject(lines.Count), Body(lines, note, publicAddress.Url)), cancellationToken).ConfigureAwait(false);
        return sent.IsSuccess ? InvitationEmailStatusDto.Sent
            : sent.Errors[0].Code == ErrorCodes.Email.NotConfigured ? InvitationEmailStatusDto.NotConfigured
            : InvitationEmailStatusDto.Failed;
    }

    internal static string Subject(int count) =>
        $"Pièces justificatives à fournir ({count}) / Receipts to provide ({count})";

    internal static string Body(IReadOnlyList<BankStatementLine> lines, string? note, Uri? url)
    {
        CultureInfo fr = CultureInfo.GetCultureInfo("fr-FR");
        CultureInfo en = CultureInfo.GetCultureInfo("en-GB");
        string where = url is null ? "Ouvrez Lettra, écran « Pièces à fournir »" : $"Ouvrez {new Uri(url, "my-receipts")}";
        string whereEn = url is null ? "Open Lettra, « Receipts to provide » screen" : $"Open {new Uri(url, "my-receipts")}";
        string message = string.IsNullOrWhiteSpace(note) ? string.Empty : $"{note.Trim()}{Environment.NewLine}{Environment.NewLine}";
        return $"""
            Bonjour,

            {message}Votre cabinet comptable attend les justificatifs (facture, ticket) de ces dépenses :
            {List(lines, fr)}
            {where} et déposez chaque pièce, en photo depuis votre téléphone ou en PDF.

            —

            Hello,

            {message}Your accounting firm needs the receipts (invoice, ticket) for these expenses:
            {List(lines, en)}
            {whereEn} and upload each one, as a photo from your phone or as a PDF.
            """;
    }

    private static string List(IReadOnlyList<BankStatementLine> lines, CultureInfo culture)
    {
        StringBuilder text = new();
        foreach (BankStatementLine line in lines.OrderBy(l => l.BookingDate))
        {
            text.Append(culture, $"- {line.BookingDate.ToString("d", culture)} · {ReminderTemplates.Money(-line.Amount, culture)} · {line.CounterpartyName ?? line.Label}").AppendLine();
        }

        return text.ToString().TrimEnd();
    }
}
