using Microsoft.EntityFrameworkCore;

namespace OrderingPlatform.Ordering.Data.IntegrationTests;

/// <summary>
/// Smoke tests proving the Postgres + Respawn test harness is wired up correctly.
/// Real per-entity tests land here as the ordering domain model grows.
/// </summary>
public class AppDbContextTests : IClassFixture<OrderingDatabaseFixture>, IAsyncLifetime
{
    private readonly OrderingDatabaseFixture _fixture;

    public AppDbContextTests(OrderingDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Database_is_reachable()
    {
        await using var context = _fixture.CreateContext();
        var canConnect = await context.Database.CanConnectAsync();
        canConnect.ShouldBeTrue();
    }
}
