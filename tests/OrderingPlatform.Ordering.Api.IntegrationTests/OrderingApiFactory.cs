using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace OrderingPlatform.Ordering.Api.IntegrationTests;

/// <summary>
/// Boots the ordering-api in-memory for HTTP-level testing. Overrides Stripe settings with a dummy
/// key so registration succeeds without reaching out to Stripe.
/// </summary>
public sealed class OrderingApiFactory : TestWebApplicationFactoryBase<Program>
{
    protected override string DatabaseConnectionString =>
        // ordering-api does not yet own a DbContext. When it does, point this at a PostgresDatabase fixture.
        $"Host={TestEnvironment.PostgresHost};Port=5432;Database=test_ordering_api_placeholder;Username=postgres;Password=postgres";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Stripe:SecretKey"] = "sk_test_dummy_not_used_in_tests",
                ["Stripe:PublishableKey"] = "pk_test_dummy_not_used_in_tests",
                ["Stripe:WebhookSecret"] = "whsec_dummy_not_used_in_tests",
            });
        });
    }
}
