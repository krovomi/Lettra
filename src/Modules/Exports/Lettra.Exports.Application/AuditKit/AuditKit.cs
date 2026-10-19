using Lettra.Application.Abstractions.Identity;
using Lettra.Application.Abstractions.Integrity;
using Lettra.Application.Abstractions.Messaging;
using Lettra.Application.Abstractions.Persistence;
using Lettra.Application.Abstractions.Tenancy;
using Lettra.Application.Features.Integrity;
using Lettra.Application.Mapping;
using Lettra.Contracts.Billing;
using Lettra.Contracts.Exports;
using Lettra.Contracts.Integrity;
using Lettra.Domain.Billing;
using Lettra.Domain.Common;
using Lettra.Domain.Integrity;
using Lettra.Domain.SharedKernel;
using Lettra.Exports.Application.EInvoicing;
using Lettra.Exports.Application.Fec;

namespace Lettra.Exports.Application.AuditKit;

/// <summary>
/// Préparer le kit contrôle fiscal d'une période (un exercice, 24 mois au plus) : FEC et son rapport de validation,
/// attestation d'intégrité, piste d'audit fiable, XML des pièces et journal des transmissions à la plateforme. Lecture
/// seule, sauf le contrôle d'intégrité qui republie l'état des alertes de la chaîne, comme celui de la nuit.
/// </summary>
public sealed record GetAuditKitQuery(DossierId Dossier, DateOnly From, DateOnly To) : IQuery<Result<AuditKitFile>>, IRequirePermission
{
    /// <summary>Le kit contient les archives scellées des clôtures : il en demande la permission.</summary>
    public Permission Permission => Permission.ArchivesExporter;
}

/// <summary>
/// Rassemble ce que le kit contient, avec les briques existantes : l'export FEC, le contrôle d'intégrité, les
/// archives scellées, le registre des clés et la facture électronique CII. Rien n'est recalculé à part.
/// </summary>
public sealed class AuditKitService(
    ITenantContext tenant,
    ICompanyProfileRepository profiles,
    IInvoiceRepository invoices,
    FecExportService fec,
    DossierIntegrityService integrity,
    IIntegrityStore journal,
    IIntegrityKeyring keyring,
    ISealedArchiveBuilder archives,
    IAuditTrailReader trail,
    IAuthorizationRegistry registry,
    TimeProvider clock)
{
    public async Task<Result<AuditKitFile>> PrepareAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        // Le FEC valide la période, la fiche entreprise et le SIREN : ses refus sont ceux du kit.
        Result<FecFile> fecFile = await fec.ExportAsync(from, to, cancellationToken).ConfigureAwait(false);
        if (fecFile.IsFailure)
        {
            return Result<AuditKitFile>.Failure(fecFile.Errors);
        }

        CompanyProfile profile = (await profiles.FindAsync(cancellationToken).ConfigureAwait(false))!;
        Result<IntegrityReport> report = await integrity.VerifyAsync(cancellationToken).ConfigureAwait(false);
        if (report.IsFailure)
        {
            return Result<AuditKitFile>.Failure(report.Errors);
        }

        IReadOnlyList<ClosingSummary> closings = [.. IntegrityMapper
            .ToSummaries(await journal.ListClosingsAsync(cancellationToken).ConfigureAwait(false))
            .Where(c => c.PeriodStart <= to && c.PeriodEnd >= from)];
        List<SealedArchive> sealedArchives = [];
        foreach (ClosingSummary closing in closings)
        {
            Result<SealedArchive> archive = await archives.BuildAsync(closing.Number, cancellationToken).ConfigureAwait(false);
            if (archive.IsFailure)
            {
                return Result<AuditKitFile>.Failure(archive.Errors);
            }

            sealedArchives.Add(archive.Value);
        }

        TenantDescriptor dossier = tenant.Current;
        AuditKitAttestationResponse attestation = new(
            dossier.DossierId.Value,
            from,
            to,
            IntegrityMapper.ToResponse(report.Value),
            IntegrityMapper.ToSummaries(await keyring.ListAsync(cancellationToken).ConfigureAwait(false)),
            closings);

        AuditTrailData data = await trail.ReadAsync(from, to, cancellationToken).ConfigureAwait(false);
        CompanyProfileResponse company = profile.ToResponse();
        Dictionary<Guid, byte[]> xml = [];
        foreach (Invoice invoice in data.Invoices)
        {
            DateOnly? preceding = await EInvoiceSerializer.PrecedingInvoiceDateAsync(invoices, invoice, cancellationToken).ConfigureAwait(false);
            xml[invoice.Id.Value] = EInvoiceSerializer.Write(invoice.ToResponse(), company, EInvoiceFormatDto.Cii, preceding);
        }

        AuditKitContent content = new(
            dossier.DossierId.Value,
            profile.Name,
            profile.Siren!.Value,
            from,
            to,
            clock.GetUtcNow(),
            fecFile.Value,
            attestation,
            sealedArchives,
            data,
            xml,
            await ReadJournalAsync(from, to, cancellationToken).ConfigureAwait(false),
            await PeopleAsync(dossier, cancellationToken).ConfigureAwait(false));
        return AuditKitWriter.Write(content);
    }

    /// <summary>Les entrées de la chaîne datées de la période (date de pièce) ou enregistrées pendant la période.</summary>
    private async Task<IReadOnlyList<JournalEntry>> ReadJournalAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        List<JournalEntry> entries = [];
        await foreach (JournalEntry entry in journal.ReadEntriesAsync(cancellationToken).ConfigureAwait(false))
        {
            DateOnly recorded = DateOnly.FromDateTime(entry.OccurredAt.UtcDateTime);
            if ((entry.IssueDate is { } day && day >= from && day <= to) || (recorded >= from && recorded <= to))
            {
                entries.Add(entry);
            }
        }

        return entries;
    }

    /// <summary>Les noms du registre du cabinet ; vide sans registre (poste isolé) : la piste garde les identifiants.</summary>
    private async Task<IReadOnlyDictionary<Guid, string>> PeopleAsync(TenantDescriptor dossier, CancellationToken cancellationToken)
    {
        CabinetRegistry? cabinet = await registry.ReadAsync(dossier.OrganisationId, cancellationToken).ConfigureAwait(false);
        return cabinet is null
            ? []
            : cabinet.People.Where(p => !string.IsNullOrWhiteSpace(p.DisplayName)).ToDictionary(p => p.Id, p => p.DisplayName!);
    }
}

internal sealed class GetAuditKitHandler(AuditKitService service) : IQueryHandler<GetAuditKitQuery, Result<AuditKitFile>>
{
    public Task<Result<AuditKitFile>> HandleAsync(GetAuditKitQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return service.PrepareAsync(query.From, query.To, cancellationToken);
    }
}
