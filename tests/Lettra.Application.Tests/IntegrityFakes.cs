using System.Runtime.CompilerServices;
using Lettra.Application.Abstractions.Integrity;
using Lettra.Domain.Common;
using Lettra.Domain.Integrity;
using Lettra.Domain.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Application.Tests;

/// <summary>Faux journal du dossier : la chaîne est construite par le domaine, le sceau est une simple étiquette.</summary>
internal sealed class InMemoryIntegrity(DossierId dossier) : IIntegrityStore, IIntegrityJournal, ISealedArchiveBuilder, IIntegrityKeyring, IIntegrityKeys, IDossierAlertPublisher
{
    public List<JournalEntry> Entries { get; } = [];

    public Dictionary<string, long> Series { get; } = new(StringComparer.Ordinal);

    public List<DossierAlertSignal> Signals { get; } = [];

    public string KeyId => "fake";

    public string PublicKey => "fake";

    public Task<Result<long>> AppendAsync(DocumentLifecycleEvent @event, CancellationToken cancellationToken)
    {
        long sequence = Entries.Count + 1;
        string previous = Entries.Count == 0 ? JournalEntry.GenesisHash(dossier) : Entries[^1].Hash;
        Result<JournalEntry> entry = JournalEntry.ForDocument(@event, sequence, previous, DateTimeOffset.UnixEpoch);
        if (entry.IsFailure)
        {
            return Task.FromResult(Result<long>.Failure(entry.Errors));
        }

        entry.Value.ApplySeal(Seal(entry.Value.Hash));
        Entries.Add(entry.Value);
        return Task.FromResult(Result<long>.Success(sequence));
    }

    public Task<IReadOnlyList<PeriodClosing>> ListClosingsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PeriodClosing>>([]);

    public async IAsyncEnumerable<JournalEntry> ReadEntriesAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Yield();
        foreach (JournalEntry entry in Entries)
        {
            yield return entry;
        }
    }

    public Task<DateTimeOffset?> FirstEntryAtAsync(CancellationToken cancellationToken) =>
        Task.FromResult<DateTimeOffset?>(Entries.Count == 0 ? null : Entries[0].OccurredAt);

    public Task<IReadOnlyDictionary<string, long>> NextNumbersAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<string, long>>(Series);

    public Task<Result<PeriodClosing>> CloseAsync(ClosingPeriod period, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Les clôtures sont testées avec la vraie persistance.");

    public Task<Result<SealedArchive>> BuildAsync(int closingNumber, CancellationToken cancellationToken) =>
        Task.FromResult<Result<SealedArchive>>(IntegrityErrors.ClosingNotFound);

    public ValueTask<Result<IIntegrityKeys>> GetAsync(OrganisationId cabinet, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Result<IIntegrityKeys>.Success(this));

    public string Seal(string entryHash) => "seal:" + entryHash;

    public string Sign(string closingHash) => "sig:" + closingHash;

    public bool VerifySeal(string entryHash, string seal) => seal == Seal(entryHash);

    public bool VerifyClosingSignature(string keyId, string publicKey, string closingHash, string signature) => signature == Sign(closingHash);

    public Task PublishAsync(DossierAlertSignal signal, CancellationToken cancellationToken)
    {
        Signals.Add(signal);
        return Task.CompletedTask;
    }
}

internal static class IntegrityFakesRegistration
{
    public static IServiceCollection AddIntegrityFakes(this IServiceCollection services, InMemoryIntegrity integrity)
    {
        services.AddSingleton<IIntegrityStore>(integrity);
        services.AddSingleton<IIntegrityJournal>(integrity);
        services.AddSingleton<ISealedArchiveBuilder>(integrity);
        services.AddSingleton<IIntegrityKeyring>(integrity);
        services.AddSingleton<IDossierAlertPublisher>(integrity);
        return services;
    }
}
