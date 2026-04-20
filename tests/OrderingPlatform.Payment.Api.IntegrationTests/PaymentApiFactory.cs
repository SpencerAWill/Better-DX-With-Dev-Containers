namespace OrderingPlatform.Payment.Api.IntegrationTests;

/// <summary>
/// Boots the payment-api in-memory for HTTP-level testing.
/// </summary>
public sealed class PaymentApiFactory : TestWebApplicationFactoryBase<Program>
{
    protected override string DatabaseConnectionString =>
        $"Host={TestEnvironment.PostgresHost};Port=5432;Database=test_payment_api_placeholder;Username=postgres;Password=postgres";
}
