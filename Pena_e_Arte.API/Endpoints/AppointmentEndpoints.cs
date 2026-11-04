using MediatR;
using Pena_e_Arte.Application.Appointments.Commands;
using Pena_e_Arte.Application.Appointments.Queries;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.API.Endpoints;

public static class AppointmentEndpoints
{
    public static void MapAppointmentEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/v1/appointments")
            .RequireAuthorization();

        group.MapGet("/", GetAppointments).RequireAuthorization("ArtistAndAbove");
        group.MapGet("/export.csv", ExportAppointmentsCsv).RequireAuthorization("OwnerOnly");
        group.MapGet("/mine", GetMyAppointments).RequireAuthorization("ClientAndAbove");
        group.MapGet("/check-slot", CheckSlotAvailability).RequireAuthorization("ClientAndAbove");
        group.MapGet("{id:guid}", GetAppointment).RequireAuthorization("ArtistAndAbove");
        group.MapPost("/", CreateAppointment).RequireAuthorization("ClientAndAbove");
        group.MapDelete("{id:guid}", CancelAppointment).RequireAuthorization("ClientAndAbove");
        group.MapPatch("{id:guid}/confirm", ConfirmAppointment).RequireAuthorization("ArtistAndAbove");
        group.MapPatch("{id:guid}/decline", DeclineAppointment).RequireAuthorization("ArtistAndAbove");
        group.MapPatch("{id:guid}/complete", CompleteAppointment).RequireAuthorization("ArtistAndAbove");
        group.MapPatch("{id:guid}/no-show", MarkNoShow).RequireAuthorization("ArtistAndAbove");
        group.MapPatch("{id:guid}/reschedule", RescheduleAppointment).RequireAuthorization("ClientAndAbove");
        group.MapPatch("{id:guid}/artist", AssignAppointmentArtist).RequireAuthorization("OwnerOnly");
        group.MapGet("{id:guid}/calendar.ics", GetIcs).RequireAuthorization("ClientAndAbove");
        // The client appointment page (DEC02): one of the caller's own appointments, 404 for anyone else's.
        group.MapGet("/mine/{id:guid}", GetMyAppointment).RequireAuthorization("ClientAndAbove");
        group.MapGet("{id:guid}/cancellation-preview", GetCancellationPreview).RequireAuthorization("ClientAndAbove");
        // E04b: whether this appointment can be moved at all, by the same rule the save applies.
        group.MapGet("{id:guid}/reschedule-preview", GetReschedulePreview).RequireAuthorization("ClientAndAbove");
        // E04a: a session's preparation for the artist; an artist gets 404 for a session not assigned to them.
        group.MapGet("{id:guid}/preparation", GetAppointmentPreparation).RequireAuthorization("ArtistAndAbove");
    }

    private static async Task<IResult> GetAppointmentPreparation(
        Guid id,
        ISender mediator,
        CancellationToken ct)
    {
        AppointmentPreparationResponse result = await mediator.Send(new GetAppointmentPreparationQuery(id), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetAppointments(
        DateTime? from,
        DateTime? to,
        Guid? artistId,
        bool? assignedToMe,
        ISender mediator,
        CancellationToken ct)
    {
        List<AppointmentResponse> result = await mediator.Send(
            new GetAppointmentsQuery(from, to, artistId, assignedToMe ?? false), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> ExportAppointmentsCsv(
        DateTime? from,
        DateTime? to,
        ISender mediator,
        CancellationToken ct)
    {
        string csv = await mediator.Send(new ExportAppointmentsCsvQuery(from, to), ct);
        return Results.Content(csv, "text/csv; charset=utf-8");
    }

    private static async Task<IResult> GetMyAppointments(
        ISender mediator,
        CancellationToken ct)
    {
        List<AppointmentResponse> result = await mediator.Send(new GetMyAppointmentsQuery(), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetMyAppointment(
        Guid id,
        ISender mediator,
        CancellationToken ct)
    {
        ClientAppointmentDetailResponse result = await mediator.Send(new GetMyAppointmentQuery(id), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetAppointment(
        Guid id,
        ISender mediator,
        CancellationToken ct)
    {
        AppointmentResponse result = await mediator.Send(new GetAppointmentQuery(id), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> CreateAppointment(
        CreateAppointmentRequest request,
        ISender mediator,
        CancellationToken ct)
    {
        AppointmentResponse result = await mediator.Send(new CreateAppointmentCommand(request), ct);
        return Results.Created($"/api/v1/appointments/{result.Id}", result);
    }

    private static async Task<IResult> CancelAppointment(
        Guid id,
        ISender mediator,
        CancellationToken ct)
    {
        await mediator.Send(new CancelAppointmentCommand(id), ct);
        return Results.NoContent();
    }

    // The staff answer to a booking request: cancels it with StudioDeclined, so the client is told
    // the studio can't take the request (and any deposit is refunded in full, as for any
    // studio-side cancellation).
    private static async Task<IResult> DeclineAppointment(
        Guid id,
        ISender mediator,
        CancellationToken ct)
    {
        await mediator.Send(new CancelAppointmentCommand(id, CancellationReason.StudioDeclined), ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ConfirmAppointment(
        Guid id,
        ISender mediator,
        CancellationToken ct)
    {
        AppointmentResponse result = await mediator.Send(new ConfirmAppointmentCommand(id), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> CompleteAppointment(
        Guid id,
        ISender mediator,
        CancellationToken ct)
    {
        AppointmentResponse result = await mediator.Send(new CompleteAppointmentCommand(id), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> MarkNoShow(
        Guid id,
        ISender mediator,
        CancellationToken ct)
    {
        AppointmentResponse result = await mediator.Send(new MarkNoShowCommand(id), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> RescheduleAppointment(
        Guid id,
        RescheduleAppointmentRequest request,
        ISender mediator,
        CancellationToken ct)
    {
        AppointmentResponse result = await mediator.Send(new RescheduleAppointmentCommand(id, request), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetIcs(
        Guid id,
        ISender mediator,
        CancellationToken ct)
    {
        string icsContent = await mediator.Send(new GetAppointmentIcsQuery(id), ct);
        return Results.Content(icsContent, "text/calendar; charset=utf-8");
    }

    // Advisory: what cancelling would do with the deposit. DELETE recomputes it at commit.
    private static async Task<IResult> GetReschedulePreview(
        Guid id,
        ISender mediator,
        CancellationToken ct)
    {
        ReschedulePreviewResponse result = await mediator.Send(new GetReschedulePreviewQuery(id), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetCancellationPreview(
        Guid id,
        ISender mediator,
        CancellationToken ct)
    {
        CancellationPreviewResponse result = await mediator.Send(new GetCancellationPreviewQuery(id), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> AssignAppointmentArtist(
        Guid id,
        AssignAppointmentArtistRequest request,
        ISender mediator,
        CancellationToken ct)
    {
        AppointmentResponse result = await mediator.Send(new AssignAppointmentArtistCommand(id, request), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> CheckSlotAvailability(
        Guid? artistId,
        DateTime date,
        int durationMinutes,
        Guid? excludeAppointmentId,
        ISender mediator,
        CancellationToken ct)
    {
        SlotAvailabilityResult result = await mediator.Send(
            new CheckSlotAvailabilityQuery(artistId, date, durationMinutes, excludeAppointmentId), ct);
        return Results.Ok(new SlotAvailabilityResponse(result.Available, result.Reason));
    }
}
