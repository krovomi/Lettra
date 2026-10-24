using Lettra.UI.Shared.Services;
using Microsoft.JSInterop;

namespace Lettra.UI.Tests.Services;

public sealed class JsModulesTests
{
    [Fact]
    public async Task Le_module_est_nettoye_puis_libere()
    {
        RecordingModule module = new();

        await JsModules.ReleaseAsync(module, "unwatch", "detach");

        module.Calls.Should().Equal("unwatch", "detach", "dispose");
    }

    [Fact]
    public async Task Un_hote_ferme_ou_un_module_detache_n_est_pas_une_erreur()
    {
        await FluentActions.Awaiting(() => JsModules.ReleaseAsync(new RecordingModule(new JSDisconnectedException("fermé")), "unwatch").AsTask())
            .Should().NotThrowAsync();
        await FluentActions.Awaiting(() => JsModules.ReleaseAsync(new RecordingModule(new JSException("détaché")), "unwatch").AsTask())
            .Should().NotThrowAsync();
        await FluentActions.Awaiting(() => JsModules.ReleaseAsync(null).AsTask()).Should().NotThrowAsync();
    }

    private sealed class RecordingModule(Exception? failure = null) : IJSObjectReference
    {
        public List<string> Calls { get; } = [];

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => Record<TValue>(identifier);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => Record<TValue>(identifier);

        public ValueTask DisposeAsync()
        {
            Calls.Add("dispose");
            return ValueTask.CompletedTask;
        }

        private ValueTask<TValue> Record<TValue>(string identifier)
        {
            Calls.Add(identifier);
            return failure is null ? ValueTask.FromResult(default(TValue)!) : ValueTask.FromException<TValue>(failure);
        }
    }
}
