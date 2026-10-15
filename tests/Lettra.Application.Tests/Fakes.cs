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

    public Task<PagedResponse<CustomerSummary>> ListAsync(string? search, int page, int pageSize, CancellationToken cancellationToken)
    {
        List<CustomerSummary> items = Saved.Values
            .OrderBy(c => c.Name, StringComparer.Ordinal)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(c => new CustomerSummary(c.Id.Value, (CustomerKindDto)(int)c.Kind, c.Name, c.Siren?.Value, c.BillingAddress.City))
            .ToList();
        return Task.FromResult(new PagedResponse<CustomerSummary>(items, page, pageSize, Saved.Count));
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
