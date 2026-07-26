using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;

namespace Pena_e_Arte.Application.Reviews.Commands;

public record CreateArtistReviewCommand(
    string Slug,
    Guid AppointmentId,
    Guid AuthorUserId,
    string AuthorName,
    int Rating,
    string Body) : IRequest;

public class CreateArtistReviewValidator : AbstractValidator<CreateArtistReviewCommand>
{
    public CreateArtistReviewValidator()
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

public class CreateArtistReviewHandler(IAppDbContext db)
    : IRequestHandler<CreateArtistReviewCommand>
{
    public async Task Handle(CreateArtistReviewCommand command, CancellationToken ct)
    {
        // Approved: public portfolio lookup.
        Artist artist = await db.Artists
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.Slug == command.Slug && a.DeletedAt == null, ct)
            ?? throw new NotFoundException(nameof(Artist), command.Slug);

        // Approved: cross-tenant ownership check — same pattern as the verified-booking
        // join in GetArtistReviewsHandler (architecture.md IgnoreQueryFilters entry 19).
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
        bool ownedByAuthorWithThisArtist = appointment is not null
            && appointment.Appointment.ArtistId == artist.Id
            && appointment.ClientUserId == command.AuthorUserId;

        if (!ownedByAuthorWithThisArtist)
            throw new NotFoundException(nameof(Appointment), command.AppointmentId);

        if (appointment!.Appointment.Status != AppointmentStatus.Completed)
            throw new BusinessRuleViolationException(
                "You can only review an artist after your own completed appointment with them.");

        bool alreadyReviewed = await db.Reviews
            .AnyAsync(r => r.AppointmentId == command.AppointmentId && r.ArtistId == artist.Id, ct);

        if (alreadyReviewed)
            throw new ConflictException("You have already reviewed this appointment.");

        Review review = Review.ForArtist(
            artist.Id,
            command.AppointmentId,
            command.AuthorUserId,
            command.AuthorName,
            command.Rating,
            command.Body);

        db.Reviews.Add(review);
        await db.SaveChangesAsync(ct);
    }
}
