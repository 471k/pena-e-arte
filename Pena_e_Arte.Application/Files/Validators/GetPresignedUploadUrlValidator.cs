using FluentValidation;
using Pena_e_Arte.Application.Files.Queries;

namespace Pena_e_Arte.Application.Files.Validators;

public class GetPresignedUploadUrlValidator : AbstractValidator<GetPresignedUploadUrlQuery>
{
    private static readonly HashSet<string> AllowedContentTypes =
    [
        "image/jpeg", "image/png", "image/webp", "application/pdf"
    ];

    public GetPresignedUploadUrlValidator()
    {
        // ObjectKey is only ever used as a folder/purpose prefix now — the handler discards
        // any file name portion and generates its own (Finding 6) — but the traversal/length
        // checks below still apply to whatever prefix the client supplies.
        RuleFor(x => x.Request.ObjectKey)
            .NotEmpty()
            .MaximumLength(500)
            .Must(key => !key.Contains(".."))
            .WithMessage("ObjectKey must not contain path traversal sequences.");

        RuleFor(x => x.Request.ContentType)
            .NotEmpty()
            .Must(ct => AllowedContentTypes.Contains(ct))
            .WithMessage("ContentType must be one of: image/jpeg, image/png, image/webp, application/pdf.");
    }
}
