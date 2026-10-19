using Lettra.Application.Abstractions.Persistence;
using Lettra.Domain.Common;

namespace Lettra.Application.Tests;

/// <summary>Le préambule commun des commandes versionnées : introuvable, version périmée, ou le document.</summary>
public sealed class ConcurrencyLoadTests
{
    private static readonly Error NotFound = Error.NotFound("Test.NotFound");

    private sealed record Document(long Version) : IVersioned;

    [Fact]
    public void Un_document_absent_rend_l_erreur_introuvable_du_module()
    {
        Result<Document> loaded = Concurrency.Load<Document>(null, NotFound, 3);

        loaded.Errors.Should().ContainSingle().Which.Should().Be(NotFound);
    }

    [Fact]
    public void Un_document_modifie_depuis_sa_lecture_rend_un_conflit_de_version()
    {
        Result<Document> loaded = Concurrency.Load(new Document(4), NotFound, 3);

        loaded.Errors.Should().ContainSingle().Which.Code.Should().Be(ConcurrencyErrors.VersionMismatchCode);
    }

    [Theory]
    [InlineData(3L)]
    [InlineData(null)]
    public void La_version_lue_ou_l_absence_de_controle_rend_le_document(long? expected)
    {
        Document document = new(3);

        Result<Document> loaded = Concurrency.Load(document, NotFound, expected);

        loaded.Value.Should().BeSameAs(document);
    }
}
