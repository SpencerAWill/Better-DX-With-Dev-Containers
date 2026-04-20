namespace OrderingPlatform.Admin.Api.IntegrationTests;

/// <summary>
/// Boots the admin-api in-memory for HTTP-level testing.
/// </summary>
public sealed class AdminApiFactory : TestWebApplicationFactoryBase<Program>
{
    protected override string DatabaseConnectionString =>
        $"Host={TestEnvironment.PostgresHost};Port=5432;Database=test_admin_api_placeholder;Username=postgres;Password=postgres";
}
