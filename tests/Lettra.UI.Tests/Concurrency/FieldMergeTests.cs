using FluentAssertions;
using Lettra.UI.Abstractions.Concurrency;

namespace Lettra.UI.Tests.Concurrency;

/// <summary>Fusion à trois versions : un seul côté change, les deux pareil, les deux différemment.</summary>
public sealed class FieldMergeTests
{
    private sealed record Article(string Label, decimal Price, string? Unit);

    private static readonly FieldMerge<Article> Merge = new FieldMerge<Article>()
        .Field(nameof(Article.Label), a => a.Label, (a, v) => a with { Label = v })
        .Field(nameof(Article.Price), a => a.Price, (a, v) => a with { Price = v })
        .Field(nameof(Article.Unit), a => a.Unit, (a, v) => a with { Unit = v });

    private static readonly Article Read = new("Conseil", 100m, "h");

    [Fact]
    public void Des_champs_differents_changes_de_chaque_cote_se_fusionnent_sans_conflit()
    {
        MergeResult<Article> result = Merge.Merge(Read, Read with { Price = 120m }, Read with { Label = "Conseil fiscal" });

        result.HasConflicts.Should().BeFalse();
        result.Merged.Should().Be(new Article("Conseil fiscal", 120m, "h"));
    }

    [Fact]
    public void Le_meme_changement_des_deux_cotes_n_est_pas_un_conflit()
    {
        MergeResult<Article> result = Merge.Merge(Read, Read with { Unit = null }, Read with { Unit = null });

        result.HasConflicts.Should().BeFalse();
        result.Merged.Unit.Should().BeNull();
    }

    [Fact]
    public void Un_champ_change_differemment_des_deux_cotes_est_un_conflit_qui_garde_la_valeur_du_serveur()
    {
        MergeResult<Article> result = Merge.Merge(Read, Read with { Price = 120m, Unit = "j" }, Read with { Price = 110m });

        result.Conflicts.Should().Equal(nameof(Article.Price));
        result.Merged.Should().Be(new Article("Conseil", 110m, "j"));
    }
}
