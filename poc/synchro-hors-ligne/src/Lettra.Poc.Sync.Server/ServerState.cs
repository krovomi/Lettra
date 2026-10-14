using System.Security.Cryptography;
using System.Text.Json;
using Lettra.Poc.Sync.Contracts;
using Lettra.Poc.Sync.Domain;

namespace Lettra.Poc.Sync.Server;

public sealed record Device(Guid Id, string SeriesCode, string Secret)
{
    public bool Revoked { get; set; }
    public string? RefreshToken { get; set; }
    /// <summary>
    /// Jeton précédent, encore accepté tant que le nouveau n'a pas servi : si la réponse d'un
    /// rafraîchissement se perd, le poste n'est pas bloqué (trouvé par le POC, cf. README).
    /// </summary>
    public string? PreviousRefreshToken { get; set; }
    public DateTimeOffset RefreshExpiresAt { get; set; }
}

/// <summary>
/// État serveur d'un dossier, en mémoire et sous verrou : le POC prouve les règles de synchro,
/// pas la persistance. En vrai : base du dossier, outbox et table d'idempotence en PostgreSQL.
/// </summary>
public sealed class ServerState(TimeProvider clock)
{
    public static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromMinutes(15);
    /// <summary>Durée maximale hors ligne fixée par l'organisation (ADR-0008, garde-fous).</summary>
    public static readonly TimeSpan MaxOfflineDuration = TimeSpan.FromDays(7);

    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, Invoice> _invoices = [];
    private readonly Dictionary<string, (long LastSequence, string? LastHash)> _series = [];
    private readonly Dictionary<Guid, (string RequestHash, object Result)> _idempotency = [];
    private readonly List<(long Seq, Guid InvoiceId)> _changeLog = [];
    private readonly Dictionary<Guid, Device> _devices = [];
    private readonly Dictionary<string, (Guid DeviceId, DateTimeOffset ExpiresAt)> _accessTokens = [];
    private long _seq;

    /// <summary>Nombre de fois où un fait ou une intention a réellement modifié l'état (hors rejeu).</summary>
    public int EffectiveWrites { get; private set; }

    // ----------------------------------------------------------------- appareils et jetons

    public Device EnrollDevice(Guid deviceId, string seriesCode, string secret = "pin-1234")
    {
        lock (_gate)
        {
            var device = new Device(deviceId, seriesCode, secret);
            _devices[deviceId] = device;
            return device;
        }
    }

    public void RevokeDevice(Guid deviceId) { lock (_gate) _devices[deviceId].Revoked = true; }

    public AuthResult Login(Guid deviceId, string secret)
    {
        lock (_gate)
        {
            if (!_devices.TryGetValue(deviceId, out var d) || d.Secret != secret) return AuthResult.Unauthorized;
            return d.Revoked ? AuthResult.Revoked : Issue(d);
        }
    }

    public AuthResult Refresh(Guid deviceId, string refreshToken)
    {
        lock (_gate)
        {
            if (!_devices.TryGetValue(deviceId, out var d)) return AuthResult.Unauthorized;
            if (d.Revoked) return AuthResult.Revoked;
            if (clock.GetUtcNow() > d.RefreshExpiresAt) return AuthResult.Unauthorized;
            if (refreshToken == d.RefreshToken) return Issue(d);
            if (refreshToken == d.PreviousRefreshToken) { d.RefreshToken = d.PreviousRefreshToken; return Issue(d); }
            return AuthResult.Unauthorized;
        }
    }

    /// <summary>Retourne l'appareil si le jeton est valide ; null sinon. Revoked est vérifié par l'appelant.</summary>
    public Device? Authenticate(string accessToken)
    {
        lock (_gate)
        {
            if (!_accessTokens.TryGetValue(accessToken, out var t) || clock.GetUtcNow() > t.ExpiresAt) return null;
            return _devices[t.DeviceId];
        }
    }

    private AuthResult Issue(Device d)
    {
        var now = clock.GetUtcNow();
        d.PreviousRefreshToken = d.RefreshToken;
        d.RefreshToken = NewToken();
        d.RefreshExpiresAt = now + MaxOfflineDuration;
        var access = NewToken();
        _accessTokens[access] = (d.Id, now + AccessTokenLifetime);
        return new AuthResult(AuthStatus.Ok, new TokenResponse(access, now + AccessTokenLifetime, d.RefreshToken));
    }

    private static string NewToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));

    // ----------------------------------------------------------------- faits

    public FactResult ApplyFact(Device device, ValidatedInvoiceFact fact)
        => Idempotent(fact.IdempotencyKey, fact, () => ApplyFactCore(device, fact));

    private FactResult ApplyFactCore(Device device, ValidatedInvoiceFact fact)
    {
        FactResult Result(FactOutcome o, string? detail = null) => new(fact.IdempotencyKey, fact.Invoice.Id, o, detail);
        var dto = fact.Invoice;

        if (fact.SeriesCode != device.SeriesCode)
            return Result(FactOutcome.Rejected, $"Le poste {device.SeriesCode} ne peut pas écrire dans la série {fact.SeriesCode}.");

        if (_invoices.TryGetValue(dto.Id, out var existing) && existing.Status == InvoiceStatus.Validated)
            return existing.ChainHash == dto.ChainHash
                ? Result(FactOutcome.AlreadyKnown)
                : Result(FactOutcome.Rejected, $"La facture {existing.Number} est déjà validée avec un autre contenu.");

        // Continuité de la série : pas de trou, pas de doublon, chaîne de hash ininterrompue.
        var (last, lastHash) = _series.GetValueOrDefault(fact.SeriesCode);
        if (fact.SequenceNumber != last + 1)
            return Result(FactOutcome.Rejected, $"Rupture de série {fact.SeriesCode} : attendu {last + 1}, reçu {fact.SequenceNumber}.");
        if (fact.PreviousHash != lastHash)
            return Result(FactOutcome.Rejected, $"Chaîne de hash rompue dans la série {fact.SeriesCode}.");

        // Revérification : on refait la validation côté serveur avec les mêmes règles déterministes.
        var replay = Invoice.NewDraft(dto.Id, dto.DossierId, dto.CustomerName, dto.Lines, dto.DueDate, dto.Notes);
        var expectedNumber = InvoiceNumber.Format(dto.IssueDate!.Value.Year, fact.SeriesCode, fact.SequenceNumber);
        try { replay.Validate(expectedNumber, dto.IssueDate.Value, fact.PreviousHash); }
        catch (ComplianceException e) { return Result(FactOutcome.Rejected, "Non conforme : " + e.Message); }

        if (replay.Number != dto.Number || replay.ChainHash != dto.ChainHash
            || replay.TotalInclTax != dto.TotalInclTax || replay.TotalExclTax != dto.TotalExclTax)
            return Result(FactOutcome.Rejected, "Le contenu ne correspond pas au maillon de chaîne transmis.");

        // Un fait ne se discute pas : il remplace le brouillon serveur, même modifié entre-temps.
        string? detail = null;
        if (existing is not null && fact.DraftBaseVersion is { } baseVersion && existing.Version > baseVersion)
            detail = $"Brouillon modifié sur le serveur (v{baseVersion} → v{existing.Version}) remplacé par la facture validée {dto.Number}.";

        var stored = Invoice.FromDto(dto with { Version = (existing?.Version ?? 0) + 1 });
        _invoices[dto.Id] = stored;
        _series[fact.SeriesCode] = (fact.SequenceNumber, dto.ChainHash);
        LogChange(dto.Id);
        EffectiveWrites++;
        return Result(FactOutcome.Accepted, detail);
    }

    // ----------------------------------------------------------------- intentions

    public IntentResult ApplyIntent(DraftCommand command)
        => Idempotent(command.IdempotencyKey, command, () => ApplyIntentCore(command));

    private IntentResult ApplyIntentCore(DraftCommand command)
    {
        IntentResult Result(IntentOutcome o, int? version = null, IReadOnlyList<string>? applied = null,
            IReadOnlyList<FieldConflict>? conflicts = null, string? detail = null)
            => new(command.IdempotencyKey, command.InvoiceId, o, version, applied ?? [], conflicts ?? [], detail);

        _invoices.TryGetValue(command.InvoiceId, out var invoice);

        switch (command)
        {
            case CreateDraftCommand create:
                if (invoice is not null) return Result(IntentOutcome.Ignored, invoice.Version, detail: "Brouillon déjà connu.");
                var draft = Invoice.FromDto(create.Draft with { Status = InvoiceStatus.Draft, Version = 1, Number = null, ChainHash = null });
                _invoices[draft.Id] = draft;
                LogChange(draft.Id);
                EffectiveWrites++;
                return Result(IntentOutcome.Applied, draft.Version, DraftFields.All);

            case UpdateDraftCommand update:
                if (invoice is null) return Result(IntentOutcome.Rejected, detail: "Brouillon inconnu du serveur.");
                if (invoice.Status == InvoiceStatus.Validated)
                    return Result(IntentOutcome.Ignored, invoice.Version, detail: $"Facture déjà validée ({invoice.Number}) : modification sans objet.");

                var baseIsCurrent = update.BaseVersion == invoice.Version;
                var merge = DraftMerge.Apply(invoice, update.Changes);
                if (merge.Applied.Count > 0) { invoice.BumpVersion(); LogChange(invoice.Id); EffectiveWrites++; }

                var outcome = merge.Conflicts.Count > 0 ? IntentOutcome.Conflict
                    : baseIsCurrent ? IntentOutcome.Applied
                    : IntentOutcome.Merged;
                return Result(outcome, invoice.Version, merge.Applied, merge.Conflicts);

            case ValidateDraftCommand validate:
                if (invoice is null) return Result(IntentOutcome.Rejected, detail: "Brouillon inconnu du serveur.");
                if (invoice.Status == InvoiceStatus.Validated)
                    return Result(IntentOutcome.Ignored, invoice.Version, detail: $"Déjà validée ({invoice.Number}).");
                if (ContentFingerprint.Of(invoice) != validate.ExpectedContent)
                    return Result(IntentOutcome.Conflict, invoice.Version,
                        detail: "Le brouillon a changé sur le serveur depuis que vous l'avez validé : relisez-le puis validez à nouveau.");

                var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
                var (last, lastHash) = _series.GetValueOrDefault(InvoiceNumber.ServerSeries);
                var number = InvoiceNumber.Format(today.Year, InvoiceNumber.ServerSeries, last + 1);
                try { invoice.Validate(number, today, lastHash); }
                catch (ComplianceException e) { return Result(IntentOutcome.Rejected, invoice.Version, detail: "Non conforme : " + e.Message); }

                _series[InvoiceNumber.ServerSeries] = (last + 1, invoice.ChainHash);
                invoice.BumpVersion();
                LogChange(invoice.Id);
                EffectiveWrites++;
                return Result(IntentOutcome.Applied, invoice.Version, detail: number);

            default:
                return Result(IntentOutcome.Rejected, detail: "Commande inconnue.");
        }
    }

    // ----------------------------------------------------------------- réception

    public ChangesResponse ChangesSince(long since)
    {
        lock (_gate)
        {
            var ids = _changeLog.Where(c => c.Seq > since).Select(c => c.InvoiceId).Distinct();
            return new ChangesResponse(_seq, [.. ids.Select(id => _invoices[id].ToDto())]);
        }
    }

    // ----------------------------------------------------------------- aides de test (« un collègue au bureau »)

    public InvoiceDto? Find(Guid id) { lock (_gate) return _invoices.GetValueOrDefault(id)?.ToDto(); }

    public IReadOnlyList<InvoiceDto> All() { lock (_gate) return [.. _invoices.Values.Select(i => i.ToDto())]; }

    public void EditOnServer(Guid id, Action<Invoice> edit)
    {
        lock (_gate)
        {
            var invoice = _invoices[id];
            edit(invoice);
            invoice.BumpVersion();
            LogChange(id);
        }
    }

    public void SeedDraft(InvoiceDto draft)
    {
        lock (_gate)
        {
            _invoices[draft.Id] = Invoice.FromDto(draft with { Status = InvoiceStatus.Draft, Version = 1 });
            LogChange(draft.Id);
        }
    }

    // -----------------------------------------------------------------

    private void LogChange(Guid id) => _changeLog.Add((++_seq, id));

    /// <summary>
    /// Idempotency-Key : même clé et même contenu → même réponse, sans réexécution.
    /// Même clé et contenu différent → refus (clé réutilisée par erreur).
    /// </summary>
    private T Idempotent<T>(Guid key, object request, Func<T> execute) where T : class
    {
        var hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request, request.GetType(), SyncHttp.Json)));
        lock (_gate)
        {
            if (_idempotency.TryGetValue(key, out var seen))
                return seen.RequestHash == hash
                    ? (T)seen.Result
                    : throw new IdempotencyKeyReusedException(key);

            var result = execute();
            _idempotency[key] = (hash, result);
            return result;
        }
    }
}

public sealed class IdempotencyKeyReusedException(Guid key)
    : Exception($"La clé d'idempotence {key} a déjà servi pour une autre requête.");

public enum AuthStatus { Ok, Unauthorized, Revoked }

public sealed record AuthResult(AuthStatus Status, TokenResponse? Token)
{
    public static readonly AuthResult Unauthorized = new(AuthStatus.Unauthorized, null);
    public static readonly AuthResult Revoked = new(AuthStatus.Revoked, null);
}
