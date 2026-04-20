using System.Text.Json;
using System.Text.Json.Nodes;

namespace OrderingPlatform.Ordering.Api.UnitTests;

/// <summary>
/// Sample unit tests showing the xUnit + Shouldly + NSubstitute convention.
/// Unit tests stay in-memory — no I/O, no DbContext, no HTTP, no Service Bus.
/// </summary>
public class CheckoutDtoTests
{
    [Fact]
    public void CheckoutRequestDto_roundtrips_through_JSON()
    {
        var request = new CheckoutRequestDto
        {
            Details = new CheckoutDetailsDto
            {
                UniqueItems = new Dictionary<string, JsonObject>
                {
                    ["hash-abc"] = new() { ["sku"] = "burger-combo" },
                },
                ItemUnits = new Dictionary<string, decimal> { ["hash-abc"] = 2m },
            },
            PreTip = new TipRequestDto { Type = TipTypeDto.Percentage, Amount = 15m },
        };

        var json = JsonSerializer.Serialize(request);
        var roundTripped = JsonSerializer.Deserialize<CheckoutRequestDto>(json);

        roundTripped.ShouldNotBeNull();
        roundTripped.Details.ItemUnits["hash-abc"].ShouldBe(2m);
        roundTripped.PreTip!.Amount.ShouldBe(15m);
        roundTripped.PreTip.Type.ShouldBe(TipTypeDto.Percentage);
    }
}
