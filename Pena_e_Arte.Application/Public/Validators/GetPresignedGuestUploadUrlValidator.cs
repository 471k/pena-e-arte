using FluentValidation;
using Pena_e_Arte.Application.Public.Queries;

namespace Pena_e_Arte.Application.Public.Validators;

public class GetPresignedGuestUploadUrlValidator : AbstractValidator<GetPresignedGuestUploadUrlQuery>
{
    private static readonly HashSet<string> AllowedContentTypes = ["image/jpeg", "image/png", "image/webp"];
    private static readonly HashSet<string> AllowedCategories = ["area", "reference"];

    public GetPresignedGuestUploadUrlValidator()
    {
        RuleFor(x => x.StudioSlug).NotEmpty();

        RuleFor(x => x.Request.ContentType)
            .NotEmpty()
            .Must(ct => AllowedContentTypes.Contains(ct))
            .WithMessage("ContentType must be one of: image/jpeg, image/png, image/webp.");

        RuleFor(x => x.Request.Category)
            .NotEmpty()
            .Must(c => AllowedCategories.Contains(c))
            .WithMessage("Category must be one of: area, reference.");
    }
}
