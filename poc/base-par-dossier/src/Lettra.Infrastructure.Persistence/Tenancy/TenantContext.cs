using Lettra.Application.Abstractions.Tenancy;

namespace Lettra.Infrastructure.Persistence.Tenancy;

/// Porte le dossier actif pour la portée courante (requête HTTP, circuit Blazor, job du Worker).
public sealed class TenantContext : ITenantContext
{
    private TenantDescriptor? _current;

    public TenantDescriptor Current =>
        _current ?? throw new TenantNotFoundException("Aucun dossier actif.");

    public bool IsResolved => _current is not null;

    public void Set(TenantDescriptor descriptor)
    {
        if (_current is not null && _current.DossierId != descriptor.DossierId)
            throw new InvalidOperationException("Le dossier actif ne change pas au milieu d'une portée.");
        _current = descriptor;
    }
}
