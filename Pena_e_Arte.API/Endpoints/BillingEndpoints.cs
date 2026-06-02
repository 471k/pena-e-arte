using MediatR;
using Pena_e_Arte.Application.Billing.Commands;
using Pena_e_Arte.Application.Billing.Queries;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Stripe;

namespace Pena_e_Arte.API.Endpoints;

public static class BillingEndpoints
{
    public static void MapBillingEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder billingGroup = app.MapGroup("/api/v1/billing")
            .RequireAuthorization();

        billingGroup.MapGet("/subscription",   GetSubscription).RequireAuthorization("OwnerOnly");
        billingGroup.MapPost("/subscription",  CreateSubscription).RequireAuthorization("OwnerOnly");

        RouteGroupBuilder webhookGroup = app.MapGroup("/api/v1/webhooks/stripe");

        webhookGroup.MapPost("/billing", HandleBillingWebhook).AllowAnonymous();
        webhookGroup.MapPost("/connect", HandleConnectWebhook).AllowAnonymous();
    }

    private static async Task<IResult> GetSubscription(
        ISender           mediator,
        CancellationToken ct)
    {
        SubscriptionResponse result = await mediator.Send(new GetSubscriptionQuery(), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> CreateSubscription(
        CreateSubscriptionRequest request,
        ISender                   mediator,
        CancellationToken         ct)
    {
        SubscriptionResponse result = await mediator.Send(new CreateSubscriptionCommand(request), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> HandleBillingWebhook(
        HttpRequest       httpRequest,
        IConfiguration    configuration,
        ILoggerFactory    loggerFactory,
        CancellationToken ct)
    {
        ILogger logger    = loggerFactory.CreateLogger("Stripe.BillingWebhook");
        string  payload   = await new StreamReader(httpRequest.Body).ReadToEndAsync(ct);
        string  signature = httpRequest.Headers["Stripe-Signature"].ToString();
        string  secret    = configuration["Stripe:WebhookSecretBilling"]!;

        try
        {
            Event stripeEvent = EventUtility.ConstructEvent(payload, signature, secret);
            logger.LogInformation("Stripe billing webhook received {@EventType}", stripeEvent.Type);
        }
        catch (StripeException ex)
        {
            logger.LogWarning(ex, "Invalid Stripe billing webhook signature");
            return Results.Unauthorized();
        }

        return Results.Ok();
    }

    private static async Task<IResult> HandleConnectWebhook(
        HttpRequest       httpRequest,
        IConfiguration    configuration,
        ILoggerFactory    loggerFactory,
        CancellationToken ct)
    {
        ILogger logger    = loggerFactory.CreateLogger("Stripe.ConnectWebhook");
        string  payload   = await new StreamReader(httpRequest.Body).ReadToEndAsync(ct);
        string  signature = httpRequest.Headers["Stripe-Signature"].ToString();
        string  secret    = configuration["Stripe:WebhookSecretConnect"]!;

        try
        {
            Event stripeEvent = EventUtility.ConstructEvent(payload, signature, secret);
            logger.LogInformation("Stripe connect webhook received {@EventType}", stripeEvent.Type);
        }
        catch (StripeException ex)
        {
            logger.LogWarning(ex, "Invalid Stripe connect webhook signature");
            return Results.Unauthorized();
        }

        return Results.Ok();
    }
}
