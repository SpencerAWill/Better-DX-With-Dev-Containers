namespace OrderingPlatform.TestingCommon;

/// <summary>
/// Connection strings for dev-container sidecars. Defaults match the compose file in <c>.devcontainer/</c>;
/// override via environment variables to point at a different host (CI, local docker-desktop, etc.).
/// </summary>
public static class TestEnvironment
{
    public static string PostgresAdminConnectionString =>
        Environment.GetEnvironmentVariable("TEST_POSTGRES_ADMIN_CONNECTION")
        ?? "Host=postgres;Port=5432;Database=postgres;Username=postgres;Password=postgres;Include Error Detail=true";

    public static string PostgresHost =>
        Environment.GetEnvironmentVariable("TEST_POSTGRES_HOST") ?? "postgres";

    public static string ServiceBusConnectionString =>
        Environment.GetEnvironmentVariable("TEST_SERVICEBUS_CONNECTION")
        ?? "Endpoint=sb://servicebus-emulator;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;";

    public static string EventHubsConnectionString =>
        Environment.GetEnvironmentVariable("TEST_EVENTHUBS_CONNECTION")
        ?? "Endpoint=sb://eventhubs-emulator;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;";

    public static string AzuriteConnectionString =>
        Environment.GetEnvironmentVariable("TEST_AZURITE_CONNECTION")
        ?? "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;BlobEndpoint=http://azurite:10000/devstoreaccount1;QueueEndpoint=http://azurite:10001/devstoreaccount1;TableEndpoint=http://azurite:10002/devstoreaccount1;";

    public static string RedisConnectionString =>
        Environment.GetEnvironmentVariable("TEST_REDIS_CONNECTION") ?? "redis:6379";
}
