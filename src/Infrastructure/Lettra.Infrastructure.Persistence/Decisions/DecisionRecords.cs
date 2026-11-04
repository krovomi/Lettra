using System.Text.Json;
using System.Text.Json.Serialization;
using Lettra.Application.Abstractions.Decisions;
using Lettra.Domain.Decisions;
using Lettra.Infrastructure.Persistence.Concurrency;
using Lettra.Infrastructure.Persistence.Integrity;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lettra.Infrastructure.Persistence.Decisions;

/// <summary>
/// Les décisions à prendre, dans la base du dossier : une ligne par clé (type + sujet), ouverte ou close. Les faits des
/// phrases sont gardés en JSON. Pas d'index unique sur la clé : deux actualisations simultanées ne doivent pas échouer,
/// l'actualisation suivante retire un doublon éventuel.
/// </summary>
internal sealed class DecisionConfiguration : IEntityTypeConfiguration<Decision>
{
    public void Configure(EntityTypeBuilder<Decision> builder)
    {
        builder.ToTable("Decisions");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).HasConversion(id => id.Value, value => new DecisionId(value)).ValueGeneratedNever();
        builder.Property(d => d.Kind).HasMaxLength(Decision.MaxKindLength).IsRequired();
        builder.Property(d => d.Subject).HasMaxLength(Decision.MaxSubjectLength).IsRequired();
        builder.Property(d => d.Link).HasMaxLength(Decision.MaxLinkLength).IsRequired();
        builder.Property(d => d.ActionKind).HasMaxLength(Decision.MaxKindLength);
        builder.Property(d => d.ActionPayload).HasMaxLength(Decision.MaxPayloadLength);
        builder.Property(d => d.Impact).HasPrecision(18, 2);
        builder.Property(d => d.Facts)
            .HasConversion(v => DecisionFactsJson.Write(v), v => DecisionFactsJson.Read(v), DecisionFactsJson.Comparer)
            .HasMaxLength(DecisionFactsJson.MaxLength)
            .IsRequired();

        // Comparée à une date dans les requêtes (historique de trente jours) : en millisecondes, comme le journal d'intégrité.
        builder.Property(d => d.ClosedAt).HasConversion(UnixMilliseconds.Converter);
        builder.Ignore(d => d.HasAction);
        builder.Ignore(d => d.IsOpen);
        builder.HasIndex(d => new { d.Kind, d.Subject });
        builder.HasIndex(d => d.Status);
        builder.IsVersioned();
    }
}

internal sealed class DecisionPolicyConfiguration : IEntityTypeConfiguration<DecisionPolicy>
{
    public void Configure(EntityTypeBuilder<DecisionPolicy> builder)
    {
        builder.ToTable("DecisionPolicy");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.IsVersioned();
    }
}

internal static class DecisionFactsJson
{
    /// <summary>Douze faits de 40 + 200 caractères au plus, avec la syntaxe JSON.</summary>
    public const int MaxLength = 4000;

    public static readonly ValueComparer<IReadOnlyDictionary<string, string>> Comparer = new(
        (a, b) => Write(a) == Write(b),
        v => Write(v).GetHashCode(StringComparison.Ordinal),
        v => Read(Write(v)));

    public static string Write(IReadOnlyDictionary<string, string>? facts) =>
        JsonSerializer.Serialize(new Dictionary<string, string>(facts ?? new Dictionary<string, string>(), StringComparer.Ordinal), DecisionJsonContext.Default.DictionaryStringString);

    public static IReadOnlyDictionary<string, string> Read(string json) =>
        JsonSerializer.Deserialize(json, DecisionJsonContext.Default.DictionaryStringString) ?? [];
}

[JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class DecisionJsonContext : JsonSerializerContext;

internal sealed class EfDecisionStore(TenantDbSession session) : IDecisionStore
{
    public async Task<IReadOnlyList<Decision>> ListForRefreshAsync(CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        return await db.Set<Decision>().AsTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Decision>> ListAsync(DateTimeOffset closedSince, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        return await db.Set<Decision>().AsNoTracking()
            .Where(d => d.Status == DecisionStatus.Proposed || d.Status == DecisionStatus.Snoozed || d.ClosedAt >= closedSince)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<Decision?> FindAsync(DecisionId id, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        return await db.Set<Decision>().AsTracking().FirstOrDefaultAsync(d => d.Id == id, cancellationToken).ConfigureAwait(false);
    }

    public async Task AddAsync(Decision decision, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        await db.Set<Decision>().AddAsync(decision, cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveAsync(Decision decision, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        db.Set<Decision>().Remove(decision);
    }

    public async Task<DecisionPolicy?> FindPolicyAsync(CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        return await db.Set<DecisionPolicy>().AsTracking().FirstOrDefaultAsync(p => p.Id == DecisionPolicy.SingletonId, cancellationToken).ConfigureAwait(false);
    }

    public async Task AddPolicyAsync(DecisionPolicy policy, CancellationToken cancellationToken)
    {
        LettraDbContext db = await session.GetAsync(cancellationToken).ConfigureAwait(false);
        await db.Set<DecisionPolicy>().AddAsync(policy, cancellationToken).ConfigureAwait(false);
    }
}
