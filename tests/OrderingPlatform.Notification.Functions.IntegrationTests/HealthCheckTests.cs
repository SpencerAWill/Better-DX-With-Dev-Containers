using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace OrderingPlatform.Notification.Functions.IntegrationTests;

/// <summary>
/// The HealthCheck function is a thin HTTP trigger — driving it directly is the right granularity.
/// Once this project gains Service Bus / Event Hubs triggers, add sibling files that stand up a
/// test harness against the <c>servicebus-emulator</c> / <c>eventhubs-emulator</c> sidecars.
/// </summary>
public class HealthCheckTests
{
    [Fact]
    public void HealthCheck_returns_ok_result()
    {
        var function = new HealthCheck(NullLogger<HealthCheck>.Instance);
        var request = new DefaultHttpContext().Request;

        var result = function.Run(request);

        result.ShouldBeOfType<OkObjectResult>();
    }
}
