using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;

namespace Pena_e_Arte.Application.Reviews.Commands;

public record CreatePortfolioImageReviewCommand(
    Guid   ImageId,
    Guid   AuthorUserId,
    string AuthorName,
    int    Rating,
    string Body) : IRequest;

public class CreatePortfolioImageReviewValidator
    : AbstractValidator<CreatePortfolioImageReviewCommand>
{
    public CreatePortfolioImageReviewValidator()
    {
        RuleFor(x => x.Rating).InclusiveBetween(1, 5);
        RuleFor(x => x.Body)
            .NotEmpty()
            .MinimumLength(10)
            .MaximumLength(2000);
        RuleFor(x => x.AuthorName).NotEmpty().MaximumLength(200);
    }
}

public class CreatePortfolioImageReviewHandler(IAppDbContext db)
    : IRequestHandler<CreatePortfolioImageReviewCommand>
{
    public async Task Handle(CreatePortfolioImageReviewCommand command, CancellationToken ct)
    {
        // Approved: public portfolio lookup — cross-tenant.
        bool imageExists = await db.PortfolioImages
            .IgnoreQueryFilters()
            .AnyAsync(p => p.Id == command.ImageId, ct);

        if (!imageExists)
            throw new NotFoundException(nameof(PortfolioImage), command.ImageId);

        bool alreadyReviewed = await db.Reviews
            .AnyAsync(r => r.PortfolioImageId == command.ImageId
                        && r.AuthorUserId     == command.AuthorUserId, ct);

        if (alreadyReviewed)
            throw new ConflictException("You have already reviewed this tattoo.");

        Review review = Review.ForPortfolioImage(
            command.ImageId,
            command.AuthorUserId,
            command.AuthorName,
            command.Rating,
            command.Body);

        db.Reviews.Add(review);
        await db.SaveChangesAsync(ct);
    }
}
