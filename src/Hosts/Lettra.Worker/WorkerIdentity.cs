using Lettra.Application.Abstractions.Identity;
using Microsoft.Extensions.Options;

namespace Lettra.Worker;

/// <summary>
/// Qui agit quand le Worker exécute un cas d'usage protégé : un compte de service (le <c>sub</c> du compte de service
/// Keycloak de la cellule), déclaré au registre des droits comme tout utilisateur. Ses rôles, donc ce qu'il peut faire,
/// viennent du registre ; sans identifiant configuré, le Worker reste anonyme et tout cas d'usage protégé est refusé.
/// </summary>
public sealed class WorkerIdentityOptions
{
    public const string SectionName = "Lettra:Worker:Identity";

    public Guid UserId { get; set; }
}

internal sealed class WorkerCurrentUser(IOptions<WorkerIdentityOptions> options) : ICurrentUser
{
    public bool IsAuthenticated => options.Value.UserId != Guid.Empty;

    public Guid UserId => options.Value.UserId;
}
