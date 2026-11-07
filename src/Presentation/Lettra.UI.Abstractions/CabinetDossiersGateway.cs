using Lettra.Contracts.Common;
using Lettra.Contracts.Dossiers;

namespace Lettra.UI.Abstractions;

/// <summary>Un paquet de réversibilité exporté : le nom proposé du fichier et son contenu chiffré.</summary>
public sealed record DossierPackageFile(string FileName, byte[] Content);

/// <summary>
/// Un fichier choisi par l'utilisateur. <see cref="OpenRead"/> le lit (poste, tests). Dans le navigateur,
/// <see cref="InputElementId"/> désigne le champ qui le tient : le fichier part alors du disque tel quel, sans être
/// copié dans la mémoire de l'application (<see cref="IBrowserFileSender"/>).
/// </summary>
public sealed record ChosenFile(string Name, long Size, Func<Stream> OpenRead, string? InputElementId = null);

/// <summary>
/// Envoi par le navigateur lui-même (fetch) d'un fichier choisi dans un champ, avec des champs texte, en formulaire
/// multipart. WebAssembly copierait le fichier en mémoire avant de l'envoyer : au-delà d'une centaine de Mio, l'onglet
/// ne tient plus. Présent dans le navigateur seulement.
/// </summary>
public interface IBrowserFileSender
{
    Task<HttpResponseMessage> PostAsync(
        Uri url,
        string inputElementId,
        string fileField,
        IReadOnlyDictionary<string, string> fields,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken cancellationToken);
}

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
    Task<OperationResult<DossierSummary>> ImportAsync(Guid cabinetId, ChosenFile package, string passphrase, CancellationToken cancellationToken);
}
