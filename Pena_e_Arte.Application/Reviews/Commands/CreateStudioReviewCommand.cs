using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;

namespace Pena_e_Arte.Application.Reviews.Commands;

public record CreateStudioReviewCommand(
    string Slug,
    Guid   AppointmentId,
    Guid   AuthorUserId,
    string AuthorName,
    int    Rating,
    string Body) : IRequest;

public class CreateStudioReviewValidator : AbstractValidator<CreateStudioReviewCommand>
{
    public CreateStudioReviewValidator()
    {
        RuleFor(x => x.AppointmentId).NotEmpty();
        RuleFor(x => x.Rating).InclusiveBetween(1, 5);
        RuleFor(x => x.Body)
            .NotEmpty()
            .MinimumLength(10)
            .MaximumLength(2000);
        RuleFor(x => x.AuthorName).NotEmpty().MaximumLength(200);
    }
}

public class CreateStudioReviewHandler(IAppDbContext db)
    : IRequestHandler<CreateStudioReviewCommand>
{
    public async Task Handle(CreateStudioReviewCommand command, CancellationToken ct)
    {
        // Approved: public portfolio lookup — see architecture.md AllowAnonymous Exceptions.
        Studio studio = await db.Studios
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Slug == command.Slug && s.IsActive, ct)
            ?? throw new NotFoundException(nameof(Studio), command.Slug);

        // Approved: cross-tenant ownership check — same pattern as the verified-booking
        // join in GetStudioReviewsHandler (architecture.md IgnoreQueryFilters entry 20).
        var appointment = await db.Appointments
            .IgnoreQueryFilters()
            .Where(a => a.Id == command.AppointmentId)
            .Join(db.Clients.IgnoreQueryFilters(),
                  a => a.ClientId,
                  c => c.Id,
                  (a, c) => new { Appointment = a, ClientUserId = c.UserId })
            .FirstOrDefaultAsync(ct);

        // 404 (not a generic error) on any ownership/scope mismatch — mirrors
        // RescheduleAppointmentHandler's "don't reveal another client's appointment
        // exists" convention — vs. a business-rule error for a real-but-wrong state.
        bool ownedByAuthorAtThisStudio = appointment is not null
            && appointment.Appointment.StudioId == studio.Id
            && appointment.ClientUserId         == command.AuthorUserId;

        if (!ownedByAuthorAtThisStudio)
            throw new NotFoundException(nameof(Appointment), command.AppointmentId);

        if (appointment!.Appointment.Status != AppointmentStatus.Completed)
            throw new BusinessRuleViolationException(
                "You can only review a studio after your own completed appointment there.");

        bool alreadyReviewed = await db.Reviews
            .AnyAsync(r => r.AppointmentId == command.AppointmentId && r.StudioId == studio.Id, ct);

        if (alreadyReviewed)
            throw new ConflictException("You have already reviewed this appointment.");

        Review review = Review.ForStudio(
            studio.Id,
            command.AppointmentId,
            command.AuthorUserId,
            command.AuthorName,
            command.Rating,
            command.Body);

        db.Reviews.Add(review);
        await db.SaveChangesAsync(ct);
    }
}
