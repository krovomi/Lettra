using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lettra.Infrastructure.Persistence.Numbering;

/// <summary>
/// Série de numérotation d'un dossier (ADR-0007). Le compteur n'avance que par
/// <see cref="DocumentNumbering"/>, qui le réserve en premier dans une transaction explicite.
/// </summary>
internal sealed class NumberSeries
{
    private NumberSeries()
    {
    }

    public NumberSeries(string code, string prefix)
    {
        Code = code;
        Prefix = prefix;
        Next = 1;
    }

    public string Code { get; private set; } = string.Empty;

    public string Prefix { get; private set; } = string.Empty;

    public long Next { get; private set; }

    public long Version { get; private set; }

    public static string Format(string prefix, long number) =>
        prefix + number.ToString("D6", CultureInfo.InvariantCulture);
}

internal sealed class NumberSeriesConfiguration : IEntityTypeConfiguration<NumberSeries>
{
    public void Configure(EntityTypeBuilder<NumberSeries> builder)
    {
        builder.ToTable("NumberSeries");
        builder.HasKey(s => s.Code);
        builder.Property(s => s.Code).HasMaxLength(16);
        builder.Property(s => s.Prefix).HasMaxLength(16).IsRequired();
        // Concurrence optimiste neutre vis-à-vis du provider : UPDATE ... WHERE Version = @lue.
        builder.Property(s => s.Version).IsConcurrencyToken();
    }
}
