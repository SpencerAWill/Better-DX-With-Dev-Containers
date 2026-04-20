using System.Net;

namespace OrderingPlatform.Ordering.Api.IntegrationTests;

/// <summary>
/// Proves the ordering-api host boots under <see cref="OrderingApiFactory"/>. Real endpoint tests
/// (POST /checkout, Stripe webhook verification, etc.) go in sibling files as endpoints stabilize.
/// </summary>
public class HostBootSmokeTests : IClassFixture<OrderingApiFactory>
{
    private readonly OrderingApiFactory _factory;

    public HostBootSmokeTests(OrderingApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Unknown_route_returns_404_proving_host_is_alive()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/__does_not_exist__");
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
