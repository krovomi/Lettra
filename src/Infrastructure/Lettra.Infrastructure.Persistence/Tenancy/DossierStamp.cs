using Lettra.Application.Abstractions.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lettra.Infrastructure.Persistence.Tenancy;

/// <summary>
/// Estampille écrite dans chaque base à sa création : « cette base appartient au dossier X du cabinet Y ».
/// La fabrique la relit avant de rendre un <see cref="LettraDbContext"/> et refuse une base qui ne
/// correspond pas au dossier actif (garde-fou contre une erreur de routage du catalogue ou de PgBouncer).
/// </summary>
internal sealed class DossierStamp
{
    public const int SingletonId = 1;

    private DossierStamp()
    {
    }

    public DossierStamp(TenantDescriptor tenant, DateTimeOffset createdAt)
    {
        DossierId = tenant.DossierId.Value;
        OrganisationId = tenant.OrganisationId.Value;
        CreatedAt = createdAt;
    }

    public int Id { get; private set; } = SingletonId;

    public Guid DossierId { get; private set; }

    public Guid OrganisationId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public bool BelongsTo(TenantDescriptor tenant) =>
        DossierId == tenant.DossierId.Value && OrganisationId == tenant.OrganisationId.Value;
}

internal sealed class DossierStampConfiguration : IEntityTypeConfiguration<DossierStamp>
{
    public void Configure(EntityTypeBuilder<DossierStamp> builder)
    {
        builder.ToTable("DossierStamp");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
    }
}

/// <summary>
/// La base ouverte n'appartient pas au dossier actif. Ne doit jamais arriver : si c'est le cas, la requête
/// est bloquée avant d'avoir lu ou écrit quoi que ce soit.
/// </summary>
public sealed class DossierMismatchException : Exception
{
    public DossierMismatchException()
    {
    }

    public DossierMismatchException(string message)
        : base(message)
    {
    }

    public DossierMismatchException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    internal static DossierMismatchException For(TenantDescriptor expected, Guid? actual) =>
        new($"La base ouverte appartient au dossier {actual?.ToString() ?? "inconnu"}, attendu {expected.DossierId.Value}. Requête bloquée.");
}

internal static class DossierGuard
{
    /// <summary>Une lecture par clé primaire sur une table d'une ligne (≈ 1 ms, mesuré par le POC).</summary>
    public static async Task EnsureBelongsToAsync(LettraDbContext context, TenantDescriptor tenant, CancellationToken cancellationToken)
    {
        DossierStamp? stamp = await context.DossierStamp.AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == DossierStamp.SingletonId, cancellationToken)
            .ConfigureAwait(false);

        if (stamp is null || !stamp.BelongsTo(tenant))
        {
            throw DossierMismatchException.For(tenant, stamp?.DossierId);
        }
    }
}
