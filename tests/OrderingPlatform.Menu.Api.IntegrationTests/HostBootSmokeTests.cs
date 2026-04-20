using System.Net;

namespace OrderingPlatform.Menu.Api.IntegrationTests;

public class HostBootSmokeTests : IClassFixture<MenuApiFactory>
{
    private readonly MenuApiFactory _factory;

    public HostBootSmokeTests(MenuApiFactory factory)
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
