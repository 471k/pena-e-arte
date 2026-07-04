using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Reviews.Commands;

public record RespondToReviewCommand(Guid ReviewId, string Response) : IRequest;

public class RespondToReviewValidator : AbstractValidator<RespondToReviewCommand>
{
    public RespondToReviewValidator()
    {
        RuleFor(x => x.ReviewId).NotEmpty();
        RuleFor(x => x.Response)
            .NotEmpty().WithMessage("Response cannot be blank.")
            .MaximumLength(2000).WithMessage("Response must be 2000 characters or fewer.");
    }
}

public class RespondToReviewHandler(IAppDbContext db, ICurrentTenant currentTenant)
    : IRequestHandler<RespondToReviewCommand>
{
    public async Task Handle(RespondToReviewCommand command, CancellationToken ct)
    {
        Domain.Entities.Review review = await db.Reviews
            .FirstOrDefaultAsync(r => r.Id == command.ReviewId, ct)
            ?? throw new NotFoundException(nameof(Domain.Entities.Review), command.ReviewId);

        bool isForThisStudio = review.StudioId == currentTenant.StudioId;
        bool isForThisArtist = review.ArtistId.HasValue
            && await db.Artists.AnyAsync(a => a.Id == review.ArtistId && a.StudioId == currentTenant.StudioId, ct);
        bool isForThisImage = review.PortfolioImageId.HasValue
            && await db.PortfolioImages.AnyAsync(pi => pi.Id == review.PortfolioImageId && pi.StudioId == currentTenant.StudioId, ct);

        if (!isForThisStudio && !isForThisArtist && !isForThisImage)
            throw new ForbiddenException("You cannot respond to this review.");

        review.Respond(command.Response);
        await db.SaveChangesAsync(ct);
    }
}
