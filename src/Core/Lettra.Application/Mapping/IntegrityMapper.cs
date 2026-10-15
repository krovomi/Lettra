using Lettra.Application.Abstractions.Integrity;
using Lettra.Contracts.Integrity;
using Lettra.Domain.Integrity;

namespace Lettra.Application.Mapping;

/// <summary>Domaine de la chaîne anti-fraude → contrats.</summary>
public static class IntegrityMapper
{
    public static IntegrityReportResponse ToResponse(IntegrityReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return new IntegrityReportResponse(
            report.IsIntact,
            report.CheckedAt,
            report.EntriesChecked,
            report.ClosingsChecked,
            report.DocumentsChecked,
            [.. report.Findings.Select(f => new IntegrityFindingResponse(f.Kind.ToString(), f.Sequence, f.ClosingNumber, f.SeriesCode))],
            report.FindingsTruncated);
    }

    public static IReadOnlyList<ClosingSummary> ToSummaries(IReadOnlyList<PeriodClosing> closings)
    {
        ArgumentNullException.ThrowIfNull(closings);
        return [.. closings.OrderBy(c => c.Number).Select(c => new ClosingSummary(
            c.Number,
            c.PeriodKind.ToString(),
            c.PeriodStart,
            c.PeriodEnd,
            c.FirstSequence,
            c.LastSequence,
            c.TotalExclTax,
            c.TotalVat,
            c.TotalInclTax,
            c.CumulativeInclTax,
            c.ClosedAt,
            c.KeyId,
            c.Hash))];
    }

    /// <summary>Registre des clés : la dernière version est celle en vigueur.</summary>
    public static IReadOnlyList<IntegrityKeySummary> ToSummaries(IReadOnlyList<IntegrityKeyVersion> versions)
    {
        ArgumentNullException.ThrowIfNull(versions);
        return [.. versions.Select((v, i) => new IntegrityKeySummary(v.Version, v.KeyId, v.PublicKey, v.CreatedAt, v.RetiredAt, i == versions.Count - 1))];
    }
}
