using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Application.BookingDrafts.Queries;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.BookingDrafts.Commands;

/// <summary>
/// Saves (creates or replaces) the signed-in person's draft at the current studio and resets its seven days.
/// The safety notes field is refused, never stripped: the client sends a payload without it, and a payload that
/// still carries it is a bug that must surface rather than be quietly cleaned.
/// </summary>
public record SaveMyBookingDraftCommand(SaveBookingDraftRequest Request) : IRequest<BookingDraftResponse>;

public class SaveMyBookingDraftHandler(IAppDbContext db, ICurrentUser currentUser, ICurrentTenant tenant, TimeProvider time)
    : IRequestHandler<SaveMyBookingDraftCommand, BookingDraftResponse>
{
    public const string ExcludedFieldMessage =
        "A saved draft never keeps the \"Anything else I should know?\" answer, so it can't be saved with it.";

    public async Task<BookingDraftResponse> Handle(SaveMyBookingDraftCommand command, CancellationToken ct)
    {
        // Defence in depth: the validator refuses this first. The handler must not rely on the pipeline alone for
        // the one rule that keeps health data out of the store.
        if (BookingDraftRules.HoldsExcludedField(command.Request.Payload))
            throw new BusinessRuleViolationException(ExcludedFieldMessage);

        DateTime now = time.GetUtcNow().UtcDateTime;
        BookingDraft? draft = await db.BookingDrafts
            .Include(d => d.Attachments)
            .FirstOrDefaultAsync(d => d.UserId == currentUser.UserId, ct);

        if (draft is null)
        {
            draft = new BookingDraft { StudioId = tenant.StudioId, UserId = currentUser.UserId };
            db.BookingDrafts.Add(draft);
        }
        else
        {
            db.BookingDraftAttachments.RemoveRange(draft.Attachments);
            draft.Attachments.Clear();
        }

        draft.PayloadJson = command.Request.Payload;
        draft.UpdatedAt = now;
        draft.ExpiresAt = now + BookingDraft.Lifetime;
        // C05: a draft the signed-in person saves themselves is theirs from the first save. If the row was an
        // UNCLAIMED one written by the guest-booking collision path, this save replaces its content entirely
        // (payload and photos above), so nothing a stranger typed becomes visible by being claimed here.
        draft.ClaimedAt ??= now;
        foreach (BookingDraftImageRequest image in command.Request.Images ?? [])
        {
            draft.Attachments.Add(new BookingDraftAttachment
            {
                StudioId = draft.StudioId,
                DraftId = draft.Id,
                ImageUrl = image.Url,
                Category = Enum.Parse<AppointmentAttachmentCategory>(image.Category),
            });
        }

        await db.SaveChangesAsync(ct);
        return GetMyBookingDraftHandler.Map(draft);
    }
}

public class SaveMyBookingDraftValidator : AbstractValidator<SaveMyBookingDraftCommand>
{
    public SaveMyBookingDraftValidator(IR2Service r2)
    {
        RuleFor(x => x.Request.Payload)
            .NotEmpty()
            .MaximumLength(BookingDraftRules.MaxPayloadLength)
            .Must(BookingDraftRules.IsJsonObject).WithMessage("The draft must be a JSON object.")
            .Must(p => !BookingDraftRules.HoldsExcludedField(p)).WithMessage(SaveMyBookingDraftHandler.ExcludedFieldMessage);

        // The same rules as a submitted booking's photos (CreateAppointmentValidator): a valid storage URL, a known
        // category, and the per-category cap.
        RuleForEach(x => x.Request.Images).ChildRules(image =>
        {
            image.RuleFor(i => i.Url).NotEmpty().MaximumLength(2048).Must(r2.IsR2Url)
                .WithMessage("Image Url must reference a valid storage URL.");
            image.RuleFor(i => i.Category).Must(c => BookingContentValidationRules.ValidImageCategories.Contains(c))
                .WithMessage("Category must be one of: AreaPhoto, Reference.");
        });
        RuleFor(x => x.Request.Images)
            .Must(images => images is null ||
                images.Count(i => i.Category == "AreaPhoto") <= BookingContentValidationRules.MaxImagesPerCategory)
            .WithMessage($"You can attach up to {BookingContentValidationRules.MaxImagesPerCategory} area photos.");
        RuleFor(x => x.Request.Images)
            .Must(images => images is null ||
                images.Count(i => i.Category == "Reference") <= BookingContentValidationRules.MaxImagesPerCategory)
            .WithMessage($"You can attach up to {BookingContentValidationRules.MaxImagesPerCategory} reference images.");
    }
}
