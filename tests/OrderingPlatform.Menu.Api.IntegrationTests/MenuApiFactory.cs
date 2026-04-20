namespace OrderingPlatform.Menu.Api.IntegrationTests;

/// <summary>
/// Boots the menu-api in-memory for HTTP-level testing.
/// </summary>
public sealed class MenuApiFactory : TestWebApplicationFactoryBase<Program>
{
    protected override string DatabaseConnectionString =>
        $"Host={TestEnvironment.PostgresHost};Port=5432;Database=test_menu_api_placeholder;Username=postgres;Password=postgres";
}
