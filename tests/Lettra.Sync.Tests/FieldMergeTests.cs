using Lettra.Contracts.Customers;
using Lettra.Contracts.Serialization;
using Lettra.Sync.Server;

namespace Lettra.Sync.Tests;

/// <summary>Fusion à trois voies sur le contrat de saisie (ADR-0013).</summary>
public sealed class FieldMergeTests
{
    private static readonly CreateCustomerRequest Base = SynchronisationTests.Martin();

    private static MergeOutcome<CreateCustomerRequest> Merge(CreateCustomerRequest mine, CreateCustomerRequest server) =>
        FieldMerge.Merge(Base, mine, server, ContractsJsonContext.Default.CreateCustomerRequest);

    [Fact]
    public void Un_champ_que_le_poste_n_a_pas_touche_garde_la_valeur_du_serveur()
    {
        MergeOutcome<CreateCustomerRequest> merge = Merge(Base with { Email = "a@martin.fr" }, Base with { City = "Lyon" });

        merge.Merged.Should().Be(Base with { Email = "a@martin.fr", City = "Lyon" });
        merge.MineChanged.Should().BeTrue();
        merge.ServerChanged.Should().BeTrue();
        merge.Conflicts.Should().BeEmpty();
    }

    [Fact]
    public void La_meme_valeur_des_deux_cotes_n_est_pas_un_conflit()
    {
        MergeOutcome<CreateCustomerRequest> merge = Merge(Base with { City = "Lyon" }, Base with { City = "Lyon" });

        merge.Conflicts.Should().BeEmpty();
        merge.MineChanged.Should().BeFalse("rien à écrire : c'est déjà la valeur du serveur");
    }

    [Fact]
    public void Deux_valeurs_differentes_donnent_un_conflit_nomme_comme_la_propriete_du_contrat()
    {
        MergeOutcome<CreateCustomerRequest> merge = Merge(Base with { AddressLine1 = "2 rue Neuve" }, Base with { AddressLine1 = "3 rue Vieille" });

        merge.Merged.AddressLine1.Should().Be("3 rue Vieille");
        merge.Conflicts.Should().ContainSingle().Which.Field.Should().Be(nameof(CreateCustomerRequest.AddressLine1));
    }

    [Fact]
    public void Une_valeur_effacee_sur_le_poste_est_un_changement()
    {
        MergeOutcome<CreateCustomerRequest> merge = Merge(Base with { Email = null }, Base);

        merge.Merged.Email.Should().BeNull();
        merge.MineChanged.Should().BeTrue();
    }
}
