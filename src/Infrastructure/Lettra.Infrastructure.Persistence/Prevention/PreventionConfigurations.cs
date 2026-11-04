using Lettra.Domain.Prevention;
using Lettra.Infrastructure.Persistence.Concurrency;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lettra.Infrastructure.Persistence.Prevention;

/// <summary>Seuils du radar de prévention : une seule ligne par base de dossier, créée au premier réglage.</summary>
internal sealed class PreventionSettingsConfiguration : IEntityTypeConfiguration<PreventionSettings>
{
    public void Configure(EntityTypeBuilder<PreventionSettings> builder)
    {
        builder.ToTable("PreventionSettings");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.DisabledSignals).HasMaxLength(PreventionSettings.DisabledSignalsLength).IsRequired();
        builder.Ignore(s => s.Disabled);
        builder.IsVersioned();
    }
}
