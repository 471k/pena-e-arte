using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.BookingDrafts.Commands;

/// <summary>Discards the signed-in person's draft at the current studio. Idempotent: no draft is not an error.</summary>
public record DeleteMyBookingDraftCommand : IRequest;

public class DeleteMyBookingDraftHandler(IAppDbContext db, ICurrentUser currentUser) : IRequestHandler<DeleteMyBookingDraftCommand>
{
    public async Task Handle(DeleteMyBookingDraftCommand command, CancellationToken ct)
    {
        BookingDraft? draft = await db.BookingDrafts
            .Include(d => d.Attachments)
            .FirstOrDefaultAsync(d => d.UserId == currentUser.UserId, ct);
        if (draft is null) return;

        db.BookingDraftAttachments.RemoveRange(draft.Attachments);
        db.BookingDrafts.Remove(draft);
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>Nothing to validate (the command carries no input); present because every endpoint has a validator.</summary>
public class DeleteMyBookingDraftValidator : AbstractValidator<DeleteMyBookingDraftCommand>;
