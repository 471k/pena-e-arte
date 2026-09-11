using MediatR;
using Pena_e_Arte.Application.Payments.Commands;
using Pena_e_Arte.Application.Payments.Queries;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.API.Endpoints;

public static class PaymentEndpoints
{
    public static void MapPaymentEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/v1/payments")
            .RequireAuthorization();

        group.MapPost("/",
            CreatePaymentIntent).RequireAuthorization("OwnerOnly").RequireRateLimiting("billing");

        group.MapPost("/cash",
            DeclareCashDeposit).RequireAuthorization("ClientAndAbove");

        group.MapPost("/deposit",
            CreateDepositPayment).RequireAuthorization("ClientAndAbove").RequireRateLimiting("billing");

        group.MapGet("/",
            GetPayments).RequireAuthorization("OwnerOnly");

        group.MapGet("/appointment/{appointmentId:guid}",
            GetPaymentByAppointment).RequireAuthorization("ClientAndAbove");

        group.MapPut("/{id:guid}/splits",
            UpdateSessionSplits).RequireAuthorization("OwnerOnly");

        group.MapPost("/{id:guid}/capture",
            CaptureDeposit).RequireAuthorization("OwnerOnly").RequireRateLimiting("billing");

        group.MapPost("/{id:guid}/cash/confirm",
            ConfirmCashDeposit).RequireAuthorization("ArtistAndAbove");

        group.MapPost("/{id:guid}/refund",
            RefundPayment).RequireAuthorization("OwnerOnly").RequireRateLimiting("billing");

        group.MapGet("/{id:guid}/client-token",
            GetClientToken).RequireAuthorization("ClientAndAbove");

        // Called by the checkout page right after the POK widget's own onSuccess fires — that
        // callback is UX only, never a source of truth (ADR-0001). This re-fetches the real
        // status from POK before the page reports success to the user.
        group.MapPost("/{id:guid}/confirm",
            ConfirmCardPayment).RequireAuthorization("ClientAndAbove").RequireRateLimiting("billing");

        group.MapGet("/{id:guid}/invoice",
            DownloadInvoice).RequireAuthorization("ClientAndAbove");

        group.MapGet("/capabilities",
            GetPaymentCapabilities);

        group.MapPost("/pok/connect",
            ConnectPokAccount).RequireAuthorization("OwnerOnly").RequireRateLimiting("billing");

        group.MapGet("/pok/connection",
            GetPokConnectionStatus).RequireAuthorization("OwnerOnly").RequireRateLimiting("billing");

        // POK sends no signature (ADR-0001 — documented as unsigned). This endpoint never reads
        // payment state from the body; it only triggers PaymentReconciliationJob to re-fetch real
        // state from POK sooner than its normal schedule. Rate-limited since there's no signature
        // to lean on for abuse protection.
        app.MapPost("/api/v1/webhooks/pok", HandlePokWebhook)
            .AllowAnonymous().RequireRateLimiting("billing");
    }

    private static IResult HandlePokWebhook(IJobScheduler jobScheduler)
    {
        jobScheduler.TriggerPaymentReconciliationNow();
        return Results.Ok();
    }

    private static async Task<IResult> ConnectPokAccount(
        ConnectPokAccountRequest request,
        ICurrentTenant tenant,
        ISender mediator,
        CancellationToken ct)
    {
        await mediator.Send(new ConnectPokAccountCommand(tenant.StudioId, request), ct);
        return Results.Ok();
    }

    private static async Task<IResult> GetPokConnectionStatus(
        ISender mediator,
        CancellationToken ct)
    {
        PokConnectionStatusResponse result = await mediator.Send(new GetPokConnectionStatusQuery(), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> CreatePaymentIntent(
        CreatePaymentIntentRequest request,
        ISender mediator,
        CancellationToken ct)
    {
        PaymentIntentResponse result = await mediator.Send(new CreatePaymentIntentCommand(request), ct);
        return Results.Created($"/api/v1/payments/{result.PaymentId}", result);
    }

    private static async Task<IResult> DeclareCashDeposit(
        DeclareCashDepositRequest request,
        ISender mediator,
        CancellationToken ct)
    {
        PaymentResponse result = await mediator.Send(
            new DeclareCashDepositCommand(request.AppointmentId, request.Note), ct);
        return Results.Created($"/api/v1/payments/{result.Id}", result);
    }

    private static async Task<IResult> CreateDepositPayment(
        CreateDepositPaymentRequest request,
        ISender mediator,
        CancellationToken ct)
    {
        PaymentIntentResponse result = await mediator.Send(
            new CreateDepositPaymentCommand(request.AppointmentId), ct);
        return Results.Created($"/api/v1/payments/{result.PaymentId}", result);
    }

    private static async Task<IResult> GetPayments(
        ISender mediator,
        CancellationToken ct,
        Guid? lastSeenId = null,
        int pageSize = 20)
    {
        List<PaymentResponse> result = await mediator.Send(new GetPaymentsQuery(lastSeenId, pageSize), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetPaymentByAppointment(
        Guid appointmentId,
        ISender mediator,
        CancellationToken ct)
    {
        PaymentResponse? result = await mediator.Send(new GetPaymentByAppointmentQuery(appointmentId), ct);
        return result is null ? Results.NotFound() : Results.Ok(result);
    }

    private static async Task<IResult> UpdateSessionSplits(
        Guid id,
        UpdateSessionSplitsRequest request,
        ISender mediator,
        CancellationToken ct)
    {
        PaymentResponse result = await mediator.Send(new UpdateSessionSplitsCommand(id, request), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> CaptureDeposit(
        Guid id,
        ISender mediator,
        CancellationToken ct)
    {
        PaymentResponse result = await mediator.Send(new CaptureDepositCommand(id), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> ConfirmCashDeposit(
        Guid id,
        ISender mediator,
        CancellationToken ct)
    {
        PaymentResponse result = await mediator.Send(new ConfirmCashDepositCommand(id), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> RefundPayment(
        Guid id,
        ISender mediator,
        CancellationToken ct,
        decimal? amount = null)
    {
        PaymentResponse result = await mediator.Send(new RefundPaymentCommand(id, amount), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetClientToken(
        Guid id,
        ISender mediator,
        CancellationToken ct)
    {
        PaymentClientTokenResponse result = await mediator.Send(new GetPaymentClientTokenQuery(id), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> ConfirmCardPayment(
        Guid id,
        ISender mediator,
        CancellationToken ct)
    {
        PaymentResponse result = await mediator.Send(new ConfirmCardPaymentCommand(id), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> DownloadInvoice(
        Guid id,
        ISender mediator,
        CancellationToken ct)
    {
        byte[] pdf = await mediator.Send(new GetPaymentInvoiceQuery(id), ct);
        return Results.File(pdf, "application/pdf", $"invoice-{id:N}.pdf");
    }

    private static async Task<IResult> GetPaymentCapabilities(
        ISender mediator,
        CancellationToken ct)
    {
        PaymentCapabilitiesResponse result = await mediator.Send(new GetPaymentCapabilitiesQuery(), ct);
        return Results.Ok(result);
    }
}
