using Lettra.Api.Infrastructure;
using Lettra.Domain.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Api.Tests;

/// <summary>Traduction d'un <see cref="Result"/> en réponse HTTP, partagée par tous les endpoints.</summary>
public sealed class HttpResultsTests
{
    private static readonly Error Missing = Error.NotFound("Customer.NotFound");

    [Fact]
    public async Task Un_succes_rend_200_avec_la_valeur()
    {
        HttpContext context = await ExecuteAsync(HttpResults.Ok(Result<string>.Success("ok")));

        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        (await BodyOf(context)).Should().Be("\"ok\"");
    }

    [Fact]
    public async Task Un_echec_rend_le_probleme_de_la_premiere_erreur()
    {
        HttpContext context = await ExecuteAsync(HttpResults.Ok(Result<string>.Failure(Missing)));

        context.Response.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        (await BodyOf(context)).Should().Contain("\"code\":\"Customer.NotFound\"");
    }

    [Theory]
    [InlineData(ErrorKind.Validation, StatusCodes.Status400BadRequest)]
    [InlineData(ErrorKind.NotFound, StatusCodes.Status404NotFound)]
    [InlineData(ErrorKind.Conflict, StatusCodes.Status409Conflict)]
    [InlineData(ErrorKind.Forbidden, StatusCodes.Status403Forbidden)]
    [InlineData(ErrorKind.Unauthorized, StatusCodes.Status401Unauthorized)]
    [InlineData(ErrorKind.Unexpected, StatusCodes.Status500InternalServerError)]
    [InlineData(ErrorKind.PreconditionFailed, StatusCodes.Status412PreconditionFailed)]
    [InlineData(ErrorKind.Unavailable, StatusCodes.Status503ServiceUnavailable)]
    public async Task Chaque_nature_d_erreur_a_son_statut(ErrorKind kind, int status)
    {
        HttpContext context = await ExecuteAsync(HttpResults.Ok(Result<string>.Failure(new Error("Test.Code", kind))));

        context.Response.StatusCode.Should().Be(status);
        (await BodyOf(context)).Should().Contain("\"code\":\"Test.Code\"");
    }

    [Fact]
    public void Toutes_les_natures_d_erreur_sont_couvertes() =>
        Enum.GetValues<ErrorKind>().Should().HaveCount(8, "une nouvelle nature d'erreur a besoin de son statut HTTP dans ErrorResults et ici");

    [Fact]
    public async Task Une_commande_reussie_rend_204()
    {
        HttpContext context = await ExecuteAsync(HttpResults.NoContent(Result.Success()));

        context.Response.StatusCode.Should().Be(StatusCodes.Status204NoContent);
    }

    [Fact]
    public async Task Une_commande_refusee_rend_le_probleme()
    {
        HttpContext context = await ExecuteAsync(HttpResults.NoContent(Result.Failure(Error.Conflict("Quote.NotDraft"))));

        context.Response.StatusCode.Should().Be(StatusCodes.Status409Conflict);
    }

    [Fact]
    public async Task Une_lecture_versionnee_porte_son_etag()
    {
        HttpContext context = await ExecuteAsync(HttpResults.Versioned(Result<int>.Success(7), v => v * 2));

        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        context.Response.Headers.ETag.ToString().Should().Be("\"14\"");
    }

    [Fact]
    public async Task Une_lecture_versionnee_en_echec_n_a_pas_d_etag()
    {
        HttpContext context = await ExecuteAsync(HttpResults.Versioned(Result<int>.Failure(Missing), v => v));

        context.Response.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        context.Response.Headers.ETag.Should().BeEmpty();
    }

    [Fact]
    public async Task Une_creation_rend_201_avec_l_adresse_calculee_sur_la_valeur()
    {
        HttpContext context = await ExecuteAsync(HttpResults.Created(v => $"/api/v1/payments/{v}", Result<int>.Success(42)));

        context.Response.StatusCode.Should().Be(StatusCodes.Status201Created);
        context.Response.Headers.Location.ToString().Should().Be("/api/v1/payments/42");
    }

    [Fact]
    public async Task Une_creation_versionnee_rend_201_avec_adresse_et_etag()
    {
        HttpContext context = await ExecuteAsync(HttpResults.Created("/api/v1/quotes/1", Result<int>.Success(3), v => v));

        context.Response.StatusCode.Should().Be(StatusCodes.Status201Created);
        context.Response.Headers.Location.ToString().Should().Be("/api/v1/quotes/1");
        context.Response.Headers.ETag.ToString().Should().Be("\"3\"");
    }

    [Fact]
    public async Task Une_creation_refusee_n_a_ni_adresse_ni_etag()
    {
        HttpContext context = await ExecuteAsync(HttpResults.Created("/api/v1/quotes/1", Result<int>.Failure(Error.Validation("Quote.Lines", "lines")), v => v));

        context.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        context.Response.Headers.Location.Should().BeEmpty();
        context.Response.Headers.ETag.Should().BeEmpty();
        (await BodyOf(context)).Should().Contain("\"lines\":[\"Quote.Lines\"]");
    }

    private static async Task<HttpContext> ExecuteAsync(IResult result)
    {
        DefaultHttpContext context = new()
        {
            RequestServices = new ServiceCollection().AddLogging().AddProblemDetails().BuildServiceProvider(),
        };
        context.Response.Body = new MemoryStream();
        await result.ExecuteAsync(context);
        return context;
    }

    private static async Task<string> BodyOf(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using StreamReader reader = new(context.Response.Body);
        return await reader.ReadToEndAsync();
    }
}
