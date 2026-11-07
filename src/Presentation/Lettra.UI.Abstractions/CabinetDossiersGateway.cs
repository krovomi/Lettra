using Lettra.Contracts.Common;
using Lettra.Contracts.Dossiers;

namespace Lettra.UI.Abstractions;

/// <summary>Un paquet de réversibilité exporté : le nom proposé du fichier et son contenu chiffré.</summary>
public sealed record DossierPackageFile(string FileName, byte[] Content);

/// <summary>
/// Les dossiers du cabinet, gérés par son administrateur : la liste, la création, l'effacement (annulable pendant le
/// délai de rétractation), l'export dans un paquet chiffré et son import. In-process sur le poste isolé (ses dossiers
/// ne s'y effacent pas), HTTP ailleurs.
/// </summary>
public interface ICabinetDossiersGateway
{
    Task<OperationResult<IReadOnlyList<CabinetDossierResponse>>> ListAsync(Guid cabinetId, CancellationToken cancellationToken);

    Task<OperationResult<DossierSummary>> CreateAsync(Guid cabinetId, CreateDossierRequest request, CancellationToken cancellationToken);

    Task<OperationResult<DossierErasureResponse>> ScheduleErasureAsync(Guid cabinetId, Guid dossierId, CancellationToken cancellationToken);

    Task<OperationResult<DossierSummary>> CancelErasureAsync(Guid cabinetId, Guid dossierId, CancellationToken cancellationToken);

    /// <summary>Le dossier entier, chiffré par la phrase secrète. Refus : <c>Dossier.PassphraseTooShort</c>, <c>Dossier.NotFound</c>.</summary>
    Task<OperationResult<DossierPackageFile>> ExportAsync(Guid cabinetId, Guid dossierId, ExportDossierRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Crée le dossier du paquet chez ce cabinet. Refus : <c>Dossier.PackageUnreadable</c> (phrase fausse, paquet altéré),
    /// <c>Dossier.AlreadyExists</c>, <c>Dossier.PackageTooNew</c>.
    /// </summary>
    Task<OperationResult<DossierSummary>> ImportAsync(Guid cabinetId, Stream package, string passphrase, CancellationToken cancellationToken);
}
