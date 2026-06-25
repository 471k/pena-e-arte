using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;

namespace Pena_e_Arte.Application.Reviews.Commands;

public record CreateArtistReviewCommand(
    string Slug,
    Guid   AuthorUserId,
    string AuthorName,
    int    Rating,
    string Body) : IRequest;

public class CreateArtistReviewValidator : AbstractValidator<CreateArtistReviewCommand>
{
    public CreateArtistReviewValidator()
    {
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

        bool alreadyReviewed = await db.Reviews
            .AnyAsync(r => r.ArtistId == artist.Id && r.AuthorUserId == command.AuthorUserId, ct);

        if (alreadyReviewed)
            throw new ConflictException("You have already reviewed this artist.");

        Review review = Review.ForArtist(
            artist.Id,
            command.AuthorUserId,
            command.AuthorName,
            command.Rating,
            command.Body);

        db.Reviews.Add(review);
        await db.SaveChangesAsync(ct);
    }
}
