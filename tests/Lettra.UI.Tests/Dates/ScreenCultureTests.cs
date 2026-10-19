using System.Globalization;
using Lettra.UI.Shared.Components.DataEntryGrid;
using Lettra.UI.Shared.Localization;

namespace Lettra.UI.Tests.Dates;

/// <summary>Culture des écrans : dates et montants dans la langue choisie, séparateur des milliers lisible en français.</summary>
[Collection("culture")]
public sealed class ScreenCultureTests : IDisposable
{
    public void Dispose() => LettraCultures.Apply(CultureInfo.GetCultureInfo("fr-FR"));

    [Fact]
    public void En_francais_les_milliers_sont_separes_par_une_espace_insecable_visible()
    {
        LettraCultures.Apply(LettraCultures.Resolve("fr"));

        CultureInfo.CurrentCulture.Name.Should().Be("fr-FR");
        CultureInfo.CurrentUICulture.Name.Should().Be("fr-FR", "les textes restent en français");
        GridNumber.FormatAmount(15128m, CultureInfo.CurrentCulture).Should().Be("15 128,00");
        new DateOnly(2026, 10, 19).ToString("d", CultureInfo.CurrentCulture).Should().Be("19/10/2026");
        CultureInfo.GetCultureInfo("fr-FR").NumberFormat.NumberGroupSeparator.Should().Be(" ", "la culture d'origine, celle des PDF, ne change pas");
    }

    [Fact]
    public void In_english_amounts_and_dates_keep_the_us_formats()
    {
        LettraCultures.Apply(LettraCultures.Resolve("en-GB"));

        CultureInfo.CurrentCulture.Should().BeSameAs(CultureInfo.GetCultureInfo("en-US"), "rien à adapter en anglais");
        GridNumber.FormatAmount(15128m, CultureInfo.CurrentCulture).Should().Be("15,128.00");
        new DateOnly(2026, 10, 19).ToString("d", CultureInfo.CurrentCulture).Should().Be("10/19/2026");
    }

    [Fact]
    public void La_culture_des_ecrans_reste_en_lecture_seule()
    {
        CultureInfo screen = LettraCultures.ForScreen(CultureInfo.GetCultureInfo("fr-FR"));

        screen.IsReadOnly.Should().BeTrue();
        screen.NumberFormat.PercentGroupSeparator.Should().Be(" ");
        FluentActions.Invoking(() => LettraCultures.ForScreen(null!)).Should().Throw<ArgumentNullException>();
    }
}
