using MediatR;
using Pena_e_Arte.Application.ConsentForms.Commands;
using Pena_e_Arte.Application.ConsentForms.Queries;
using Pena_e_Arte.Application.IntakeForms.Commands;
using Pena_e_Arte.Application.IntakeForms.Queries;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.API.Endpoints;

public static class FormEndpoints
{
    public static void MapFormEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder intake = app.MapGroup("/api/v1/intake-forms")
            .RequireAuthorization();

        intake.MapPost("/", SubmitIntakeForm).RequireAuthorization("ClientAndAbove");
        intake.MapGet("/", GetIntakeForms).RequireAuthorization("ClientAndAbove");
        intake.MapGet("{id:guid}", GetIntakeFormById).RequireAuthorization("ClientAndAbove");

        RouteGroupBuilder consent = app.MapGroup("/api/v1/consent-forms")
            .RequireAuthorization();

        consent.MapPost("/", SignConsentForm).RequireAuthorization("ClientAndAbove");
        consent.MapGet("/active-template", GetActiveConsentTemplate).RequireAuthorization("ClientAndAbove");
        consent.MapGet("/", GetConsentForms).RequireAuthorization("ClientAndAbove");
        consent.MapGet("{id:guid}", GetConsentFormById).RequireAuthorization("ClientAndAbove");
    }

    private static async Task<IResult> GetActiveConsentTemplate(
        ConsentTemplateKind kind,
        ISender mediator,
        CancellationToken ct)
    {
        ConsentTemplateResponse result = await mediator.Send(new GetActiveConsentTemplateQuery(kind), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> SubmitIntakeForm(
        SubmitIntakeFormRequest request,
        ISender mediator,
        CancellationToken ct)
    {
        IntakeFormResponse result = await mediator.Send(new SubmitIntakeFormCommand(request), ct);
        return Results.Created($"/api/v1/intake-forms/{result.Id}", result);
    }

    private static async Task<IResult> GetIntakeForms(
        Guid? clientId,
        Guid? appointmentId,
        ISender mediator,
        CancellationToken ct)
    {
        List<IntakeFormResponse> result = await mediator.Send(new GetIntakeFormsQuery(clientId, appointmentId), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetIntakeFormById(
        Guid id,
        ISender mediator,
        CancellationToken ct)
    {
        IntakeFormResponse result = await mediator.Send(new GetIntakeFormByIdQuery(id), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> SignConsentForm(
        SignConsentFormRequest request,
        ISender mediator,
        CancellationToken ct)
    {
        ConsentFormResponse result = await mediator.Send(new SignConsentFormCommand(request), ct);
        return Results.Created($"/api/v1/consent-forms/{result.Id}", result);
    }

    private static async Task<IResult> GetConsentForms(
        Guid? clientId,
        Guid? appointmentId,
        ISender mediator,
        CancellationToken ct)
    {
        List<ConsentFormResponse> result = await mediator.Send(new GetConsentFormsQuery(clientId, appointmentId), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetConsentFormById(
        Guid id,
        ISender mediator,
        CancellationToken ct)
    {
        ConsentFormDetailResponse result = await mediator.Send(new GetConsentFormByIdQuery(id), ct);
        return Results.Ok(result);
    }
}
