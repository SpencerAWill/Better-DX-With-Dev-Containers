using Stripe;

namespace Project.API.Endpoints;

public static class StripeWebhookEndpoints
{
    public static RouteGroupBuilder MapStripeWebhookEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/webhooks/stripe").WithTags("Webhooks");

        group.MapPost("/", HandleStripeWebhook)
            .WithName("StripeWebhook")
            .WithDescription("Handles incoming Stripe webhook events.")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest);

        return group;
    }

    private static async Task<IResult> HandleStripeWebhook(
        HttpContext context,
        ILoggerFactory loggerFactory,
        IConfiguration configuration,
        CancellationToken cancellationToken = default
    ) {
        var logger = loggerFactory.CreateLogger("StripeWebhook");
        var endpointSecret = configuration["Stripe:WebhookSecret"]
            ?? throw new InvalidOperationException("Stripe:WebhookSecret is not configured.");

        // Read the raw request body for signature verification
        var json = await new StreamReader(context.Request.Body).ReadToEndAsync(cancellationToken);

        Event stripeEvent;
        try {
            stripeEvent = EventUtility.ConstructEvent(
                json,
                context.Request.Headers["Stripe-Signature"],
                endpointSecret
            );
        } catch (StripeException ex) {
            logger.LogWarning(ex, "Stripe webhook signature verification failed.");
            return Results.BadRequest("Invalid signature.");
        }

        logger.LogDebug("Received Stripe event {EventType} ({EventId}).", stripeEvent.Type, stripeEvent.Id);

        switch (stripeEvent.Type) {
            case EventTypes.PaymentIntentSucceeded:
                await HandlePaymentIntentSucceeded(stripeEvent, logger);
                break;

            case EventTypes.PaymentIntentPaymentFailed:
                await HandlePaymentIntentFailed(stripeEvent, logger);
                break;

            default:
                logger.LogDebug("Unhandled Stripe event type: {EventType}.", stripeEvent.Type);
                break;
        }

        return Results.Ok();
    }

    private static Task HandlePaymentIntentSucceeded(Event stripeEvent, ILogger logger)
    {
        var paymentIntent = stripeEvent.Data.Object as PaymentIntent
            ?? throw new InvalidOperationException("Expected PaymentIntent in event data.");

        var publicOrderId = paymentIntent.Metadata["public_order_id"];
        var internalOrderId = paymentIntent.Metadata["internal_order_id"];

        logger.LogInformation(
            "Payment succeeded for order {PublicOrderId} <=> {InternalOrderId} (PaymentIntent {PaymentIntentId}).",
            publicOrderId, internalOrderId, paymentIntent.Id);

        // TODO: Update the order status from "pending" to "paid" in the database

        // TODO: Enqueue the order to the message bus for fulfillment

        // TODO: Send order confirmation (email, push notification, etc.)

        return Task.CompletedTask;
    }

    private static Task HandlePaymentIntentFailed(Event stripeEvent, ILogger logger)
    {
        var paymentIntent = stripeEvent.Data.Object as PaymentIntent
            ?? throw new InvalidOperationException("Expected PaymentIntent in event data.");

        var publicOrderId = paymentIntent.Metadata.GetValueOrDefault("public_order_id", "unknown");

        logger.LogWarning(
            "Payment failed for order {PublicOrderId} (PaymentIntent {PaymentIntentId}): {FailureMessage}.",
            publicOrderId, paymentIntent.Id, paymentIntent.LastPaymentError?.Message);

        // TODO: Update the order status to "payment_failed" in the database

        return Task.CompletedTask;
    }
}
