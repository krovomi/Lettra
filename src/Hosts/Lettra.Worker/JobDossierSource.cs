using Lettra.Application.Abstractions.Tenancy;
using Lettra.Domain.SharedKernel;

namespace Lettra.Worker;

/// <summary>
/// Les jobs n'ont pas de requête HTTP : le dossier visé est fixé par le job sur son scope avant d'ouvrir la base.
/// </summary>
internal sealed class JobDossierSource : IRequestedDossierSource
{
    public DossierId? RequestedDossier { get; set; }
}
