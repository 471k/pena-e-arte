using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;

namespace Pena_e_Arte.Application.Reviews.Commands;

public record CreateStudioReviewCommand(
    string Slug,
    Guid   AuthorUserId,
    string AuthorName,
    int    Rating,
    string Body) : IRequest;

public class CreateStudioReviewValidator : AbstractValidator<CreateStudioReviewCommand>
{
    public CreateStudioReviewValidator()
    {
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

        bool alreadyReviewed = await db.Reviews
            .AnyAsync(r => r.StudioId == studio.Id && r.AuthorUserId == command.AuthorUserId, ct);

        if (alreadyReviewed)
            throw new ConflictException("You have already reviewed this studio.");

        Review review = Review.ForStudio(
            studio.Id,
            command.AuthorUserId,
            command.AuthorName,
            command.Rating,
            command.Body);

        db.Reviews.Add(review);
        await db.SaveChangesAsync(ct);
    }
}
