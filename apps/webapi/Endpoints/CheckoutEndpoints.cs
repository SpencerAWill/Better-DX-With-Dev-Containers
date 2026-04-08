using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Stripe;

namespace Project.API.Endpoints;

public record PaymentMethodDto {
    // TODO: payment method details
}

public enum TipTypeDto {
    Percentage,
    FixedAmount
}

public record TipRequestDto {
    public required TipTypeDto Type { get; init; }
    public decimal Amount { get; init; } = 0m;
}

public record CheckoutDetailsDto {
    /// <summary>
    /// A dictionary of unique items (selections) in the cart, where the key is the item hash (hash considers options) and the value is the item details.
    /// This is used to calculate the total price of the cart and to validate the items in the cart.
    /// </summary>
    public IDictionary<string, JsonObject> UniqueItems { get; init; } = new Dictionary<string, JsonObject>();

    /// <summary>
    /// A dictionary of item hashes and their units in the cart. The item hash should match the keys in the UniqueItems dictionary.
    /// </summary>
    public IDictionary<string, decimal> ItemUnits { get; init; } = new Dictionary<string, decimal>();
}

public record CheckoutRequestDto {
    public required CheckoutDetailsDto Details { get; init; }
    public ICollection<PaymentMethodDto> Payments { get; init; } = [];
    public ICollection<string> DiscountCodes { get; init; } = [];
    public TipRequestDto? PreTip { get; init; }
}

public record CheckoutResponseDto {
    public required string ClientSecret { get; init; }
    public required string PaymentIntentId { get; init; }
}

public class CheckoutOptions {
    public bool EnableRecaptcha { get; set; } = true;
}

public static class CheckoutEndpoints
{
    public static RouteGroupBuilder MapCheckoutEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/checkout").WithTags("Checkout");

        group.MapPost("/", Checkout)
            .WithName("Checkout")
            .WithDescription("Creates a pending order and returns a Stripe client secret for payment confirmation.")
            .Produces(StatusCodes.Status200OK, typeof(CheckoutResponseDto))
            .Produces(StatusCodes.Status400BadRequest);

        return group;
    }

    private static async Task<IResult> Checkout(
        CheckoutRequestDto request,
        ILoggerFactory loggerFactory,
        StripeClient stripeClient,
        IOptions<CheckoutOptions> options,
        IWebHostEnvironment env,
        HttpContext context,
        CancellationToken cancellationToken = default
    ) {
        var logger = loggerFactory.CreateLogger("Checkout");
        logger.LogDebug("Checkout request started.");

        // Validate recaptcha (if not in development environment)
        if (!env.IsDevelopment() && options.Value.EnableRecaptcha) {
            logger.LogTrace("Checking recaptcha.");
            // Check recaptcha here, if it fails return BadRequest
        }
        else {
            logger.LogDebug("Skipping recaptcha check in development environment.");
        }

        // Validate the details
        logger.LogTrace("Validating order details.");
        {
            // validate unique items in parallel
            var itemValidations = new Dictionary<string, bool>();
            await Task.WhenAll(request.Details.UniqueItems.Select(async kvp => {
                var hash = kvp.Key;
                var item = kvp.Value;
                logger.LogTrace("Validating item with hash {ItemHash}.", hash);

                itemValidations[hash] = await Task.FromResult(true); // Replace with actual validation logic for the item
            }));
            // then, check those validations
            if (itemValidations.Values.Any(isValid => !isValid)) {
                logger.LogWarning("Checkout request with invalid items.");
                return Results.BadRequest("One or more items in the cart are invalid.");
            }

            // validate the item units are positive
            if (request.Details.ItemUnits.Values.Any(units => units <= 0)) {
                logger.LogWarning("Checkout request with non-positive item units.");
                return Results.BadRequest("Item units must be greater than zero.");
            }

            // In a real application, you would also check if they are in stock, etc.
        }
        logger.LogDebug("Cart items validated successfully.");

        // TODO: validate the payment methods
        logger.LogTrace("Validating payment methods.");
        {
            // In a real application, you would also validate the payment method details, check if the payment method is valid, etc.
        }
        logger.LogDebug("Payment methods validated successfully.");

        // TODO: validate any discount codes
        logger.LogTrace("Validating discount codes.");
        {
            // In a real application, you would check if the discount codes are valid, if they are applicable to the items in the cart, etc.
        }
        logger.LogDebug("Discount codes validated successfully.");

        // TODO: validate the pre-tip
        logger.LogTrace("Validating pre-tip.");
        {
            if (request.PreTip != null && request.PreTip.Amount < 0) {
                logger.LogWarning("Checkout request with negative pre-tip amount.");
                return Results.BadRequest("Pre-tip amount cannot be negative.");
            }
        }
        logger.LogDebug("Pre-tip validated successfully.");

        var uniqueItems = request.Details.UniqueItems;
        var itemUnits = request.Details.ItemUnits;

        // TODO: Calculate the total price
        logger.LogTrace("Calculating total price.");
        long totalAmountInCents = 0;
        {
            // get the item pricing service

            // calculate unit price for each individual item

            // calculate line item totals (unit price * units)

            // calculate subtotal (sum of line item totals)

            // calculate discounts

            // calculate taxes

            // calculate final total and assign to totalAmountInCents
        }
        logger.LogDebug("Total price calculated successfully.");

        // Create PaymentIntent (unconfirmed — client will confirm via Stripe.js)
        logger.LogTrace("Creating payment intent.");
        Stripe.PaymentIntent paymentIntent;
        {
            paymentIntent = await stripeClient.V1.PaymentIntents.CreateAsync(
                new PaymentIntentCreateOptions {
                    Amount = totalAmountInCents,
                    Currency = "usd",
                    PaymentMethodTypes = ["card"],
                },
                cancellationToken: cancellationToken);
        }
        logger.LogDebug("Payment intent {PaymentIntentId} created.", paymentIntent.Id);

        // Return the client secret so the frontend can confirm payment via Stripe.js
        logger.LogInformation("Checkout initiated with PaymentIntent {PaymentIntentId}.", paymentIntent.Id);
        return Results.Ok(new CheckoutResponseDto {
            ClientSecret = paymentIntent.ClientSecret,
            PaymentIntentId = paymentIntent.Id,
        });
    }
}
