using Lettra.Application.Abstractions.Tenancy;
using Lettra.Contracts.Common;
using Lettra.Domain.SharedKernel;

namespace Lettra.Api.Infrastructure;

/// <summary>Dossier demandé : en-tête <c>X-Dossier</c>. Toujours revérifié contre le catalogue et les droits.</summary>
internal sealed class HeaderDossierSource(IHttpContextAccessor accessor) : IRequestedDossierSource
{
    public DossierId? RequestedDossier =>
        Guid.TryParse(accessor.HttpContext?.Request.Headers[LettraHeaders.Dossier], out Guid id) && id != Guid.Empty
            ? new DossierId(id)
            : null;
}
