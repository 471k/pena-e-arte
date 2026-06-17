using MediatR;
using Pena_e_Arte.Application.Payments.Commands;
using Pena_e_Arte.Application.Payments.Queries;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;

namespace Pena_e_Arte.API.Endpoints;

public static class PaymentEndpoints
{
    public static void MapPaymentEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/v1/payments")
            .RequireAuthorization();

        group.MapPost("/",
            CreatePaymentIntent).RequireAuthorization("OwnerOnly");

        group.MapPost("/cash",
            DeclareCashDeposit).RequireAuthorization("ClientAndAbove");

        group.MapPost("/deposit",
            CreateDepositPayment).RequireAuthorization("ClientAndAbove");

        group.MapGet("/",
            GetPayments).RequireAuthorization("OwnerOnly");

        group.MapGet("/appointment/{appointmentId:guid}",
            GetPaymentByAppointment).RequireAuthorization("ClientAndAbove");

        group.MapPut("/{id:guid}/splits",
            UpdateSessionSplits).RequireAuthorization("OwnerOnly");

        group.MapPost("/{id:guid}/capture",
            CaptureDeposit).RequireAuthorization("OwnerOnly");

        group.MapPost("/{id:guid}/cash/confirm",
            ConfirmCashDeposit).RequireAuthorization("ArtistAndAbove");

        group.MapPost("/{id:guid}/refund",
            RefundPayment).RequireAuthorization("OwnerOnly");

        group.MapGet("/{id:guid}/client-secret",
            GetClientSecret).RequireAuthorization("ClientAndAbove");

        group.MapGet("/{id:guid}/invoice",
            DownloadInvoice).RequireAuthorization("ClientAndAbove");
    }

    private static async Task<IResult> CreatePaymentIntent(
        CreatePaymentIntentRequest request,
        ISender                    mediator,
        CancellationToken          ct)
    {
        PaymentIntentResponse result = await mediator.Send(new CreatePaymentIntentCommand(request), ct);
        return Results.Created($"/api/v1/payments/{result.PaymentId}", result);
    }

    private static async Task<IResult> DeclareCashDeposit(
        DeclareCashDepositRequest request,
        ISender                   mediator,
        CancellationToken         ct)
    {
        PaymentResponse result = await mediator.Send(
            new DeclareCashDepositCommand(request.AppointmentId, request.Note), ct);
        return Results.Created($"/api/v1/payments/{result.Id}", result);
    }

    private static async Task<IResult> CreateDepositPayment(
        CreateDepositPaymentRequest request,
        ISender                     mediator,
        CancellationToken           ct)
    {
        PaymentIntentResponse result = await mediator.Send(
            new CreateDepositPaymentCommand(request.AppointmentId), ct);
        return Results.Created($"/api/v1/payments/{result.PaymentId}", result);
    }

    private static async Task<IResult> GetPayments(
        ISender           mediator,
        CancellationToken ct,
        Guid?             lastSeenId = null,
        int               pageSize   = 20)
    {
        List<PaymentResponse> result = await mediator.Send(new GetPaymentsQuery(lastSeenId, pageSize), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetPaymentByAppointment(
        Guid              appointmentId,
        ISender           mediator,
        CancellationToken ct)
    {
        PaymentResponse? result = await mediator.Send(new GetPaymentByAppointmentQuery(appointmentId), ct);
        return result is null ? Results.NotFound() : Results.Ok(result);
    }

    private static async Task<IResult> UpdateSessionSplits(
        Guid                       id,
        UpdateSessionSplitsRequest request,
        ISender                    mediator,
        CancellationToken          ct)
    {
        PaymentResponse result = await mediator.Send(new UpdateSessionSplitsCommand(id, request), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> CaptureDeposit(
        Guid              id,
        ISender           mediator,
        CancellationToken ct)
    {
        PaymentResponse result = await mediator.Send(new CaptureDepositCommand(id), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> ConfirmCashDeposit(
        Guid              id,
        ISender           mediator,
        CancellationToken ct)
    {
        PaymentResponse result = await mediator.Send(new ConfirmCashDepositCommand(id), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> RefundPayment(
        Guid              id,
        ISender           mediator,
        CancellationToken ct,
        decimal?          amount = null)
    {
        PaymentResponse result = await mediator.Send(new RefundPaymentCommand(id, amount), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetClientSecret(
        Guid              id,
        ISender           mediator,
        CancellationToken ct)
    {
        PaymentClientSecretResponse result = await mediator.Send(new GetPaymentClientSecretQuery(id), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> DownloadInvoice(
        Guid              id,
        ISender           mediator,
        CancellationToken ct)
    {
        byte[] pdf = await mediator.Send(new GetPaymentInvoiceQuery(id), ct);
        return Results.File(pdf, "application/pdf", $"invoice-{id:N}.pdf");
    }
}
