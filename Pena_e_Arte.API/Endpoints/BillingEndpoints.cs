using MediatR;
using Pena_e_Arte.Application.Billing.Commands;
using Pena_e_Arte.Application.Billing.Queries;
using Pena_e_Arte.Application.Payments.Commands;
using Pena_e_Arte.Application.Plans.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Stripe;
using System.Collections.Generic;

namespace Pena_e_Arte.API.Endpoints;

public static class BillingEndpoints
{
    public static void MapBillingEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder billingGroup = app.MapGroup("/api/v1/billing")
            .RequireAuthorization();

        billingGroup.MapGet("/plans",              GetPlans).RequireAuthorization("OwnerOnly");
        billingGroup.MapPost("/plans",             CreatePlan).RequireAuthorization("IssuerOnly");
        billingGroup.MapPut("/plans/{id:guid}",    UpdatePlan).RequireAuthorization("IssuerOnly");
        billingGroup.MapDelete("/plans/{id:guid}", DeletePlan).RequireAuthorization("IssuerOnly");

        billingGroup.MapGet("/subscription",   GetSubscription).RequireAuthorization("OwnerOnly");
        billingGroup.MapPost("/subscription",  CreateSubscription).RequireAuthorization("OwnerOnly");

        RouteGroupBuilder webhookGroup = app.MapGroup("/api/v1/webhooks/stripe");

        webhookGroup.MapPost("/billing", HandleBillingWebhook).AllowAnonymous();
        webhookGroup.MapPost("/connect", HandleConnectWebhook).AllowAnonymous();
    }

    private static async Task<IResult> GetPlans(
        ISender           mediator,
        CancellationToken ct)
    {
        List<PlanResponse> result = await mediator.Send(new GetPlansQuery(), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> CreatePlan(
        CreatePlanRequest request,
        ISender           mediator,
        CancellationToken ct)
    {
        PlanResponse result = await mediator.Send(new CreatePlanCommand(request), ct);
        return Results.Created($"/api/v1/billing/plans/{result.Id}", result);
    }

    private static async Task<IResult> UpdatePlan(
        Guid              id,
        UpdatePlanRequest request,
        ISender           mediator,
        CancellationToken ct)
    {
        PlanResponse result = await mediator.Send(new UpdatePlanCommand(id, request), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> DeletePlan(
        Guid              id,
        ISender           mediator,
        CancellationToken ct)
    {
        await mediator.Send(new DeletePlanCommand(id), ct);
        return Results.NoContent();
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
        ISender           mediator,
        ILoggerFactory    loggerFactory,
        CancellationToken ct)
    {
        ILogger logger    = loggerFactory.CreateLogger("Stripe.BillingWebhook");
        string  payload   = await new StreamReader(httpRequest.Body).ReadToEndAsync(ct);
        string  signature = httpRequest.Headers["Stripe-Signature"].ToString();
        string  secret    = configuration["Stripe:WebhookSecretBilling"]!;

        Event stripeEvent;
        try
        {
            stripeEvent = EventUtility.ConstructEvent(payload, signature, secret);
        }
        catch (StripeException ex)
        {
            logger.LogWarning(ex, "Invalid Stripe billing webhook signature");
            return Results.Unauthorized();
        }

        logger.LogInformation("Stripe billing webhook received {@EventType}", stripeEvent.Type);

        switch (stripeEvent.Type)
        {
            case "invoice.paid" when stripeEvent.Data.Object is Invoice invoice:
            {
                string? stripeSubId = invoice.Parent?.SubscriptionDetails?.SubscriptionId;
                if (stripeSubId is not null)
                    await mediator.Send(new HandleInvoicePaidCommand(stripeSubId, invoice.PeriodEnd), ct);
                break;
            }

            case "customer.subscription.updated" when stripeEvent.Data.Object is Stripe.Subscription sub:
            {
                string?  priceId   = sub.Items?.Data?.FirstOrDefault()?.Price?.Id;
                DateTime periodEnd = sub.Items?.Data?.FirstOrDefault()?.CurrentPeriodEnd
                                     ?? DateTime.UtcNow.AddMonths(1);
                await mediator.Send(
                    new HandleSubscriptionUpdatedCommand(sub.Id, sub.Status, periodEnd, priceId), ct);
                break;
            }

            case "customer.subscription.deleted" when stripeEvent.Data.Object is Stripe.Subscription sub:
                await mediator.Send(new HandleSubscriptionDeletedCommand(sub.Id), ct);
                break;
        }

        return Results.Ok();
    }

    private static async Task<IResult> HandleConnectWebhook(
        HttpRequest       httpRequest,
        IConfiguration    configuration,
        ISender           mediator,
        ILoggerFactory    loggerFactory,
        CancellationToken ct)
    {
        ILogger logger    = loggerFactory.CreateLogger("Stripe.ConnectWebhook");
        string  payload   = await new StreamReader(httpRequest.Body).ReadToEndAsync(ct);
        string  signature = httpRequest.Headers["Stripe-Signature"].ToString();
        string  secret    = configuration["Stripe:WebhookSecretConnect"]!;

        Event stripeEvent;
        try
        {
            stripeEvent = EventUtility.ConstructEvent(payload, signature, secret);
        }
        catch (StripeException ex)
        {
            logger.LogWarning(ex, "Invalid Stripe connect webhook signature");
            return Results.Unauthorized();
        }

        logger.LogInformation("Stripe connect webhook received {@EventType}", stripeEvent.Type);

        switch (stripeEvent.Type)
        {
            case "payment_intent.succeeded" when stripeEvent.Data.Object is PaymentIntent intent:
                await mediator.Send(new ConfirmPaymentCommand(intent.Id), ct);
                break;

            case "payment_intent.payment_failed" when stripeEvent.Data.Object is PaymentIntent intent:
                await mediator.Send(new MarkPaymentFailedCommand(intent.Id), ct);
                break;

            case "account.updated" when stripeEvent.Data.Object is Stripe.Account account:
                if (account.ChargesEnabled)
                    logger.LogInformation(
                        "Stripe Connect account onboarding complete {@StripeAccountId}", account.Id);
                break;
        }

        return Results.Ok();
    }
}
