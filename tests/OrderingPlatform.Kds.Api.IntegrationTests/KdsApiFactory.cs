namespace OrderingPlatform.Kds.Api.IntegrationTests;

/// <summary>
/// Boots the kds-api in-memory for HTTP-level testing.
/// </summary>
public sealed class KdsApiFactory : TestWebApplicationFactoryBase<Program>
{
    protected override string DatabaseConnectionString =>
        $"Host={TestEnvironment.PostgresHost};Port=5432;Database=test_kds_api_placeholder;Username=postgres;Password=postgres";
}
