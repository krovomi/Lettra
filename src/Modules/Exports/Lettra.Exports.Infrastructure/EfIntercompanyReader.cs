using Lettra.Application.Abstractions.Tenancy;
using Lettra.Domain.Banking;
using Lettra.Domain.Billing;
using Lettra.Domain.Parties;
using Lettra.Domain.SharedKernel;
using Lettra.Exports.Application.Banking;
using Lettra.Infrastructure.Persistence;
using Lettra.Infrastructure.Persistence.EInvoicing;
using Lettra.Infrastructure.Persistence.Integrity;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Lettra.Exports.Infrastructure;

/// <summary>
/// Ouvre chaque dossier dans sa propre portée (comme la vue cabinet) et y lit ce que les comptes intra-groupe
/// comparent. Encaissé : le rapprochement ou la plateforme, le plus grand des deux (comme les créances). Les virements sont reconnus par l'empreinte de l'IBAN de l'autre société, calculée avec la clé du
/// dossier lu : les IBAN des relevés ne sont jamais gardés en clair. Lecture seule ; un dossier en échec est ignoré
/// et journalisé sans aucune de ses données.
/// </summary>
internal sealed partial class EfIntercompanyReader(IServiceScopeFactory scopes, ITenantCatalog catalog, ILogger<EfIntercompanyReader> logger) : IIntercompanyReader
{
    public Task<GroupCompanyIdentity?> ReadCompanyAsync(DossierId dossier, CancellationToken cancellationToken) =>
        InDossierAsync(dossier, async (provider, token) =>
        {
            LettraDbContext db = await provider.GetRequiredService<TenantDbSession>().GetAsync(token).ConfigureAwait(false);
            CompanyProfile? profile = await db.Set<CompanyProfile>().AsNoTracking().FirstOrDefaultAsync(token).ConfigureAwait(false);
            return profile is null ? null : new GroupCompanyIdentity(profile.Siren?.Value, profile.Iban?.Value);
        }, cancellationToken);

    public Task<IntercompanyFacts?> ReadFactsAsync(DossierId dossier, IReadOnlyList<GroupCompanyKey> others, DateOnly since, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(others);
        return InDossierAsync(dossier, async (provider, token) =>
        {
            LettraDbContext db = await provider.GetRequiredService<TenantDbSession>().GetAsync(token).ConfigureAwait(false);
            string[] sirens = [.. others.Select(o => o.Siren)];
            var invoices = await db.Set<Invoice>()
                .AsNoTracking()
                .Where(i => i.Kind != InvoiceKind.CreditNote && i.Status != InvoiceStatus.Draft && i.Number != null && i.IssueDate != null && i.IssueDate >= since)
                .Where(i => i.Customer.Siren != null && sirens.Contains(i.Customer.Siren))
                .Select(i => new { i.Id, i.Number, i.IssueDate, i.Customer.Siren, i.TotalInclTax, i.CreditedInclTax, i.PaidInclTax })
                .ToListAsync(token)
                .ConfigureAwait(false);
            Guid[] ids = [.. invoices.Select(i => i.Id.Value)];
            List<PlatformTransmissionRecord> records = await db.Set<PlatformTransmissionRecord>()
                .AsNoTracking()
                .Include(r => r.Events)
                .Where(r => ids.Contains(r.InvoiceId))
                .ToListAsync(token)
                .ConfigureAwait(false);
            Dictionary<Guid, decimal> collected = records
                .GroupBy(r => r.InvoiceId)
                .ToDictionary(g => g.Key, g => EfPlatformTransmissionStore.ToTransmission(EfPlatformTransmissionStore.Latest(g)!).CollectedAmount);
            List<Supplier> suppliers = await db.Set<Supplier>().AsNoTracking().Where(s => !s.IsArchived && s.Siren != null).ToListAsync(token).ConfigureAwait(false);

            IIbanFingerprints fingerprints = provider.GetRequiredService<IIbanFingerprints>();
            Dictionary<string, string> payees = new(StringComparer.Ordinal);
            foreach (GroupCompanyKey other in others)
            {
                if (await fingerprints.FingerprintAsync(other.Iban, token).ConfigureAwait(false) is { } hash)
                {
                    payees[hash] = other.Siren;
                }
            }

            string[] hashes = [.. payees.Keys];
            var debits = await db.Set<BankStatementLine>()
                .AsNoTracking()
                .Where(l => l.BookingDate >= since && l.Amount < 0m && l.CounterpartyIbanHash != null && hashes.Contains(l.CounterpartyIbanHash))
                .Select(l => new { l.BookingDate, l.Amount, l.Label, l.Reference, Hash = l.CounterpartyIbanHash! })
                .ToListAsync(token)
                .ConfigureAwait(false);
            bool hasBankLines = payees.Count > 0
                && await db.Set<BankStatementLine>().AsNoTracking().AnyAsync(l => l.BookingDate >= since, token).ConfigureAwait(false);

            return new IntercompanyFacts(
                [.. invoices.Select(i => new IntercompanySale(i.Id.Value, i.Number!, i.IssueDate!.Value, i.Siren!, i.TotalInclTax - i.CreditedInclTax, Math.Max(i.PaidInclTax, collected.GetValueOrDefault(i.Id.Value))))],
                [.. suppliers.Where(s => sirens.Contains(s.Siren!.Value)).Select(s => new IntercompanySupplier(s.Siren!.Value, s.Iban?.Value))],
                [.. debits.Select(d => new IntercompanyDebit(payees[d.Hash], d.BookingDate, -d.Amount, $"{d.Label} {d.Reference}"))],
                hasBankLines);
        }, cancellationToken);
    }

    private async Task<T?> InDossierAsync<T>(DossierId dossier, Func<IServiceProvider, CancellationToken, Task<T?>> read, CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            TenantDescriptor? descriptor = await catalog.FindAsync(dossier, cancellationToken).ConfigureAwait(false);
            return descriptor is null ? null : await DossierJobScope.RunAsync(scopes, descriptor, read, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogDossierFailed(dossier.Value, exception);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Comptes intra-groupe : lecture du dossier {DossierId} en échec")]
    private partial void LogDossierFailed(Guid dossierId, Exception exception);
}
