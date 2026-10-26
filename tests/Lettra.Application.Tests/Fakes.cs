using System.Collections.Concurrent;
using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Mapping;
using Lettra.Contracts.Common;
using Lettra.Contracts.Customers;
using Lettra.Domain.Common;
using Lettra.Domain.Parties;
using Lettra.Domain.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Lettra.Application.Tests;

internal sealed class FakeTenantContext : ITenantContext
{
    public TenantDescriptor? Descriptor { get; set; }

    public TenantDescriptor Current => Descriptor ?? throw new InvalidOperationException();

    public bool IsResolved => Descriptor is not null;
}

internal sealed class FakeUser : ICurrentUser
{
    public bool IsAuthenticated { get; set; } = true;

    public Guid UserId { get; set; } = Guid.CreateVersion7();
}

internal sealed class FakePermissions : IPermissionService
{
    public HashSet<Permission> Granted { get; } = [.. Enum.GetValues<Permission>()];

    public List<AuthorizationScope> Asked { get; } = [];

    public ValueTask<IReadOnlySet<Permission>> GetEffectiveAsync(Guid userId, AuthorizationScope scope, CancellationToken cancellationToken)
    {
        Asked.Add(scope);
        return ValueTask.FromResult<IReadOnlySet<Permission>>(Granted);
    }
}

/// <summary>Faux dépôt : le « commit » ne rend visible que ce qui a été sauvegardé.</summary>
internal sealed class InMemoryCustomers : ICustomerRepository, ICustomerReadStore, IUnitOfWork
{
    private readonly List<Customer> _pending = [];

    public ConcurrentDictionary<CustomerId, Customer> Saved { get; } = new();

    public Task AddAsync(Customer customer, CancellationToken cancellationToken)
    {
        _pending.Add(customer);
        return Task.CompletedTask;
    }

    public Task<Customer?> FindAsync(CustomerId id, CancellationToken cancellationToken) =>
        Task.FromResult(Saved.GetValueOrDefault(id));

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        foreach (Customer customer in _pending)
        {
            Saved[customer.Id] = customer;
        }

        int count = _pending.Count;
        _pending.Clear();
        return Task.FromResult(count);
    }

    public PartyListFilter? LastFilter { get; private set; }

    public Task<PagedResponse<CustomerSummary>> ListAsync(PartyListFilter filter, CancellationToken cancellationToken)
    {
        LastFilter = filter;
        (int page, int pageSize) = (filter.Page, filter.PageSize);
        // Comme le stockage : un client supprimé n'est plus listé.
        List<Customer> listed = [.. Saved.Values.Where(c => !c.IsArchived)];
        List<CustomerSummary> items = listed
            .OrderBy(c => c.Name, StringComparer.Ordinal)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(c => new CustomerSummary(c.Id.Value, (CustomerKindDto)(int)c.Kind, c.Name, c.Siren?.Value, c.BillingAddress.City))
            .ToList();
        return Task.FromResult(new PagedResponse<CustomerSummary>(items, page, pageSize, listed.Count));
    }

    public Task<CustomerResponse?> GetAsync(CustomerId id, CancellationToken cancellationToken) =>
        Task.FromResult(Saved.GetValueOrDefault(id)?.ToResponse());
}

/// <summary>Capture tout ce qui est journalisé (message formaté et valeurs des paramètres).</summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    public ConcurrentQueue<string> Entries { get; } = new();

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(this);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(CapturingLoggerProvider owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            string values = state is IEnumerable<KeyValuePair<string, object?>> pairs
                ? string.Join('|', pairs.Select(p => $"{p.Key}={p.Value}"))
                : string.Empty;
            owner.Entries.Enqueue($"{formatter(state, exception)} {values} {exception}");
        }
    }
}

/// <summary>Faux provisionneur : enregistre les créations demandées.</summary>
internal sealed class RecordingDossierProvisioner : IDossierProvisioner
{
    public List<(OrganisationId Organisation, string Name)> Created { get; } = [];

    public Task<Result<DossierId>> CreateAsync(OrganisationId organisation, string name, CancellationToken cancellationToken)
    {
        Created.Add((organisation, name));
        return Task.FromResult<Result<DossierId>>(new DossierId(Guid.CreateVersion7()));
    }
}

/// <summary>Registre des droits vide : ni utilisateur, ni portée connus.</summary>
internal sealed class EmptyAuthorizationDirectory : IAuthorizationDirectory
{
    public ValueTask<UserAuthorizationProfile?> FindUserAsync(Guid userId, CancellationToken cancellationToken) =>
        ValueTask.FromResult<UserAuthorizationProfile?>(null);

    public ValueTask<ScopePath?> GetScopePathAsync(AuthorizationScope scope, CancellationToken cancellationToken) =>
        ValueTask.FromResult<ScopePath?>(null);
}

internal sealed class EmptyDossierDirectory : IDossierDirectory
{
    public Task<IReadOnlyList<Lettra.Contracts.Dossiers.DossierSummary>> ListAsync(OrganisationId organisation, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Lettra.Contracts.Dossiers.DossierSummary>>([]);
}

/// <summary>Vue cabinet en mémoire : indicateurs rangés, recalcul qui ne fait rien, coches de revue.</summary>
internal sealed class InMemoryCabinetStores :
    Lettra.Application.Abstractions.Cabinet.IDossierIndicatorStore,
    Lettra.Application.Abstractions.Cabinet.IDossierIndicatorRefresher,
    Lettra.Application.Abstractions.Cabinet.IDossierReviewStore
{
    private readonly ConcurrentDictionary<DossierId, Lettra.Application.Abstractions.Cabinet.StoredDossierIndicators> _indicators = new();
    private readonly ConcurrentDictionary<(string Period, string Item), Lettra.Domain.Review.DossierReviewMark> _marks = new();

    public Task ReplaceAsync(DossierId dossier, IReadOnlyList<Lettra.Application.Abstractions.Cabinet.DossierIndicator> indicators, DateTimeOffset computedAt, CancellationToken cancellationToken)
    {
        _indicators[dossier] = new(dossier, computedAt, indicators);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Lettra.Application.Abstractions.Cabinet.StoredDossierIndicators>> ReadAsync(IReadOnlyCollection<DossierId> dossiers, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Lettra.Application.Abstractions.Cabinet.StoredDossierIndicators>>(
            [.. dossiers.Where(_indicators.ContainsKey).Select(d => _indicators[d])]);

    public Task<Lettra.Application.Abstractions.Cabinet.IndicatorRefreshOutcome> RefreshAsync(IReadOnlyCollection<DossierId> dossiers, CancellationToken cancellationToken) =>
        Task.FromResult(new Lettra.Application.Abstractions.Cabinet.IndicatorRefreshOutcome(0, 0));

    public Task<IReadOnlyList<Lettra.Domain.Review.DossierReviewMark>> ListAsync(Lettra.Domain.Review.ReviewPeriod period, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Lettra.Domain.Review.DossierReviewMark>>([.. _marks.Values.Where(m => m.Period == period.ToString())]);

    public Task SaveAsync(Lettra.Domain.Review.DossierReviewMark mark, CancellationToken cancellationToken)
    {
        _marks[(mark.Period, mark.ItemKey)] = mark;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(Lettra.Domain.Review.ReviewPeriod period, string itemKey, CancellationToken cancellationToken)
    {
        _marks.TryRemove((period.ToString(), itemKey), out _);
        return Task.CompletedTask;
    }
}

/// <summary>Mission et temps passés en mémoire.</summary>
internal sealed class InMemoryMissionStore : Lettra.Application.Abstractions.Cabinet.IMissionStore
{
    private readonly ConcurrentDictionary<Guid, Lettra.Domain.Missions.TimeEntry> _time = new();
    private Lettra.Domain.Missions.Mission? _mission;

    public Task<Lettra.Domain.Missions.Mission?> FindAsync(CancellationToken cancellationToken) => Task.FromResult(_mission);

    public Task AddAsync(Lettra.Domain.Missions.Mission mission, CancellationToken cancellationToken)
    {
        _mission = mission;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Lettra.Domain.Missions.TimeEntry>> ListTimeAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Lettra.Domain.Missions.TimeEntry>>(
            [.. _time.Values.Where(t => t.Date >= from && t.Date <= to).OrderByDescending(t => t.Date)]);

    public Task<Lettra.Domain.Missions.TimeEntry?> FindTimeAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_time.GetValueOrDefault(id));

    public Task AddTimeAsync(Lettra.Domain.Missions.TimeEntry entry, CancellationToken cancellationToken)
    {
        _time[entry.Id] = entry;
        return Task.CompletedTask;
    }

    public Task RemoveTimeAsync(Lettra.Domain.Missions.TimeEntry entry, CancellationToken cancellationToken)
    {
        _time.TryRemove(entry.Id, out _);
        return Task.CompletedTask;
    }
}

/// <summary>Fiche anti-blanchiment en mémoire.</summary>
internal sealed class InMemoryVigilanceStore : Lettra.Application.Abstractions.Cabinet.IVigilanceStore
{
    public Lettra.Domain.Vigilance.ClientDueDiligence? File { get; private set; }

    public Task<Lettra.Domain.Vigilance.ClientDueDiligence?> FindAsync(CancellationToken cancellationToken) => Task.FromResult(File);

    public Task AddAsync(Lettra.Domain.Vigilance.ClientDueDiligence file, CancellationToken cancellationToken)
    {
        File = file;
        return Task.CompletedTask;
    }
}

/// <summary>Attestations de vigilance en mémoire ; les fournisseurs viennent de <see cref="Suppliers"/> et des attestations gardées.</summary>
internal sealed class InMemoryAttestationStore : Lettra.Application.Abstractions.Persistence.IAttestationStore
{
    private readonly ConcurrentDictionary<SupplierId, Lettra.Domain.Vigilance.SubcontractorAttestation> _attestations = new();

    public List<(SupplierId Id, string Name)> Suppliers { get; } = [];

    public Task<Lettra.Domain.Vigilance.SubcontractorAttestation?> FindAsync(SupplierId supplier, CancellationToken cancellationToken) =>
        Task.FromResult(_attestations.GetValueOrDefault(supplier));

    public Task AddAsync(Lettra.Domain.Vigilance.SubcontractorAttestation attestation, CancellationToken cancellationToken)
    {
        _attestations[attestation.Id] = attestation;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Lettra.Application.Abstractions.Persistence.SupplierAttestationEntry>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Lettra.Application.Abstractions.Persistence.SupplierAttestationEntry>>(
            [
                .. Suppliers.Select(s => new Lettra.Application.Abstractions.Persistence.SupplierAttestationEntry(s.Id, s.Name, _attestations.GetValueOrDefault(s.Id))),
                .. _attestations.Where(a => Suppliers.All(s => s.Id != a.Key))
                    .Select(a => new Lettra.Application.Abstractions.Persistence.SupplierAttestationEntry(a.Key, a.Key.Value.ToString(), a.Value)),
            ]);
}

/// <summary>Tableau de bord d'un dossier vide : aucune pièce.</summary>
internal sealed class NoBillingKpis : Lettra.Application.Features.Kpi.IBillingKpiReader
{
    public Task<Lettra.Application.Features.Kpi.BillingKpiFacts> ReadAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new Lettra.Application.Features.Kpi.BillingKpiFacts([], []));
}

internal static class CabinetFakesRegistration
{
    /// <summary>Les ports de la vue cabinet, pour que le conteneur des tests se valide.</summary>
    public static IServiceCollection AddCabinetFakes(this IServiceCollection services)
    {
        InMemoryCabinetStores stores = new();
        services.AddSingleton<Lettra.Application.Abstractions.Cabinet.IDossierIndicatorStore>(stores);
        services.AddSingleton<Lettra.Application.Abstractions.Cabinet.IDossierIndicatorRefresher>(stores);
        services.AddSingleton<Lettra.Application.Abstractions.Cabinet.IDossierReviewStore>(stores);
        services.AddSingleton<Lettra.Application.Abstractions.Cabinet.IMissionStore>(new InMemoryMissionStore());
        services.AddSingleton<Lettra.Application.Abstractions.Cabinet.IVigilanceStore>(new InMemoryVigilanceStore());
        services.AddSingleton<Lettra.Application.Abstractions.Persistence.IAttestationStore>(new InMemoryAttestationStore());
        services.AddSingleton<Lettra.Application.Features.Kpi.IBillingKpiReader>(new NoBillingKpis());
        return services.AddRecurringFakes();
    }
}
