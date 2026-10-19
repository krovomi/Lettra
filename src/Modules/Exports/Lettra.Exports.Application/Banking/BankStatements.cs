using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Contracts.Banking;
using Lettra.Contracts.Common;
using Lettra.Domain.Banking;
using Lettra.Domain.Billing;
using Lettra.Domain.Common;
using Lettra.Domain.SharedKernel;
using Lettra.Exports.Application.Banking.Statements;

namespace Lettra.Exports.Application.Banking;

/// <summary>
/// Import d'un relevé : lecture (<see cref="BankStatementParser"/>), empreinte de chaque opération, les opérations déjà
/// importées (même compte, date, montant, libellé, référence, rang) sont écartées et comptées. Seules les nouvelles sont
/// enregistrées. Aucune écriture comptable : elles viennent de la validation des rapprochements.
/// </summary>
internal sealed class ImportBankStatementHandler(
    IBankStatementRepository statements,
    IReceivableDocumentReader receivables,
    IKnownIbanStore knownIbans,
    IIbanFingerprints ibans,
    IBankingSettingsRepository settings,
    IUnitOfWork unitOfWork,
    ICurrentUser user,
    TimeProvider clock) : ICommandHandler<ImportBankStatementCommand, Result<BankImportResponse>>
{
    public async Task<Result<BankImportResponse>> HandleAsync(ImportBankStatementCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ImportBankStatementRequest request = command.Request ?? new ImportBankStatementRequest();
        if (string.IsNullOrWhiteSpace(request.ContentBase64))
        {
            return BankStatementErrors.FileRequired;
        }

        if (request.ContentBase64.Length > (BankStatementParser.MaxContentLength / 3 * 4) + 8)
        {
            return BankStatementErrors.FileTooLarge;
        }

        byte[] content;
        try
        {
            content = Convert.FromBase64String(request.ContentBase64);
        }
        catch (FormatException)
        {
            return BankStatementErrors.FileInvalid(0);
        }

        Result<ParsedBankStatement> parsed = BankStatementParser.Parse(content, request.Csv);
        if (parsed.IsFailure)
        {
            return Result<BankImportResponse>.Failure(parsed.Errors);
        }

        return await BankImport.StoreAsync(
            request.FileName, parsed.Value, statements, receivables, knownIbans, ibans, settings, unitOfWork, user.UserId, clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Démo : un relevé CAMT.053 fictif fabriqué à partir des factures dues, importé comme un vrai.</summary>
internal sealed class ImportDemoBankStatementHandler(
    IBankStatementRepository statements,
    IReceivableDocumentReader receivables,
    IKnownIbanStore knownIbans,
    IIbanFingerprints ibans,
    IBankingSettingsRepository settings,
    IUnitOfWork unitOfWork,
    ICurrentUser user,
    TimeProvider clock) : ICommandHandler<ImportDemoBankStatementCommand, Result<BankImportResponse>>
{
    public async Task<Result<BankImportResponse>> HandleAsync(ImportDemoBankStatementCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        IReadOnlyList<ReceivableDocument> documents = await receivables.ListAsync(null, cancellationToken).ConfigureAwait(false);
        DateOnly today = LegalCalendar.Today(clock);
        byte[] file = DemoBankStatement.Build(documents, today);
        Result<ParsedBankStatement> parsed = BankStatementParser.Parse(file, null);
        if (parsed.IsFailure)
        {
            return Result<BankImportResponse>.Failure(parsed.Errors);
        }

        return await BankImport.StoreAsync(
            DemoBankStatement.FileName(today), parsed.Value, statements, receivables, knownIbans, ibans, settings, unitOfWork, user.UserId, clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
    }
}

internal static class BankImport
{
    public static async Task<Result<BankImportResponse>> StoreAsync(
        string? fileName,
        ParsedBankStatement statement,
        IBankStatementRepository statements,
        IReceivableDocumentReader receivables,
        IKnownIbanStore knownIbans,
        IIbanFingerprints ibans,
        IBankingSettingsRepository settings,
        IUnitOfWork unitOfWork,
        Guid userId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // Rang de chaque opération parmi les opérations identiques du fichier : deux virements identiques le même jour
        // restent deux opérations, un fichier réimporté ne crée rien.
        Dictionary<string, int> seen = new(StringComparer.Ordinal);
        List<(BankTransaction Transaction, string Fingerprint)> keyed = [];
        foreach (BankTransaction transaction in statement.Transactions)
        {
            string baseKey = BankText.TransactionFingerprint(statement.AccountIban, transaction, 0);
            int occurrence = seen.GetValueOrDefault(baseKey);
            seen[baseKey] = occurrence + 1;
            keyed.Add((transaction, occurrence == 0 ? baseKey : BankText.TransactionFingerprint(statement.AccountIban, transaction, occurrence)));
        }

        await ibans.UpgradeLegacyAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlySet<string> existing = await statements.ExistingFingerprintsAsync([.. keyed.Select(k => k.Fingerprint)], cancellationToken).ConfigureAwait(false);
        Guid importId = Guid.CreateVersion7();
        List<BankStatementLine> lines = [];
        foreach ((BankTransaction transaction, string fingerprint) in keyed.Where(k => !existing.Contains(k.Fingerprint)))
        {
            string? ibanHash = await ibans.FingerprintAsync(transaction.CounterpartyIban, cancellationToken).ConfigureAwait(false);
            lines.Add(BankStatementLine.Create(Guid.CreateVersion7(), importId, transaction, fingerprint, ibanHash));
        }

        BankStatementImport import = BankStatementImport.Create(
            importId, string.IsNullOrWhiteSpace(fileName) ? "releve" : fileName, statement, lines.Count, keyed.Count - lines.Count, userId, now);
        await statements.AddImportAsync(import, lines, cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        IReadOnlyList<MatchProposal> proposals = await Reconciliation.ProposeAsync(
            lines.Where(l => l.IsCredit).ToList(), receivables, knownIbans, settings, cancellationToken).ConfigureAwait(false);
        return new BankImportResponse(
            import.Id, (BankStatementFormatDto)(int)import.Format, import.AccountDisplay, import.From, import.To, import.Imported, import.Duplicates, proposals.Count);
    }
}

internal static class Reconciliation
{
    public static async Task<IReadOnlyList<MatchProposal>> ProposeAsync(
        IReadOnlyList<BankStatementLine> pendingCredits,
        IReceivableDocumentReader receivables,
        IKnownIbanStore knownIbans,
        IBankingSettingsRepository settings,
        CancellationToken cancellationToken)
    {
        if (pendingCredits.Count == 0)
        {
            return [];
        }

        IReadOnlyList<ReceivableDocument> documents = await receivables.ListAsync(null, cancellationToken).ConfigureAwait(false);
        List<OpenInvoice> open =
        [
            .. documents.Where(d => d.Kind != InvoiceKind.CreditNote && d.Outstanding > 0m)
                .Select(d => new OpenInvoice(d.InvoiceId.Value, d.Number, d.CustomerId, d.CustomerName, d.IssueDate, d.DueDate, d.Outstanding)),
        ];
        IReadOnlyList<KnownIban> ibans = await knownIbans.ListAsync(cancellationToken).ConfigureAwait(false);
        decimal threshold = (await settings.FindAsync(cancellationToken).ConfigureAwait(false) ?? BankingSettings.Default()).WriteOffThreshold;
        return ReconciliationMatcher.ProposeAll(
            [.. pendingCredits.Select(l => new BankLineCandidate(l.Id, l.BookingDate, l.Amount, l.Label, l.Reference, l.CounterpartyName, l.CounterpartyIbanHash))],
            open,
            ibans,
            threshold);
    }

    public static MatchProposalResponse ToResponse(MatchProposal proposal) => new(
        (MatchRuleDto)(int)proposal.Rule,
        proposal.CustomerId.Value,
        proposal.CustomerName,
        [.. proposal.Allocations.Select(a => new ProposedAllocationResponse(a.InvoiceId, a.Number, a.Amount))],
        proposal.WriteOff,
        proposal.Evidence);

    public static BankLineResponse ToResponse(BankStatementLine line, string? paymentNumber, MatchProposal? proposal) => new(
        line.Id,
        line.Version,
        line.BookingDate,
        line.Amount,
        line.Label,
        line.Reference,
        line.CounterpartyName,
        line.CounterpartyIbanDisplay,
        (BankLineStatusDto)(int)line.Status,
        line.PaymentId,
        paymentNumber,
        proposal is null ? null : ToResponse(proposal));
}

internal sealed class GetReconciliationHandler(
    IBankStatementRepository statements,
    IPaymentRepository payments,
    IReceivableDocumentReader receivables,
    IKnownIbanStore knownIbans,
    IIbanFingerprints ibans,
    IBankingSettingsRepository settings) : IQueryHandler<GetReconciliationQuery, Result<ReconciliationResponse>>
{
    public async Task<Result<ReconciliationResponse>> HandleAsync(GetReconciliationQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        await ibans.UpgradeLegacyAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<BankStatementLine> lines = await statements.ListLinesAsync(Math.Clamp(query.Max, 1, 2000), cancellationToken).ConfigureAwait(false);
        IReadOnlyList<MatchProposal> proposals = await Reconciliation.ProposeAsync(
            [.. lines.Where(l => l.Status == BankLineStatus.Pending && l.IsCredit)], receivables, knownIbans, settings, cancellationToken).ConfigureAwait(false);
        Dictionary<Guid, MatchProposal> byLine = proposals.ToDictionary(p => p.BankLineId);
        IReadOnlyDictionary<Guid, string> numbers = await payments.NumbersAsync(
            [.. lines.Where(l => l.PaymentId is not null).Select(l => l.PaymentId!.Value)], cancellationToken).ConfigureAwait(false);
        (int pending, int matched, int ignored) = await statements.CountAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<BankStatementImport> imports = await statements.ListImportsAsync(20, cancellationToken).ConfigureAwait(false);
        decimal threshold = (await settings.FindAsync(cancellationToken).ConfigureAwait(false) ?? BankingSettings.Default()).WriteOffThreshold;
        return new ReconciliationResponse(
            [.. lines.Select(l => Reconciliation.ToResponse(l, l.PaymentId is Guid p ? numbers.GetValueOrDefault(p) : null, byLine.GetValueOrDefault(l.Id)))],
            pending,
            matched,
            ignored,
            proposals.Count,
            threshold,
            [.. imports.Select(i => new BankImportSummary(i.Id, i.FileName, (BankStatementFormatDto)(int)i.Format, i.AccountDisplay, i.From, i.To, i.Imported, i.Duplicates, i.ImportedAt))]);
    }
}

/// <summary>
/// Valide des rapprochements : pour chaque ligne, un encaissement du montant de la ligne, à sa date, imputé comme
/// demandé (en général la proposition affichée). Chaque encaissement a sa transaction : une ligne refusée n'empêche
/// pas les autres, et son refus est rendu avec ses codes d'erreur.
/// </summary>
internal sealed class ConfirmMatchesHandler(IBankStatementRepository statements, ICommandDispatcher commands)
    : ICommandHandler<ConfirmMatchesCommand, Result<ConfirmMatchesResponse>>
{
    public const int MaxItems = 200;

    public async Task<Result<ConfirmMatchesResponse>> HandleAsync(ConfirmMatchesCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        IReadOnlyList<ConfirmMatchItem> items = command.Request?.Items ?? [];
        if (items.Count is 0 or > MaxItems)
        {
            return BankStatementErrors.TooManyLines;
        }

        List<ConfirmMatchOutcome> outcomes = [];
        foreach (ConfirmMatchItem item in items)
        {
            BankStatementLine? line = item is null ? null : await statements.FindLineAsync(item.BankLineId, cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                outcomes.Add(new ConfirmMatchOutcome(item?.BankLineId ?? Guid.Empty, null, null, [new ErrorDetail(BankStatementErrors.LineNotFoundCode)]));
                continue;
            }

            RecordPaymentRequest request = new()
            {
                CustomerId = item!.CustomerId,
                Date = line.BookingDate,
                Amount = line.Amount,
                Method = item.Method,
                Reference = line.Reference ?? BankText.Clip(line.Label, Payment.MaxReferenceLength),
                Allocations = item.Allocations ?? [],
                BankLineId = line.Id,
            };
            Result<PaymentResponse> recorded = await commands.SendAsync(new RecordPaymentCommand(command.Dossier, request), cancellationToken).ConfigureAwait(false);
            outcomes.Add(recorded.IsSuccess
                ? new ConfirmMatchOutcome(line.Id, recorded.Value.Id, recorded.Value.Number, [])
                : new ConfirmMatchOutcome(line.Id, null, null, [.. recorded.Errors.Select(e => new ErrorDetail(e.Code, e.Target))]));
        }

        int confirmed = outcomes.Count(o => o.PaymentId is not null);
        return new ConfirmMatchesResponse(confirmed, outcomes.Count - confirmed, outcomes);
    }
}

internal sealed class IgnoreBankLineHandler(IBankStatementRepository statements, IUnitOfWork unitOfWork, ICurrentUser user, TimeProvider clock)
    : ICommandHandler<IgnoreBankLineCommand, Result<BankLineResponse>>
{
    public async Task<Result<BankLineResponse>> HandleAsync(IgnoreBankLineCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        BankStatementLine? line = await statements.FindLineAsync(command.LineId, cancellationToken).ConfigureAwait(false);
        if (line is null)
        {
            return BankStatementErrors.LineNotFound;
        }

        Result changed = line.SetIgnored(command.Request?.Ignored ?? true, user.UserId, clock.GetUtcNow());
        if (changed.IsFailure)
        {
            return Result<BankLineResponse>.Failure(changed.Errors);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Reconciliation.ToResponse(line, null, null);
    }
}

internal sealed class GetBankingSettingsHandler(IBankingSettingsRepository settings) : IQueryHandler<GetBankingSettingsQuery, Result<BankingSettingsResponse>>
{
    public async Task<Result<BankingSettingsResponse>> HandleAsync(GetBankingSettingsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        BankingSettings current = await settings.FindAsync(cancellationToken).ConfigureAwait(false) ?? BankingSettings.Default();
        return new BankingSettingsResponse(current.WriteOffThreshold, BankingSettings.MaxWriteOffThreshold, current.Version);
    }
}

internal sealed class UpdateBankingSettingsHandler(IBankingSettingsRepository settings, IUnitOfWork unitOfWork, ICurrentUser user, TimeProvider clock)
    : ICommandHandler<UpdateBankingSettingsCommand, Result<BankingSettingsResponse>>
{
    public async Task<Result<BankingSettingsResponse>> HandleAsync(UpdateBankingSettingsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        BankingSettings? current = await settings.FindAsync(cancellationToken).ConfigureAwait(false);
        bool created = current is null;
        current ??= BankingSettings.Default();
        Result updated = current.Update(command.Request?.WriteOffThreshold ?? -1m, user.UserId, clock.GetUtcNow());
        if (updated.IsFailure)
        {
            return Result<BankingSettingsResponse>.Failure(updated.Errors);
        }

        if (created)
        {
            await settings.AddAsync(current, cancellationToken).ConfigureAwait(false);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new BankingSettingsResponse(current.WriteOffThreshold, BankingSettings.MaxWriteOffThreshold, current.Version);
    }
}

internal sealed class GetBankReconciliationStatusHandler(IBankStatementRepository statements)
    : IQueryHandler<GetBankReconciliationStatusQuery, Result<BankReconciliationStatusResponse>>
{
    public async Task<Result<BankReconciliationStatusResponse>> HandleAsync(GetBankReconciliationStatusQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        BankReconciliationStatus status = await statements.StatusAsync(cancellationToken).ConfigureAwait(false);
        return new BankReconciliationStatusResponse(status.LastImportAt, status.LastStatementDate, status.PendingLines, status.OldestPendingDate, status.LastMatchedAt);
    }
}
