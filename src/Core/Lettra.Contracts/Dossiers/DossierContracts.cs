using System.ComponentModel.DataAnnotations;
using Lettra.Contracts.Common;

namespace Lettra.Contracts.Dossiers;

/// <summary>Création d'un dossier client du cabinet. Mutable pour <c>EditForm</c>.</summary>
public sealed record CreateDossierRequest
{
    public const int MaxNameLength = 200;

    [Required(ErrorMessage = ErrorCodes.Tenancy.DossierNameRequired)]
    [StringLength(MaxNameLength, ErrorMessage = ErrorCodes.Tenancy.DossierNameTooLong)]
    public string Name { get; set; } = string.Empty;
}

public sealed record DossierSummary(Guid Id, string Name);

/// <summary>
/// Un dossier dont le cabinet a demandé l'effacement : fermé à tous, il reste annulable jusqu'à
/// <paramref name="ErasureDueAt"/>. Ensuite, sa clé est détruite et sa base supprimée, sans retour possible.
/// </summary>
public sealed record DossierErasureResponse(Guid Id, string Name, DateTimeOffset ErasureDueAt);
