using Lettra.Application.Abstractions.Sync;
using Lettra.Application.Mapping;
using Lettra.Contracts.Common;
using Lettra.Contracts.Sync;
using Lettra.Domain.Common;
using Lettra.Infrastructure.Persistence;
using Lettra.Infrastructure.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lettra.Sync.Tests;

/// <summary>Le jeton de réception ne saute jamais une écriture qui peut encore se valider.</summary>
public sealed class ChangeFeedTests : IAsyncLifetime
{
    private readonly SyncWorld _world = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _world.DisposeAsync();

    private Task<Result<SyncChangesResponse>> ReadAsync(string? token) =>
        _world.Server.RunAsync(sp => sp.GetRequiredService<ISyncChangeFeed>().ReadAsync(token, 100, new SyncFeedScope(true, true), default));

    [Fact]
    public async Task Le_jeton_s_arrete_devant_un_trou_recent_puis_le_franchit_apres_le_delai()
    {
        (await _world.Server.SendAsync(SynchronisationTests.Martin().ToCommand(_world.Server.Dossier))).IsSuccess.Should().BeTrue();
        SyncChangesResponse snapshot = (await ReadAsync(null)).Value;
        snapshot.Token.Should().Be("j:1", "aucune séquence ne manque sous la ligne 1 : l'instantané la couvre");
        snapshot.Customers.Should().ContainSingle();

        // Une transaction a pris la séquence 2 sans être encore validée ; la 3 est déjà là.
        long now = _world.Clock.Now.ToUnixTimeMilliseconds();
        await _world.Server.RunAsync(async sp =>
        {
            LettraDbContext db = await sp.GetRequiredService<ITenantDbContextFactory>().CreateAsync(default);
            await using (db)
            {
                return await db.Database.ExecuteSqlInterpolatedAsync(
                    $"INSERT INTO SyncJournal (Sequence, Entity, EntityId, ChangedAtMs) VALUES (3, 1, {Guid.CreateVersion7()}, {now})");
            }
        });

        SyncChangesResponse blocked = (await ReadAsync("j:1")).Value;
        blocked.Token.Should().Be("j:1", "la 3 attend la 2, qui peut encore se valider");
        blocked.Customers.Should().BeEmpty();
        blocked.HasMore.Should().BeFalse();

        _world.Clock.Advance(TimeSpan.FromSeconds(31));
        (await ReadAsync("j:1")).Value.Token.Should().Be("j:3", "au-delà du délai, la séquence 2 est tenue pour annulée");
    }

    [Theory]
    [InlineData("x")]
    [InlineData("j:-1")]
    [InlineData("s:1:9:0")]
    public async Task Un_jeton_inconnu_est_refuse(string token)
    {
        Result<SyncChangesResponse> read = await ReadAsync(token);

        read.Errors.Should().ContainSingle(e => e.Code == ErrorCodes.Sync.TokenInvalid);
    }
}
