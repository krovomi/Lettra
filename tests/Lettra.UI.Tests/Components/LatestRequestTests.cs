using Lettra.UI.Shared.Components;

namespace Lettra.UI.Tests.Components;

public sealed class LatestRequestTests
{
    [Fact]
    public async Task Une_reponse_lente_remplacee_par_une_demande_plus_recente_est_ignoree()
    {
        using LatestRequest requests = new();
        TaskCompletionSource<string> slow = new();

        Task<string?> first = requests.RunAsync(_ => slow.Task);
        string? second = await requests.RunAsync(_ => Task.FromResult("récente"));
        slow.SetResult("ancienne");

        (await first).Should().BeNull();
        second.Should().Be("récente");
    }

    [Fact]
    public async Task Une_demande_annulee_par_son_jeton_ne_leve_pas_d_exception()
    {
        using LatestRequest requests = new();

        Task<string?> first = requests.RunAsync(async token =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return "jamais";
        });
        _ = requests.Start();

        (await first).Should().BeNull();
    }

    [Fact]
    public async Task La_suite_d_une_demande_garde_son_jeton_tant_qu_aucune_autre_ne_la_remplace()
    {
        using LatestRequest requests = new();
        CancellationToken started = requests.Start();

        string? next = await requests.ContinueAsync(token =>
        {
            token.Should().Be(started);
            return Task.FromResult("page 2");
        });

        next.Should().Be("page 2");
        requests.Current.Should().Be(started);
    }

    [Fact]
    public async Task Fermer_l_ecran_annule_la_demande_en_cours_et_les_suivantes()
    {
        LatestRequest requests = new();
        CancellationToken pending = requests.Start();

        requests.Dispose();

        pending.IsCancellationRequested.Should().BeTrue();
        requests.Start().IsCancellationRequested.Should().BeTrue();
        requests.Current.IsCancellationRequested.Should().BeTrue();
        (await requests.RunAsync(_ => Task.FromResult("après"))).Should().BeNull();
    }

    [Fact]
    public async Task Une_erreur_qui_n_est_pas_une_annulation_remonte()
    {
        using LatestRequest requests = new();

        Func<Task> call = () => requests.RunAsync<string>(_ => throw new InvalidOperationException("panne"));

        await call.Should().ThrowAsync<InvalidOperationException>();
    }
}
