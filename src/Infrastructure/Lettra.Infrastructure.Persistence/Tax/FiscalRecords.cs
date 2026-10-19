using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lettra.Infrastructure.Persistence.Tax;

/// <summary>
/// Réglages fiscaux du dossier (module Exports, déclarations) : une seule ligne par base de dossier. Absente tant que
/// personne ne les a enregistrés ; les valeurs par défaut s'appliquent alors.
/// </summary>
internal sealed class FiscalSettingsRecord
{
    /// <summary>Identifiant fixe de l'unique ligne.</summary>
    public static readonly Guid SingletonId = new("6c657474-7261-0000-0000-666973636131");

    public Guid Id { get; set; }

    public int FiscalYearEndMonth { get; set; }

    public int FiscalYearEndDay { get; set; }

    /// <summary><c>VatPeriodicityDto</c> : 1 mensuelle, 2 trimestrielle.</summary>
    public int VatPeriodicity { get; set; }

    public int VatDeadlineDay { get; set; }

    public bool CorporateIncomeTax { get; set; }

    public bool Cfe { get; set; }

    public DateOnly? TrackedFrom { get; set; }

    public long Version { get; set; }

    public long UpdatedAtMs { get; set; }
}

/// <summary>Une échéance du calendrier fiscal marquée comme déposée : l'obligation et le premier jour de sa période.</summary>
internal sealed class FiscalFilingRecord
{
    /// <summary><c>FiscalObligationDto</c>.</summary>
    public int Obligation { get; set; }

    public DateOnly PeriodStart { get; set; }

    public long FiledAtMs { get; set; }

    public Guid FiledBy { get; set; }
}

internal sealed class FiscalSettingsRecordConfiguration : IEntityTypeConfiguration<FiscalSettingsRecord>
{
    public void Configure(EntityTypeBuilder<FiscalSettingsRecord> builder)
    {
        builder.ToTable("FiscalSettings");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
    }
}

internal sealed class FiscalFilingRecordConfiguration : IEntityTypeConfiguration<FiscalFilingRecord>
{
    public void Configure(EntityTypeBuilder<FiscalFilingRecord> builder)
    {
        builder.ToTable("FiscalFilings");
        builder.HasKey(r => new { r.Obligation, r.PeriodStart });
    }
}
