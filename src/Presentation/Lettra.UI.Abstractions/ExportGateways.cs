using Lettra.Contracts.Common;
using Lettra.Contracts.Exports;

namespace Lettra.UI.Abstractions;

/// <summary>Un FEC remis par le serveur : son nom réglementaire (SIREN, « FEC », date de clôture) et son contenu (ISO 8859-15).</summary>
public sealed record FecDocument(string FileName, byte[] Content);

/// <summary>
/// Fichier des écritures comptables du dossier (module Exports, phase 5) : export sur un exercice et validateur intégré.
/// Les deux exigent la permission <c>FecExporter</c>.
/// </summary>
public interface IFecGateway
{
    /// <summary>
    /// Exporte le FEC de l'exercice (bornes comprises, 24 mois au plus). Refus possibles : <c>Fec.PeriodInvalid</c>,
    /// <c>Fec.PeriodTooLong</c>, <c>Fec.SirenMissing</c> (fiche entreprise), <c>Fec.ExportInvalid</c>.
    /// </summary>
    Task<OperationResult<FecDocument>> ExportAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken);

    /// <summary>Passe un FEC (celui de Lettra ou d'un autre logiciel) au validateur intégré, éventuellement sur un exercice.</summary>
    Task<OperationResult<FecValidationResponse>> ValidateAsync(
        string fileName, byte[] content, DateOnly? from, DateOnly? to, CancellationToken cancellationToken);
}
