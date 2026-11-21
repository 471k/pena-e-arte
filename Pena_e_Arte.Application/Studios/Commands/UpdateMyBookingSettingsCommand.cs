using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Studios.Commands;

/// <summary>The spacing choices for the slot list's start times (C03 follow-up, Phi 20 Nov).</summary>
public static class BookingSlotStep
{
    public static readonly IReadOnlyList<int> AllowedMinutes = [15, 30, 60];
}

public record UpdateMyBookingSettingsCommand(UpdateBookingSettingsRequest Request) : IRequest<BookingSettingsResponse>;

public class UpdateMyBookingSettingsHandler(IAppDbContext db, ICurrentTenant tenant)
    : IRequestHandler<UpdateMyBookingSettingsCommand, BookingSettingsResponse>
{
    public async Task<BookingSettingsResponse> Handle(UpdateMyBookingSettingsCommand command, CancellationToken ct)
    {
        Studio studio = await db.Studios.FirstOrDefaultAsync(s => s.Id == tenant.StudioId, ct)
            ?? throw new NotFoundException(nameof(Studio), tenant.StudioId);

        studio.BookingSlotStepMinutes = command.Request.SlotStepMinutes;
        await db.SaveChangesAsync(ct);

        return new BookingSettingsResponse(studio.BookingSlotStepMinutes, BookingSlotStep.AllowedMinutes);
    }
}

public class UpdateMyBookingSettingsValidator : AbstractValidator<UpdateMyBookingSettingsCommand>
{
    public UpdateMyBookingSettingsValidator()
    {
        RuleFor(x => x.Request.SlotStepMinutes)
            .Must(step => step is null || BookingSlotStep.AllowedMinutes.Contains(step.Value))
            .WithMessage($"Start times can be every session length, or every {string.Join(", ", BookingSlotStep.AllowedMinutes)} minutes.");
    }
}
