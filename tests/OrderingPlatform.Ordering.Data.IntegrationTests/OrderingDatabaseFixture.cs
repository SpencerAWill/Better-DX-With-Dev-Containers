using Microsoft.EntityFrameworkCore;

namespace OrderingPlatform.Ordering.Data.IntegrationTests;

/// <summary>
/// xUnit class fixture: fresh Postgres database per test class with the ordering schema applied.
/// Once real migrations land in <c>libs/ordering-data</c>, swap <c>EnsureCreatedAsync</c> for
/// <c>MigrateAsync</c> so tests exercise the migration path we deploy with.
/// </summary>
public sealed class OrderingDatabaseFixture : PostgresDatabase
{
    public OrderingDatabaseFixture() : base(
        databaseName: $"test_ordering_{Guid.NewGuid():N}",
        applyMigrations: ApplySchema)
    {
    }

    private static async Task ApplySchema(string connectionString)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        await using var context = new AppDbContext(options);
        await context.Database.EnsureCreatedAsync();
    }

    public AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        return new AppDbContext(options);
    }
}
