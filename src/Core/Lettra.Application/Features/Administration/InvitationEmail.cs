using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Mail;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Contracts.Administration;
using Lettra.Contracts.Common;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Features.Administration;

/// <summary>
/// Renvoie l'e-mail d'invitation d'une personne qui ne s'est pas encore connectée (message perdu, adresse corrigée).
/// </summary>
public sealed record ResendInvitationCommand(OrganisationId Organisation, Guid UserId)
    : ICommand<Result<InvitationEmailResponse>>, IRequireOrganisationPermission
{
    public Permission Permission => Permission.UtilisateursGerer;
}

/// <summary>
/// L'e-mail d'invitation au cabinet, en français puis en anglais (la langue de la personne n'est pas encore connue).
/// Il dit qui invite, dans quel cabinet, et comment se connecter : avec cette adresse, sur l'adresse publique de Lettra.
/// Un échec d'envoi n'annule jamais l'invitation : l'administrateur la renvoie, ou prévient la personne lui-même.
/// </summary>
internal sealed class InvitationMailer(IEmailSender sender, IAuthorizationRegistry registry, ICurrentUser user, LettraPublicAddress publicAddress)
{
    public async Task<InvitationEmailStatusDto> SendAsync(OrganisationId cabinet, string displayName, string email, CancellationToken cancellationToken)
    {
        if (!sender.IsConfigured)
        {
            return InvitationEmailStatusDto.NotConfigured;
        }

        Result<Email> to = Email.Create(email);
        if (to.IsFailure)
        {
            return InvitationEmailStatusDto.Failed;
        }

        CabinetRegistry? registered = await registry.ReadAsync(cabinet, cancellationToken).ConfigureAwait(false);
        string cabinetName = registered?.CabinetName ?? "Lettra";
        string? inviter = registered?.People.FirstOrDefault(p => p.Id == user.UserId)?.DisplayName;
        EmailMessage message = new(to.Value, Subject(cabinetName), Body(displayName, inviter, cabinetName, email, publicAddress.Url), SenderName: cabinetName);
        Result sent = await sender.SendAsync(message, cancellationToken).ConfigureAwait(false);
        return sent.IsSuccess ? InvitationEmailStatusDto.Sent
            : sent.Errors[0].Code == ErrorCodes.Email.NotConfigured ? InvitationEmailStatusDto.NotConfigured
            : InvitationEmailStatusDto.Failed;
    }

    internal static string Subject(string cabinetName) =>
        $"Invitation à rejoindre {cabinetName} sur Lettra / Invitation to join {cabinetName} on Lettra";

    internal static string Body(string displayName, string? inviter, string cabinetName, string email, Uri? url)
    {
        string who = string.IsNullOrWhiteSpace(inviter) ? "Un administrateur" : inviter;
        string whoEn = string.IsNullOrWhiteSpace(inviter) ? "An administrator" : inviter;
        string where = url is null ? "Ouvrez Lettra" : $"Ouvrez {url}";
        string whereEn = url is null ? "Open Lettra" : $"Open {url}";
        return $"""
            Bonjour {displayName},

            {who} vous invite à rejoindre {cabinetName} sur Lettra.
            {where} et connectez-vous avec cette adresse : {email}.
            Vos accès vous seront donnés par l'administrateur du cabinet après votre première connexion.

            Si vous n'attendiez pas cette invitation, ignorez ce message.

            —

            Hello {displayName},

            {whoEn} invites you to join {cabinetName} on Lettra.
            {whereEn} and sign in with this address: {email}.
            The firm's administrator will give you access after your first sign-in.

            If you were not expecting this invitation, you can ignore this message.
            """;
    }
}

internal sealed class ResendInvitationHandler(IAuthorizationRegistry registry, InvitationMailer mailer)
    : ICommandHandler<ResendInvitationCommand, Result<InvitationEmailResponse>>
{
    public async Task<Result<InvitationEmailResponse>> HandleAsync(ResendInvitationCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        CabinetRegistry? registered = await registry.ReadAsync(command.Organisation, cancellationToken).ConfigureAwait(false);
        RegistryPerson? person = registered?.People.FirstOrDefault(p => p.Id == command.UserId);
        if (person is null)
        {
            return RightsErrors.UserNotFound;
        }

        if (person.State != AccountState.Invited || string.IsNullOrEmpty(person.Email))
        {
            return AdministrationErrors.NotInvited;
        }

        InvitationEmailStatusDto status = await mailer.SendAsync(command.Organisation, person.DisplayName ?? person.Email, person.Email, cancellationToken).ConfigureAwait(false);
        return new InvitationEmailResponse(person.Id, status);
    }
}
