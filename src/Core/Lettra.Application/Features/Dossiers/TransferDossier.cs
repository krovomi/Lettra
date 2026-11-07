using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Contracts.Dossiers;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Application.Features.Dossiers;

/// <summary>
/// Exporter un dossier du cabinet dans un paquet de réversibilité, chiffré par une phrase secrète. Tout le dossier sort :
/// réservé à l'administrateur du cabinet.
/// </summary>
public sealed record ExportDossierCommand(OrganisationId Organisation, DossierId Dossier, string Passphrase)
    : ICommand<Result<DossierPackage>>, IRequireOrganisationPermission
{
    public Permission Permission => Permission.CabinetAdministrer;
}

/// <summary>Importer un paquet de réversibilité dans le cabinet : le dossier y entre avec son identifiant d'origine.</summary>
public sealed record ImportDossierCommand(OrganisationId Organisation, Stream Package, string Passphrase)
    : ICommand<Result<DossierSummary>>, IRequireOrganisationPermission
{
    public Permission Permission => Permission.CabinetAdministrer;
}

internal sealed class ExportDossierHandler(IDossierTransfer transfer) : ICommandHandler<ExportDossierCommand, Result<DossierPackage>>
{
    public Task<Result<DossierPackage>> HandleAsync(ExportDossierCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return PassphraseRules.IsValid(command.Passphrase)
            ? transfer.ExportAsync(command.Organisation, command.Dossier, command.Passphrase, cancellationToken)
            : Task.FromResult<Result<DossierPackage>>(DossierErrors.PassphraseTooShort);
    }
}

internal sealed class ImportDossierHandler(IDossierTransfer transfer) : ICommandHandler<ImportDossierCommand, Result<DossierSummary>>
{
    public Task<Result<DossierSummary>> HandleAsync(ImportDossierCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return PassphraseRules.IsValid(command.Passphrase)
            ? transfer.ImportAsync(command.Organisation, command.Package, command.Passphrase, cancellationToken)
            : Task.FromResult<Result<DossierSummary>>(DossierErrors.PassphraseTooShort);
    }
}

internal static class PassphraseRules
{
    /// <summary>
    /// Longueur comptée en caractères : la phrase n'est pas retouchée (ni espaces retirés, ni casse), elle est utilisée
    /// telle que saisie pour dériver la clé du paquet.
    /// </summary>
    public static bool IsValid(string? passphrase) =>
        passphrase is { Length: >= DossierPackageRules.MinPassphraseLength and <= DossierPackageRules.MaxPassphraseLength }
        && !string.IsNullOrWhiteSpace(passphrase);
}

/// <summary>Poste relié à un serveur : il n'a qu'une réplique du dossier, l'export et l'import se font sur le serveur.</summary>
internal sealed class NoDossierTransfer : IDossierTransfer
{
    public Task<Result<DossierPackage>> ExportAsync(OrganisationId organisation, DossierId dossier, string passphrase, CancellationToken cancellationToken) =>
        Task.FromResult<Result<DossierPackage>>(DossierErrors.TransferUnavailable);

    public Task<Result<DossierSummary>> ImportAsync(OrganisationId organisation, Stream package, string passphrase, CancellationToken cancellationToken) =>
        Task.FromResult<Result<DossierSummary>>(DossierErrors.TransferUnavailable);
}
