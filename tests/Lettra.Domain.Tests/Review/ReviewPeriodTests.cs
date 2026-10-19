using Lettra.Domain.Common;
using Lettra.Domain.Review;

namespace Lettra.Domain.Tests.Review;

public sealed class ReviewPeriodTests
{
    [Theory]
    [InlineData("2026-09", "2026-09-01", "2026-09-30")]
    [InlineData("2026-02", "2026-02-01", "2026-02-28")]
    [InlineData("2026-T3", "2026-07-01", "2026-09-30")]
    [InlineData("2026-t4", "2026-10-01", "2026-12-31")]
    public void Une_periode_ecrite_se_lit_en_mois_ou_trimestre(string text, string start, string end)
    {
        Result<ReviewPeriod> period = ReviewPeriod.Parse(text);

        period.IsSuccess.Should().BeTrue();
        period.Value.Start.Should().Be(DateOnly.Parse(start, System.Globalization.CultureInfo.InvariantCulture));
        period.Value.End.Should().Be(DateOnly.Parse(end, System.Globalization.CultureInfo.InvariantCulture));
        period.Value.ToString().Should().Be(text.ToUpperInvariant());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2026-13")]
    [InlineData("2026-00")]
    [InlineData("2026-T5")]
    [InlineData("1999-01")]
    [InlineData("2026/09")]
    [InlineData("26-09")]
    public void Une_periode_mal_ecrite_est_refusee(string? text) =>
        ReviewPeriod.Parse(text).Errors.Should().ContainSingle().Which.Code.Should().Be(ReviewErrors.PeriodInvalidCode);

    [Fact]
    public void Le_mois_a_revoir_est_le_mois_ecoule_meme_en_janvier()
    {
        ReviewPeriod.PreviousMonth(new DateOnly(2026, 10, 19)).ToString().Should().Be("2026-09");
        ReviewPeriod.PreviousMonth(new DateOnly(2027, 1, 3)).ToString().Should().Be("2026-12");
    }

    [Fact]
    public void Une_coche_refuse_un_point_inconnu_et_une_note_trop_longue_et_nettoie_la_note()
    {
        ReviewPeriod period = ReviewPeriod.OfMonth(2026, 9);
        DateTimeOffset now = new(2026, 10, 2, 9, 0, 0, TimeSpan.Zero);

        DossierReviewMark.Check(period, "inconnu", Guid.NewGuid(), now, null).Errors.Should().ContainSingle()
            .Which.Code.Should().Be(ReviewErrors.ItemUnknownCode);
        DossierReviewMark.Check(period, DossierReviewChecklist.VatDeclared, Guid.NewGuid(), now, new string('x', 501)).Errors.Should().ContainSingle()
            .Which.Code.Should().Be(ReviewErrors.NoteTooLongCode);

        DossierReviewMark mark = DossierReviewMark.Check(period, DossierReviewChecklist.VatDeclared, Guid.NewGuid(), now, "  CA3 déposée  ").Value;
        mark.Period.Should().Be("2026-09");
        mark.Note.Should().Be("CA3 déposée");
        DossierReviewMark.Check(period, DossierReviewChecklist.VatDeclared, Guid.NewGuid(), now, "   ").Value.Note.Should().BeNull();
    }
}
