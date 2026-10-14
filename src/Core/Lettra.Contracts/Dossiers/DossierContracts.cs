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
