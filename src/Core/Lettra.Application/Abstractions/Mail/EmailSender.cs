using Lettra.Contracts.Common;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Abstractions.Mail;

/// <summary>Une pièce jointe d'un e-mail (une facture PDF, un Factur-X…).</summary>
/// <param name="FileName">Nom proposé au destinataire, avec son extension.</param>
/// <param name="ContentType">Type MIME, par exemple <c>application/pdf</c>.</param>
/// <param name="Content">Contenu du fichier.</param>
public sealed record EmailAttachment(string FileName, string ContentType, ReadOnlyMemory<byte> Content);

/// <summary>
/// Un e-mail à envoyer. L'adresse d'expédition est celle de l'installation (réglage <c>Lettra:Email:FromAddress</c>) :
/// le cas d'usage ne choisit que le nom affiché (<paramref name="SenderName"/>, la raison sociale du dossier par exemple)
/// et l'adresse de réponse (<paramref name="ReplyTo"/>, celle de l'entreprise), pour qu'une réponse du client parte chez
/// elle et pas chez Lettra.
/// </summary>
/// <param name="To">Destinataire.</param>
/// <param name="Subject">Objet, sur une ligne.</param>
/// <param name="TextBody">Corps en texte brut (toujours fourni : lisible partout, y compris par un lecteur d'écran).</param>
/// <param name="SenderName">Nom affiché de l'expéditeur ; sans lui, celui de l'installation.</param>
/// <param name="ReplyTo">Adresse de réponse, facultative.</param>
/// <param name="Attachments">Pièces jointes, facultatives.</param>
public sealed record EmailMessage(
    Email To,
    string Subject,
    string TextBody,
    string? SenderName = null,
    Email? ReplyTo = null,
    IReadOnlyList<EmailAttachment>? Attachments = null);

/// <summary>
/// Port d'envoi d'e-mails, unique pour tout Lettra (relances, invitations, factures récurrentes…). L'adaptateur est
/// choisi par la configuration : SMTP (MailPit dans la démo, qui garde les messages sans rien envoyer dehors) ou coupé
/// (par défaut), qui répond <c>Email.NotConfigured</c> sans rien tenter. Un échec n'est jamais une exception : l'appelant
/// garde la trace et propose l'envoi à la main.
/// </summary>
public interface IEmailSender
{
    /// <summary>Vrai quand un envoi peut réussir (un serveur est configuré) ; faux en mode coupé.</summary>
    bool IsConfigured { get; }

    Task<Result> SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

/// <summary>
/// L'adresse publique de Lettra (réglage <c>Lettra:Email:PublicUrl</c>), citée dans les messages qui invitent à se
/// connecter ; <c>null</c> si l'installation ne la connaît pas (poste isolé).
/// </summary>
public sealed record LettraPublicAddress(Uri? Url);

/// <summary>Codes d'erreur de l'envoi d'e-mails.</summary>
public static class EmailErrors
{
    /// <summary>Aucun serveur d'envoi n'est configuré sur cette installation : rien n'est parti.</summary>
    public static Error NotConfigured => new(ErrorCodes.Email.NotConfigured, ErrorKind.Conflict);

    /// <summary>Le serveur d'envoi a refusé le message ou ne répond pas.</summary>
    public static Error SendFailed => new(ErrorCodes.Email.SendFailed, ErrorKind.Unexpected);
}

/// <summary>Envoi coupé (réglage par défaut, et poste isolé) : rien ne part, la réponse est immédiate.</summary>
internal sealed class NoEmailSender : IEmailSender
{
    public bool IsConfigured => false;

    public Task<Result> SendAsync(EmailMessage message, CancellationToken cancellationToken) =>
        Task.FromResult<Result>(EmailErrors.NotConfigured);
}
