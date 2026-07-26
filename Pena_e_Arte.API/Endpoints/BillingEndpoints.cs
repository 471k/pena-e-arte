using MediatR;
using Pena_e_Arte.Application.Billing.Commands;
using Pena_e_Arte.Application.Billing.Commands.CreateBillingPortal;
using Pena_e_Arte.Application.Billing.Queries;
using Pena_e_Arte.Application.Payments.Commands;
using Pena_e_Arte.Application.Plans.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Interfaces;
using Stripe;
using Stripe.Checkout;
using System.Collections.Generic;

namespace Pena_e_Arte.API.Endpoints;

public static class BillingEndpoints
{
    public static void MapBillingEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder billingGroup = app.MapGroup("/api/v1/billing")
            .RequireAuthorization();

        billingGroup.MapGet("/plans", GetPlans).RequireAuthorization("OwnerOnly");
        billingGroup.MapPost("/plans", CreatePlan).RequireAuthorization("IssuerOnly");
        billingGroup.MapPut("/plans/{id:guid}", UpdatePlan).RequireAuthorization("IssuerOnly");
        billingGroup.MapDelete("/plans/{id:guid}", DeletePlan).RequireAuthorization("IssuerOnly");

        billingGroup.MapGet("/subscription", GetSubscription).RequireAuthorization("OwnerOnly");
        billingGroup.MapGet("/usage", GetPlanUsage).RequireAuthorization("OwnerOnly");
        billingGroup.MapPost("/subscription", CreateSubscription).RequireAuthorization("OwnerOnly");
        billingGroup.MapPost("/subscription/checkout", CreateCheckout)
            .RequireAuthorization("OwnerOnly").RequireRateLimiting("billing");
        billingGroup.MapPost("/subscription/checkout/finalize", FinalizeCheckout)
            .RequireAuthorization("OwnerOnly").RequireRateLimiting("billing");
        billingGroup.MapPut("/subscription/plan", ChangePlan).RequireAuthorization("OwnerOnly");
        billingGroup.MapDelete("/subscription/plan/pending", CancelPlanChange).RequireAuthorization("OwnerOnly");
        billingGroup.MapPost("/portal", CreateBillingPortalSession).RequireAuthorization("OwnerOnly");

        RouteGroupBuilder webhookGroup = app.MapGroup("/api/v1/webhooks/stripe");

        webhookGroup.MapPost("/billing", HandleBillingWebhook).AllowAnonymous();
        webhookGroup.MapPost("/connect", HandleConnectWebhook).AllowAnonymous();
    }

    private static async Task<IResult> GetPlans(
        ISender mediator,
        CancellationToken ct)
    {
        List<PlanResponse> result = await mediator.Send(new GetPlansQuery(), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> CreatePlan(
        CreatePlanRequest request,
        ISender mediator,
        CancellationToken ct)
    {
        PlanResponse result = await mediator.Send(new CreatePlanCommand(request), ct);
        return Results.Created($"/api/v1/billing/plans/{result.Id}", result);
    }

    private static async Task<IResult> UpdatePlan(
        Guid id,
        UpdatePlanRequest request,
        ISender mediator,
        CancellationToken ct)
    {
        PlanResponse result = await mediator.Send(new UpdatePlanCommand(id, request), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> DeletePlan(
        Guid id,
        ISender mediator,
        CancellationToken ct)
    {
        await mediator.Send(new DeletePlanCommand(id), ct);
        return Results.NoContent();
    }

    private static async Task<IResult> GetSubscription(
        ISender mediator,
        CancellationToken ct)
    {
        SubscriptionResponse result = await mediator.Send(new GetSubscriptionQuery(), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetPlanUsage(
        ISender mediator,
        CancellationToken ct)
    {
        PlanUsageResponse? result = await mediator.Send(new GetPlanUsageQuery(), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> CreateSubscription(
        CreateSubscriptionRequest request,
        ISender mediator,
        CancellationToken ct)
    {
        SubscriptionResponse result = await mediator.Send(new CreateSubscriptionCommand(request), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> CreateCheckout(
        CreateCheckoutRequest request,
        ISender mediator,
        CancellationToken ct)
    {
        CheckoutSessionResponse result = await mediator.Send(new CreateSubscriptionCheckoutCommand(request), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> FinalizeCheckout(
        FinalizeCheckoutRequest request,
        ICurrentTenant tenant,
        ISender mediator,
        CancellationToken ct)
    {
        SubscriptionResponse? result = await mediator.Send(
            new ActivateCheckoutSubscriptionCommand(request.SessionId, tenant.StudioId), ct);

        // Null = checkout not yet completed at Stripe; tell the client to keep waiting.
        return result is null ? Results.Accepted() : Results.Ok(result);
    }

    private static async Task<IResult> ChangePlan(
        ChangePlanRequest request,
        ISender mediator,
        CancellationToken ct)
    {
        SubscriptionResponse result = await mediator.Send(new ChangePlanCommand(request), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> CancelPlanChange(
        ISender mediator,
        CancellationToken ct)
    {
        SubscriptionResponse result = await mediator.Send(new CancelPlanChangeCommand(), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> CreateBillingPortalSession(
        CreateBillingPortalRequest request,
        ISender mediator,
        CancellationToken ct)
    {
        CreateBillingPortalResult result = await mediator.Send(
            new CreateBillingPortalCommand(request.ReturnUrl), ct);
        return Results.Ok(new { url = result.Url });
    }

    private static async Task<IResult> HandleBillingWebhook(
        HttpRequest httpRequest,
        IConfiguration configuration,
        ISender mediator,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        ILogger logger = loggerFactory.CreateLogger("Stripe.BillingWebhook");
        string payload = await new StreamReader(httpRequest.Body).ReadToEndAsync(ct);
        string signature = httpRequest.Headers["Stripe-Signature"].ToString();
        string secret = configuration["Stripe:WebhookSecretBilling"]!;

        Event stripeEvent;
        try
        {
            stripeEvent = EventUtility.ConstructEvent(payload, signature, secret, throwOnApiVersionMismatch: false);
        }
        catch (StripeException ex)
        {
            logger.LogWarning(ex, "Invalid Stripe billing webhook signature");
            return Results.Unauthorized();
        }

        logger.LogInformation("Stripe billing webhook received {@EventType}", stripeEvent.Type);

        try
        {
            switch (stripeEvent.Type)
            {
                // Owner completed the hosted Checkout — activate their subscription.
                case "checkout.session.completed" when stripeEvent.Data.Object is Session session:
                    await mediator.Send(new ActivateCheckoutSubscriptionCommand(session.Id, null), ct);
                    break;

                case "invoice.paid" when stripeEvent.Data.Object is Invoice invoice:
                    {
                        string? stripeSubId = invoice.Parent?.SubscriptionDetails?.SubscriptionId;
                        if (stripeSubId is not null)
                            await mediator.Send(new HandleInvoicePaidCommand(stripeSubId, invoice.PeriodEnd), ct);
                        break;
                    }

                case "customer.subscription.updated" when stripeEvent.Data.Object is Stripe.Subscription sub:
                    {
                        string? priceId = sub.Items?.Data?.FirstOrDefault()?.Price?.Id;
                        DateTime periodEnd = sub.Items?.Data?.FirstOrDefault()?.CurrentPeriodEnd
                                             ?? DateTime.UtcNow.AddMonths(1);
                        await mediator.Send(
                            new HandleSubscriptionUpdatedCommand(
                                sub.Id, sub.Status, periodEnd, priceId, sub.CancelAtPeriodEnd), ct);
                        break;
                    }

                case "customer.subscription.deleted" when stripeEvent.Data.Object is Stripe.Subscription sub:
                    await mediator.Send(new HandleSubscriptionDeletedCommand(sub.Id), ct);
                    break;
            }
        }
        catch (Exception ex)
        {
            // Signature is already verified at this point — a processing failure here
            // is our bug, not Stripe's. Returning non-200 would just make Stripe retry
            // the same event for up to 3 days without fixing anything; log and move on.
            logger.LogError(ex, "Failed to process Stripe billing webhook {@EventType}", stripeEvent.Type);
        }

        return Results.Ok();
    }

    private static async Task<IResult> HandleConnectWebhook(
        HttpRequest httpRequest,
        IConfiguration configuration,
        ISender mediator,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        ILogger logger = loggerFactory.CreateLogger("Stripe.ConnectWebhook");
        string payload = await new StreamReader(httpRequest.Body).ReadToEndAsync(ct);
        string signature = httpRequest.Headers["Stripe-Signature"].ToString();
        string secret = configuration["Stripe:WebhookSecretConnect"]!;

        Event stripeEvent;
        try
        {
            stripeEvent = EventUtility.ConstructEvent(payload, signature, secret, throwOnApiVersionMismatch: false);
        }
        catch (StripeException ex)
        {
            logger.LogWarning(ex, "Invalid Stripe connect webhook signature");
            return Results.Unauthorized();
        }

        logger.LogInformation("Stripe connect webhook received {@EventType}", stripeEvent.Type);

        try
        {
            switch (stripeEvent.Type)
            {
                // Manual-capture flow: client authorized the card — deposit is now held
                case "payment_intent.amount_capturable_updated" when stripeEvent.Data.Object is PaymentIntent intent:
                    await mediator.Send(new MarkPaymentAuthorizedCommand(intent.Id), ct);
                    break;

                case "payment_intent.succeeded" when stripeEvent.Data.Object is PaymentIntent intent:
                    await mediator.Send(new ConfirmPaymentCommand(intent.Id), ct);
                    break;

                case "payment_intent.payment_failed" when stripeEvent.Data.Object is PaymentIntent intent:
                    await mediator.Send(new MarkPaymentFailedCommand(intent.Id), ct);
                    break;
            }
        }
        catch (Exception ex)
        {
            // See HandleBillingWebhook — same reasoning: log, don't make Stripe retry.
            logger.LogError(ex, "Failed to process Stripe connect webhook {@EventType}", stripeEvent.Type);
        }

        return Results.Ok();
    }
}
