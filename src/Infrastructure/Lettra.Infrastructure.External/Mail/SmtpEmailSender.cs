using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;
using Lettra.Application.Abstractions.Mail;
using Lettra.Domain.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lettra.Infrastructure.External.Mail;

/// <summary>
/// Envoi par SMTP avec la pile .NET (<see cref="SmtpClient"/>), sans paquet tiers : suffisant pour un relais ou MailPit.
/// Un client par message (il n'est pas sûr entre fils). Le destinataire et le contenu ne sont jamais journalisés.
/// </summary>
internal sealed partial class SmtpEmailSender(IOptions<EmailOptions> options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public bool IsConfigured => true;

    public async Task<Result> SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        EmailOptions settings = options.Value;
        using MailMessage mail = Build(message, settings);
        using SmtpClient client = new(settings.Host, settings.Port)
        {
            EnableSsl = settings.EnableSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            Timeout = (int)settings.Timeout.TotalMilliseconds,
        };
        if (!string.IsNullOrEmpty(settings.UserName))
        {
            client.Credentials = new NetworkCredential(settings.UserName, settings.Password);
        }

        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(settings.Timeout);
        try
        {
            await client.SendMailAsync(mail, timeout.Token).ConfigureAwait(false);
            return Result.Success();
        }
        catch (Exception exception) when (exception is SmtpException or InvalidOperationException
            || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            LogSendFailed(settings.Host, settings.Port, exception);
            return EmailErrors.SendFailed;
        }
    }

    internal static MailMessage Build(EmailMessage message, EmailOptions settings)
    {
        MailMessage mail = new()
        {
            From = new MailAddress(settings.FromAddress, message.SenderName ?? settings.FromName, Encoding.UTF8),
            Subject = message.Subject,
            SubjectEncoding = Encoding.UTF8,
            Body = message.TextBody,
            BodyEncoding = Encoding.UTF8,
            IsBodyHtml = false,
        };
        mail.To.Add(new MailAddress(message.To.Value));
        if (message.ReplyTo is { } replyTo)
        {
            mail.ReplyToList.Add(new MailAddress(replyTo.Value));
        }

        foreach (EmailAttachment attachment in message.Attachments ?? [])
        {
            // Le flux appartient à la pièce jointe, libérée avec le message.
            Attachment part = new(new MemoryStream(attachment.Content.ToArray(), writable: false), attachment.FileName, attachment.ContentType);
            part.ContentDisposition!.DispositionType = DispositionTypeNames.Attachment;
            mail.Attachments.Add(part);
        }

        return mail;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Envoi d'e-mail refusé ou impossible par {Host}:{Port}")]
    private partial void LogSendFailed(string host, int port, Exception exception);
}
